using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
[assembly: InternalsVisibleTo("KducerTests")]


namespace Kolver
{
    /// <summary>
    /// represents a sequence of programs as found in the Kducer controller, including barcode for the sequence, link modes, and link times for the sequence
    /// You can serialize the sequence using getSequenceModbusHoldingRegistersAsByteArray for storing to a file or database
    /// sequence size was increased with version 38 of the Kducer software from 16 programs per sequence to 32, therefore to serialize there are two methods available, one standard and one for v37 and prior versions
    /// </summary>
    public class KducerSequenceOfTighteningPrograms
    {
        private readonly string barcode = "";
        private readonly List<byte> programNumbers;
        private readonly List<byte> programLinkModes;
        private readonly List<byte> programLinkTimes;

        private ushort kduVersion = 38;

        /// <summary>
        /// creates a sequence (of programs) to send to a kducer controller
        /// </summary>
        /// <param name="programNumbers">list of program numbers</param>
        /// <param name="programLinkModes">list of program transition modes. 0 = "time" (default), 1 = "press OK", 2 = "auto"</param>
        /// <param name="programLinkTimes">list of program transition times in tenths of seconds, for transition modes 0 (time). min value 3 (0.3 seconds), max value 30 (3 seconds)</param>
        /// <param name="barcode">optional barcode for sequence selection via barcode, max 16 ASCII characters</param>
        /// <param name="kduVersion">optional, to specify older sequence structure from KDU firmware v37 (max 16 programs per sequence). v38 and later (default) have max 32 programs per sequence</param>
        /// <exception cref="ArgumentException">if basic parameter validation fails</exception>
        public KducerSequenceOfTighteningPrograms(IList<byte> programNumbers, IList<byte> programLinkModes, IList<byte> programLinkTimes, string barcode = "", ushort kduVersion = 38)
        {
            this.kduVersion = kduVersion;

            if (string.IsNullOrEmpty(barcode) == false)
            {
                if (barcode.Length > 16)
                    throw new ArgumentException("Max sequence barcode length is 16", nameof(barcode));
                else
                    this.barcode = barcode.Replace(',', '.'); // no commas in barcodes
            }

            List<byte> prNums;
            if (programNumbers == null)
                throw new ArgumentNullException(nameof(programNumbers));
            else if (programNumbers.Count == 0)
                throw new ArgumentException("List of programs is empty", nameof(programNumbers));
            else if (programNumbers[0] == 0)
                throw new ArgumentException("Must have at least one nonzero program number", nameof(programNumbers));
            else
                prNums = programNumbers.ToList();

            foreach (byte programNumber in prNums)
                if (programNumber > getMaxProgramNumberForVersion(kduVersion))
                    throw new ArgumentException($"Invalid program number {programNumber} in list, max for KDU version {kduVersion} is {getMaxProgramNumberForVersion(kduVersion)}", nameof(programNumbers));


            List<byte> prLinkModes;
            if (programLinkModes == null || programLinkModes.Count == 0)
                prLinkModes = (new byte[programNumbers.Count]).ToList();
            else
                prLinkModes = programLinkModes.ToList();

            foreach (byte linkMode in prLinkModes)
                if (linkMode > getMaxTransitionModeForVersion(kduVersion))
                    throw new ArgumentException($"Invalid program link mode {linkMode} in list, max for KDU version {kduVersion} is {getMaxTransitionModeForVersion(kduVersion)}", nameof(programLinkModes));


            List<byte> prLinkTimes = Enumerable.Repeat<byte>(3, getMaxListLenForVersion(kduVersion)).ToList();
            if (programLinkTimes != null)
            {
                for (int i = 0; i < programLinkTimes.Count; i++)
                {   // copy and clamp values
                    prLinkTimes[i] = programLinkTimes[i];
                    if (prLinkTimes[i] > 100)
                        prLinkTimes[i] = 100;
                    else if (prLinkTimes[i] < 3)
                        prLinkTimes[i] = 3;
                }
            }

            if (prNums.Count != prLinkModes.Count)
                throw new ArgumentException("Number of programs not equal to number of link modes", nameof(programNumbers));
            else if (prNums.Count > getMaxListLenForVersion(kduVersion))
                throw new ArgumentException($"Too many sequence items, max for KDU version {kduVersion} is {getMaxListLenForVersion(kduVersion)}. You are using the constructor that takes an IList of programs. Did you mean to use the constructor that takes the serialized byte array (or modbus tcp bytes)?", nameof(programNumbers));
            else if (prLinkModes.Count > getMaxListLenForVersion(kduVersion))
                throw new ArgumentException($"Too many sequence items, max for KDU version {kduVersion} is {getMaxListLenForVersion(kduVersion)}", nameof(programLinkModes));
            else if (prLinkTimes.Count > getMaxListLenForVersion(kduVersion))
                throw new ArgumentException($"Too many sequence items, max for KDU version {kduVersion} is {getMaxListLenForVersion(kduVersion)}", nameof(programLinkTimes));

            this.programNumbers = prNums;
            this.programLinkModes = prLinkModes;
            this.programLinkTimes = prLinkTimes;            
        }

