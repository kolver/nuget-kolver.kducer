using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Kolver
{
    /// <summary>
    /// Represents a KDU controller and provides convenience methods to control and monitor the controller.
    /// Handles cyclic Modbus TCP communications automatically via TAP (async/await).
    /// </summary>
    public class Kducer
        : IDisposable
    {
        private const int SHORT_WAIT = 50;
        private const int PR_SEQ_CHANGE_WAIT = 300;
        private readonly ILogger kduLogger;

        private int POLL_INTERVAL_MS = 100;

        private bool lockScrewdriverUntilGetResult;
        private bool lockScrewdriverIndefinitelyAfterResult;
        private bool replaceResultTimestampWithLocalTimestamp = true;
        private bool logFailedConnectionsAsWarning = true;

        private readonly ReducedModbusTcpClientAsync mbClient;
        private CancellationToken kduCommsCancellationToken;
#pragma warning disable CA2213 // Disposable fields should be disposed
        private readonly CancellationTokenSource linkedCancellationTokenSource; // linkedCancellationTokenSource ensures that the underlying fire-and-forget task is cancelled when Kducer is Disposed
#pragma warning restore CA2213 // Disposable fields should be disposed
        private Task asyncComms;
        private readonly ConcurrentQueue<KducerTighteningResult> resultsQueue = new ConcurrentQueue<KducerTighteningResult>();
        private readonly ConcurrentQueue<KduUnitOperationTaskAsync> userCmdQueue = new ConcurrentQueue<KduUnitOperationTaskAsync>();

        /// <summary>
        /// istantiates a Kducer and starts async communications with the KDU controller
        /// communications are stopped automatically when this object is disposed
        /// </summary>
        /// <param name="kduIpAddress">IP address of the KDU controller</param>
        /// <param name="kduCommsCancellationToken">cancellation token for the underlying async Modbus TCP communications, pass CancellationToken.None if not needed</param>
        /// <param name="loggerFactory">optional, pass to log info, warnings, and errors. pass NullLoggerFactory.Instance if not needed</param>
        /// <param name="tcpConnectionTimeoutMs">timeout/interval for automatic reconnection attempts with the KDU controller</param>
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx timeout/interval for individual Modbus TCP exchanges with the KDU controller</param>
        public Kducer(IPAddress kduIpAddress, ILoggerFactory loggerFactory, CancellationToken kduCommsCancellationToken, int tcpConnectionTimeoutMs = 5000, int tcpRxTxTimeoutMs = 250)
        {
            if (kduIpAddress == null)
                throw new ArgumentNullException(nameof(kduIpAddress));

            linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(kduCommsCancellationToken);
            this.kduCommsCancellationToken = linkedCancellationTokenSource.Token;
            kduLogger = loggerFactory.CreateLogger(typeof(Kducer));
            mbClient = new ReducedModbusTcpClientAsync(kduIpAddress, tcpConnectionTimeoutMs, tcpRxTxTimeoutMs);
            StartAsyncComms();
        }
        /// <summary>
        /// istantiates a Kducer and starts async communications with the KDU controller
        /// </summary>
        /// <param name="kduIpAddress">IP address of the KDU controller</param>
        /// <param name="kduCommsCancellationToken">cancellation token for the underlying async Modbus TCP communications, pass CancellationToken.None if not needed</param>
        /// <param name="loggerFactory">optional, pass to log info, warnings, and errors. pass NullLoggerFactory.Instance if not needed</param>
        /// <param name="tcpConnectionTimeoutMs">timeout/interval for automatic reconnection attempts with the KDU controller</param>
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx timeout/interval for individual Modbus TCP exchanges with the KDU controller</param>
        public Kducer(String kduIpAddress, ILoggerFactory loggerFactory, CancellationToken kduCommsCancellationToken, int tcpConnectionTimeoutMs = 5000, int tcpRxTxTimeoutMs = 250) :
            this(IPAddress.Parse(kduIpAddress), loggerFactory, kduCommsCancellationToken, tcpConnectionTimeoutMs, tcpRxTxTimeoutMs) { }

        /// <summary>
        /// if true, failed connection attempts will be logged as "LogWarning"
        /// if false, failed connection attempts will be logged as "LogInformation"
        /// default reconnection interval is 5 seconds, so if your line often has KDU controllers that are off or disconnected, you can set this to false to reduce the number warning logs generated
        /// </summary>
        /// <param name="setting"></param>
        public void LogFailedTCPConnectionsAsWarning(bool setting) { logFailedConnectionsAsWarning = setting; }
        /// <summary>
        /// if true, the screwdriver lever is disabled ("stop motor on") after a new result is detected internally by this library
        /// the screwdriver is automatically re-enabled when you (the user) obtain the tightening result via "GetResultAsync" (unless lockScrewdriverIndefinitelyAfterResult is true)
        /// this option is ignored when using RunScrewdriverUntilResultAsync because RunScrewdriverUntilResultAsync is made for automatic machines (CA series screwdrivers) where the screwdriver is not in the hands of an operator
        /// </summary>
        /// <param name="setting"></param>
        public void LockScrewdriverUntilGetResult(bool setting) { lockScrewdriverUntilGetResult = setting; }
        /// <summary>
        /// if true, the screwdriver lever is disabled ("stop motor on") after a new result is detected internally by this library
        /// you (the user) have to re-enable the screwdriver manually by using Kducer.StopMotorOff
        /// this option is ignored when using RunScrewdriverUntilResultAsync because RunScrewdriverUntilResultAsync is made for automatic machines (CA series screwdrivers) where the screwdriver is not in the hands of an operator
        /// </summary>
        /// <param name="setting"></param>
        public void LockScrewdriverIndefinitelyAfterResult(bool setting) { lockScrewdriverIndefinitelyAfterResult = setting; }
        /// <summary>
        /// if true, the timestamp of the result from the controller is replaced with the local machine timestamp
        /// this is recommended because the clock on the KDU does not track timezones, annual daylight time changes, etc
        /// </summary>
        /// <param name="setting"></param>
        public void ReplaceResultTimestampWithLocalTimestamp(bool setting) { replaceResultTimestampWithLocalTimestamp = setting; }
        /// <summary>
        /// changes the default interval at which the KDU is cyclically polled over Modbus TCP to see if there's a new tightening result available
        /// the default value of 100ms is adequate for a LAN network
        /// </summary>
        /// <param name="milliseconds"></param>
        public void ChangeModbusTcpCyclicPollingInterval(int milliseconds) { POLL_INTERVAL_MS = milliseconds; }

        private void StartAsyncComms()
        {
            asyncComms = AsyncCommsLoop();
        }

        private async Task AsyncCommsLoop()
        {
            await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);

            while (true)
            {
                try
                {
                    uint largeResultsQueueWarningThreshold = 10;
                    await mbClient.ConnectAsync(kduCommsCancellationToken).ConfigureAwait(false);
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
                            kduLogger.LogWarning("There are {NumberOfKducerTighteningResult} accumulated in the FIFO tightening results queue. Did you forget to dispose this Kducer object?", resultsQueue.Count);
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
                    throw;
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
            CancellationTokenSource cancelGetResult = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, kduCommsCancellationToken);

            while (true)
            {
                if (!resultsQueue.IsEmpty)
                {
                    if (resultsQueue.TryDequeue(out KducerTighteningResult res) == true)
                    {
                        cancelGetResult.Dispose();
                        return res;
                    }
                }
                await Task.Delay(POLL_INTERVAL_MS, cancelGetResult.Token).ConfigureAwait(false);
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
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd).ConfigureAwait(false);
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
            return await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmd).ConfigureAwait(false);
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
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd).ConfigureAwait(false);
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
            return await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmd).ConfigureAwait(false);
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
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd).ConfigureAwait(false);
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
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs screwdriver until the tightening completes according to the KDU parameters of the currently selected program
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>The KducerTighteningResult object from which you can obtain data about the tightening result</returns>
        public async Task<KducerTighteningResult> RunScrewdriverUntilResultAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource cancelRunScrewdriver = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, kduCommsCancellationToken);

            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.RunScrewdriver, cancelRunScrewdriver.Token, 0, resultsQueue, replaceResultTimestampWithLocalTimestamp);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd).ConfigureAwait(false);
            cancelRunScrewdriver.Dispose();
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
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmd).ConfigureAwait(false);
            return userCmd.result;
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
            internal bool completed;
            internal bool replaceResultTimestampWithLocalTimestamp;
            internal Exception exceptionThrownInAsyncTask;
            internal KducerTighteningResult tighteningResult;

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
        /// <summary>
        /// Ensures that the async cyclic Modbus TCP communications loop with the KDU controller is stopped
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        /// <summary>
        /// Ensures that the async cyclic Modbus TCP communications loop with the KDU controller is stopped
        /// when deriving from Kducer, the deriving class should implement the following method:
        /// private bool _disposedValue;
        /// protected override void Dispose(bool disposing)
        /// {
        ///     if (!_disposedValue)
        ///     {
        ///         if (disposing)
        ///         {
        ///              // here call Dispose on any IDisposable instance members not part of base class
        ///         }
        ///         // here release any unmanaged instance members if any (things like "private IntPtr nativeResource = Marshal.AllocHGlobal(100);" are unmanaged resources ) 
        ///         _disposedValue = true;
        ///      }
        ///      // Call base class implementation.
        ///      base.Dispose(disposing);
        /// }
        /// </summary>
        /// <param name="disposing"></param>
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
    }

}
