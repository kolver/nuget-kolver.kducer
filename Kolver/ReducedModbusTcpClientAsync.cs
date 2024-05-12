// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
[assembly: InternalsVisibleTo("KducerTests")]

namespace Kolver
{
    internal sealed class ReducedModbusTcpClientAsync
        : IDisposable 
    {
        private readonly IPEndPoint kduEndPoint;
        private readonly int rxTxTimeoutMs;
        public readonly string kduIpAddress;
        private readonly Socket kduSock;

        public ReducedModbusTcpClientAsync(IPAddress kduIpAddress, int rxTxTimeoutMs = 250)
        {
            this.rxTxTimeoutMs = rxTxTimeoutMs;
            kduEndPoint = new IPEndPoint(kduIpAddress, 502);
            this.kduIpAddress = kduIpAddress.ToString();
            kduSock = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                SendTimeout = rxTxTimeoutMs,
                ReceiveTimeout = rxTxTimeoutMs,
                LingerState = new LingerOption(true, 0),
                ReceiveBufferSize = 1024,
                SendBufferSize = 1024,
                ExclusiveAddressUse = true,
                NoDelay = true
            };
        }
        // IDisposable implementation for a sealed class
        public void Dispose()
        {
            kduSock.Dispose();
        }

        public bool Connected() { return kduSock.Connected; }

        /// <summary>
        /// TAP (re)connection method that wraps around APM .net standard 2.0 connection methods
        /// </summary>
        /// <returns></returns>
        /// <exception cref="SocketException">If connection attempt times out</exception>
        public async Task ConnectAsync()
        {
            if (Connected())
                return;
            await Task.Factory.FromAsync(kduSock.BeginConnect, kduSock.EndConnect, kduEndPoint, null).ConfigureAwait(false);
        }
        /// <summary>
        /// sends all bytes, issuing multiple calls to socket send if necessary
        /// uses Factory.FromAsync to wrap APM into TAP maintaining .net standard 2.0 compatibility
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        private static async Task SendAllAsync(Socket socket, byte[] data)
        {
            int bytesSent = 0;
            List<ArraySegment<byte>> wrapper = new List<ArraySegment<byte>>(1) { new ArraySegment<byte>() };
            while (bytesSent < data.Length)
            {
                ArraySegment<byte> bytesChunk = new ArraySegment<byte>(data, bytesSent, data.Length - bytesSent);
                wrapper[0] = bytesChunk;
                int nBytesActuallySent = await Task<int>.Factory.FromAsync(socket.BeginSend, socket.EndSend, wrapper, SocketFlags.None, null).ConfigureAwait(false);
                if (nBytesActuallySent == 0)
                    throw new SocketException();

                bytesSent += nBytesActuallySent;
            }
        }
        /// <summary>
        /// receive all bytes, issuing multiple calls to socket recieve if necessary
        /// uses Factory.FromAsync to wrap APM into TAP maintaining .net standard 2.0 compatibility
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        /// <exception cref="SocketException"></exception>
        private static async Task<byte[]> ReceiveAllAsync(Socket socket, int length)
        {
            byte[] data = new byte[length];
            int bytesReceived = 0;
            List<ArraySegment<byte>> wrapper = new List<ArraySegment<byte>>(1) { new ArraySegment<byte>() };
            while (bytesReceived < length)
            {
                ArraySegment<byte> bytesChunk = new ArraySegment<byte>(data, bytesReceived, length - bytesReceived);
                wrapper[0] = bytesChunk;
                int nBytesActuallyReceived = await Task<int>.Factory.FromAsync(socket.BeginReceive, socket.EndReceive, wrapper, SocketFlags.None, null).ConfigureAwait(false);
                if (nBytesActuallyReceived == 0)
                    throw new SocketException();

                bytesReceived += nBytesActuallyReceived;
            }
            return data;
        }

        private static void ThrowIfBadMbap(byte[] requestMbap, byte[] responseMbap, int expectedLength)
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

