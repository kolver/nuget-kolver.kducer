using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Kolver
{
    public class Kducer
        : IDisposable
    {
        public int POLL_INTERVAL_MS = 50;
        private static readonly int SHORT_WAIT = 50;
        private static readonly int PR_SEQ_CHANGE_WAIT = 300;
        /// <summary>
        /// if true, the screwdriver lever is disabled ("stop motor on") after a new result is detected
        /// it is automatically re-enabled when the result has been processed via "GetResultAsync"
        /// this option is ignored when using RunScrewdriverUntilResultAsync
        /// </summary>
        public bool lockScrewdriverUntilResultsProcessed = false;

        private ReducedModbusTcpClientAsync mbClient;
        private CancellationToken kduCommsCancellationToken;
        private readonly CancellationTokenSource linkedCancellationTokenSource;
        private Task asyncComms;
        private readonly ConcurrentQueue<byte[]> resultsQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<KduUnitOperationTaskAsync> userCmdQueue = new ConcurrentQueue<KduUnitOperationTaskAsync>();
        private Kducer(IPAddress kduIpAddress, CancellationToken kduCommsCancellationToken) // private constructor: must use static constructors below
        {
            linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(kduCommsCancellationToken);
            this.kduCommsCancellationToken = linkedCancellationTokenSource.Token;
            mbClient = new ReducedModbusTcpClientAsync(kduIpAddress);
        }

        public static Kducer CreateKducerAndStartAsyncComms(IPAddress kduIpAddress, CancellationToken kduCommsCancellationToken)
        {
            Kducer kdu = new Kducer(kduIpAddress, kduCommsCancellationToken);
            kdu.StartAsyncComms();
            return kdu;
        }

        public static Kducer CreateKducerAndStartAsyncComms(String kduIpAddress, CancellationToken kduCommsCancellationToken)
        {
            return CreateKducerAndStartAsyncComms(IPAddress.Parse(kduIpAddress), kduCommsCancellationToken);
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
                    await mbClient.ConnectAsync(kduCommsCancellationToken);
                    if (kduCommsCancellationToken.IsCancellationRequested)
                        return;
                    await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false); // clear new result flag if already present

                    KduUnitOperationTaskAsync pollForResults = new KduUnitOperationTaskAsync(resultsQueue, kduCommsCancellationToken);

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
                            await pollForResults.CheckAndEnqueueKduResult(mbClient, lockScrewdriverUntilResultsProcessed).ConfigureAwait(false);
                        }

                        await interval.ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
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
        /// Creates a task that returns and removes a result from the FIFO results queue. The task awaits until there is a result available.
        /// </summary>
        /// <returns>The KDU result</returns>
        public async Task<byte[]> GetResultAsync()
        {
            while (true)
            {
                if (!resultsQueue.IsEmpty)
                {
                    if (resultsQueue.TryDequeue(out byte[] res) == true)
                        return res;
                }
                await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a task that returns and removes a result from the FIFO results queue. The task awaits until there is a result available.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<byte[]> GetResultAsync(CancellationToken cancellationToken)
        {
            CancellationToken cancelGetResult;
            if (cancellationToken == null)
                cancelGetResult = kduCommsCancellationToken;
            else
                cancelGetResult = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, kduCommsCancellationToken).Token;

            while (true)
            {
                if (!resultsQueue.IsEmpty)
                {
                    if (resultsQueue.TryDequeue(out byte[] res) == true)
                        return res;
                }
                await Task.Delay(POLL_INTERVAL_MS, cancelGetResult).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Selects a program on the KDU and waits 300ms to ensure the KDU program is loaded.
        /// </summary>
        /// <param name="kduProgramNumber">The program number to select. 1 to 64 or 1 to 200 depending on the KDU model and firmware version.</param>
        /// <returns>true if the request went through successfully, false otherwise.</returns>
        public async Task<bool> SelectProgramNumberAsync(ushort kduProgramNumber)
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.SetProgram, kduProgramNumber, kduCommsCancellationToken);
            userCmdQueue.Enqueue(userCmd);
            while (!userCmd.completed)
                await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Reads the program number currently active in the KDU
        /// </summary>
        /// <returns>the program number</returns>
        public async Task<ushort> GetProgramNumberAsync()
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.GetProgram, kduCommsCancellationToken);
            userCmdQueue.Enqueue(userCmd);
            while(!userCmd.completed)
                await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);
            return userCmd.result;
        }

        /// <summary>
        /// Selects a sequence on the KDU and waits 300ms to ensure the KDU sequence is loaded.
        /// </summary>
        /// <param name="kduSequenceNumber">The sequence number to select. 1 to 8 or 1 to 24 depending on the KDU model and firmware version.</param>
        /// <returns>true if the request went through successfully, false otherwise.</returns>
        public async Task<bool> SelectSequenceNumberAsync(ushort kduSequenceNumber)
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.SetSequence, kduSequenceNumber, kduCommsCancellationToken);
            userCmdQueue.Enqueue(userCmd);
            while (!userCmd.completed)
                await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Reads the sequence number currently active in the KDU
        /// </summary>
        /// <returns>the sequence number</returns>
        public async Task<ushort> GetSequenceNumberAsync()
        {
            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.GetSequence, kduCommsCancellationToken);
            userCmdQueue.Enqueue(userCmd);
            while (!userCmd.completed)
                await Task.Delay(POLL_INTERVAL_MS, kduCommsCancellationToken).ConfigureAwait(false);
            return userCmd.result;
        }

        /// <summary>
        /// Runs screwdriver until a new result is ready
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<ushort> RunScrewdriverUntilResultAsync(CancellationToken cancellationToken)
        {
            CancellationToken cancelRunScrewdriver;
            if (cancellationToken == null)
                cancelRunScrewdriver = kduCommsCancellationToken;
            else
                cancelRunScrewdriver = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, kduCommsCancellationToken).Token;

            KduUnitOperationTaskAsync userCmd = new KduUnitOperationTaskAsync(UserCmd.RunScrewdriver, resultsQueue, cancelRunScrewdriver);
            userCmdQueue.Enqueue(userCmd);
            while (!userCmd.completed)
                await Task.Delay(POLL_INTERVAL_MS, cancelRunScrewdriver).ConfigureAwait(false);
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
            private readonly ConcurrentQueue<byte[]> resultsQueue;
            internal ushort result;
            internal bool completed = false;

            internal KduUnitOperationTaskAsync(UserCmd cmd, CancellationToken cancellationToken)
            {
                this.cmd = cmd;
                this.cancellationToken = cancellationToken;
            }

            internal KduUnitOperationTaskAsync(UserCmd cmd, ushort payload, CancellationToken cancellationToken)
            {
                this.cmd = cmd;
                this.payload = payload;
                this.cancellationToken = cancellationToken;
            }

            internal KduUnitOperationTaskAsync(UserCmd cmd, ConcurrentQueue<byte[]> resultsQueue, CancellationToken cancellationToken)
            {
                this.cmd = cmd;
                this.cancellationToken = cancellationToken;
                this.resultsQueue = resultsQueue;
            }

            internal KduUnitOperationTaskAsync(ConcurrentQueue<byte[]> resultsQueue, CancellationToken cancellationToken)
            {
                this.cancellationToken = cancellationToken;
                this.resultsQueue = resultsQueue;
            }

            internal async Task ProcessUserCmdAsync(ReducedModbusTcpClientAsync mbClient)
            {
                switch(cmd)
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
                        while ( !cancellationToken.IsCancellationRequested && (await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false))[1] == 0 )
                        {
                            await mbClient.WriteSingleCoilAsync(32, true).ConfigureAwait(false);
                            await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                        }
                        await mbClient.WriteSingleCoilAsync(32, false).ConfigureAwait(false);
                        if (!cancellationToken.IsCancellationRequested)
                            resultsQueue.Enqueue(await mbClient.ReadInputRegistersAsync(295, 67).ConfigureAwait(false));
                        break;
                }

                completed = true;
            }

            internal async Task CheckAndEnqueueKduResult(ReducedModbusTcpClientAsync mbClient, bool lockScrewdriverUntilResultsProcessed)
            {
                byte newResultFlag = (await mbClient.ReadInputRegistersAsync(294, 1).ConfigureAwait(false))[1];

                if (newResultFlag == 1)
                {
                    resultsQueue.Enqueue(await mbClient.ReadInputRegistersAsync(295, 67).ConfigureAwait(false));
                    if (lockScrewdriverUntilResultsProcessed)
                    {
                        await mbClient.WriteSingleCoilAsync(34, true).ConfigureAwait(false);
                        await Task.Delay(SHORT_WAIT, cancellationToken).ConfigureAwait(false);
                    }
                }
                else if (lockScrewdriverUntilResultsProcessed && resultsQueue.IsEmpty)
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
