// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kolver
{
#pragma warning disable CA1063 // the dispose implementation is correct
#pragma warning disable CA1816 // the dispose implementation is correct
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
        private static readonly (ushort addr, ushort count) IR_KTLS_POSITIONS = (393, 9);
        private const ushort COIL_STOP_MOTOR = 34;
        private const ushort COIL_REMOTE_LEVER = 32;
        private const ushort HR_PROGRAM_NR = 7372;
        private const ushort HR_SEQUENCE_NR = 7371;
        private const ushort HR_PROGRAM_DATA_1_TO_64 = 0;
        private const ushort HR_PROGRAM_DATA_65_TO_200 = 7790;
        private const ushort HR_SEQUENCE_DATA_1_TO_8 = 7405;
        private const ushort HR_SEQUENCE_DATA_9_TO_24 = 7950;
        private const ushort HR_GENERALSETTINGS_V37 = 7361;
        private const ushort HR_GENERALSETTINGS = 7906;
        private const ushort HR_SEQUENCE_PROGRAM_MODE = 7374;
        private const ushort HR_PERMANENT_MEMORY_REPROGRAM = 7789;
        private const ushort HR_BARCODE = 7379;
        private const ushort HR_SPECIAL_RXALLCONFDATA = 8016;
        private const ushort HR_GENERALSETTINGS_ADDITIONAL_V40 = 8026;
        private const ushort HR_SPECIAL_EXTENDEDGRAPHS = 8015;
        private static readonly (ushort addr, ushort count) HR_DATE_TIME = (8007, 6);

        private readonly IPAddress kduIpAddress;
        private readonly int tcpRxTxTimeoutMs;

        private bool lockScrewdriverUntilGetResult;
        private bool lockScrewdriverIndefinitelyAfterResult;
        private bool replaceResultTimestampWithLocalTimestamp = true;
        private bool highResGraphsEnabled = false;

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
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx socket timeout (retransmission interval) with the KDU controller</param>
        public Kducer(IPAddress kduIpAddress, int tcpRxTxTimeoutMs = defaultRxTxSocketTimeout)
        {
            if (kduIpAddress == null)
                throw new ArgumentNullException(nameof(kduIpAddress));

            asyncCommsCts = new CancellationTokenSource();
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
        /// <param name="tcpRxTxTimeoutMs">tcp tx/rx timeout/interval for individual Modbus TCP exchanges with the KDU controller</param>
        public Kducer(string kduIpAddress, int tcpRxTxTimeoutMs = defaultRxTxSocketTimeout) :
            this(IPAddress.Parse(kduIpAddress), tcpRxTxTimeoutMs) { }

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

                    if (kduSoftwareVersion == 38 || kduSoftwareVersion == 39)
                    {
                        await mbClient.WriteSingleRegisterAsync(HR_SPECIAL_EXTENDEDGRAPHS, 0).ConfigureAwait(false);  // disable extended graphs, not needed in other KDU versions (automatically disabled on new connections)
                    }

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
                            commsTask = KduAsyncOperationTasks.CheckAndEnqueueKduResult(resultsQueue, mbClient, lockScrewdriverUntilGetResult, lockScrewdriverIndefinitelyAfterResult, replaceResultTimestampWithLocalTimestamp, highResGraphsEnabled, asyncCommsCts.Token);
                        }

                        await Task.WhenAll(interval, commsTask).ConfigureAwait(false);

                        if (resultsQueue.Count >= largeResultsQueueWarningThreshold)
                        {
                            //kduLogger.LogWarning("There are {NumberOfKducerTighteningResult} accumulated in the FIFO tightening results queue. Did you forget to dispose this Kducer object?", resultsQueue.Count);
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
                    //kduLogger.LogWarning(tcpError, "TCP connection error. Async communications will continue and reattempt.");
                    mbClient.Dispose();
                    mbClient = new ReducedModbusTcpClientAsync(kduIpAddress, tcpRxTxTimeoutMs);
                }
                catch (ModbusException modbusError)
                {
                    if (asyncCommsCts.Token.IsCancellationRequested)
                        return;
                    //kduLogger.LogWarning(modbusError, "KDU replied with a Modbus exception. Async communications will continue but the modbus command will NOT be reattempted!");
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
                    //kduLogger.LogCritical(e, "Unexpected exception. Async communications will stop!");
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
                //kduLogger.LogWarning(e, "GetResult was called but the FIFO results queue was empty");
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

            try
            {
                while (true)
                {
                    if (!resultsQueue.IsEmpty)
                    {
                        if (resultsQueue.TryDequeue(out KducerTighteningResult res) == true)
                            return res;
                    }
                    await Task.Delay(POLL_INTERVAL_MS, cancel).ConfigureAwait(false);
                    if (throwExceptionIfKduConnectionDrops == true)
                    {
                        if (mbClient.Connected() == false)
                            throw new SocketException(10057); // not connected
                    }
                }
            }
            finally
            {
                cancelGetResult.Dispose();
            }
        }

        /// <summary>
        /// Clears the FIFO tightening result queue.
        /// Note: GetResult and GetResultAsync already remove the result from the queue.
        /// Use ClearResultsQueue only if you want to discard all tightening results that you have not retrieved via GetResult or GetResultAsync.
        /// This method is equivalent to calling GetResult() while HasNewResult() is true
        /// </summary>
        public void ClearResultsQueue()
        {
            // ConcurrentQueue.Clear() is not available on .NET Standard 2.0
            while (!resultsQueue.IsEmpty)
                resultsQueue.TryDequeue(out KducerTighteningResult _);
        }

        /// <summary>
        /// Selects a program on the KDU and waits 300ms to ensure the KDU program is loaded. Note: if the KDU is in sequence mode, this command will also set the KDU to program mode
        /// </summary>
        /// <param name="kduProgramNumber">The program number to select. 1 to 64 or 1 to 200 depending on the KDU model and firmware version.</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="InvalidOperationException">For KDU v37 only, if REMOTE PROG setting is not set to CN5 TCP</exception>
        public async Task SelectProgramNumberAsync(ushort kduProgramNumber)
        {
            await SetToProgramModeAsync().ConfigureAwait(false);
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
        /// Selects a sequence on the KDU and waits 300ms to ensure the KDU sequence is loaded. Note: if the KDU is in program mode, this command will also set the KDU to sequence mode
        /// </summary>
        /// <param name="kduSequenceNumber">The sequence number to select. 1 to 8 or 1 to 24 depending on the KDU model and firmware version.</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="InvalidOperationException">For KDU v37 only, if REMOTE SEQ setting is not set to CN5 TCP</exception>
        public async Task SelectSequenceNumberAsync(ushort kduSequenceNumber)
        {
            await SetToSequenceModeAsync().ConfigureAwait(false);
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
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<KducerTighteningResult> RunScrewdriverUntilResultAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource cancelRunScrewdriver = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, asyncCommsCts.Token);

            try
            {
                KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.RunScrewdriver, cancelRunScrewdriver.Token, (ushort)(highResGraphsEnabled ? 1 : 0), replaceResultTimestampWithLocalTimestamp);
                await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
                return userCmdTask.tighteningResult;
            }
            finally
            {
                cancelRunScrewdriver.Dispose();
            }
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
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
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
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        public async Task<Dictionary<ushort, KducerTighteningProgram>> GetAllTighteningProgramDataAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetAllTighteningProgramsData, asyncCommsCts.Token);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.multipleTighteningPrograms;
        }

        /// <summary></summary>
        /// <returns>the numeric version of the mainboard of the KDU</returns>
        /// <exception cref="SocketException">If the KDU is not connected (15 seconds timeout)</exception>
        public async Task<ushort> GetKduMainboardVersionAsync()
        {
            int cnt = 0;
            while (kduSoftwareVersion == 0) // version is obtained in the main async comms loop
            {
                cnt += (POLL_INTERVAL_MS * 2);
                if (cnt > 15000)
                {
                    throw new SocketException(10057); // not connected
                }
                await Task.Delay(POLL_INTERVAL_MS * 2, asyncCommsCts.Token).ConfigureAwait(false);
            }
            return kduSoftwareVersion;
        }

        private async Task WaitForKduMainboardVersion()
        {
            await GetKduMainboardVersionAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a new tightening program to the program number selected. Note: the new program number is NOT automatically selected!
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
            await WaitForKduMainboardVersion().ConfigureAwait(false);

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

            await WaitForKduMainboardVersion().ConfigureAwait(false);

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
            await WaitForKduMainboardVersion().ConfigureAwait(false);

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

            await WaitForKduMainboardVersion().ConfigureAwait(false);

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
            await WaitForKduMainboardVersion().ConfigureAwait(false);

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
            await WaitForKduMainboardVersion().ConfigureAwait(false);

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
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        public async Task<Dictionary<ushort, KducerSequenceOfTighteningPrograms>> GetAllSequencesDataAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetAllSequencesData, asyncCommsCts.Token, kduSoftwareVersion);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.multipleSequences;
        }

        /// <summary>
        /// Reads the general settings data of the KDU controller
        /// </summary>
        /// <returns>KducerControllerGeneralSettings object representing the general settings data</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<KducerControllerGeneralSettings> GetGeneralSettingsDataAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetGeneralSettingsData, asyncCommsCts.Token);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.generalSettings;
        }

        /// <summary>
        /// Reads the 8 status bits (first 8 modbus coils). These correspond to CN3 output pins 43 through 36:  [ LEVER, FORWARD MOTOR ON, SCREW OK, SCREW NOK, END PROGRAM, END SEQUENCE, STOP MOTOR, READY ]
        /// </summary>
        /// <returns>bool[8] array with the bit values: [ LEVER, FORWARD MOTOR ON, SCREW OK, SCREW NOK, END PROGRAM, END SEQUENCE, STOP MOTOR, READY ]</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<bool[]> GetCn3OutputBitsAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetCoils, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.bits;
        }

        /// <summary>
        /// Reads the 20 input bits (first 20 modbus discrete inputs), these correspond to CN3 input pins 1 through 20
        /// </summary>
        /// <returns>bool[8] array with the bit values: [ LEVER, FORWARD MOTOR ON, SCREW OK, SCREW NOK, END PROGRAM, END SEQUENCE, STOP MOTOR, READY ]</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<bool[]> GetCn3InputBitsAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetDiscreteInputs, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.bits;
        }

        /// <summary>
        /// Writes the general settings data of the KDU controller
        /// </summary>
        /// <param name="writeInPermanentMemory">default false. use true only where necessary. not applicable for controllers v37 and earlier (ask kolver for a free update)</param>
        /// <param name="newGeneralSettingsData">The new data to write</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task SendGeneralSettingsDataAsync(KducerControllerGeneralSettings newGeneralSettingsData, bool writeInPermanentMemory = false)
        {
            if (newGeneralSettingsData == null)
                throw new ArgumentNullException(nameof(newGeneralSettingsData));

            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendGeneralSettingsData, asyncCommsCts.Token);
            userCmdTask.kduVersion = kduSoftwareVersion;
            userCmdTask.writeHoldingRegistersInPermanentMemory = writeInPermanentMemory;
            userCmdTask.generalSettings = newGeneralSettingsData;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads all settings, programs, and sequences from a KDU controller
        /// </summary>
        /// <returns>KducerControllerGeneralSettings object representing the general settings data</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>>> GetSettingsAndProgramsAndSequencesDataAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetSettingsAndProgramsAndSequencesData, asyncCommsCts.Token);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.settingsAndProgramsAndSequencesTuple;
        }

        /// <summary>
        /// Writes the general settings, programs, and sequences data. IP address and protocol data is not written. This method is faster than sending the settings, programs, and sequences separately.
        /// </summary>
        /// <param name="writeInPermanentMemory">default false. use true only where necessary. not applicable for controllers v37 and earlier (ask kolver for a free update)</param>
        /// <param name="kduFileDataTuple">The new data to write (general settings, programs, sequences), coming from the KducerKduDataFileReader class</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task SendAllSettingsProgramsAndSequencesDataAsync(Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> kduFileDataTuple, bool writeInPermanentMemory = false)
        {
            if (kduFileDataTuple == null || kduFileDataTuple.Item1 == null || kduFileDataTuple.Item2 == null || kduFileDataTuple.Item3 == null)
                throw new ArgumentNullException(nameof(kduFileDataTuple));

            await WaitForKduMainboardVersion().ConfigureAwait(false);

            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SendSettingsAndProgramsAndSequencesData, asyncCommsCts.Token);
            userCmdTask.kduVersion = kduSoftwareVersion;
            userCmdTask.writeHoldingRegistersInPermanentMemory = writeInPermanentMemory;
            userCmdTask.settingsAndProgramsAndSequencesTuple = kduFileDataTuple;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the KDU to program mode (opposite of SetToSequenceModeAsync)
        /// </summary>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SetToProgramModeAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SetSequenceOrProgramMode, asyncCommsCts.Token, 0);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the KDU to sequence mode (opposite of SetToProgramModeAsync)
        /// </summary>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SetToSequenceModeAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SetSequenceOrProgramMode, asyncCommsCts.Token, 1);
            userCmdTask.kduVersion = kduSoftwareVersion;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the real time positions and the stored target position for the next screwof the K-TLS arm
        /// </summary>
        /// <returns> Tuple of (ushort[3], ushort[3], ushort[3]). First array contains the positions of Arm 1. Second array contains the positions of Arm 2. Third array contains the target positions for the next screw, if saved. A value of 65535 (0xFFFF) indicates an invalid value (sensor not connected or position not memorized). The units are mm for length, tenths of degrees for angles.</returns>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        /// <exception cref="InvalidOperationException">if KDU version is &lt; 40, as K-TLS support was introduced with KDU v40</exception>
        public async Task<Tuple<ushort[], ushort[], ushort[]>> GetKtlsPositionsAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);
            if (kduSoftwareVersion < 40)
                throw new InvalidOperationException($"KDU version is {kduSoftwareVersion}, update to version 40 or newer to get K-TLS functionality.");
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetKtlsPositions, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopWithResultAsync(userCmdTask).ConfigureAwait(false);
            ushort[] arm1Positions = new ushort[3];
            ushort[] arm2Positions = new ushort[3];
            ushort[] targetPositions = new ushort[3];
            for (int i = 0; i < 3; i++)
            {
                arm1Positions[i] = userCmdTask.ktlsPositions[i];
                arm2Positions[i] = userCmdTask.ktlsPositions[i + 3];
                targetPositions[i] = userCmdTask.ktlsPositions[i + 6];
            }
            return new Tuple<ushort[],ushort[],ushort[]>(arm1Positions, arm2Positions, targetPositions);
        }

        /// <summary>
        /// Set high resolution graph mode (disabled by default)
        /// </summary>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SetHighResGraphModeAsync(bool enableHighResGraphs)
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);
            if (kduSoftwareVersion < 38)
                throw new InvalidOperationException($"KDU version is {kduSoftwareVersion}, update to version 38 or newer to get high res graph functionality.");
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SetHighResGraphMode, asyncCommsCts.Token, (ushort)(enableHighResGraphs ? 1 : 0));
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
            highResGraphsEnabled = enableHighResGraphs;
        }

        /// <summary>
        /// Gets the date time of the controller
        /// </summary>
        /// <returns>A DateTime object representing the date-time set on the controller</returns>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task<DateTime> GetDateTimeAsync()
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);
            if (kduSoftwareVersion < 39)
                throw new InvalidOperationException($"KDU version is {kduSoftwareVersion}, update to version 39 or newer to get and set the datetime via this library.");
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.GetDateTime, asyncCommsCts.Token);
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
            return userCmdTask.dateTime;
        }

        /// <summary>
        /// Sets the date time of the controller
        /// </summary>
        /// <param name="newDateTime">A DateTime object representing the date-time to set on the controller</param>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SetDateTimeAsync(DateTime newDateTime)
        {
            await WaitForKduMainboardVersion().ConfigureAwait(false);
            if (kduSoftwareVersion < 39)
                throw new InvalidOperationException($"KDU version is {kduSoftwareVersion}, update to version 39 or newer to get and set the datetime via this library.");
            KduUserCmdTaskAsync userCmdTask = new KduUserCmdTaskAsync(UserCmd.SetDateTime, asyncCommsCts.Token);
            userCmdTask.dateTime = newDateTime;
            await EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(userCmdTask).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the date time of the controller, same as calling SetDateTimeAsync(DateTime.Now)
        /// </summary>
        /// <exception cref="ModbusException">If KDU received the command but was unable to process it, for example if the KDU configuration menu is open on the touch screen</exception>
        /// <exception cref="SocketException">If the KDU disconnected in the middle of processing the command</exception>
        public async Task SetDateTimeToNowAsync()
        {
            await SetDateTimeAsync(DateTime.Now).ConfigureAwait(false);
        }

        private async Task EnqueueAndWaitForUserCmdToBeProcessedInAsyncCommsLoopAsync(KduUserCmdTaskAsync userCmdTask)
        {
            userCmdTaskQueue.Enqueue(userCmdTask);
            while (!userCmdTask.completed)
            {
                await Task.Delay(POLL_INTERVAL_MS, asyncCommsCts.Token).ConfigureAwait(false);
                if (userCmdTask.exceptionThrownInAsyncTask != null)
                {
                    if (userCmdTask.exceptionThrownInAsyncTask is ModbusException ex && ex.GetModbusExceptionCode() == 6)
                        throw new ModbusException("KDU replied with a Modbus busy exception so the desired command was not processed.", 6);
                    else
                        throw userCmdTask.exceptionThrownInAsyncTask;
                }
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
            SendMultipleSequencesDataV38,
            GetGeneralSettingsData,
            SendGeneralSettingsData,
            GetSettingsAndProgramsAndSequencesData,
            SendSettingsAndProgramsAndSequencesData,
            SetSequenceOrProgramMode,
            GetCoils,
            GetDiscreteInputs,
            GetKtlsPositions,
            SetHighResGraphMode,
            GetDateTime,
            SetDateTime
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
            internal KducerControllerGeneralSettings generalSettings;
            internal Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> settingsAndProgramsAndSequencesTuple;
            internal bool[] bits;
            internal ushort[] ktlsPositions;
            internal DateTime dateTime;

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
                            tighteningResult = await KduAsyncOperationTasks.RunScrewdriver(mbClient, replaceResultTimestampWithLocalTimestamp, payload > 0, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetTighteningProgramData:
                            singleTighteningProgram = await KduAsyncOperationTasks.GetTighteningProgramData(mbClient, payload).ConfigureAwait(false);
                            break;

                        case UserCmd.GetActiveTighteningProgramData:
                            singleTighteningProgram = await KduAsyncOperationTasks.GetTighteningProgramData(mbClient, await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false)).ConfigureAwait(false);
                            break;

                        case UserCmd.GetAllTighteningProgramsData:
                            multipleTighteningPrograms = await KduAsyncOperationTasks.GetAllProgramsData(mbClient, kduVersion, cancellationToken).ConfigureAwait(false);
                            if (kduVersion == 38 || kduVersion == 39)
                                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
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
                            if (kduVersion == 38 || kduVersion == 39)
                                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetGeneralSettingsData:
                            generalSettings = await KduAsyncOperationTasks.GetGeneralSettings(mbClient, kduVersion).ConfigureAwait(false);
                            break;

                        case UserCmd.SendGeneralSettingsData:
                            await KduAsyncOperationTasks.SendGeneralSettings(mbClient, generalSettings, kduVersion, writeHoldingRegistersInPermanentMemory, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetSettingsAndProgramsAndSequencesData:
                            settingsAndProgramsAndSequencesTuple = await KduAsyncOperationTasks.GetSettingsAndProgramsAndSequencesData(mbClient, kduVersion, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SendSettingsAndProgramsAndSequencesData:
                            await KduAsyncOperationTasks.SendSettingsAndProgramsAndSequencesData(mbClient, settingsAndProgramsAndSequencesTuple, kduVersion, writeHoldingRegistersInPermanentMemory, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.SetSequenceOrProgramMode:
                            await KduAsyncOperationTasks.SetSequenceOrProgramMode(mbClient, payload, kduVersion, cancellationToken).ConfigureAwait(false);
                            break;

                        case UserCmd.GetCoils:
                            bits = await mbClient.ReadCoilsAsync(0, 8).ConfigureAwait(false);
                            break;

                        case UserCmd.GetDiscreteInputs:
                            bits = await mbClient.ReadDiscreteInputsAsync(0, 20).ConfigureAwait(false);
                            break;

                        case UserCmd.GetKtlsPositions:
                            ktlsPositions = await KduAsyncOperationTasks.GetKtlsPositions(mbClient).ConfigureAwait(false);
                            break;

                        case UserCmd.SetHighResGraphMode:
                            await KduAsyncOperationTasks.SetHighResGraphMode(mbClient, payload).ConfigureAwait(false);
                            break;

                        case UserCmd.GetDateTime:
                            dateTime = await KduAsyncOperationTasks.GetDateTime(mbClient).ConfigureAwait(false);
                            break;

                        case UserCmd.SetDateTime:
                            await KduAsyncOperationTasks.SetDateTime(mbClient, dateTime).ConfigureAwait(false);
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
                catch (Exception e)
                {
                    exceptionThrownInAsyncTask = e;
                    throw;
                }

                completed = true;
            }
        }

        private static class KduAsyncOperationTasks
        {
            internal static async Task CheckAndEnqueueKduResult(ConcurrentQueue<KducerTighteningResult> resultsQueue, ReducedModbusTcpClientAsync mbClient, bool lockScrewdriverUntilResultsProcessed, bool lockScrewdriverIndefinitelyAfterResult, bool replaceResultTimestampWithLocalTimestamp, bool highResGraphsEnabled, CancellationToken cancellationToken)
            {
                byte newResultFlag = (await mbClient.ReadInputRegistersAsync(Kducer.IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false))[1];
                cancellationToken.ThrowIfCancellationRequested();

                if (newResultFlag == 1)
                {
                    byte[] angleGraphBytesHighRes = null, torqueGraphBytesHighRes = null;
                    if (highResGraphsEnabled)
                        (torqueGraphBytesHighRes, angleGraphBytesHighRes) = await KduAsyncOperationTasks.GetHighResGraphsByteArrays(mbClient, cancellationToken).ConfigureAwait(false);

                    byte[] resultInputRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_RESULT_DATA.addr, Kducer.IR_RESULT_DATA.count).ConfigureAwait(false);
                    byte[] torqueGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_TORQUEGRAPH_DATA.addr, Kducer.IR_TORQUEGRAPH_DATA.count).ConfigureAwait(false);
                    byte[] angleGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_ANGLEGRAPH_DATA.addr, Kducer.IR_ANGLEGRAPH_DATA.count).ConfigureAwait(false);
                    resultsQueue.Enqueue(new KducerTighteningResult(resultInputRegisters, replaceResultTimestampWithLocalTimestamp, new KducerTorqueAngleTimeGraph(torqueGraphRegisters, angleGraphRegisters, torqueGraphBytesHighRes, angleGraphBytesHighRes)));
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
                if ((await mbClient.ReadHoldingRegistersAsync(Kducer.HR_PROGRAM_NR, 1).ConfigureAwait(false))[1] != prNr)
                    throw new InvalidOperationException("Program selection failed. Make sure the KDU is not in SEQUENCE mode and the KDU \"REMOTE PROG\" setting is set to \"CN5 TCP\"");    // this only happens in KDU v37 and prior (without a corresponding modbus exception)
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
                if ((await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_NR, 1).ConfigureAwait(false))[1] != seqNr)
                    throw new InvalidOperationException("Sequence selection failed. Make sure the KDU is in SEQUENCE mode and the KDU \"REMOTE SEQ\" setting set to \"CN5 TCP\"");    // this only happens in KDU v37 and prior (without a corresponding modbus exception)
            }

            internal static async Task SetSequenceOrProgramMode(ReducedModbusTcpClientAsync mbClient, ushort mode, ushort kduVersion, CancellationToken cancellationToken)
            {
                if ((await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_PROGRAM_MODE, 1).ConfigureAwait(false))[1] == mode)
                    return;

                if (kduVersion < 38)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);
                }
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_PROGRAM_MODE, mode).ConfigureAwait(false);
                if (kduVersion < 38)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
                    await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task SetStopMotorState(ReducedModbusTcpClientAsync mbClient, bool stopMotorState, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleCoilAsync(Kducer.COIL_STOP_MOTOR, stopMotorState).ConfigureAwait(false);
                await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
            }

            internal static async Task<Tuple<byte[],byte[]>> GetHighResGraphsByteArrays(ReducedModbusTcpClientAsync mbClient, CancellationToken cancellationToken)
            {
                byte[] angleGraphBytesHighRes = null, torqueGraphBytesHighRes = null;
                await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false); // takes at least 50ms to stream graphs, might as well sleep
                byte[] numBytesToRx_asBytes = await mbClient.SocketExposed_ReceiveAllAsync(4).ConfigureAwait(false); // first, receive 4 bytes (uint32_t) telling us how many bytes KDU is going to send
                uint numBytesToBeRecv;
                if (BitConverter.IsLittleEndian)
                    numBytesToBeRecv = ((uint)numBytesToRx_asBytes[3] << 24) | ((uint)numBytesToRx_asBytes[2] << 16) | ((uint)numBytesToRx_asBytes[1] << 8) | numBytesToRx_asBytes[0];
                else
                    numBytesToBeRecv = ((uint)numBytesToRx_asBytes[0] << 24) | ((uint)numBytesToRx_asBytes[1] << 16) | ((uint)numBytesToRx_asBytes[2] << 8) | numBytesToRx_asBytes[3];
                if (numBytesToBeRecv > 0)
                {
                    angleGraphBytesHighRes = await mbClient.SocketExposed_ReceiveAllAsync((int)numBytesToBeRecv / 2).ConfigureAwait(false);
                    torqueGraphBytesHighRes = await mbClient.SocketExposed_ReceiveAllAsync((int)numBytesToBeRecv / 2).ConfigureAwait(false);
                    if (numBytesToBeRecv != (angleGraphBytesHighRes.Length + torqueGraphBytesHighRes.Length))
                        throw new InvalidOperationException($"Failed to get high res torque and angle graph data from KDU-1A at {mbClient.kduIpAddress}. Expected {numBytesToBeRecv} bytes, received {angleGraphBytesHighRes.Length + torqueGraphBytesHighRes.Length}");
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new Tuple<byte[], byte[]>(torqueGraphBytesHighRes, angleGraphBytesHighRes);
            }

            internal static async Task<KducerTighteningResult> RunScrewdriver(ReducedModbusTcpClientAsync mbClient, bool replaceResultTimestampWithLocalTimestamp, bool highResGraphsEnabled, CancellationToken cancellationToken)
            {
                byte[] angleGraphBytesHighRes = null, torqueGraphBytesHighRes = null;
                byte new_result_flag_to_clear = (await mbClient.ReadInputRegistersAsync(Kducer.IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false))[1];
                if (highResGraphsEnabled && new_result_flag_to_clear != 0)
                    await GetHighResGraphsByteArrays(mbClient, cancellationToken).ConfigureAwait(false);

                while (!cancellationToken.IsCancellationRequested && (await mbClient.ReadInputRegistersAsync(Kducer.IR_NEW_RESULT_SELFCLEARING_FLAG, 1).ConfigureAwait(false))[1] == 0)
                {
                    await mbClient.WriteSingleCoilAsync(Kducer.COIL_REMOTE_LEVER, true).ConfigureAwait(false);
                    await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (highResGraphsEnabled)
                    (torqueGraphBytesHighRes, angleGraphBytesHighRes) = await GetHighResGraphsByteArrays(mbClient, cancellationToken).ConfigureAwait(false);

                await mbClient.WriteSingleCoilAsync(Kducer.COIL_REMOTE_LEVER, false).ConfigureAwait(false);
                byte[] resultInputRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_RESULT_DATA.addr, Kducer.IR_RESULT_DATA.count).ConfigureAwait(false);
                byte[] torqueGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_TORQUEGRAPH_DATA.addr, Kducer.IR_TORQUEGRAPH_DATA.count).ConfigureAwait(false);
                byte[] angleGraphRegisters = await mbClient.ReadInputRegistersAsync(Kducer.IR_ANGLEGRAPH_DATA.addr, Kducer.IR_ANGLEGRAPH_DATA.count).ConfigureAwait(false);
                return new KducerTighteningResult(resultInputRegisters, replaceResultTimestampWithLocalTimestamp, new KducerTorqueAngleTimeGraph(torqueGraphRegisters, angleGraphRegisters, torqueGraphBytesHighRes, angleGraphBytesHighRes));
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

                ushort activeProgram = await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false);

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, programNumber).ConfigureAwait(false);
                await mbClient.WriteMultipleRegistersAsync(Kducer.HR_PROGRAM_DATA_65_TO_200, tighteningProgram.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);

                if (activeProgram != programNumber)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, activeProgram).ConfigureAwait(false);

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
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
                    if (cancellationToken.IsCancellationRequested)
                    {
                        if (writeToPermanentMemory) await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
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
                Dictionary<ushort, KducerTighteningProgram> allTighteningPrograms;

                if (kduVersion < 38)
                {
                    allTighteningPrograms = new Dictionary<ushort, KducerTighteningProgram>();
                    for (ushort programNumber = 1; programNumber <= 64; programNumber++)
                    {
                        allTighteningPrograms.Add(programNumber, new KducerTighteningProgram(await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (programNumber - 1)), 115).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                else if (kduVersion < 40)
                {
                    allTighteningPrograms = new Dictionary<ushort, KducerTighteningProgram>();
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
                }
                else
                {
                    Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> parsedData = await DownloadEntireConfFromKduV40(mbClient, cancellationToken).ConfigureAwait(false);
                    allTighteningPrograms = parsedData.Item2;
                }

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

                ushort activeSequence = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, sequenceNumber).ConfigureAwait(false);
                await mbClient.WriteMultipleRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, sequenceOfPrograms.getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater()).ConfigureAwait(false);

                if (activeSequence != sequenceNumber)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, activeSequence).ConfigureAwait(false);

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
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

                ushort activeSequence = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);

                foreach (KeyValuePair<ushort, KducerSequenceOfTighteningPrograms> kvp in multipleSequences)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, kvp.Key).ConfigureAwait(false);
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, kvp.Value.getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater()).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        if (writeToPermanentMemory) await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, activeSequence).ConfigureAwait(false);

                if (writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
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
                Dictionary<ushort, KducerSequenceOfTighteningPrograms> allSequences;

                if (kduVersion < 38)
                {
                    allSequences = new Dictionary<ushort, KducerSequenceOfTighteningPrograms>();
                    for (ushort sequenceNumber = 1; sequenceNumber <= 8; sequenceNumber++)
                    {
                        allSequences.Add(sequenceNumber, new KducerSequenceOfTighteningPrograms(await mbClient.ReadHoldingRegistersAsync((ushort)(Kducer.HR_SEQUENCE_DATA_1_TO_8 + 48 * (sequenceNumber - 1)), 32).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                else if (kduVersion < 40)
                {
                    allSequences = new Dictionary<ushort, KducerSequenceOfTighteningPrograms>();
                    ushort activeSequence = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);
                    for (ushort sequenceNumber = 1; sequenceNumber <= 24; sequenceNumber++)
                    {
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, sequenceNumber).ConfigureAwait(false);
                        allSequences.Add(sequenceNumber, new KducerSequenceOfTighteningPrograms(await mbClient.ReadHoldingRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, 56).ConfigureAwait(false)));
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, activeSequence).ConfigureAwait(false);
                }
                else
                {
                    Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> parsedData = await DownloadEntireConfFromKduV40(mbClient, cancellationToken).ConfigureAwait(false);
                    allSequences = parsedData.Item3;
                }

                return allSequences;
            }

            internal static async Task<KducerControllerGeneralSettings> GetGeneralSettings(ReducedModbusTcpClientAsync mbClient, ushort kduVersion)
            {
                if (kduVersion <= 37)
                    return new KducerControllerGeneralSettings(await mbClient.ReadHoldingRegistersAsync(Kducer.HR_GENERALSETTINGS_V37, 39).ConfigureAwait(false));
                else if (kduVersion == 38)
                    return new KducerControllerGeneralSettings(await mbClient.ReadHoldingRegistersAsync(Kducer.HR_GENERALSETTINGS, 43).ConfigureAwait(false));
                else if (kduVersion == 39)
                    return new KducerControllerGeneralSettings(await mbClient.ReadHoldingRegistersAsync(Kducer.HR_GENERALSETTINGS, 44).ConfigureAwait(false));
                else// if (kduVersion == 40)
                {
                    byte[] concatenatedRegisters = new byte[92];
                    (await mbClient.ReadHoldingRegistersAsync(Kducer.HR_GENERALSETTINGS, 44).ConfigureAwait(false)).CopyTo(concatenatedRegisters, 0);
                    (await mbClient.ReadHoldingRegistersAsync(Kducer.HR_GENERALSETTINGS_ADDITIONAL_V40, 2).ConfigureAwait(false)).CopyTo(concatenatedRegisters, 88);
                    return new KducerControllerGeneralSettings(concatenatedRegisters);
                }
            }

            internal static async Task SendGeneralSettings(ReducedModbusTcpClientAsync mbClient, KducerControllerGeneralSettings kduGeneralSettings, ushort kduVersion, bool writeToPermanentMemory, CancellationToken cancellationToken)
            {
                if (kduVersion <= 37 || writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);
                }

                if (kduVersion <= 37)
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS_V37, kduGeneralSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior()).ConfigureAwait(false);
                else if (kduVersion == 38)
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS, kduGeneralSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38()).ConfigureAwait(false);
                else if (kduVersion == 39)
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS, kduGeneralSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39()).ConfigureAwait(false);
                else// if (kduVersion == 40)
                {
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS, kduGeneralSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39()).ConfigureAwait(false);
                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS_ADDITIONAL_V40, kduGeneralSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv40_SecondTrancheOnly()).ConfigureAwait(false);
                }

                if (kduVersion <= 37 || writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
                }
            }

            internal static async Task<Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>>> GetSettingsAndProgramsAndSequencesData(ReducedModbusTcpClientAsync mbClient, ushort kduVersion, CancellationToken cancellationToken)
            {
                if (kduVersion < 40)
                {
                    Dictionary<ushort, KducerTighteningProgram> programs = await GetAllProgramsData(mbClient, kduVersion, cancellationToken).ConfigureAwait(false);
                    Dictionary<ushort, KducerSequenceOfTighteningPrograms> sequences = await GetAllSequencesData(mbClient, kduVersion, cancellationToken).ConfigureAwait(false);
                    KducerControllerGeneralSettings settings = await GetGeneralSettings(mbClient, kduVersion).ConfigureAwait(false);
                    if (kduVersion > 37)
                        await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
                    return new Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>>(settings, programs, sequences);
                }
                else
                {
                    return await DownloadEntireConfFromKduV40(mbClient, cancellationToken).ConfigureAwait(false);
                }
            }

            internal static async Task SendSettingsAndProgramsAndSequencesData(ReducedModbusTcpClientAsync mbClient, Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> settingsProgramsSequences, ushort kduVersion, bool writeToPermanentMemory, CancellationToken cancellationToken)
            {
                if (kduVersion <= 37 || writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 1).ConfigureAwait(false);
                }

                if (kduVersion <= 37)
                {
                    foreach (KeyValuePair<ushort, KducerTighteningProgram> kvp in settingsProgramsSequences.Item2)
                    {
                        await mbClient.WriteMultipleRegistersAsync((ushort)(Kducer.HR_PROGRAM_DATA_1_TO_64 + 115 * (kvp.Key - 1)), kvp.Value.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    foreach (KeyValuePair<ushort, KducerSequenceOfTighteningPrograms> kvp in settingsProgramsSequences.Item3)
                    {
                        await mbClient.WriteMultipleRegistersAsync((ushort)(Kducer.HR_SEQUENCE_DATA_1_TO_8 + 48 * (kvp.Key - 1)), kvp.Value.getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior()).ConfigureAwait(false);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }

                    await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS_V37, settingsProgramsSequences.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior()).ConfigureAwait(false);
                }
                else
                {
                    ushort activeProgram = await KduAsyncOperationTasks.GetActiveProgramNumber(mbClient).ConfigureAwait(false);
                    foreach (KeyValuePair<ushort, KducerTighteningProgram> kvp in settingsProgramsSequences.Item2)
                    {
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, kvp.Key).ConfigureAwait(false);
                        await mbClient.WriteMultipleRegistersAsync(Kducer.HR_PROGRAM_DATA_65_TO_200, kvp.Value.getProgramModbusHoldingRegistersAsByteArray()).ConfigureAwait(false);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            if(writeToPermanentMemory) await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PROGRAM_NR, activeProgram).ConfigureAwait(false);

                    ushort activeSequence = await KduAsyncOperationTasks.GetActiveSequenceNumber(mbClient).ConfigureAwait(false);
                    foreach (KeyValuePair<ushort, KducerSequenceOfTighteningPrograms> kvp in settingsProgramsSequences.Item3)
                    {
                        await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, kvp.Key).ConfigureAwait(false);
                        await mbClient.WriteMultipleRegistersAsync(Kducer.HR_SEQUENCE_DATA_9_TO_24, kvp.Value.getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater()).ConfigureAwait(false);
                        if (cancellationToken.IsCancellationRequested)
                        {
                            if(writeToPermanentMemory) await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 0).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SEQUENCE_NR, activeSequence).ConfigureAwait(false);

                    if (kduVersion == 38)
                        await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS, settingsProgramsSequences.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38()).ConfigureAwait(false);
                    else if (kduVersion == 39)
                        await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS, settingsProgramsSequences.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39()).ConfigureAwait(false);
                    else// if (kduVersion == 40)
                    {
                        await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS, settingsProgramsSequences.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39()).ConfigureAwait(false);
                        await mbClient.WriteMultipleRegistersAsync(Kducer.HR_GENERALSETTINGS_ADDITIONAL_V40, settingsProgramsSequences.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv40_SecondTrancheOnly()).ConfigureAwait(false);
                    }
                }

                if (kduVersion <= 37 || writeToPermanentMemory)
                {
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_PERMANENT_MEMORY_REPROGRAM, 2).ConfigureAwait(false);
                    await Task.Delay(PERMANENT_MEMORY_REWRITE_WAIT, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false);
                }
            }

            internal static async Task<ushort[]> GetKtlsPositions(ReducedModbusTcpClientAsync mbClient)
            {
                byte[] positionsAsByteArr = await mbClient.ReadInputRegistersAsync(Kducer.IR_KTLS_POSITIONS.addr, Kducer.IR_KTLS_POSITIONS.count).ConfigureAwait(false);
                ushort[] ktlsPositions = new ushort[Kducer.IR_KTLS_POSITIONS.count];
                for (int i = 0; i < Kducer.IR_KTLS_POSITIONS.count; i++)
                    ktlsPositions[i] = ModbusByteConversions.TwoModbusBigendianBytesToUshort(positionsAsByteArr, i*2);
                return ktlsPositions;
            }

            internal static async Task SetHighResGraphMode(ReducedModbusTcpClientAsync mbClient, ushort mode)
            {
                if (mode > 0)
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SPECIAL_EXTENDEDGRAPHS, 1291).ConfigureAwait(false);
                else
                    await mbClient.WriteSingleRegisterAsync(Kducer.HR_SPECIAL_EXTENDEDGRAPHS, 0).ConfigureAwait(false);
            }

            private static async Task<Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>>> DownloadEntireConfFromKduV40(ReducedModbusTcpClientAsync mbClient, CancellationToken cancellationToken)
            {
                await mbClient.WriteSingleRegisterAsync(Kducer.HR_SPECIAL_RXALLCONFDATA, 48096).ConfigureAwait(false);
                await Task.Delay(PR_SEQ_CHANGE_WAIT, cancellationToken).ConfigureAwait(false); // takes 300-500ms to stream the entire conf, might as well sleep
                byte[] numBytesToRx_asBytes = await mbClient.SocketExposed_ReceiveAllAsync(4).ConfigureAwait(false); // first, receive 4 bytes (uint32_t) telling us how many bytes KDU is going to send
                uint numBytesToBeRecv;
                if (BitConverter.IsLittleEndian)
                    numBytesToBeRecv = ((uint)numBytesToRx_asBytes[3] << 24) | ((uint)numBytesToRx_asBytes[2] << 16) | ((uint)numBytesToRx_asBytes[1] << 8) | numBytesToRx_asBytes[0];
                else
                    numBytesToBeRecv = ((uint)numBytesToRx_asBytes[0] << 24) | ((uint)numBytesToRx_asBytes[1] << 16) | ((uint)numBytesToRx_asBytes[2] << 8) | numBytesToRx_asBytes[3];
                byte[] entireConfData = await mbClient.SocketExposed_ReceiveAllAsync((int)numBytesToBeRecv).ConfigureAwait(false); // then receive the bytes (even if it's a different number than we expect, so we can go back to modbus after)
                if (numBytesToBeRecv != 48096 || entireConfData.Length != 48096)
                    throw new InvalidOperationException($"Failed to get all configuration data from KDU-1A at {mbClient.kduIpAddress}. Expected 48096 bytes, KDU-1A sent {numBytesToBeRecv}, client received {entireConfData.Length}");
                await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false); // give KDU a moment to return to modbus tcp mode
                return KducerKduDataFileReader.ParseKduConfBytes(entireConfData);
            }

            internal static async Task<DateTime> GetDateTime(ReducedModbusTcpClientAsync mbClient)
            {
                byte[] dtRegs = await mbClient.ReadHoldingRegistersAsync(Kducer.HR_DATE_TIME.addr, Kducer.HR_DATE_TIME.count).ConfigureAwait(false);
                ushort year = ModbusByteConversions.TwoModbusBigendianBytesToUshort(dtRegs, 0);
                if (year < 1000)
                    year += 2000;
                DateTime kdu_datetime = new DateTime(year, dtRegs[3], dtRegs[5], dtRegs[7], dtRegs[9], dtRegs[11]);
                return kdu_datetime;
            }

            internal static async Task SetDateTime(ReducedModbusTcpClientAsync mbClient, DateTime dateTime)
            {
                byte[] dateTimeRegsAsBytes = new byte[12];
                dateTimeRegsAsBytes[1] = (byte)(dateTime.Year % 2000);
                dateTimeRegsAsBytes[3] = (byte)dateTime.Month;
                dateTimeRegsAsBytes[5] = (byte)dateTime.Day;
                dateTimeRegsAsBytes[7] = (byte)dateTime.Hour;
                dateTimeRegsAsBytes[9] = (byte)dateTime.Minute;
                dateTimeRegsAsBytes[11] = (byte)dateTime.Second;
                await mbClient.WriteMultipleRegistersAsync(Kducer.HR_DATE_TIME.addr, dateTimeRegsAsBytes).ConfigureAwait(false);
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
#pragma warning restore CA1063 // the dispose implementation is correct
#pragma warning restore CA1816 // the dispose implementation is correct
}
