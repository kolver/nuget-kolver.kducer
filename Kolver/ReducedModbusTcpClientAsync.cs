using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("KducerTests")]

namespace Kolver
{
    internal sealed class ReducedModbusTcpClientAsync
        : IDisposable 
    {
        private readonly IPEndPoint kduEndPoint;
        private readonly Socket kduSock;
        private readonly int connectionAttemptTimeoutMs;
        public readonly string kduIpAddress;

        public ReducedModbusTcpClientAsync(IPAddress kduIpAddress, int connectionAttemptTimeoutMs = 5000, int rxTxTimeoutMs = 250)
        {
            this.connectionAttemptTimeoutMs = connectionAttemptTimeoutMs;
            kduEndPoint = new IPEndPoint(kduIpAddress, 502);
            kduSock = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                SendTimeout = rxTxTimeoutMs,
                ReceiveTimeout = rxTxTimeoutMs,
                LingerState = new LingerOption(true, 0),
                ReceiveBufferSize = 1024,
                SendBufferSize = 1024,
                ExclusiveAddressUse = true
            };
            this.kduIpAddress = kduIpAddress.ToString();
        }
        // IDisposable implementation for a sealed class
        public void Dispose()
        {
            kduSock.Dispose();
        }

        public bool Connected() { return kduSock.Connected; }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            if (kduSock.Connected)
                return;