        /// <summary>
        /// creates a sequence (of programs) to send to a kducer controller
        /// </summary>
        /// <param name="programNumbers">list of program numbers</param>
        /// <param name="programLinkModes">list of program transition modes. 0 = "time" (default), 1 = "press OK", 2 = "auto"</param>
        /// <param name="barcode">optional barcode for sequence selection via barcode, max 16 ASCII characters</param>
        /// <param name="kduVersion">optional, to specify older sequence structure from KDU firmware v37 (max 16 programs per sequence). v38 and later (default) have max 32 programs per sequence</param>
        /// <exception cref="ArgumentException">if basic parameter validation fails</exception>
        public KducerSequenceOfTighteningPrograms(IList<byte> programNumbers, IList<byte> programLinkModes, string barcode = "", ushort kduVersion = 38) :
            this(programNumbers, programLinkModes, null, barcode, kduVersion )
        { }

        /// <summary>
        /// creates a sequence (of programs) to send to a kducer controller
        /// uses default link mode time for all programs (refer to the KDU manual)
        /// </summary>
        /// <param name="programNumbers">list of program numbers</param>
        /// <param name="barcode">optional barcode for sequence selection via barcode, max 16 ASCII characters</param>
        /// <param name="kduVersion">optional, to specify older sequence structure from KDU firmware v37 (max 16 programs per sequence). v38 and later (default) have max 32 programs per sequence</param>
        /// <exception cref="ArgumentException">if basic parameter validation fails</exception>
        public KducerSequenceOfTighteningPrograms(IList<byte> programNumbers, string barcode = "", ushort kduVersion = 38) :
            this(programNumbers, null, null, barcode, kduVersion )
        { }

        /// <summary>
        /// creates a sequence object from the corresponding modbus holding registers (112 or 64 byte array depending on KDU version).
        /// the data is expected to come from the corresponding Kducer method, or from deserializing a serialized sequence.
        /// as such, NO data validation is done beyond the length of the array passed.
        /// note: the sequence letter (number) is not included.
        /// </summary>
        /// <param name="sequenceModbusHoldingRegistersAsByteArray"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException">length of byte array must be 112 (KDU v38 and later) or 64 (KDU v37 and earlier)</exception>
        public KducerSequenceOfTighteningPrograms(byte[] sequenceModbusHoldingRegistersAsByteArray)
        {
            if (sequenceModbusHoldingRegistersAsByteArray == null)
                throw new ArgumentNullException(nameof(sequenceModbusHoldingRegistersAsByteArray));

            int numberOfItems;

            if (sequenceModbusHoldingRegistersAsByteArray.Length == 112)    // v38/later format
            {
                kduVersion = 38;
                numberOfItems = getMaxListLenForVersion(38);
            }
            else if (sequenceModbusHoldingRegistersAsByteArray.Length == 64)    // v37/prior format
            {
                kduVersion = 37;
                numberOfItems = getMaxListLenForVersion(37);
            }
            else
                throw new ArgumentException($"You are using the constructor for Modbus Holding Registers Bytes. Byte array length should be 64 or 112 bytes. Did you mean to use the constructor with an IList of programs?", nameof(sequenceModbusHoldingRegistersAsByteArray));

            barcode = ModbusByteConversions.ModbusBytesToAsciiString(sequenceModbusHoldingRegistersAsByteArray, 0, 16).Replace(',', '.'); // no commas in barcodes
            programNumbers = new List<byte>(numberOfItems);
            programLinkModes = new List<byte>(numberOfItems);
            programLinkTimes = new List<byte>(numberOfItems);
            for (int i = 0; i < numberOfItems; i++)
            {
                programNumbers.Add(sequenceModbusHoldingRegistersAsByteArray[16 + i]);
                programLinkModes.Add(sequenceModbusHoldingRegistersAsByteArray[16 + i + numberOfItems]);
                programLinkTimes.Add(sequenceModbusHoldingRegistersAsByteArray[16 + i + numberOfItems + numberOfItems]);
            }
        }

