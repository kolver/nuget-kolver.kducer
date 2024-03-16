using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Kolver
{
    public class Kducer
        : IDisposable
    {
        public int POLL_INTERVAL_MS = 100;
        private static readonly int SHORT_WAIT = 50;
        private static readonly int PR_SEQ_CHANGE_WAIT = 300;
        private readonly ILogger kduLogger;
        /// <summary>
        /// if true, the screwdriver lever is disabled ("stop motor on") after a new result is detected internally by this library
        /// the screwdriver is automatically re-enabled when you (the user) obtain the tightening result via "GetResultAsync" (unless lockScrewdriverIndefinitelyAfterResult is true)
        /// this option is ignored when using RunScrewdriverUntilResultAsync because RunScrewdriverUntilResultAsync is made for automatic machines (CA series screwdrivers) where the screwdriver is not in the hands of an operator
        /// </summary>
        public bool lockScrewdriverUntilGetResult = false;
        /// <summary>
        /// if true, the screwdriver lever is disabled ("stop motor on") after a new result is detected internally by this library
        /// you (the user) have to re-enable the screwdriver manually by using Kducer.StopMotorOff
        /// this option is ignored when using RunScrewdriverUntilResultAsync because RunScrewdriverUntilResultAsync is made for automatic machines (CA series screwdrivers) where the screwdriver is not in the hands of an operator
        /// </summary>
        public bool lockScrewdriverIndefinitelyAfterResult = false;
        /// <summary>
        /// if true, the timestamp of the result from the controller is replaced with the local machine timestamp
        /// this is recommended because the clock on the KDU does not track timezones, annual daylight time changes, etc
        /// </summary>
        public bool replaceResultTimestampWithLocalTimestamp = true;
        /// <summary>
        /// if true, failed connection attempts will be logged as "LogWarning"
        /// if false, failed connection attempts will be logged as "LogInformation"
        /// default reconnection interval is 5 seconds, so if your line often has KDU controllers that are off or disconnected, you can set this to false to reduce the number warning logs generated
        /// </summary>
        public bool logFailedConnectionsAsWarning = true;

        private ReducedModbusTcpClientAsync mbClient;
        private CancellationToken kduCommsCancellationToken;
        private readonly CancellationTokenSource linkedCancellationTokenSource;
        private Task asyncComms;
        private readonly ConcurrentQueue<KducerTighteningResult> resultsQueue = new ConcurrentQueue<KducerTighteningResult>();
        private readonly ConcurrentQueue<KduUnitOperationTaskAsync> userCmdQueue = new ConcurrentQueue<KduUnitOperationTaskAsync>();
        private Kducer(IPAddress kduIpAddress, CancellationToken kduCommsCancellationToken, ILogger kduLogger) // private constructor: must use static constructors below
        {
            linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(kduCommsCancellationToken);
            this.kduCommsCancellationToken = linkedCancellationTokenSource.Token;
            this.kduLogger = kduLogger;
            mbClient = new ReducedModbusTcpClientAsync(kduIpAddress);
        }
        /// <summary>
        /// istantiates a Kducer and connects to the KDU controller
        /// </summary>
        /// <param name="kduIpAddress"></param>
        /// <param name="kduCommsCancellationToken">If you don't need to use a cancellation token, use CancellationToken.None</param>
        /// <param name="loggerFactory">If you don't need logging, use NullLoggerFactory.Instance</param>
        /// <returns></returns>
        public static Kducer CreateKducerAndStartAsyncComms(IPAddress kduIpAddress, CancellationToken kduCommsCancellationToken, ILoggerFactory loggerFactory)
        {
            Kducer kdu = new Kducer(kduIpAddress, kduCommsCancellationToken, loggerFactory.CreateLogger(typeof(Kducer)));
            kdu.StartAsyncComms();
            return kdu;
        }
        /// <summary>
        /// istantiates a Kducer and connects to the KDU controller
        /// </summary>
        /// <param name="kduIpAddress"></param>
        /// <param name="kduCommsCancellationToken">If you don't need to use a cancellation token, use CancellationToken.None</param>
        /// <param name="loggerFactory">If you don't need logging, use NullLoggerFactory.Instance</param>
        /// <returns></returns>
        public static Kducer CreateKducerAndStartAsyncComms(String kduIpAddress, CancellationToken kduCommsCancellationToken, ILoggerFactory loggerFactory)
        {
            return CreateKducerAndStartAsyncComms(IPAddress.Parse(kduIpAddress), kduCommsCancellationToken, loggerFactory);
        }

        private void StartAsyncComms()
        {
            asyncComms = AsyncCommsLoop();
        }

        private async Task AsyncCommsLoop()
        {
            while (true)
            {
                try
                {
                    uint largeResultsQueueWarningThreshold = 10;
                    await mbClient.ConnectAsync(kduCommsCancellationToken);
                    if (kduCommsCancellationToken.IsCancellationRequested)
                        return;
                    await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false); // clear new result flag if already present

                    KduUnitOperationTaskAsync pollForResults = new KduUnitOperationTaskAsync(UserCmd.None, kduCommsCancellationToken, payload:0, resultsQueue);

                    while (true)
                    {
                        if (kduCommsCancellationToken.IsCancellationRequested)
                            return;

                        Task interval = Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken);

                        if (userCmdQueue.TryDequeue(out KduUnitOperationTaskAsync userCmd))
                        {
                            await userCmd.ProcessUserCmdAsync(mbClient).ConfigureAwait(false);
                        }
                        else
                        {
                            await pollForResults.CheckAndEnqueueKduResult(mbClient, lockScrewdriverUntilGetResult, lockScrewdriverIndefinitelyAfterResult, replaceResultTimestampWithLocalTimestamp).ConfigureAwait(false);
                        }

                        if (resultsQueue.Count >= largeResultsQueueWarningThreshold)
                        {
                            kduLogger.LogWarning("There are {numberOfKducerTighteningResult} accumulated in the FIFO tightening results queue. Did you forget to dispose this Kducer object?", resultsQueue.Count);
                            largeResultsQueueWarningThreshold *= 10;
                        }

                        await interval.ConfigureAwait(false);
                    }
                }
                catch (TimeoutException connectionTimeout)
                {
                    if (logFailedConnectionsAsWarning)
                        kduLogger.LogWarning(connectionTimeout, "TCP connection failed. Async communications will continue and reattempt.");
                    else
                        kduLogger.LogInformation(connectionTimeout, "TCP connection failed. Async communications will continue and reattempt.");
                }
                catch (SocketException tcpError)
                {
                    kduLogger.LogWarning(tcpError, "TCP transmission error. Async communications will continue and reattempt.");
                }
                catch (ModbusException modbusError)
                {
                    kduLogger.LogWarning(modbusError, "KDU replied with a Modbus exception. Async communications will continue but the modbus command will NOT be reattempted!");
                }
                catch (Exception e)
                {
                    kduLogger.LogCritical(e, "Unexpected exception. Async communications will stop!");
                    throw e;
                }
            }
        }

        /// <summary>
        /// Checks if there is a KDU result available in the FIFO results queue
        /// </summary>
        /// <returns>true if there is at least one result available, false otherwise</returns>
        public bool HasNewResult() { return !resultsQueue.IsEmpty; }

        /// <summary>
        /// Creates a task that returns and removes a tightening result from the FIFO results queue. The task awaits until there is a result to return.
        /// If the KDU is disconnected or disconnects, the task will continue attempting to reconnect and wait indefinitely until there is a result to return,
        /// TCP socket exceptions will not be propagated.
        /// </summary>
        /// <param name="cancellationToken">If you don't need to use a cancellation token, use CancellationToken.None</param>
        /// <returns>a KducerTighteningResult object (from a FIFO results queue) from which you can obtain data about the tightening result</returns>
        public async Task<KducerTighteningResult> GetResultAsync(CancellationToken cancellationToken)
        {
            CancellationToken cancelGetResult = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, kduCommsCancellationToken).Token;

            while (true)
            {
                if (!resultsQueue.IsEmpty)
                {
                    if (resultsQueue.TryDequeue(out KducerTighteningResult res) == true)
                        return res;
                }
                await Task.Delay(POLL_INTERVAL_MS, cancelGetResult).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Selects a program on the KDU and waits 300ms to ensure the KDU program is loaded.
        /// </summary>
        /// <param name="kduProgramNumber">The program number to select. 1 to 64 or 1 to 200 depending on the KDU model and firmware version.</param>
        /// <exception cref="ModbusServerBusyException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="TimeoutException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SelectProgramNumberAsync(ushort kduProgramNumber)
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.SetProgram, kduCommsCancellationToken, kduProgramNumber);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd);
        }

        /// <summary>
        /// Reads the program number currently active in the KDU
        /// </summary>
        /// <returns>the program number</returns>
        /// <exception cref="ModbusServerBusyException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="TimeoutException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<ushort> GetProgramNumberAsync()
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.GetProgram, kduCommsCancellationToken);
            return await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmd);
        }

        /// <summary>
        /// Selects a sequence on the KDU and waits 300ms to ensure the KDU sequence is loaded.
        /// </summary>
        /// <param name="kduSequenceNumber">The sequence number to select. 1 to 8 or 1 to 24 depending on the KDU model and firmware version.</param>
        /// <exception cref="ModbusServerBusyException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="TimeoutException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SelectSequenceNumberAsync(ushort kduSequenceNumber)
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.SetSequence, kduCommsCancellationToken, kduSequenceNumber);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd);
        }

        /// <summary>
        /// Reads the sequence number currently active in the KDU
        /// </summary>
        /// <returns>the sequence number</returns>
        /// <exception cref="ModbusServerBusyException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="TimeoutException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<ushort> GetSequenceNumberAsync()
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.GetSequence, kduCommsCancellationToken);
            return await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmd);
        }

        /// <summary>
        /// Disables the screwdriver lever by setting the "Stop Motor ON" state on the KDU controller
        /// </summary>
        /// <exception cref="ModbusServerBusyException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="TimeoutException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task DisableScrewdriver()
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.StopMotorOn, kduCommsCancellationToken);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd);
        }

        /// <summary>
        /// Enables the screwdriver lever by setting the "Stop Motor OFF" state on the KDU controller
        /// This is necessary to re-enable the lever after calling DisableScrewdriver, or when lockScrewdriverIndefinitelyAfterResult is true
        /// </summary>
        /// <exception cref="ModbusServerBusyException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="TimeoutException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task EnableScrewdriver() 
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.StopMotorOff, kduCommsCancellationToken);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd);
        }

        /// <summary>
        /// Runs screwdriver until the tightening completes according to the KDU parameters of the currently selected program
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>The KducerTighteningResult object from which you can obtain data about the tightening result</returns>
        public async Task<KducerTighteningResult> RunScrewdriverUntilResultAsync(CancellationToken cancellationToken)
        {
            CancellationToken cancelRunScrewdriver = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, kduCommsCancellationToken).Token;

            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.RunScrewdriver, cancelRunScrewdriver, 0, resultsQueue, replaceResultTimestampWithLocalTimestamp);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd);
            return userCmd.tighteningResult;
        }
        private async Task EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(KduUnitOperationTaskAsync userCmd)
        {
            userCmdQueue.Enqueue(userCmd);
            while (!userCmd.completed)
                await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);
            if (userCmd.exceptionThrownInAsyncTask != null)
            {
                if (userCmd.exceptionThrownInAsyncTask is ModbusServerBusyException)
                    throw new ModbusServerBusyException("KDU replied with a Modbus busy exception so the desired command was not processed.");
                else
                    throw userCmd.exceptionThrownInAsyncTask;
            }
        }

        private async Task<ushort> EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(KduUnitOperationTaskAsync userCmd)
        {
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd);
            return userCmd.result;
        }

        internal class NoKduResultException : Exception
        {
            public NoKduResultException()
            {
            }

            public NoKduResultException(string message)
                : base(message)
            {
            }

            public NoKduResultException(string message, Exception inner)
                : base(message, inner)
            {
            }
        }
        private enum UserCmd
        {
            None,
            SetProgram,
            GetProgram,
            SetSequence,
            GetSequence,
            StopMotorOn,
            StopMotorOff,
            RunScrewdriver
        }

        private class KduUnitOperationTaskAsync
        {
            private readonly UserCmd cmd;
            private readonly ushort payload;
            private readonly CancellationToken cancellationToken;
            private readonly ConcurrentQueue<KducerTighteningResult> resultsQueue;
            internal ushort result;
            internal bool completed = false;
            internal bool replaceResultTimestampWithLocalTimestamp;
            internal Exception exceptionThrownInAsyncTask = null;
            internal KducerTighteningResult tighteningResult = null;

            internal KduUnitOperationTaskAsync(UserCmd cmd, CancellationToken cancellationToken, ushort payload = 0, ConcurrentQueue<KducerTighteningResult> resultsQueue = null, bool replaceResultTimestampWithLocalTimestamp = true)
            {
                this.cmd = cmd;
                this.cancellationToken = cancellationToken;
                this.payload = payload;
                this.resultsQueue = resultsQueue;
                this.replaceResultTimestampWithLocalTimestamp = replaceResultTimestampWithLocalTimestamp;
            }

            internal async Task ProcessUserCmdAsync(ReducedModbusTcpClientAsync mbClient)
            {
                try
                {
                    switch (cmd)
                    {
                        case UserCmd.GetProgram:
                            byte[] prNr = await mbClient.ReadHoldingRegistersAsync(7372, 1).ConfigureAwait(false);
                            result = prNr[1];
                            break;

                        case UserCmd.SetProgram:
                            if ((await mbClient.ReadHoldingRegistersAsync(7372, 1).ConfigureAwait(false))[1] == payload)
                                break;
                            await mbClient.WriteSingleRegisterAsync(7373, payload).ConfigureAwait(false);
                            await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetSequence:
                            byte[] seqNr = await mbClient.ReadHoldingRegistersAsync(7373, 1).ConfigureAwait(false);
                            result = seqNr[1];
                            break;

                        case UserCmd.SetSequence:
                            if ((await mbClient.ReadHoldingRegistersAsync(7373, 1).ConfigureAwait(false))[1] == payload)
                                break;
                            await mbClient.WriteSingleRegisterAsync(7373, payload).ConfigureAwait(false);
                            await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.StopMotorOn:
                            await mbClient.WriteSingleCoilAsync(34, true).ConfigureAwait(false);
                            await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.StopMotorOff:
                            await mbClient.WriteSingleCoilAsync(34, false).ConfigureAwait(false);
                            await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.RunScrewdriver:
                            await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false);
                            while (!cancellationToken.IsCancellationRequested && (await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false))[1] == 0)
                            {
                                await mbClient.WriteSingleCoilAsync(32, true).ConfigureAwait(false);
                                await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                            }
                            await mbClient.WriteSingleCoilAsync(32, false).ConfigureAwait(false);
                            tighteningResult = new KducerTighteningResult(await mbClient.ReadInputRegistersAsync(295, 67).ConfigureAwait(false), replaceResultTimestampWithLocalTimestamp);
                            break;
                    }
                }
                catch (TimeoutException connectionTimeout)
                {
                    exceptionThrownInAsyncTask = connectionTimeout;
                }
                catch (SocketException tcpError)
                {
                    exceptionThrownInAsyncTask = tcpError;
                }
                catch (ModbusException modbusError)
                {
                    exceptionThrownInAsyncTask = modbusError;
                }

                completed = true;
            }

            internal async Task CheckAndEnqueueKduResult(ReducedModbusTcpClientAsync mbClient, bool lockScrewdriverUntilResultsProcessed, bool lockScrewdriverIndefinitelyAfterResult, bool replaceResultTimestampWithLocalTimestamp)
            {
                byte newResultFlag = (await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false))[1];

                if (newResultFlag == 1)
                {
                    resultsQueue.Enqueue(new KducerTighteningResult(await mbClient.ReadInputRegistersAsync(295, 67).ConfigureAwait(false), replaceResultTimestampWithLocalTimestamp));
                    if (lockScrewdriverUntilResultsProcessed || lockScrewdriverIndefinitelyAfterResult)
                    {
                        await mbClient.WriteSingleCoilAsync(34, true).ConfigureAwait(false);
                        await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (!lockScrewdriverIndefinitelyAfterResult && lockScrewdriverUntilResultsProcessed && resultsQueue.IsEmpty)
                {
                    await mbClient.WriteSingleCoilAsync(34, false).ConfigureAwait(false);
                    await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // IDisposable implementation for a non-sealed class
        private bool _disposedValue;
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    linkedCancellationTokenSource.Cancel(); // signal to the task that we are done. fire-and-forget tasks are not stopped by GC
                    mbClient.Dispose();
                }
                _disposedValue = true;
            }
        }
        // when deriving from Kducer, the deriving class should implement the following method:
        /*
        private bool _disposedValue;
        protected override void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // here call Dispose on any IDisposable instance members not part of base class
                }
                // here release any unmanaged instance members if any (things like "private IntPtr nativeResource = Marshal.AllocHGlobal(100);" are unmanaged resources ) 
                _disposedValue = true;
            }
            // Call base class implementation.
            base.Dispose(disposing);
        }*/
    }

}