            const int cancellationCheckIntervalMs = 5;
            int counter = 0;
            Task conn = kduSock.ConnectAsync(kduEndPoint);
            while (true)
            {
                await Task.Delay(cancellationCheckIntervalMs, cancellationToken).ConfigureAwait(false);

                if (kduSock.Connected || conn.IsCompleted || cancellationToken.IsCancellationRequested)
                    return;

                if ( ++counter > (connectionAttemptTimeoutMs / cancellationCheckIntervalMs) )
                {
                    throw new TimeoutException($"Connection to {kduEndPoint.Address.ToString()} timed out");
                }
            }
        }

        private async Task SendAllAsync(Socket socket, byte[] data)
        {
            int bytesSent = 0;
            while (bytesSent < data.Length)
            {
                ArraySegment<byte> bytesChunk = new ArraySegment<byte>(data, bytesSent, data.Length - bytesSent);
                int nBytesActuallySent = await socket.SendAsync(bytesChunk, SocketFlags.None).ConfigureAwait(false);
                if (nBytesActuallySent == 0)
                    throw new SocketException();

                bytesSent += nBytesActuallySent;
            }
        }
        private async Task<byte[]> ReceiveAllAsync(Socket socket, int length)
        {
            byte[] data = new byte[length];
            int bytesReceived = 0;
            while (bytesReceived < length)
            {
                ArraySegment<byte> bytesChunk = new ArraySegment<byte>(data, bytesReceived, length - bytesReceived);
                int nBytesActuallyReceived = await socket.ReceiveAsync(bytesChunk, SocketFlags.None).ConfigureAwait(false);
                if (nBytesActuallyReceived == 0)
                    throw new SocketException();

                bytesReceived += nBytesActuallyReceived;
            }
            return data;
        }

        private void ThrowIfBadMbap(byte[] requestMbap, byte[] responseMbap, int expectedLength)
        {
            if (requestMbap.Length < 7 || responseMbap.Length < 7)
                throw new ModbusException($"Invalid modbus response header. Request: {String.Join("",requestMbap)}, response header: {String.Join("", responseMbap)}");

            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte)
            if (requestMbap[0] != responseMbap[0] ||
                requestMbap[1] != responseMbap[1] ||
                requestMbap[2] != responseMbap[2] ||
                requestMbap[3] != responseMbap[3] ||
                requestMbap[6] != responseMbap[6])
                throw new ModbusException($"Invalid modbus response header. Request: {String.Join("", requestMbap)}, response header: {String.Join("",responseMbap)}");

            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(responseMbap, 4));
            if ( length != (1+2)/*exception len is unit ID + 2*/ && length != expectedLength )
                throw new ModbusException($"Invalid modbus response length. Request: {String.Join("", requestMbap)}, response header: {String.Join("",responseMbap)}");
        }

        private bool ThrowIfBadResponse(byte[] request, byte[] responseData, int expectedLength)
        {
            if (request.Length < 9 || responseData.Length < 2)
                throw new ModbusException($"Invalid modbus response. Request: {request}, response: {responseData}");

            // Function Code
            if (request[7] != responseData[0])
            {
                if (responseData[0] > 0x80 && (responseData[0] - 0x80) == request[7])
                {
                    short exceptionCode = request[8];
                    // modbus exception
                    if (exceptionCode == 6)
                        throw new ModbusException($"Received modbus exception code {exceptionCode}: modbus server busy. Request: {request}, response: {responseData}");
                    else
                        throw new ModbusException($"Received modbus exception code {exceptionCode}. Request: {request}, response: {responseData}");
                }
                else
                    throw new ModbusException($"Invalid modbus response. Request: {request}, response: {responseData}");
            }

            if (responseData.Length != (expectedLength-1))
                throw new ModbusException($"Invalid modbus response length. Request: {request}, response header: {responseData}");

            return true;
        }

        private async Task<byte[]> ReadRegistersAsync(byte fc, ushort startAddress, ushort numberOfRegisters)
        {
            byte[] mbRequest = new byte[12];
            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte), Function Code (1 byte), Address (2 bytes), Quantity (2 bytes)

            mbRequest[5] = 6; // Length
            mbRequest[7] = fc; // 3 = Read Holding Registers, 4 = Read Input Registers

            mbRequest[8] = (byte)(startAddress >> 8);
            mbRequest[9] = (byte)startAddress;

            mbRequest[10] = (byte)(numberOfRegisters >> 8);
            mbRequest[11] = (byte)numberOfRegisters;

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 1/*byteCnt*/ + numberOfRegisters * 2;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(responseMbap, 4)) - 1;

            // Receive modbus response data
            byte[] responseData = await ReceiveAllAsync(kduSock, nBytesToRecieve).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadResponse(mbRequest, responseData, expectedResponseLength);

            // Extract and return
            byte[] data = new byte[numberOfRegisters * 2];
            Array.Copy(responseData, 2, data, 0, data.Length);
            return data;
        }

        public async Task<byte[]> ReadHoldingRegistersAsync(ushort startAddress, ushort numberOfRegisters)
        {
            return await ReadRegistersAsync(3/*read HR*/, startAddress, numberOfRegisters);
        }

        public async Task<byte[]> ReadInputRegistersAsync(ushort startAddress, ushort numberOfRegisters)
        {
            return await ReadRegistersAsync(4/*read IR*/, startAddress, numberOfRegisters);
        }

        public async Task WriteSingleCoilAsync(ushort address, bool value)
        {
            byte[] mbRequest = new byte[12];
            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte), Function Code (1 byte), Address (2 bytes), Quantity (2 bytes)

            mbRequest[5] = 6; // Length
            mbRequest[7] = 5; // write coils

            mbRequest[8] = (byte)(address >> 8);
            mbRequest[9] = (byte)address;

            if (value)
                mbRequest[10] = 0xff;

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 2/*addr*/ + 2/*val*/;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(responseMbap, 4)) - 1;

            // Receive modbus response data
            byte[] responseData = await ReceiveAllAsync(kduSock, nBytesToRecieve).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadResponse(mbRequest, responseData, expectedResponseLength);

            return;
        }

        public async Task WriteSingleRegisterAsync(ushort address, ushort value)
        {
            byte[] mbRequest = new byte[12];
            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte), Function Code (1 byte), Address (2 bytes), Quantity (2 bytes)

            mbRequest[5] = 6; // Length
            mbRequest[7] = 6; // write single register

            mbRequest[8] = (byte)(address >> 8);
            mbRequest[9] = (byte)address;

            mbRequest[10] = (byte)(value >> 8);
            mbRequest[11] = (byte)value;

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 2/*addr*/ + 2/*val*/;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = IPAddress.NetworkToHostOrder(BitConverter.ToInt16(responseMbap, 4)) - 1;

            // Receive modbus response data
            byte[] responseData = await ReceiveAllAsync(kduSock, nBytesToRecieve).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadResponse(mbRequest, responseData, expectedResponseLength);

            return;
        }
    }

    internal class ModbusException : Exception
    {
        public ModbusException()
        {
        }

        public ModbusException(string message)
            : base(message)
        {
        }

        public ModbusException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
