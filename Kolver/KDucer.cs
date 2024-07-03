// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
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
        private const int PERMANENT_MEMORY_REWRITE_WAIT = 4000;
        private int POLL_INTERVAL_MS = 100;
        private const int defaultRxTxSocketTimeout = 300; 

        private const ushort IR_NEW_RESULT_SELFCLEARING_FLAG = 294;
        private static readonly (ushort addr, ushort count) IR_RESULT_DATA = (295, 67);
        private static readonly (ushort addr, ushort count) IR_TORQUEGRAPH_DATA = (152, 71);
        private static readonly (ushort addr, ushort count) IR_ANGLEGRAPH_DATA = (223, 71);
        private static readonly (ushort addr, ushort count) IR_CONTROLLER_SOFTWARE_VERSION = (370, 10);
        private const ushort COIL_STOP_MOTOR = 34;
        private const ushort COIL_REMOTE_LEVER = 32;
        private const ushort HR_PROGRAM_NR = 7372;
        private const ushort HR_SEQUENCE_NR = 7371;
        private const ushort HR_PROGRAM_DATA_1_TO_64 = 0;
        private const ushort HR_PROGRAM_DATA_65_TO_200 = 7790;
        private const ushort HR_SEQUENCE_DATA_1_TO_8 = 7405;
        private const ushort HR_SEQUENCE_DATA_9_TO_24 = 7950;
        private const ushort HR_PERMANENT_MEMORY_REPROGRAM = 7789;
        private const ushort HR_BARCODE = 7379;

        private readonly ILogger kduLogger;
        private readonly IPAddress kduIpAddress;
        private readonly int tcpRxTxTimeoutMs;

        private bool lockScrewdriverUntilGetResult;
        private bool lockScrewdriverIndefinitelyAfterResult;
        private bool replaceResultTimestampWithLocalTimestamp = true;

        private ushort kduSoftwareVersion;

        private ReducedModbusTcpClientAsync mbClient;
        private readonly CancellationTokenSource asyncCommsCts; // asyncCommsCts ensures that the underlying fire-and-forget task is cancelled when Kducer is Disposed
        private Task asyncComms;
        private readonly ConcurrentQueue<KducerTighteningResult> resultsQueue = new ConcurrentQueue<KducerTighteningResult>();
        private readonly ConcurrentQueue<KduUserCmdTaskAsync> userCmdTaskQueue = new ConcurrentQueue<KduUserCmdTaskAsync>();

        /// <summary>
        /// istantiates a Kducer and starts async communications with the KDU controller
        /// the underlying TCP/IP connection with the KDU starts automatically after instantiating this object
        /// you can confirm/await the connection via the corresponding IsConnected methods
        /// if the TCP/IP connection drops, this object will automatically reattempt to connect indefinitely
        /// TCP/IP communications are stopped ONLY when this object is disposed
        /// if you need to stop the TCP/IP communications immediately, you must call Dispose() on this object (after which you cannot use it anymore)
        /// </summary>
        /// <param name="kduIpAddress">IP address of the KDU controller</param>
        /// <param name="loggerFactory">optional, pass to log info, warnings, and errors. pass NullLoggerFactory.Instance if not needed</param>
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx socket timeout (retransmission interval) with the KDU controller</param>
        public Kducer(IPAddress kduIpAddress, ILoggerFactory loggerFactory, int tcpRxTxTimeoutMs = defaultRxTxSocketTimeout)
        {
            if (kduIpAddress == null)
                throw new ArgumentNullException(nameof(kduIpAddress));

            asyncCommsCts = new CancellationTokenSource();
            kduLogger = loggerFactory.CreateLogger(typeof(Kducer));
            this.kduIpAddress = kduIpAddress;
            this.tcpRxTxTimeoutMs = tcpRxTxTimeoutMs;
            mbClient = new ReducedModbusTcpClientAsync(kduIpAddress, tcpRxTxTimeoutMs);
            StartAsyncComms();
        }
        /// <summary>
        /// istantiates a Kducer and starts async communications with the KDU controller
        /// the underlying TCP/IP connection with the KDU starts automatically after instantiating this object
        /// you can confirm/await the connection via the corresponding IsConnected methods
        /// if the TCP/IP connection drops, this object will automatically reattempt to connect indefinitely
        /// TCP/IP communications are stopped ONLY when this object is disposed
        /// if you need to stop the TCP/IP communications immediately, you must call Dispose() on this object (after which you cannot use it anymore)
        /// </summary>
        /// <param name="kduIpAddress">IP address of the KDU controller</param>
        /// <param name="loggerFactory">optional, pass to log info, warnings, and errors. pass NullLoggerFactory.Instance if not needed</param>
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx timeout/interval for individual Modbus TCP exchanges with the KDU controller</param>
        public Kducer(string kduIpAddress, ILoggerFactory loggerFactory, int tcpRxTxTimeoutMs = defaultRxTxSocketTimeout) :
            this(IPAddress.Parse(kduIpAddress), loggerFactory, tcpRxTxTimeoutMs) { }

        /// <summary>
        /// istantiates a Kducer and starts async communications with the KDU controller
        /// the underlying TCP/IP connection with the KDU starts automatically after instantiating this object
        /// you can confirm/await the connection via the corresponding IsConnected methods
        /// if the TCP/IP connection drops, this object will automatically reattempt to connect indefinitely
        /// TCP/IP communications are stopped ONLY when this object is disposed
        /// if you need to stop the TCP/IP communications immediately, you must call Dispose() on this object (after which you cannot use it anymore)
        /// </summary>
        /// <param name="kduIpAddress">IP address of the KDU controller</param>
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx timeout/interval for individual Modbus TCP exchanges with the KDU controller</param>
        public Kducer(string kduIpAddress, int tcpRxTxTimeoutMs = defaultRxTxSocketTimeout) :
            this(IPAddress.Parse(kduIpAddress), NullLoggerFactory.Instance, tcpRxTxTimeoutMs)
        { }
        /// <summary>
        /// istantiates a Kducer and starts async communications with the KDU controller
        /// the underlying TCP/IP connection with the KDU starts automatically after instantiating this object
        /// you can confirm/await the connection via the corresponding IsConnected methods
        /// if the TCP/IP connection drops, this object will automatically reattempt to connect indefinitely
        /// TCP/IP communications are stopped ONLY when this object is disposed
        /// if you need to stop the TCP/IP communications immediately, you must call Dispose() on this object (after which you cannot use it anymore)
        /// </summary>
        /// <param name="kduIpAddress">IP address of the KDU controller</param>
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx timeout/interval for individual Modbus TCP exchanges with the KDU controller</param>
        public Kducer(IPAddress kduIpAddress, int tcpRxTxTimeoutMs = defaultRxTxSocketTimeout) :
            this(kduIpAddress, NullLoggerFactory.Instance, tcpRxTxTimeoutMs)
        { }
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
        /// <summary>
        /// note: after istantiating the Kducer object, it may take a few hundred milliseconds to establish the connection
        /// returns true if the KDU is connected, false otherwise
        /// </summary>
        /// <returns>true if the KDU is connected, false otherwise</returns>
        public bool IsConnected() { return mbClient.Connected(); }
        /// <summary>
        /// this function awaits up to the provided time (timeoutMs) for the KDU to be connected
        /// </summary>
        /// <param name="timeoutMs">milliseconds to wait before returning false, if the kdu is still not connected</param>
        /// <returns>the task returns true as soon as the KDU is connected, false if it's still not connected after the timeout expires</returns>
        public async Task<bool> IsConnectedWithTimeoutAsync(int timeoutMs = 1000)
        {
            int cnt = 0;
            while (mbClient.Connected() == false)
            {
                cnt += POLL_INTERVAL_MS;
                if (cnt > timeoutMs)
                {
                    return false;
                }
                await Task.Delay(POLL_INTERVAL_MS, asyncCommsCts.Token).ConfigureAwait(false);
            }
            return true;
        }
        /// <summary>
        /// this function blocks up to the provided time (timeoutMs) waiting for the KDU to be connected
        /// </summary>
        /// <param name="timeoutMs">milliseconds to wait before returning false, if the kdu is still not connected</param>
        /// <returns>true as soon as the KDU is connected, false if it's still not connected after the timeout expires</returns>
        public bool IsConnectedWithTimeoutBlocking(int timeoutMs = 1000)
        {
            int cnt = 0;
            while (mbClient.Connected() == false)
            {
                cnt += POLL_INTERVAL_MS;
                if (cnt > timeoutMs)
                {
                    return false;
                }
                Thread.Sleep(POLL_INTERVAL_MS);
            }
            return true;
        }

        private void StartAsyncComms()
        {
            asyncComms = AsyncCommsLoop();
        }

        private async Task AsyncCommsLoop()
        {
            await Task.Delay(POLL_INTERVAL_MS, asyncCommsCts.Token).ConfigureAwait(false);

            while (true)
            {
                try
                {
                    uint largeResultsQueueWarningThreshold = 10;
                    await mbClient.ConnectAsync().ConfigureAwait(false);

                    asyncCommsCts.Token.ThrowIfCancellationRequested();

                    byte[] kduVersion = await mbClient.ReadInputRegistersAsync(IR_CONTROLLER_SOFTWARE_VERSION.addr, IR_CONTROLLER_SOFTWARE_VERSION.count).ConfigureAwait(false);
                    string kduVersionStr = ModbusByteConversions.ModbusBytesToAsciiString(kduVersion, 0, 20);
                    kduSoftwareVersion = ushort.Parse(kduVersionStr.Split('.')[3].Substring(0,2)); // "KDU-1A vM.00.38" => 38

                    await mbClient.ReadInputRegistersAsync(IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false); // clear new result flag if already present

                    while (true)
                    {
                        asyncCommsCts.Token.ThrowIfCancellationRequested();

                        Task interval = Task.Delay(POLL_INTERVAL_MS, asyncCommsCts.Token);
                        Task commsTask;

                        if (userCmdTaskQueue.TryDequeue(out KduUserCmdTaskAsync userCmd))
                        {
                            commsTask = userCmd.ProcessUserCmdTaskAsync(mbClient);
                        }
                        else
                        {
                            commsTask = KduAsyncOperationTasks.CheckAndEnqueueKduResult(resultsQueue, mbClient, lockScrewdriverUntilGetResult, lockScrewdriverIndefinitelyAfterResult, replaceResultTimestampWithLocalTimestamp, asyncCommsCts.Token);
                        }

                        await Task.WhenAll(interval, commsTask).ConfigureAwait(false);

                        if (resultsQueue.Count >= largeResultsQueueWarningThreshold)
                        {
                            kduLogger.LogWarning("There are {NumberOfKducerTighteningResult} accumulated in the FIFO tightening results queue. Did you forget to dispose this Kducer object?", resultsQueue.Count);
                            largeResultsQueueWarningThreshold *= 10;
                        }
                    }
                }
                catch (Exception quitTask) when (quitTask is OperationCanceledException || quitTask is ObjectDisposedException)
                {
                    mbClient.Dispose();
                    return;
                }
                catch (SocketException tcpError)
                {
                    if (asyncCommsCts.Token.IsCancellationRequested)
                        return;
                    kduLogger.LogWarning(tcpError, "TCP connection error. Async communications will continue and reattempt.");
                    mbClient.Dispose();
                    mbClient = new ReducedModbusTcpClientAsync(kduIpAddress, tcpRxTxTimeoutMs);
                }
                catch (ModbusException modbusError)
                {
                    if (asyncCommsCts.Token.IsCancellationRequested)
                        return;
                    kduLogger.LogWarning(modbusError, "KDU replied with a Modbus exception. Async communications will continue but the modbus command will NOT be reattempted!");
                }
                catch (Exception e)
                {
                    try
                    {
                        asyncCommsCts.Token.ThrowIfCancellationRequested();
                    }
                    catch (Exception quitTask) when (quitTask is OperationCanceledException || quitTask is ObjectDisposedException)
                    {
                        mbClient.Dispose();
                        return;
                    }
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
        /// Returns and removes a tightening result from the FIFO results queue
        /// </summary>
        /// <returns>a KducerTighteningResult object (from a FIFO queue) from which you can obtain data about the tightening result</returns>
        /// <exception cref="InvalidOperationException">If there are no results available</exception>
        public KducerTighteningResult GetResult()
        {
            if (resultsQueue.TryDequeue(out KducerTighteningResult res) == true)
                return res;
            else
            {
                InvalidOperationException e = new InvalidOperationException("There are no KDU results available");
                kduLogger.LogWarning(e, "GetResult was called but the FIFO results queue was empty");
                throw e;
            }
        }

        /// <summary>
        /// Creates a task that returns and removes a tightening result from the FIFO results queue. The task awaits until there is a result to return.
        /// The Kducer instance automatically attempts to reconnect if the TCP connection drops.
        /// In case the connection drops, you can decide whether this method should wait indefinitely to reconnect, or throw an exception, via throwExceptionIfKduConnectionDrops
        /// </summary>
        /// <param name="cancellationToken">to cancel this task. use CancellationToken.None if not needed.</param>
        /// <param name="throwExceptionIfKduConnectionDrops">if true and the TCP connection to the KDU drops, this method throws an exception. If false, this method waits indefinitely until the KDU reconnects</param>
        /// <exception cref="SocketException">if throwExceptionIfKduConnectionDrops is true and the TCP connection drops, this exception will be thrown</exception>
        /// <returns>a KducerTighteningResult object (from a FIFO queue) from which you can obtain data about the tightening result</returns>
        public async Task<KducerTighteningResult> GetResultAsync(CancellationToken cancellationToken, bool throwExceptionIfKduConnectionDrops = false)
        {
            CancellationTokenSource cancelGetResult = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, asyncCommsCts.Token);
            CancellationToken cancel = cancelGetResult.Token;

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
                await Task.Delay(POLL_INTERVAL_MS, cancel).ConfigureAwait(false);
                if (throwExceptionIfKduConnectionDrops == true)
                {
                    if (mbClient.Connected() == false)
                        throw new SocketException();
                }
            }
        }

        /// <summary>
        /// Selects a program on the KDU and waits 300ms to ensure the KDU program is loaded.
        /// </summary>
        /// <param name="kduProgramNumber">The program number to select. 1 to 64 or 1 to 200 depending on the KDU model and firmware version.</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SelectProgramNumberAsync(ushort kduProgramNumber)
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SetProgram, asyncCommsCts.Token, kduProgramNumber);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the program number currently active in the KDU
        /// </summary>
        /// <returns>the program number</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<ushort> GetProgramNumberAsync()
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetProgramNumber, asyncCommsCts.Token);
            return await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Selects a sequence on the KDU and waits 300ms to ensure the KDU sequence is loaded.
        /// </summary>
        /// <param name="kduSequenceNumber">The sequence number to select. 1 to 8 or 1 to 24 depending on the KDU model and firmware version.</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SelectSequenceNumberAsync(ushort kduSequenceNumber)
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SetSequence, asyncCommsCts.Token, kduSequenceNumber);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the sequence number currently active in the KDU
        /// </summary>
        /// <returns>the sequence number</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<ushort> GetSequenceNumberAsync()
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetSequence, asyncCommsCts.Token);
            return await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Disables the screwdriver lever by setting the "Stop Motor ON" state on the KDU controller
        /// </summary>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task DisableScrewdriver()
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.StopMotorOn, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Enables the screwdriver lever by setting the "Stop Motor OFF" state on the KDU controller
        /// This is necessary to re-enable the lever after calling DisableScrewdriver, or when lockScrewdriverIndefinitelyAfterResult is true
        /// </summary>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task EnableScrewdriver() 
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.StopMotorOff, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs screwdriver until the tightening completes according to the KDU parameters of the currently selected program
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns>The KducerTighteningResult object from which you can obtain data about the tightening result</returns>
        public async Task<KducerTighteningResult> RunScrewdriverUntilResultAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource cancelRunScrewdriver = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, asyncCommsCts.Token);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.RunScrewdriver, cancelRunScrewdriver.Token, 0, replaceResultTimestampWithLocalTimestamp);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
            cancelRunScrewdriver.Dispose();
            return userCmdTask.tighteningResult;
        }

        /// <summary>
        /// Reads the program data of the program currently active in the KDU
        /// </summary>
        /// <returns>the program data</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<KducerTighteningProgram> GetActiveTighteningProgramDataAsync()
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetActiveTighteningProgramData, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.singleTighteningProgram;
        }

        /// <summary>
        /// Reads the program data of the program currently active in the KDU
        /// </summary>
        /// <returns>the program data</returns>
        /// <param name="programNumber">program number for which to get data</param>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<KducerTighteningProgram> GetTighteningProgramDataAsync(ushort programNumber)
        {
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetTighteningProgramData, asyncCommsCts.Token, programNumber);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.singleTighteningProgram;
        }

        /// <summary>
        /// Reads the program data of the all the tightening programs in the KDU
        /// </summary>
        /// <returns>the dictionary of (program number, program data) pairs</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<Dictionary<ushort, KducerTighteningProgram>> GetAllTighteningProgramDataAsync()
        {
            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetAllTighteningProgramsData, asyncCommsCts.Token, kduSoftwareVersion);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.multipleTighteningPrograms;
        }

        /// <summary></summary>
        /// <returns>the numeric version of the mainboard of the KDU</returns>
        public async Task<ushort> GetKduMainboardVersion()
        {
            while (kduSoftwareVersion == 0) // version is obtained in the main async comms loop
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            return kduSoftwareVersion;
        }

        /// <summary>
        /// Sends a new tightening program to the program number selected.
        /// Note: there is no parameter data validation in KducerTighteningProgram, however the controller may refuse the data with a ModbusException if illegal values are sent.
        /// For controllers v38 and later, by default the data is saved in volatile memory only, the previous data is restored on reboot and when entering the configuration menu on the controller.
        /// Saving in permanent memory is much slower and should only be used if really needed. The memory can wear out after several million writing cycles.
        /// </summary>
        /// <param name="writeInPermanentMemory">default false. use true only where necessary. not applicable for controllers v37 and earlier (ask kolver for a free update)</param>
        /// <param name="programNumberToSet">The program number to overwrite with the new data. </param>
        /// <param name="newTighteningProgram">The new program data to write</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentException">If the program number is 0, or >64 for controller v37, or >200 for controller >v38.</exception>
        public async Task SendNewProgramDataAsync(ushort programNumberToSet, KducerTighteningProgram newTighteningProgram, bool writeInPermanentMemory = false)
        {
            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask;
            if (kduSoftwareVersion < 38)
            {
                if (programNumberToSet == 0 || programNumberToSet > 64)
                    throw new ArgumentException($"Program number for controller version {kduSoftwareVersion} must be 1-64. (ask kolver for a free update)", nameof(programNumberToSet));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendTighteningProgramDataV37, asyncCommsCts.Token, programNumberToSet);
            }
            else
            {
                if (programNumberToSet == 0 || programNumberToSet > 200)
                    throw new ArgumentException($"Program number for controller version {kduSoftwareVersion} must be 1-200.", nameof(programNumberToSet));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendTighteningProgramDataV38, asyncCommsCts.Token, programNumberToSet);
            }

            userCmdTask.singleTighteningProgram = newTighteningProgram;
            userCmdTask.writeHoldingRegistersInPermanentMemory = writeInPermanentMemory;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends multiple tightening programs in a row. This is much faster than multiple calls to SendNewProgramDataAsync because there's no forced wait in between programs.
        /// Note: there is no parameter data validation in KducerTighteningProgram, however the controller may refuse the data with a ModbusException if illegal values are sent.
        /// For controllers v38 and later, by default the data is saved in volatile memory only, the previous data is restored on reboot and when entering the configuration menu on the controller.
        /// Saving in permanent memory is much slower and should only be used if really needed. The memory can wear out after several million writing cycles.
        /// </summary>
        /// <param name="dictionaryProgramNrKeyProgramDataValue">a dictionary of program numbers and corresponding KducerTighteningProgram data to write</param>
        /// <param name="writeInPermanentMemory">default false. use true only where necessary. not applicable for controllers v37 and earlier (ask kolver for a free update)</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentException">If the program number is 0, or >64 for controller v37, or >200 for controller >v38.</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task SendMultipleNewProgramsDataAsync(Dictionary<ushort, KducerTighteningProgram> dictionaryProgramNrKeyProgramDataValue, bool writeInPermanentMemory = false)
        {
            if (dictionaryProgramNrKeyProgramDataValue == null)
                throw new ArgumentNullException(nameof(dictionaryProgramNrKeyProgramDataValue));

            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask;
            if (kduSoftwareVersion < 38)
            {
                foreach (ushort prNr in dictionaryProgramNrKeyProgramDataValue.Keys)
                    if (prNr == 0 || prNr > 64)
                        throw new ArgumentException($"Program number for controller version {kduSoftwareVersion} must be 1-64. (ask kolver for a free update)", nameof(dictionaryProgramNrKeyProgramDataValue));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendMultipleTighteningProgramsDataV37, asyncCommsCts.Token);
            }
            else
            {
                foreach (ushort prNr in dictionaryProgramNrKeyProgramDataValue.Keys)
                    if (prNr == 0 || prNr > 200)
                        throw new ArgumentException($"Program number for controller version {kduSoftwareVersion} must be 1-200.", nameof(dictionaryProgramNrKeyProgramDataValue));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendMultipleTighteningProgramsDataV38, asyncCommsCts.Token);
            }

            userCmdTask.multipleTighteningPrograms = dictionaryProgramNrKeyProgramDataValue;
            userCmdTask.writeHoldingRegistersInPermanentMemory = writeInPermanentMemory;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a barcode to the KDU controller
        /// The barcode will appear in the KDU controller screen. It is not necessary to activate a barcode mode in the KDU controller for this to work.
        /// The barcode will be associated with the tightening results for the currently selected program or sequence
        /// The barcode type set by this command is "serial number". If the program or sequence already has a barcode identifier, it will remain unchanged, but this serial number barcode is the one that will appear in the CSV results
        /// For more information on barcode modes, see the KDU-1A user manual
        /// Changing program or sequence after sending the barcode will reset the barcode to an empty string
        /// </summary>
        /// <param name="barcode">string up to 16 ASCII characters to send. Commas ',' will be replaced with dots '.' due to CSV data format for results. An empty barcode (empty string "") is converted to a single space (workaround).</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentException">If length of the barcode string is more than 16 ASCII characters</exception>
        public async Task SendBarcodeAsync(string barcode)
        {
            if (string.IsNullOrEmpty(barcode))
                barcode = " ";
            else if (barcode.Length > 16)
                throw new ArgumentException($"Maximum barcode length is 16 characters, barcode provided was: {barcode}", nameof(barcode));

            barcode = barcode.Replace(',', '.');

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendBarcode, asyncCommsCts.Token);
            userCmdTask.barcode = barcode;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a new sequence to the sequence number selected.
        /// the controller may refuse the data with a ModbusException if illegal values are sent.
        /// For controllers v38 and later, by default the data is saved in volatile memory only, the previous data is restored on reboot and when entering the configuration menu on the controller.
        /// Saving in permanent memory is much slower and should only be used if really needed. The memory can wear out after several million writing cycles.
        /// </summary>
        /// <param name="writeInPermanentMemory">default false. use true only where necessary. not applicable for controllers v37 and earlier (ask kolver for a free update)</param>
        /// <param name="sequenceNumberToSet">1 = A, 2 = B, etc</param>
        /// <param name="newSequence">The new data to write</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentException">If the sequence number is 0, or >8 for controller v37, or >24 for controller >v38.</exception>
        public async Task SendNewSequenceDataAsync(ushort sequenceNumberToSet, KducerSequenceOfTighteningPrograms newSequence, bool writeInPermanentMemory = false)
        {
            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask;
            if (kduSoftwareVersion < 38)
            {
                if (sequenceNumberToSet == 0 || sequenceNumberToSet > 8)
                    throw new ArgumentException($"Sequence number for controller version {kduSoftwareVersion} must be 1-8. (ask kolver for a free update)", nameof(sequenceNumberToSet));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendSequenceDataV37, asyncCommsCts.Token, sequenceNumberToSet);
            }
            else
            {
                if (sequenceNumberToSet == 0 || sequenceNumberToSet > 24)
                    throw new ArgumentException($"Sequence number for controller version {kduSoftwareVersion} must be 1-24.", nameof(sequenceNumberToSet));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendSequenceDataV38, asyncCommsCts.Token, sequenceNumberToSet);
            }

            userCmdTask.singleSequence = newSequence;
            userCmdTask.writeHoldingRegistersInPermanentMemory = writeInPermanentMemory;
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends multiple sequences in a row. This is much faster than multiple calls to SendNewSequenceDataAsync because there's no forced wait in between sequences.
        /// the controller may refuse the data with a ModbusException if illegal values are sent.
        /// For controllers v38 and later, by default the data is saved in volatile memory only, the previous data is restored on reboot and when entering the configuration menu on the controller.
        /// Saving in permanent memory is much slower and should only be used if really needed. The memory can wear out after several million writing cycles.
        /// </summary>
        /// <param name="dictionarySequenceNrKeySequenceDataValue">a dictionary of program numbers and corresponding KducerTighteningProgram data to write</param>
        /// <param name="writeInPermanentMemory">default false. use true only where necessary. not applicable for controllers v37 and earlier (ask kolver for a free update)</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentException">If the sequence number is 0, or >8 for controller v37, or >24 for controller >v38.</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task SendMultipleNewSequencesDataAsync(Dictionary<ushort, KducerSequenceOfTighteningPrograms> dictionarySequenceNrKeySequenceDataValue, bool writeInPermanentMemory = false)
        {
            if (dictionarySequenceNrKeySequenceDataValue == null)
                throw new ArgumentNullException(nameof(dictionarySequenceNrKeySequenceDataValue));

            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask;
            if (kduSoftwareVersion < 38)
            {
                foreach (ushort seqNr in dictionarySequenceNrKeySequenceDataValue.Keys)
                    if (seqNr == 0 || seqNr > 8)
                        throw new ArgumentException($"Sequence number for controller version {kduSoftwareVersion} must be 1-8. (ask kolver for a free update)", nameof(dictionarySequenceNrKeySequenceDataValue));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendMultipleSequencesDataV37, asyncCommsCts.Token);
            }
            else
            {
                foreach (ushort seqNr in dictionarySequenceNrKeySequenceDataValue.Keys)
                    if (seqNr == 0 || seqNr > 24)
                        throw new ArgumentException($"Sequence number for controller version {kduSoftwareVersion} must be 1-24.", nameof(dictionarySequenceNrKeySequenceDataValue));
                userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendMultipleSequencesDataV38, asyncCommsCts.Token);
            }

            userCmdTask.multipleSequences = dictionarySequenceNrKeySequenceDataValue;
            userCmdTask.writeHoldingRegistersInPermanentMemory = writeInPermanentMemory;
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the sequence data from the desired sequence number
        /// </summary>
        /// <returns>the sequence data</returns>
        /// <param name="sequenceNumber">sequence number for which to get data</param>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<KducerSequenceOfTighteningPrograms> GetSequenceDataAsync(ushort sequenceNumber)
        {
            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetSequenceData, asyncCommsCts.Token, sequenceNumber);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.singleSequence;
        }

        /// <summary>
        /// Reads the sequence data of the program currently active in the KDU
        /// </summary>
        /// <returns>the sequence data</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<KducerSequenceOfTighteningPrograms> GetActiveSequenceDataAsync()
        {
            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetActiveSequenceData, asyncCommsCts.Token);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.singleSequence;
        }

        /// <summary>
        /// Reads the sequence data of the all the sequence in the KDU
        /// </summary>
        /// <returns>the dictionary of (sequence number, sequence data) pairs</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<Dictionary<ushort, KducerSequenceOfTighteningPrograms>> GetAllSequencesDataAsync()
        {
            while (kduSoftwareVersion == 0)
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetAllSequencesData, asyncCommsCts.Token, kduSoftwareVersion);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.multipleSequences;
        }

        private async Task EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(KduUserCmdTaskAsync userCmdTask)
        {
            userCmdTaskQueue.Enqueue(userCmdTask);
            while (!userCmdTask.completed)
                await Task.Delay(POLL_INTERVAL_MS, asyncCommsCts.Token).ConfigureAwait(false);
            if (userCmdTask.exceptionThrownInAsyncTask != null)
            {
                if (userCmdTask.exceptionThrownInAsyncTask is ModbusException ex && ex.GetModbusExceptionCode() == 6)
                    throw new ModbusException("KDU replied with a Modbus busy exception so the desired command was not processed.", 6);
                else
                    throw userCmdTask.exceptionThrownInAsyncTask;
            }
        }

        private async Task<ushort> EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(KduUserCmdTaskAsync userCmdTask)
        {
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.result;
        }

        private enum UserCmd
        {
            None,
            SetProgram,
            GetProgramNumber,
            SetSequence,
            GetSequence,
            StopMotorOn,
            StopMotorOff,
            RunScrewdriver,
            GetActiveTighteningProgramData,
            GetTighteningProgramData,
            GetAllTighteningProgramsData,
            SendTighteningProgramDataV37,
            SendTighteningProgramDataV38,
            SendMultipleTighteningProgramsDataV37,
            SendMultipleTighteningProgramsDataV38,
            SendBarcode,
            GetActiveSequenceData,
            GetSequenceData,
            GetAllSequencesData,
            SendSequenceDataV37,
            SendSequenceDataV38,
            SendMultipleSequencesDataV37,
            SendMultipleSequencesDataV38
        }

        private class KduUserCmdTaskAsync
        {
            private readonly UserCmd cmd;
            private readonly ushort payload;
            private readonly CancellationToken cancellationToken;
            internal ushort result;
            internal bool completed;
            internal bool replaceResultTimestampWithLocalTimestamp;
            internal bool writeHoldingRegistersInPermanentMemory;
            internal Exception exceptionThrownInAsyncTask;
            internal KducerTighteningResult tighteningResult;
            internal KducerTighteningProgram singleTighteningProgram;
            internal Dictionary<ushort, KducerTighteningProgram> multipleTighteningPrograms;
            internal ushort kduVersion;
            internal KducerSequenceOfTighteningPrograms singleSequence;
            internal Dictionary<ushort, KducerSequenceOfTighteningPrograms> multipleSequences;
            internal string barcode;

            internal KduUserCmdTaskAsync(UserCmd cmd, CancellationToken cancellationToken, ushort payload = 0, bool replaceResultTimestampWithLocalTimestamp = true)
            {
                this.cmd = cmd;
                this.cancellationToken = cancellationToken;
                this.payload = payload;
                this.replaceResultTimestampWithLocalTimestamp = replaceResultTimestampWithLocalTimestamp;
            }

            internal async Task ProcessUserCmdTaskAsync(ReducedModbusTcpClientAsync mbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    switch (cmd)
                    {
                        case UserCmd.GetProgramNumber:
                            result = await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false);
                            break;

                        case UserCmd.SetProgram:
                            await KduAsyncOperationTasks.SetProgram(mbClient, payload, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetSequence:
                            result = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);
                            break;

                        case UserCmd.SetSequence:
                            await KduAsyncOperationTasks.SetSequence(mbClient, payload, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.StopMotorOn:
                            await KduAsyncOperationTasks.SetStopMotorState(mbClient, true, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.StopMotorOff:
                            await KduAsyncOperationTasks.SetStopMotorState(mbClient, false, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.RunScrewdriver:
                            tighteningResult = await KduAsyncOperationTasks.RunScrewdriver(mbClient, replaceResultTimestampWithLocalTimestamp, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetTighteningProgramData:
                            singleTighteningProgram = await KduAsyncOperationTasks.GetTighteningProgramData(mbClient, payload).ConfigureAwait(false);
                            break;

                        case UserCmd.GetActiveTighteningProgramData:
                            singleTighteningProgram = await KduAsyncOperationTasks.GetTighteningProgramData(mbClient, await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false)).ConfigureAwait(false);
                            break;

                        case UserCmd.GetAllTighteningProgramsData:
                            multipleTighteningPrograms = await KduAsyncOperationTasks.GetAllProgramsData(mbClient, payload, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendTighteningProgramDataV37:
                            await KduAsyncOperationTasks.SendTighteningProgramV37(mbClient, payload, singleTighteningProgram, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendTighteningProgramDataV38:
                            await KduAsyncOperationTasks.SendTighteningProgramV38(mbClient, payload, singleTighteningProgram, writeHoldingRegistersInPermanentMemory, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendMultipleTighteningProgramsDataV37:
                            await KduAsyncOperationTasks.SendMultipleTighteningProgramsV37(mbClient, multipleTighteningPrograms, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendMultipleTighteningProgramsDataV38:
                            await KduAsyncOperationTasks.SendMultipleTighteningProgramsV38(mbClient, multipleTighteningPrograms, writeHoldingRegistersInPermanentMemory, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendBarcode:
                            await KduAsyncOperationTasks.SendBarcode(mbClient, barcode).ConfigureAwait(false);
                            break;

                        case UserCmd.GetActiveSequenceData:
                            singleSequence = await KduAsyncOperationTasks.GetSequenceData(mbClient, await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false), kduVersion).ConfigureAwait(false);
                            break;

                        case UserCmd.GetSequenceData:
                            singleSequence = await KduAsyncOperationTasks.GetSequenceData(mbClient, payload, kduVersion).ConfigureAwait(false);
                            break;

                        case UserCmd.SendSequenceDataV37:
                            await KduAsyncOperationTasks.SendSequenceOfTighteningProgramsV37(mbClient, payload, singleSequence, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendSequenceDataV38:
                            await KduAsyncOperationTasks.SendSequenceOfTighteningProgramsV38(mbClient, payload, singleSequence, writeHoldingRegistersInPermanentMemory, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendMultipleSequencesDataV37:
                            await KduAsyncOperationTasks.SendMultipleSequencesV37(mbClient, multipleSequences, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendMultipleSequencesDataV38:
                            await KduAsyncOperationTasks.SendMultipleSequencesV38(mbClient, multipleSequences, writeHoldingRegistersInPermanentMemory, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetAllSequencesData:
                            multipleSequences = await KduAsyncOperationTasks.GetAllSequencesData(mbClient, kduVersion, cancellationToken).ConfigureAwait(false);
                            break;

                    }
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
        }

        private static class KduAsyncOperationTasks
        {
            internal static async Task CheckAndEnqueueKduResult(ConcurrentQueue<KducerTighteningResult> resultsQueue, ReducedModbusTcpClientAsync mbClient, bool lockScrewdriverUntilResultsProcessed, bool lockScrewdriverIndefinitelyAfterResult, bool replaceResultTimestampWithLocalTimestamp, CancellationToken cancellationToken)
            {
                byte newResultFlag = (await mbClient.ReadInputRegistersAsync(Kducer.IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false))[1];
                cancellationToken.ThrowIfCancellationRequested();

                if (newResultFlag == 1)
                {
                    byte[] resultInputRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_RESULT_DATA.addr, Kducer.IR_RESULT_DATA.count).ConfigureAwait(false);
                    byte[] torqueGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_TORQUEGRAPH_DATA.addr, Kducer.IR_TORQUEGRAPH_DATA.count).ConfigureAwait(false);
                    byte[] angleGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_ANGLEGRAPH_DATA.addr, Kducer.IR_ANGLEGRAPH_DATA.count).ConfigureAwait(false);
                    resultsQueue.Enqueue(new KducerTighteningResult(resultInputRegisters, replaceResultTimestampWithLocalTimestamp, new KducerTorqueAngleTimeGraph(torqueGraphRegisters, angleGraphRegisters)));
                    cancellationToken.ThrowIfCancellationRequested();

                    if (lockScrewdriverUntilResultsProcessed || lockScrewdriverIndefinitelyAfterResult)
                    {
                        await mbClient.WriteSingleCoilAsync(Kducer.COIL_STOP_MOTOR, true).ConfigureAwait(false);
                        await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (!lockScrewdriverIndefinitelyAfterResult && lockScrewdriverUntilResultsProcessed && resultsQueue.IsEmpty)
                {
                    await mbClient.WriteSingleCoilAsync(Kducer.COIL_STOP_MOTOR, false).ConfigureAwait(false);
                    await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                }
            }

            internal static async Task<ushort> GetActiveProgramNumber(ReducedModbusTcpClientAsync mbClient)
            {
                byte[] prNr = await mbClient.ReadHoldingRegistersAsync(Kducer.HR_PROGRAM_NR, 1).ConfigureAwait(false);
                return prNr[1];
            }

            internal static async Task SetProgram(ReducedModbusTcpClientAsync mbClient, ushort prNr, CancellationToken cancellationToken)
            {
                if ((await mbClient.ReadHoldingRegistersAsync(Kducer.HR_PROGRAM_NR, 1).ConfigureAwait(false))[1] == prNr)
                    return;
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, prNr).ConfigureAwait(false);
                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task<ushort> GetActiveSequenceNumber(ReducedModbusTcpClientAsync mbClient)
            {
                byte[] seqNr = await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_NR, 1).ConfigureAwait(false);
                return seqNr[1];
            }

            internal static async Task SetSequence(ReducedModbusTcpClientAsync mbClient, ushort seqNr, CancellationToken cancellationToken)
            {
                if ((await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_NR, 1).ConfigureAwait(false))[1] == seqNr)
                    return;
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, seqNr).ConfigureAwait(false);
                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SetStopMotorState(ReducedModbusTcpClientAsync mbClient, bool stopMotorState, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleCoilAsync(Kducer.COIL_STOP_MOTOR, stopMotorState).ConfigureAwait(false);
                await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task<KducerTighteningResult> RunScrewdriver(ReducedModbusTcpClientAsync mbClient, bool replaceResultTimestampWithLocalTimestamp, CancellationToken cancellationToken)
            {
                await mbClient.ReadInputRegistersAsync(Kducer.IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false);
                while (!cancellationToken.IsCancellationRequested && (await mbClient.ReadInputRegistersAsync(Kducer.IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false))[1] == 0)
                {
                    await mbClient.WriteSingleCoilAsync(Kducer.COIL_REMOTE_LEVER, true).ConfigureAwait(false);
                    await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                }
                await mbClient.WriteSingleCoilAsync(Kducer.COIL_REMOTE_LEVER, false).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                byte[] resultInputRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_RESULT_DATA.addr, Kducer.IR_RESULT_DATA.count).ConfigureAwait(false);
                byte[] torqueGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_TORQUEGRAPH_DATA.addr, Kducer.IR_TORQUEGRAPH_DATA.count).ConfigureAwait(false);
                byte[] angleGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_ANGLEGRAPH_DATA.addr, Kducer.IR_ANGLEGRAPH_DATA.count).ConfigureAwait(false);
                return new KducerTighteningResult(resultInputRegisters, replaceResultTimestampWithLocalTimestamp, new KducerTorqueAngleTimeGraph(torqueGraphRegisters, angleGraphRegisters));
            }

            internal static async Task<KducerTighteningProgram> GetTighteningProgramData(ReducedModbusTcpClientAsync mbClient, ushort programNumber)
            {
                byte[] programData;
                if (programNumber <= 64)
                    programData = await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (programNumber - 1)), 115).ConfigureAwait(false);
                else
                {
                    ushort activeProgram = await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false);

                    if (activeProgram != programNumber)
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, programNumber).ConfigureAwait(false);
                    
                    programData = await mbClient.ReadHoldingRegistersAsync(Kducer.HR_PROGRAM_DATA_65_TO_200, 115).ConfigureAwait(false);

                    if (activeProgram != programNumber)
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, activeProgram).ConfigureAwait(false);
                }
                return new KducerTighteningProgram(programData);
            }

            internal static async Task SendTighteningProgramV38(ReducedModbusTcpClientAsync mbClient, ushort programNumber, KducerTighteningProgram tighteningProgram, bool writeToPermanentMemory, CancellationToken cancellationToken)
            {
                if (writeToPermanentMemory)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, programNumber).ConfigureAwait(false);
                await mbClient.WriteMultipleRegistersAsync(Kducer.HR_PROGRAM_DATA_65_TO_200, tighteningProgram.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SendTighteningProgramV37(ReducedModbusTcpClientAsync mbClient, ushort programNumber, KducerTighteningProgram tighteningProgram, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);
                await mbClient.WriteMultipleRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (programNumber - 1)), tighteningProgram.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SendMultipleTighteningProgramsV38(ReducedModbusTcpClientAsync mbClient, Dictionary<ushort, KducerTighteningProgram> multipleTighteningPrograms, bool writeToPermanentMemory, CancellationToken cancellationToken)
            {
                if (writeToPermanentMemory)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);

                foreach (KeyValuePair<ushort, KducerTighteningProgram> kvp in multipleTighteningPrograms)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, kvp.Key).ConfigureAwait(false);
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_PROGRAM_DATA_65_TO_200, kvp.Value.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);
                }

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SendMultipleTighteningProgramsV37(ReducedModbusTcpClientAsync mbClient, Dictionary<ushort, KducerTighteningProgram> multipleTighteningPrograms, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);

                foreach (KeyValuePair<ushort, KducerTighteningProgram> kvp in multipleTighteningPrograms)
                {
                    await mbClient.WriteMultipleRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (kvp.Key - 1)), kvp.Value.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task<Dictionary<ushort, KducerTighteningProgram>> GetAllProgramsData(ReducedModbusTcpClientAsync mbClient, ushort kduVersion, CancellationToken cancellationToken)
            {
                Dictionary<ushort, KducerTighteningProgram> allTighteningPrograms = new Dictionary<ushort, KducerTighteningProgram>();

                if (kduVersion < 38)
                {
                    for (ushort programNumber = 1; programNumber <= 64; programNumber++)
                    {
                        allTighteningPrograms.Add(programNumber, new KducerTighteningProgram(await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (programNumber - 1)), 115).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    return allTighteningPrograms;
                }

                ushort activeProgram = await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false);

                for (ushort programNumber = 1; programNumber <= 200; programNumber++)
                {
                    if (programNumber <= 64)
                    {
                        allTighteningPrograms.Add(programNumber, new KducerTighteningProgram(await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (programNumber - 1)), 115).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    else
                    {
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, programNumber).ConfigureAwait(false);
                        allTighteningPrograms.Add(programNumber, new KducerTighteningProgram(await mbClient.ReadHoldingRegistersAsync(Kducer.HR_PROGRAM_DATA_65_TO_200, 115).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, activeProgram).ConfigureAwait(false);

                return allTighteningPrograms;
            }

            internal static async Task SendBarcode(ReducedModbusTcpClientAsync mbClient, string barcode)
            {
                byte[] barcode_bytes = new byte[16];
                char[] barcode_chars;

                if (barcode.Length > 16)
                    barcode_chars = barcode.Substring(0, 16).ToCharArray();
                else
                    barcode_chars = barcode.ToCharArray();

                Encoding.ASCII.GetBytes(barcode_chars, 0, barcode_chars.Length, barcode_bytes, 0);

                await mbClient.WriteMultipleRegistersAsync(Kducer.HR_BARCODE, barcode_bytes).ConfigureAwait(false);
            }

            internal static async Task<KducerSequenceOfTighteningPrograms> GetSequenceData(ReducedModbusTcpClientAsync mbClient, ushort sequenceNumber, ushort kduVersion)
            {
                byte[] sequenceData;
                if (kduVersion <= 37)
                    sequenceData = await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_SEQUENCE_DATA_1_TO_8 + 48 * (sequenceNumber - 1)), 32).ConfigureAwait(false);
                else
                {
                    ushort activeSequence = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);

                    if (activeSequence != sequenceNumber)
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, sequenceNumber).ConfigureAwait(false);

                    sequenceData = await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, 56).ConfigureAwait(false);

                    if (activeSequence != sequenceNumber)
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, activeSequence).ConfigureAwait(false);
                }
                return new KducerSequenceOfTighteningPrograms(sequenceData);
            }

            internal static async Task SendSequenceOfTighteningProgramsV38(ReducedModbusTcpClientAsync mbClient, ushort sequenceNumber, KducerSequenceOfTighteningPrograms sequenceOfPrograms, bool writeToPermanentMemory, CancellationToken cancellationToken)
            {
                if (writeToPermanentMemory)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, sequenceNumber).ConfigureAwait(false);
                await mbClient.WriteMultipleRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, sequenceOfPrograms.getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater()).ConfigureAwait(false);

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SendSequenceOfTighteningProgramsV37(ReducedModbusTcpClientAsync mbClient, ushort sequenceNumber, KducerSequenceOfTighteningPrograms sequenceOfPrograms, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);
                await mbClient.WriteMultipleRegistersAsync((ushort)(Kducer.HR_SEQUENCE_DATA_1_TO_8 + 48 * (sequenceNumber - 1)), sequenceOfPrograms.getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior()).ConfigureAwait(false);
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SendMultipleSequencesV38(ReducedModbusTcpClientAsync mbClient, Dictionary<ushort, KducerSequenceOfTighteningPrograms> multipleSequences, bool writeToPermanentMemory, CancellationToken cancellationToken)
            {
                if (writeToPermanentMemory)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);

                foreach (KeyValuePair<ushort, KducerSequenceOfTighteningPrograms> kvp in multipleSequences)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, kvp.Key).ConfigureAwait(false);
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, kvp.Value.getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater()).ConfigureAwait(false);
                }

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SendMultipleSequencesV37(ReducedModbusTcpClientAsync mbClient, Dictionary<ushort, KducerSequenceOfTighteningPrograms> multipleSequences, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);

                foreach (KeyValuePair<ushort, KducerSequenceOfTighteningPrograms> kvp in multipleSequences)
                {
                    await mbClient.WriteMultipleRegistersAsync((ushort)(Kducer.HR_SEQUENCE_DATA_1_TO_8 + 48 * (kvp.Key - 1)), kvp.Value.getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior()).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task<Dictionary<ushort, KducerSequenceOfTighteningPrograms>> GetAllSequencesData(ReducedModbusTcpClientAsync mbClient, ushort kduVersion, CancellationToken cancellationToken)
            {
                Dictionary<ushort, KducerSequenceOfTighteningPrograms> allSequences = new Dictionary<ushort, KducerSequenceOfTighteningPrograms>();

                if (kduVersion < 38)
                {
                    for (ushort sequenceNumber = 1; sequenceNumber <= 8; sequenceNumber++)
                    {
                        allSequences.Add(sequenceNumber, new KducerSequenceOfTighteningPrograms(await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_SEQUENCE_DATA_1_TO_8 + 48 * (sequenceNumber - 1)), 32).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    return allSequences;
                }

                ushort activeSequence = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);

                for (ushort sequenceNumber = 1; sequenceNumber <= 24; sequenceNumber++)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, sequenceNumber).ConfigureAwait(false);
                    allSequences.Add(sequenceNumber, new KducerSequenceOfTighteningPrograms(await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, 56).ConfigureAwait(false)));
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, activeSequence).ConfigureAwait(false);

                return allSequences;
            }
        }

        /// <summary>
        /// Always call Dispose when finished using Kducer, to stop the background Modbus TCP communication loop
        /// </summary>
        public void Dispose()
        {
            mbClient.Dispose();
            asyncCommsCts.Dispose();
        }
    }

}
