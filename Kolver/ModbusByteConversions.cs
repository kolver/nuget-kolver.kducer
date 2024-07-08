// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;

namespace Kolver
{
    internal class ModbusByteConversions
    {
        internal static ushort TwoModbusBigendianBytesToUshort(byte[] mbBytes, int index)
        {
            if (BitConverter.IsLittleEndian)
                return (ushort)(mbBytes[index] << 8 | mbBytes[index + 1]);
            else
                return BitConverter.ToUInt16(mbBytes, index);
        }
        internal static uint FourModbusBigendianBytesToUint(byte[] mbBytes, int index)
        {
            if (BitConverter.IsLittleEndian)
                return (uint)((mbBytes[index] << 24) | (mbBytes[index + 1] << 16) | (mbBytes[index + 2] << 8) | mbBytes[index + 3]);
            else
                return BitConverter.ToUInt32(mbBytes, index);
        }

        internal static string ModbusBytesToAsciiString(byte[] mbBytes, int index, int characterCount)
        {
            return new string(System.Text.Encoding.ASCII.GetChars(mbBytes, index, characterCount));
        }

        internal static void CopyUshortToBytesAsModbusBigendian(ushort value, byte[] destinationBytes, int index)
        {
            if (BitConverter.IsLittleEndian)
            {
                destinationBytes[index] = (byte)(value >> 8);
                destinationBytes[index + 1] = (byte)value;
            }
            else
            {
                destinationBytes[index] = (byte)value;
                destinationBytes[index + 1] = (byte)(value >> 8);
            }
        }

        internal static void CopyUintToBytesAsModbusBigendian(uint value, byte[] destinationBytes, int index)
        {
            if (BitConverter.IsLittleEndian)
            {
                destinationBytes[index] = (byte)(value >> 24);
                destinationBytes[index + 1] = (byte)(value >> 16);
                destinationBytes[index + 2] = (byte)(value >> 8);
                destinationBytes[index + 3] = (byte)value;
            }
            else
            {
                destinationBytes[index] = (byte)value;
                destinationBytes[index + 1] = (byte)(value >> 8);
                destinationBytes[index + 2] = (byte)(value >> 16);
                destinationBytes[index + 3] = (byte)(value >> 24);
            }
        }
    }
}
