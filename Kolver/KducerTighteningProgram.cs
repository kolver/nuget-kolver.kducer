// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("KducerTests")]

namespace Kolver
{
    /// <summary>
    /// Representation of a tightening program of a Kducer controller with all its parameters
    /// Provides convenience methods to get and set each parameter
    /// You can serialize the program using getProgramModbusHoldingRegistersAsByteArray for storing to a file or database
    /// </summary>
    public class KducerTighteningProgram
    {
        private readonly static byte[] defaultProgramBytesForKDSMT15 = { 0, 0, 0, 50, 0, 0, 0, 150, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 44, 0, 0, 0, 150, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL6 =  { 0, 0, 0, 50, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 44, 0, 0, 2, 88, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL10 = { 0, 0, 0, 100, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 44, 0, 0, 3, 232, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL15 = { 0, 0, 0, 100, 0, 0, 5, 220, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 44, 0, 0, 5, 220, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL20 = { 0, 0, 0, 200, 0, 0, 7, 208, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 200, 0, 0, 7, 208, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL30 = { 0, 0, 1, 44, 0, 0, 11, 184, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 100, 0, 0, 11, 184, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL35 = { 0, 0, 1, 44, 0, 0, 13, 172, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 100, 0, 0, 13, 172, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL45 = { 0, 0, 1, 244, 0, 0, 17, 148, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 90, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 90, 0, 0, 17, 148, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL50 = { 0, 0, 1, 244, 0, 0, 19, 136, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 90, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 90, 0, 0, 19, 136, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL70 = { 0, 0, 2, 188, 0, 0, 27, 88, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 50, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 50, 0, 0, 27, 88, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly static byte[] defaultProgramBytesForKDSPL3 = { 0, 0, 0, 50, 0, 0, 0, 150, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 44, 0, 0, 0, 150, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        private readonly byte[] tighteningProgramHoldingRegistersAsByteArray = new byte[230];
        /// <summary>
        /// the byte array returned can be sent to the KDU-1A to modify program parameters
        /// the byte array can also be used to serialize this program (note: the program number is not included in this array)
        /// </summary>
        /// <returns>the 115 modbus holding registers representing this program</returns>
        public byte[] getProgramModbusHoldingRegistersAsByteArray() {  return tighteningProgramHoldingRegistersAsByteArray; }
        /// <summary>
        /// creates a program from a 230-byte array retrieved from Modbus TCP holding registers
        /// note: the program number where this program originated from is not included in this information
        /// </summary>
        /// <param name="tighteningProgramHoldingRegistersAsByteArray">the byte array from reading 115 holding registers of the program</param>
        public KducerTighteningProgram(byte[] tighteningProgramHoldingRegistersAsByteArray)
        {
            if (tighteningProgramHoldingRegistersAsByteArray == null)
                throw new ArgumentNullException(nameof(tighteningProgramHoldingRegistersAsByteArray));
            if (tighteningProgramHoldingRegistersAsByteArray.Length < 180 || tighteningProgramHoldingRegistersAsByteArray.Length > 230)
                throw new ArgumentException("Expected length is 180 to 230 bytes", nameof(tighteningProgramHoldingRegistersAsByteArray));

            tighteningProgramHoldingRegistersAsByteArray.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
        }
        /// <summary>
        /// creates a program with valid default values for a screwdriver
        /// </summary>
        /// <param name="screwdriverModel">optional, choose from: KDS-MT1.5 (default), KDS-PL6, KDS-PL10, KDS-PL15, KDS-PL20, KDS-PL30, KDS-PL35, KDS-PL45, KDS-PL50, KDS-PL70, KDS-PL3</param>
        public KducerTighteningProgram(string screwdriverModel = "KDS-MT1.5")
        {
            if (screwdriverModel == null)
                throw new ArgumentNullException(nameof(screwdriverModel));

            if (screwdriverModel.StartsWith("KDS-MT1.5", StringComparison.Ordinal) || screwdriverModel.StartsWith("KDS-MT15", StringComparison.Ordinal) || screwdriverModel.StartsWith(ScrewdriverBaseModel.NONE_CONNECTED, StringComparison.Ordinal))
                defaultProgramBytesForKDSMT15.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL6", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL6.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL10", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL10.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL15", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL15.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL20", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL20.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL30", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL30.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL35", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL35.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL45", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL45.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL50", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL50.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL70", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL70.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else if (screwdriverModel.StartsWith("KDS-PL3", StringComparison.Ordinal))
                defaultProgramBytesForKDSPL3.CopyTo(this.tighteningProgramHoldingRegistersAsByteArray, 0);
            else
                throw new ArgumentException("Invalid model, choose from: KDS-MT1.5 (default), KDS-PL6, KDS-PL10, KDS-PL15, KDS-PL20, KDS-PL30, KDS-PL35, KDS-PL45, KDS-PL50, KDS-PL70, KDS-PL3");
        }
        /// <summary>
        /// creates a program with valid default values for a screwdriver
        /// </summary>
        /// <param name="screwdriver">a screwdriver type returned from "GetScrewdriverInfo"</param>
        public KducerTighteningProgram(KdsScrewdriver screwdriver) :
            this(screwdriver?.BaseModel.Name ?? throw new ArgumentNullException(nameof(screwdriver))) { }
        /// <summary>
        /// creates a program with valid default values for a screwdriver
        /// </summary>
        /// <param name="screwdriver"></param>
        public KducerTighteningProgram(ScrewdriverBaseModel screwdriver) :
            this(screwdriver?.Name ?? throw new ArgumentNullException(nameof(screwdriver))) { }
        /// <summary>
        /// creates a program with valid default values for a screwdriver
        /// </summary>
        /// <param name="screwdriver"></param>
        public KducerTighteningProgram(ScrewdriverBaseModelId screwdriver) :
            this(ScrewdriverBaseModel.Get(screwdriver)) { }
        /// <summary>in cNm</summary>
        public ushort GetTorqueTarget()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 2);
        }
        /// <summary>in cNm</summary>
        public void SetTorqueTarget(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 2);
        }
        /// <summary>in cNm</summary>
        public ushort GetTorqueMax()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 6);
        }
        /// <summary>in cNm</summary>
        public void SetTorqueMax(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 6);
        }
        /// <summary>in cNm</summary>
        public ushort GetTorqueMin()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 10);
        }
        /// <summary>in cNm</summary>
        public void SetTorqueMin(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 10);
        }
        /// <summary>in degrees</summary>
        public ushort GetAngleTarget()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 14);
        }
        /// <summary>in degrees</summary>
        public void SetAngleTarget(ushort degrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degrees, tighteningProgramHoldingRegistersAsByteArray, 14);
        }
        /// <summary>in degrees</summary>
        public ushort GetAngleMax()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 18);
        }
        /// <summary>in degrees</summary>
        public void SetAngleMax(ushort degrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degrees, tighteningProgramHoldingRegistersAsByteArray, 18);
        }
        /// <summary>in degrees</summary>
        public ushort GetAngleMin()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 22);
        }
        /// <summary>in degrees</summary>
        public void SetAngleMin(ushort degrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degrees, tighteningProgramHoldingRegistersAsByteArray, 22);
        }
        /// <summary>in cNm</summary>
        public ushort GetAngleStartAt()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 26);
        }
        /// <summary>in cNm</summary>
        public void SetAngleStartAt(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 26);
        }
        /// <summary>in RPM</summary>
        public ushort GetFinalSpeed()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 30);
        }
        /// <summary>in RPM</summary>
        public void SetFinalSpeed(ushort rpm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(rpm, tighteningProgramHoldingRegistersAsByteArray, 30);
        }
        /// <summary>in RPM</summary>
        public ushort GetDownshiftInitialSpeed()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 34);
        }
        /// <summary>in RPM</summary>
        public void SetDownshiftInitialSpeed(ushort rpm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(rpm, tighteningProgramHoldingRegistersAsByteArray, 34);
        }
        /// <summary>in cNm or degrees</summary>
        public ushort GetDownshiftThreshold()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 38);
        }
        /// <summary>in cNm or degrees</summary>
        public void SetDownshiftThreshold(ushort cNmOrDegrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNmOrDegrees, tighteningProgramHoldingRegistersAsByteArray, 38);
        }
        /// <summary>0=Torque Control with Angle Monitoring, 1=Angle Control with Torque Monitoring</summary>
        public ushort GetTorqueAngleMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 40);
        }
        /// <summary>0=Torque Control with Angle Monitoring, 1=Angle Control with Torque Monitoring</summary>
        public void SetTorqueAngleMode(ushort mode)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, tighteningProgramHoldingRegistersAsByteArray, 40);
        }
        /// <summary>0=Torque Threshold, 1=Lever, 2=External I/O signal</summary>
        public ushort GetAngleStartAtMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 42);
        }
        /// <summary>0=Torque Threshold, 1=Lever, 2=External I/O signal</summary>
        public void SetAngleStartAtMode(ushort mode)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, tighteningProgramHoldingRegistersAsByteArray, 42);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetDownshiftAtTorqueOnOff()
        {
            return GetMiscConfBit(0);
        }
        /// <summary>part of misc conf register</summary>
        public void SetDownshiftAtTorqueOnOff(bool onOff)
        {
            SetMiscConfBit(0, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetRampOnOff()
        {
            return GetMiscConfBit(1);
        }
        /// <summary>part of misc conf register</summary>
        public void SetRampOnOff(bool onOff)
        {
            SetMiscConfBit(1, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetRunTimeOnOff()
        {
            return GetMiscConfBit(2);
        }
        /// <summary>part of misc conf register</summary>
        public void SetRunTimeOnOff(bool onOff)
        {
            SetMiscConfBit(2, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetMinTimeOnOff()
        {
            return GetMiscConfBit(3);
        }
        /// <summary>part of misc conf register</summary>
        public void SetMinTimeOnOff(bool onOff)
        {
            SetMiscConfBit(3, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetMaxTimeOnOff()
        {
            return GetMiscConfBit(4);
        }
        /// <summary>part of misc conf register</summary>
        public void SetMaxTimeOnOff(bool onOff)
        {
            SetMiscConfBit(4, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetSerialPrintOnOff()
        {
            return GetMiscConfBit(7);
        }
        /// <summary>part of misc conf register</summary>
        public void SetSerialPrintOnOff(bool onOff)
        {
            SetMiscConfBit(7, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetPressOkOnOff()
        {
            return GetMiscConfBit(8);
        }
        /// <summary>part of misc conf register</summary>
        public void SetPressOkOnOff(bool onOff)
        {
            SetMiscConfBit(8, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetPressEscOnOff()
        {
            return GetMiscConfBit(9);
        }
        /// <summary>part of misc conf register</summary>
        public void SetPressEscOnOff(bool onOff)
        {
            SetMiscConfBit(9, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetLeverErrorOnOff()
        {
            return GetMiscConfBit(10);
        }
        /// <summary>part of misc conf register</summary>
        public void SetLeverErrorOnOff(bool onOff)
        {
            SetMiscConfBit(10, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetReverseAllowedOnOff()
        {
            return GetMiscConfBit(11);
        }
        /// <summary>part of misc conf register</summary>
        public void SetReverseAllowedOnOff(bool onOff)
        {
            SetMiscConfBit(11, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetCounterclockwiseTighteningOnOff()
        {
            return GetMiscConfBit(12);
        }
        /// <summary>part of misc conf register</summary>
        public void SetCounterclockwiseTighteningOnOff(bool onOff)
        {
            SetMiscConfBit(12, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetUseDock05Screwdriver2OnOff()
        {
            return GetMiscConfBit(13);
        }
        /// <summary>part of misc conf register</summary>
        public void SetUseDock05Screwdriver2OnOff(bool onOff)
        {
            SetMiscConfBit(13, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetDownshiftAtAngleOnOff()
        {
            return GetMiscConfBit(14) && GetMiscConfBit(0);
        }
        /// <summary>part of misc conf register</summary>
        public void SetDownshiftAtAngleOnOff(bool onOff)
        {
            SetMiscConfBit(14, onOff);
            SetMiscConfBit(0, onOff);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetRamp()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 48);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetRamp(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 48);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetRunTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 52);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetRuntime(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 52);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetMinTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 56);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetMintime(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 56);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetMaxTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 60);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetMaxtime(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 60);
        }
        /// <summary>0=Time, 1=Angle, 2=Off</summary>
        public ushort GetMaxPowerPhaseMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 62);
        }
        /// <summary>0=Time, 1=Angle, 2=Off</summary>
        public void SetMaxPowerPhaseMode(ushort mode)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, tighteningProgramHoldingRegistersAsByteArray, 62);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetMaxPowerPhaseTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 66);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetMaxPowerPhaseTime(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 66);
        }
        /// <summary>in degrees</summary>
        public ushort GetMaxPowerPhaseAngle()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 70);
        }
        /// <summary>in degrees</summary>
        public void SetMaxPowerPhaseAngle(ushort degrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degrees, tighteningProgramHoldingRegistersAsByteArray, 70);
        }
        /// <summary>in RPM</summary>
        public ushort GetReverseSpeed()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 74);
        }
        /// <summary>in RPM</summary>
        public void SetReverseSpeed(ushort rpm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(rpm, tighteningProgramHoldingRegistersAsByteArray, 74);
        }
        /// <summary>in cNm</summary>
        public ushort GetMaxReverseTorque()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 78);
        }
        /// <summary>in cNm</summary>
        public void SetMaxReverseTorque(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 78);
        }
        /// <summary>0=Time, 1=Angle, 2=Off</summary>
        public ushort GetPreTighteningReverseMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 80);
        }
        /// <summary>0=Time, 1=Angle, 2=Off</summary>
        public void SetPreTighteningReverseMode(ushort mode)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, tighteningProgramHoldingRegistersAsByteArray, 80);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetPreTighteningReverseTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 84);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetPreTighteningReverseTime(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 84);
        }
        /// <summary>in degrees</summary>
        public ushort GetPreTighteningReverseAngle()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 88);
        }
        /// <summary>in degrees</summary>
        public void SetPreTighteningReverseAngle(ushort degrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degrees, tighteningProgramHoldingRegistersAsByteArray, 88);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetPreTighteningReverseDelay()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 92);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetPreTighteningReverseDelay(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 92);
        }
        /// <summary>0=Time, 1=Angle, 2=Off</summary>
        public ushort GetAfterTighteningReverseMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 94);
        }
        /// <summary>0=Time, 1=Angle, 2=Off</summary>
        public void SetAfterTighteningReverseMode(ushort mode)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, tighteningProgramHoldingRegistersAsByteArray, 94);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetAfterTighteningReverseTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 98);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetAfterTighteningReverseTime(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 98);
        }
        /// <summary>in degrees</summary>
        public ushort GetAfterTighteningReverseAngle()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 102);
        }
        /// <summary>in degrees</summary>
        public void SetAfterTighteningReverseAngle(ushort degrees)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degrees, tighteningProgramHoldingRegistersAsByteArray, 102);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public ushort GetAfterTighteningReverseDelay()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 106);
        }
        /// <summary>in tenths of seconds, i.e. for 1.5 seconds the value is 15</summary>
        public void SetAfterTighteningReverseDelay(ushort secondsTimesTen)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(secondsTimesTen, tighteningProgramHoldingRegistersAsByteArray, 106);
        }
        /// <summary>string of up to 16 ascii characters</summary>
        public string GetBarcode()
        {
            return ModbusByteConversions.ModbusBytesToAsciiString(tighteningProgramHoldingRegistersAsByteArray, 112, 16);
        }
        /// <summary>string of up to 16 ascii characters</summary>
        public void SetBarcode(string barcode)
        {
            if (barcode == null)
                throw new ArgumentNullException(nameof(barcode));
            if (barcode.Length > 16)
                throw new ArgumentException("At most 16 characters", nameof(barcode));
            byte[] barcodeBytes = new byte[16];
            System.Text.Encoding.ASCII.GetBytes(barcode).CopyTo(barcodeBytes, 0);
            barcodeBytes.CopyTo(tighteningProgramHoldingRegistersAsByteArray, 112);
        }
        /// <summary>CBS880 bit tray or SWBX accessory position associated with this program. 0=Off</summary>
        public ushort GetSocket()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 130);
        }
        /// <summary>CBS880 bit tray or SWBX accessory position associated with this program. 0=Off</summary>
        public void SetSocket(ushort socket)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(socket, tighteningProgramHoldingRegistersAsByteArray, 130);
        }
        /// <summary>number of screws in program. 0=disable screw count.</summary>
        public ushort GetNumberOfScrews()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 134);
        }
        /// <summary>number of screws in program. 0=disable screw count.</summary>
        public void SetNumberOfScrews(ushort numberOfScrews)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(numberOfScrews, tighteningProgramHoldingRegistersAsByteArray, 134);
        }
        /// <summary>string of up to 30 ascii characters, shown in the main screen when using this program.</summary>
        public string GetDescription()
        {
            return ModbusByteConversions.ModbusBytesToAsciiString(tighteningProgramHoldingRegistersAsByteArray, 136, 30);
        }
        /// <summary>string of up to 30 ascii characters, shown in the main screen when using this program.</summary>>
        public void SetDescription(string barcode)
        {
            if (barcode == null)
                throw new ArgumentNullException(nameof(barcode));
            if (barcode.Length > 30)
                throw new ArgumentException("At most 30 characters", nameof(barcode));
            byte[] barcodeBytes = new byte[30];
            System.Text.Encoding.ASCII.GetBytes(barcode).CopyTo(barcodeBytes, 0);
            barcodeBytes.CopyTo(tighteningProgramHoldingRegistersAsByteArray, 136);
        }
        internal ushort GetTorqueCompensationValue()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 168);
        }
        internal void SetTorqueCompensationValue(ushort oneThousandIsTheDefault)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(oneThousandIsTheDefault, tighteningProgramHoldingRegistersAsByteArray, 168);
        }
        /// <summary>0 = Off, 1 = Time Window with Average value, 2 = Time Window with Peak value, 3 = Angle Window with Average value, 4 = Angle Window with Peak value</summary>
        public ushort GetRunningTorqueMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 170);
        }
        /// <summary>0 = Off, 1 = Time Window with Average value, 2 = Time Window with Peak value, 3 = Angle Window with Average value, 4 = Angle Window with Peak value</summary>
        public void SetRunningTorqueMode(ushort mode)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, tighteningProgramHoldingRegistersAsByteArray, 170);
        }
        /// <summary>degrees for angle modes, milliseconds for time modes</summary>
        public ushort GetRunningTorqueWindowStart()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 172);
        }
        /// <summary>degrees for angle modes, milliseconds for time modes</summary>
        public void SetRunningTorqueWindowStart(ushort degreesOrTimeInMs)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degreesOrTimeInMs, tighteningProgramHoldingRegistersAsByteArray, 172);
        }
        /// <summary>degrees for angle modes, milliseconds for time modes</summary>
        public ushort GetRunningTorqueWindowEnd()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 174);
        }
        /// <summary>degrees for angle modes, milliseconds for time modes</summary>
        public void SetRunningTorqueWindowEnd(ushort degreesOrTimeInMs)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(degreesOrTimeInMs, tighteningProgramHoldingRegistersAsByteArray, 174);
        }
        /// <summary>in cNm</summary>
        public ushort GetRunningTorqueMin()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 176);
        }
        /// <summary>in cNm</summary>
        public void SetRunningTorqueMin(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 176);
        }
        /// <summary>in cNm</summary>
        public ushort GetRunningTorqueMax()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 178);
        }
        /// <summary>in cNm</summary>
        public void SetRunningTorqueMax(ushort cNm)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(cNm, tighteningProgramHoldingRegistersAsByteArray, 178);
        }
        /// <summary>(KDU-1A v40 and newer only) Tolerance (+/-) for the position measurement from the Ktls Sensor. In mm or 10*degrees (tenths of degrees). Max value 250.</summary>
        public ushort GetKtlsSensor1Tolerance()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 180);
        }
        /// <summary>(KDU-1A v40 and newer only) Tolerance (+/-) for the position measurement from the Ktls Sensor. In mm or 10*degrees (tenths of degrees). Max value 250.</summary>
        public void SetKtlsSensor1Tolerance(ushort tolerance)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(tolerance, tighteningProgramHoldingRegistersAsByteArray, 180);
        }
        /// <summary>(KDU-1A v40 and newer only) Tolerance (+/-) for the position measurement from the Ktls Sensor. In mm or 10*degrees (tenths of degrees). Max value 250.</summary>
        public ushort GetKtlsSensor2Tolerance()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 182);
        }
        /// <summary>(KDU-1A v40 and newer only) Tolerance (+/-) for the position measurement from the Ktls Sensor. In mm or 10*degrees (tenths of degrees). Max value 250.</summary>
        public void SetKtlsSensor2Tolerance(ushort tolerance)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(tolerance, tighteningProgramHoldingRegistersAsByteArray, 182);
        }
        /// <summary>(KDU-1A v40 and newer only) Tolerance (+/-) for the position measurement from the Ktls Sensor. In mm or 10*degrees (tenths of degrees). Max value 250.</summary>
        public ushort GetKtlsSensor3Tolerance()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 184);
        }
        /// <summary>(KDU-1A v40 and newer only) Tolerance (+/-) for the position measurement from the Ktls Sensor. In mm or 10*degrees (tenths of degrees). Max value 250.</summary>
        public void SetKtlsSensor3Tolerance(ushort tolerance)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(tolerance, tighteningProgramHoldingRegistersAsByteArray, 184);
        }
        /// <summary>(KDU-1A v40 and newer only) 0 = Off, 1 = Nm, 2 = kgf.cm, 3 = lbf.in, 4 = ozf.in, 5 = lbf.ft</summary>
        public ushort GetSubstituteTorqueUnits()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 186);
        }
        /// <summary>(KDU-1A v40 and newer only) 0 = Off, 1 = Nm, 2 = kgf.cm, 3 = lbf.in, 4 = ozf.in, 5 = lbf.ft</summary>
        public void SetSubstituteTorqueUnits(ushort units)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(units, tighteningProgramHoldingRegistersAsByteArray, 186);
        }
        /// <summary>(KDU-1A v41 and newer only) in degrees</summary>
        public ushort GetTotalAngleMin()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 188);
        }
        /// <summary>(KDU-1A v41 and newer only) in degrees</summary>
        public void SetTotalAngleMin(ushort units)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(units, tighteningProgramHoldingRegistersAsByteArray, 188);
        }
        /// <summary>(KDU-1A v41 and newer only) in degrees</summary>
        public ushort GetTotalAngleMax()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningProgramHoldingRegistersAsByteArray, 190);
        }
        /// <summary>(KDU-1A v41 and newer only) in degrees</summary>
        public void SetTotalAngleMax(ushort units)
        {
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(units, tighteningProgramHoldingRegistersAsByteArray, 190);
        }

        private void SetMiscConfBit(int confBitIdx, bool bitState)
        {
            int byteIdx = 45; // index of misc conf bytes in modbus registers
            if (confBitIdx >= 8)
            {
                confBitIdx -= 8;
                byteIdx = 44;
            }
            byte mask = (byte)(1 << confBitIdx);

            if (bitState) // set to 1
                tighteningProgramHoldingRegistersAsByteArray[byteIdx] |= mask;
            else // Set to zero
                tighteningProgramHoldingRegistersAsByteArray[byteIdx] &= (byte)(~mask);
        }
        private bool GetMiscConfBit(int confBitIdx)
        {
            int byteIdx = 45; // index of misc conf bytes in modbus registers
            if (confBitIdx >= 8)
            {
                confBitIdx -= 8;
                byteIdx = 44;
            }
            byte mask = (byte)(1 << confBitIdx);

            return (tighteningProgramHoldingRegistersAsByteArray[byteIdx] & mask) != 0;
        }
    }
}