            int length = ModbusByteConversions.TwoModbusBigendianBytesToUshort(responseMbap, 4);
            if ( length != (1+2)/*exception len is unit ID + 2*/ && length != expectedLength )
                throw new ModbusException($"Invalid modbus response length in header. Request: {String.Join("", requestMbap)}, response header: {String.Join("",responseMbap)}");
        }

        private static bool ThrowIfBadResponse(byte[] request, byte[] responseData, int expectedLength)
        {
            if (request.Length < 9 || responseData.Length < 2)
                throw new ModbusException($"Invalid modbus response length, expected {expectedLength - 1} got {responseData.Length}");

            // Function Code
            if (request[7] != responseData[0])
            {
                if (responseData[0] > 0x80 && (responseData[0] - 0x80) == request[7])
                {
                    int exceptionCode = responseData[0] - request[7];
                    // modbus exception
                    if (exceptionCode == 6)
                        throw new ModbusException($"Received modbus exception code {exceptionCode}: modbus server busy.", exceptionCode);
                    else
                        throw new ModbusException($"Received modbus exception code {exceptionCode}.", exceptionCode);
                }
                else
                    throw new ModbusException($"Invalid modbus function code in response: expected {request[7]} got {responseData[0]}.");
            }

            if (responseData.Length != (expectedLength-1))
                throw new ModbusException($"Invalid modbus response length, expected {expectedLength - 1} got {responseData.Length}");

            return true;
        }

        private async Task<byte[]> ReadRegistersAsync(byte fc, ushort startAddress, ushort numberOfRegisters)
        {
            byte[] mbRequest = new byte[12];
            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte), Function Code (1 byte), Address (2 bytes), Quantity (2 bytes)

            mbRequest[5] = 6; // Length
            mbRequest[7] = fc; // 3 = Read Holding Registers, 4 = Read Input Registers

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(startAddress, mbRequest, 8);

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(numberOfRegisters, mbRequest, 10);

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 1/*byteCnt*/ + numberOfRegisters * 2;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = ModbusByteConversions.TwoModbusBigendianBytesToUshort(responseMbap, 4) - 1;

            // Receive modbus response data
            byte[] responseData = await ReceiveAllAsync(kduSock, nBytesToRecieve).ConfigureAwait(false);
            // Verify
            ThrowIfBadResponse(mbRequest, responseData, expectedResponseLength);

            // Extract and return
            byte[] data = new byte[numberOfRegisters * 2];
            Array.Copy(responseData, 2, data, 0, data.Length);
            return data;
        }

        public async Task<byte[]> ReadHoldingRegistersAsync(ushort startAddress, ushort numberOfRegisters)
        {
            return await ReadRegistersAsync(3/*read HR*/, startAddress, numberOfRegisters).ConfigureAwait(false);
        }

        public async Task<byte[]> ReadInputRegistersAsync(ushort startAddress, ushort numberOfRegisters)
        {
            return await ReadRegistersAsync(4/*read IR*/, startAddress, numberOfRegisters).ConfigureAwait(false);
        }

        public async Task WriteSingleCoilAsync(ushort address, bool value)
        {
            byte[] mbRequest = new byte[12];
            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte), Function Code (1 byte), Address (2 bytes), Quantity (2 bytes)

            mbRequest[5] = 6; // Length
            mbRequest[7] = 5; // write coils

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(address, mbRequest, 8);

            if (value)
                mbRequest[10] = 0xff;

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 2/*addr*/ + 2/*val*/;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = ModbusByteConversions.TwoModbusBigendianBytesToUshort(responseMbap, 4) - 1;

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

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(address, mbRequest, 8);

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(value, mbRequest, 10);

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 2/*addr*/ + 2/*val*/;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = ModbusByteConversions.TwoModbusBigendianBytesToUshort(responseMbap, 4) - 1;

            // Receive modbus response data
            byte[] responseData = await ReceiveAllAsync(kduSock, nBytesToRecieve).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadResponse(mbRequest, responseData, expectedResponseLength);

            return;
        }

        public async Task WriteMultipleRegistersAsync(ushort address, byte[] values)
        {
            byte[] mbRequest = new byte[12 + 1 + values.Length];
            // Transaction ID (2 bytes), Protocol ID (2 bytes), Length (2 bytes), Unit ID (1 byte), Function Code (1 byte), Address (2 bytes), Quantity (2 bytes), Qty Bytes (1 byte), Data bytes (qty)

            mbRequest[5] = (byte)(7 + values.Length); // Length
            mbRequest[7] = 16; // write multiple registers

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(address, mbRequest, 8);

            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)(values.Length/2), mbRequest, 10);

            mbRequest[12] = (byte)values.Length;

            values.CopyTo(mbRequest, 13);

            int expectedResponseLength = 1/*uID*/ + 1/*FC*/ + 2/*addr*/ + 2/*qty*/;

            // Send modbus request
            await SendAllAsync(kduSock, mbRequest).ConfigureAwait(false); ;

            // Receive modbus mbap (header)
            byte[] responseMbap = await ReceiveAllAsync(kduSock, 7).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadMbap(responseMbap, responseMbap, expectedResponseLength);

            int nBytesToRecieve = ModbusByteConversions.TwoModbusBigendianBytesToUshort(responseMbap, 4) - 1;

            // Receive modbus response data
            byte[] responseData = await ReceiveAllAsync(kduSock, nBytesToRecieve).ConfigureAwait(false); ;
            // Verify
            ThrowIfBadResponse(mbRequest, responseData, expectedResponseLength);

            return;
        }
    }
    /// <summary>
    /// represents a Modbus Exception
    /// this can either be an error in the modbus response header
    /// or a real modbus exception code
    /// </summary>
    public class ModbusException : Exception
    {
        private readonly int ModbusExceptionCode;
        public ModbusException() { }

        public ModbusException(string message)
            : base(message) { }

        public ModbusException(string message, Exception inner)
            : base(message, inner) { }

        public ModbusException(string message, int modbusExceptionCode)
        : this(message)
        {
            ModbusExceptionCode = modbusExceptionCode;
        }

        public int GetModbusExceptionCode() { return ModbusExceptionCode; }
    }
}