        /// <summary> the list of programs as an array of bytes </summary>
        public byte[] getProgramsAsByteArray() {  return programNumbers.ToArray(); }
        /// <summary> the link modes between programs as an array of bytes </summary>
        public byte[] getLinkModesAsByteArray() { return programLinkModes.GetRange(0,programNumbers.Count).ToArray(); }
        /// <summary> the link times between programs as an array of bytea. note: the default value for link times is 3 (0.3 sec), even if the none of the link modes are times </summary>
        public byte[] getLinkTimesAsByteArray() { return programLinkTimes.GetRange(0, programNumbers.Count).ToArray(); }
        /// <summary>string of up to 16 ascii characters</summary>
        public string GetBarcode() { return barcode; }
        private static int getMaxListLenForVersion(ushort kduVersion)
        {
            if (kduVersion >= 38)
                return 32;
            else
                return 16;
        }

        private static byte getMaxProgramNumberForVersion(ushort kduVersion)
        {
            if (kduVersion >= 38)
                return 200;
            else
                return 64;
        }

        private static byte getMaxTransitionModeForVersion(ushort kduVersion)
        {
            return 2;
        }

        /// <summary>
        /// returns the byte array representing this sequence
        /// use this to serialize the sequence
        /// the foramt differs depending on the kdu version specified when creating it
        /// </summary>
        /// <returns>the byte array (K-Ducer Modbus TCP format) representing this sequence</returns>
        public byte[] getSequenceModbusHoldingRegistersAsByteArray()
		{
            if (kduVersion >= 38)
                return getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater();
            else
                return getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior();
        }

        internal byte[] getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater()
        {
            byte[] registerBytes = new byte[112];

            char[] barcode_chars = barcode.ToCharArray();
            Encoding.ASCII.GetBytes(barcode_chars, 0, barcode_chars.Length, registerBytes, 0);

            programNumbers.CopyTo(registerBytes, 16);
            programLinkModes.CopyTo(registerBytes, 16 + 32);
            Enumerable.Repeat<byte>(3, getMaxListLenForVersion(38)).ToArray().CopyTo(registerBytes, 16 + 32 + 32);
            programLinkTimes.CopyTo(registerBytes, 16 + 32 + 32);

            return registerBytes;
        }

        internal byte[] getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior()
        {
            int count = programNumbers.Count;
            if (count > 16)
                throw new InvalidOperationException("Requested sequence registers for KDU v37, but sequence has more than 16 programs");

            byte[] registerBytes = new byte[64];

            char[] barcode_chars = barcode.ToCharArray();
            Encoding.ASCII.GetBytes(barcode_chars, 0, barcode_chars.Length, registerBytes, 0);

            programNumbers.GetRange(0, count).CopyTo(registerBytes, 16);
            programLinkModes.GetRange(0, count).CopyTo(registerBytes, 16 + 16);
            Enumerable.Repeat<byte>(3, getMaxListLenForVersion(37)).ToArray().CopyTo(registerBytes, 16 + 16 + 16);
            programLinkTimes.GetRange(0, count).CopyTo(registerBytes, 16 + 16 + 16);

            return registerBytes;
        }
    }
}
