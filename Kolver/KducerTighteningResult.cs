// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Kolver
{
    /// <summary>
    /// Representation of the tightening result data provided by the KDU controller
    /// Provides convenience methods to get the data
    /// </summary>
    public class KducerTighteningResult
    {
        private readonly byte[] tighteningResultInputRegistersAsByteArray;
        private readonly KducerTorqueAngleTimeGraph torqueAngleGraph;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="tighteningResultInputRegistersAsByteArray">the byte array from reading 67 input registers starting at address 295</param>
        /// <param name="replaceTimeStampWithCurrentTime">replaces the timestamp in the byte array with the local machine timestamp at the time of creating this object</param>
        /// <param name="torqueAngleGraph">the optional torque-angle graph data corresponding to this result</param>
        public KducerTighteningResult(byte[] tighteningResultInputRegistersAsByteArray, bool replaceTimeStampWithCurrentTime, KducerTorqueAngleTimeGraph torqueAngleGraph = null)
        {
            if (tighteningResultInputRegistersAsByteArray == null)
                throw new ArgumentNullException(nameof(tighteningResultInputRegistersAsByteArray));

            this.tighteningResultInputRegistersAsByteArray = new byte[tighteningResultInputRegistersAsByteArray.Length];
            tighteningResultInputRegistersAsByteArray.CopyTo(this.tighteningResultInputRegistersAsByteArray, 0);

            if (replaceTimeStampWithCurrentTime)
            {
                DateTime now = DateTime.Now;
                byte[] timeStampAsModbusBytes = new byte[12];

                ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)(now.Year % 2000), timeStampAsModbusBytes, 0);
                ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)now.Month, timeStampAsModbusBytes, 2);
                ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)now.Day, timeStampAsModbusBytes, 4);
                ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)now.Hour, timeStampAsModbusBytes, 6);
                ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)now.Minute, timeStampAsModbusBytes, 8);
                ModbusByteConversions.CopyUshortToBytesAsModbusBigendian((ushort)now.Second, timeStampAsModbusBytes, 10);

                timeStampAsModbusBytes.CopyTo(this.tighteningResultInputRegistersAsByteArray, 102);
            }

            if (torqueAngleGraph != null)
                this.torqueAngleGraph = torqueAngleGraph;
            else
                this.torqueAngleGraph = new KducerTorqueAngleTimeGraph(new byte[142], new byte[142]);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>final torque result in cNm. if using running torque modes, this is the clamping torque value</returns>
        public ushort GetTorqueResult()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 46);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>maximum torque measured at any time during the tightening in cNm</returns>
        public ushort GetPeakTorque()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 48);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>running (prevailing) torque in cNm, if running torque mode is active, 0 otherwise</returns>
        public ushort GetRunningTorque()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 52);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>prevailing (running) torque in cNm, if running torque mode is active, 0 otherwise</returns>
        public ushort GetPrevailingTorque()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 52);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the rotational angle result in degrees</returns>
        public ushort GetAngleResult()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 56);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>true if screw result is OK</returns>
        public bool IsScrewOK()
        {
            return (tighteningResultInputRegistersAsByteArray[17] == 1);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>string up to 16 ASCII characters with the barcode, if any was scanned or sent via modbus</returns>
        public string GetBarcode()
        {
            return ModbusByteConversions.ModbusBytesToAsciiString(tighteningResultInputRegistersAsByteArray, 0, 16).TrimEnd('\0');
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>string up to 30 ASCII characters with the program name</returns>
        public string GetProgramDescription()
        {
            return ModbusByteConversions.ModbusBytesToAsciiString(tighteningResultInputRegistersAsByteArray, 72, 30).TrimEnd('\0');
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the program number of this result</returns>
        public ushort GetProgramNr()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 18);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the screwdriver motor model</returns>
        public string GetScrewdriverModel()
        {
            return kdsModels[ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 20)];
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the screwdriver serial number</returns>
        public uint GetScrewdriverSerialNr()
        {
            return ModbusByteConversions.FourModbusBigendianBytesToUint(tighteningResultInputRegistersAsByteArray, 22);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the target torque of this result in cNm</returns>
        public ushort GetTargetTorque()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 30);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the target speed in RPM. final speed, not downshift speed</returns>
        public ushort GetTargetSpeed()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 32);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the tightening time of this result in milliseconds</returns>
        public ushort GetScrewTime()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 34);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the number of OK up to and including this result. NOK screws are not counted, for example: if this is the very screw being tightened and the result is NOK, the value will be zero.</returns>
        public ushort GetScrewsOKcount()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 36);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the target (total) number of screws of the program of this result</returns>
        public ushort GetTargetScrewsOKcount()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 38);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the sequence number of this result, if sequence is being used. 0=None, A=1, B=2 etc</returns>
        public ushort GetSequenceNr()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 40);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the sequence letter of this result, if sequence is being used. "-" for none, "A", "B", etc</returns>
        public char GetSequence()
        {
            char seq = (char)('A' - 1 + GetSequenceNr());
            if (seq == (char)('A' - 1))
                return '-';
            else
                return seq;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>Index of current program in the sequence. From 1 up to 32</returns>
        public ushort GetProgramIdxInSequence()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 42);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>number of programs in the sequence. From 1 up to 32</returns>
        public ushort GetNrProgramsInSequence()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 44);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>short descriptive string of the result, for example "Screw OK" or "Over Max Torque"</returns>
        public string GetResultCode()
        {
            return resultNotesByIndex[ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 70)];
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>timestamp of result as string in ISO 8601 format YYYY-MM-DD HH:MM:SS
        /// Note: the data may have been overwritten with the local machine timestamp if replaceTimeStampWithCurrentTime was true (default) when this result was created
        /// </returns>
        public string GetResultTimestamp()
        {
            ushort YY = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 102);
            ushort MM = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 104);
            ushort DD = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 106);
            ushort hh = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 108);
            ushort mm = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 110);
            ushort ss = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, 112);
            return $"20{YY:D2}-{MM:D2}-{DD:D2} {hh:D2}:{mm:D2}:{ss:D2}";
        }

        private static readonly List<string> kdsModels = new List<string>
        {
            "Not connected", "KDS-PL6", "KDS-PL10", "KDS-PL15", "KDS-MT1.5", "KDS-PL20", "KDS-PL30ANG",
            "KDS-PL35", "KDS-PL45ANG", "KDS-PL50", "KDS-PL70ANG"
        };

        private static readonly List<string> resultNotesByIndex = new List<string>
        {
            "", "", "", "", "", "", "", "", "", "", "", "", "",
            "Screw OK", "Angle OK", "Reverse Torque OK", "Run Time OK",
            "Under Min Time", "Over Max Time", "", "", "Error PV Torque", "Reverse Torque Error",
            "Auto Reverse Incomplete", "Angle Not Reached", "Release Lever Error", "Err Overcurrent Protection",
            "Err Overcurrent Protection", "Err Overcurrent Protection", "Err Temperature Protection","Err Motor Protection",
            "Under Min Torque", "Over Max Torque", "", "Over Max Angle", "Under Min Angle", "Pre-Reverse Incomplete",
            "", "Error KDS connection", "Running Torque Incomplete", "Running Torque Under Min", "Running Torque Over Max"
        };

        /// <summary>
        /// CSV column header for the CSV representation of the results (single line, KDU v37 style)
        /// </summary>
        public static string csvColumnHeaderSingleLine { get; } = "Barcode,Result,Program nr,Program descr,Model,S/N,Target,Target units,Duration,Final Speed,OK screw count,Screw qty in program,Sequence,Seq. program index,Seq. program qty,Torque result,Peak torque,Running Torque Mode,Running Torque,Total torque,Angle result,Angle start at mode,Angle start at torque target,Angle start at angle value,Downshift mode,Downshift speed,Downshift threshold,Torque units,Angle units,Speed units,Time units,Date-Time,Notes,Torque chart units,Torque chart x-interval ms,First torque chart point,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,Last torque chart point,Angle chart x-interval ms,First angle chart point,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,Last angle chart point\n";
        /// <summary>
        /// generates a CSV representation of the results  (single line, KDU v37 style)
        /// get the column headers via csvColumnHeaderSingleLine
        /// </summary>
        /// <param name="torqueUnitsForResStr">choose between "cNm","mNm","Nm","lbf-in","kgf-cm","lbf-ft","ozf-in"</param>
        /// <returns>a CSV (comma separated values) string representation of the results
        /// it follows the same format as the data saved by the KDU controller to a the USB drive
        /// can be read and visualized by programs like K-Graph (assuming the graph data was included)
        /// </returns>
        public string GetResultsAsCSVstringSingleLine(string torqueUnitsForResStr = "Nm")
        {
            // this function was taken from another program and used a list of ushorts as the modbus data
            ushort[] mbResRegs_ush = new ushort[67];
            for(int i = 0; i < tighteningResultInputRegistersAsByteArray.Length; i+=2)
            {
                mbResRegs_ush[i / 2] = ModbusByteConversions.TwoModbusBigendianBytesToUshort(tighteningResultInputRegistersAsByteArray, i);
            }
            List<int> mbResRegs = mbResRegs_ush.Select(x => (int)x).ToList();
            string resultStringCSV = "";

            string barcode = GetBarcode();
            string progrDesc = GetProgramDescription();
            string okNok = (mbResRegs[8] == 1) ? "OK" : "NOK";
            string model = kdsModels[mbResRegs[10]];
            byte[] srBytes = new byte[4];
            srBytes[0] = (byte)mbResRegs[12];
            srBytes[1] = (byte)(mbResRegs[12] >> 8);
            srBytes[2] = (byte)mbResRegs[11];
            srBytes[3] = (byte)(mbResRegs[11] >> 8);
            int serialNr = BitConverter.ToInt32(srBytes, 0);
            string targetTorque = ConvertModbusCNmTorqueStr(mbResRegs[15], torqueUnitsForResStr);
            int finalSpeed = mbResRegs[16];
            string sTime = $"{(mbResRegs[17] / 1000.0):F2}";
            int screwsMade = mbResRegs[18];
            int nScrews = mbResRegs[19];
            string sequence;
            string seqPrgIdx;
            string totPrgIdx;

            if (mbResRegs[20] == 0)
            {
                sequence = "";
                seqPrgIdx = "";
                totPrgIdx = "";
            }
            else
            {
                sequence = ((char)('A' - 1 + mbResRegs[20])).ToString();
                seqPrgIdx = $"{mbResRegs[21]}";
                totPrgIdx = $"{mbResRegs[22]}";
            }

            string torque = ConvertModbusCNmTorqueStr(mbResRegs[23], torqueUnitsForResStr);
            string maxTorque = ConvertModbusCNmTorqueStr(mbResRegs[24], torqueUnitsForResStr);
            int rtMode = mbResRegs[25];
            string rTorque = ConvertModbusCNmTorqueStr(mbResRegs[26], torqueUnitsForResStr);
            string totalTorque = ConvertModbusCNmTorqueStr(mbResRegs[27], torqueUnitsForResStr);
            int taMode = mbResRegs[57];
            int targetAngle = mbResRegs[58];
            string target = (taMode == 0) ? targetTorque : $"{targetAngle}";

            string targetUnits = torqueUnitsForResStr;

            string angToDSSpeed = "";
            for (int i = 28; i < 28 + 6; i++)
            {
                angToDSSpeed += $"{mbResRegs[i]},";
            }

            string dsTorque = ConvertModbusCNmTorqueStr(mbResRegs[34], torqueUnitsForResStr);

            string resultTxt = " ";
            if (mbResRegs[35] < resultNotesByIndex.Count)
            {
                resultTxt += resultNotesByIndex[mbResRegs[35]];
            }

            string dateStr = $"20{mbResRegs[51]}/{mbResRegs[52]}/{mbResRegs[53]} {mbResRegs[54]}.{mbResRegs[55]}.{mbResRegs[56]}";

            List<string> dataInOrder = new List<string>()
            {
                barcode, okNok, $"{mbResRegs[9]}", progrDesc, model, $"{serialNr}",
                target, torqueUnitsForResStr, sTime, $"{finalSpeed}",
                $"{screwsMade}", $"{nScrews}", sequence, seqPrgIdx, totPrgIdx, torque,
                maxTorque, $"{rtMode}", rTorque, totalTorque
            };

            string csvSoFar = string.Join(",", dataInOrder) + "," + angToDSSpeed;

            int modelNr = mbResRegs[10];
            string graphUnits = (modelNr < 5) ? "mNm" : "cNm";

            string graphTimeInterval = $"{torqueAngleGraph.getTimeIntervalBetweenConsecutivePoints()}";
            List<string> dataInOrder2 = new List<string>()
            {
                dsTorque.ToString(), targetUnits, "deg,rpm,sec", dateStr, resultTxt, graphUnits,graphTimeInterval,torqueAngleGraph.getTorqueSeriesAsCsvWith70columns(), graphTimeInterval,torqueAngleGraph.getAngleSeriesAsCsvWith70columns() //, "deg,1", graphUnits, "1"
            };

            resultStringCSV = csvSoFar + string.Join(",", dataInOrder2);
            return resultStringCSV;
        }

        private static double GetConversionFactorCNmToUnits(string desiredTorqueUnits)
        {
            switch (desiredTorqueUnits)
            {
                case "mNm":
                case "Nmm":
                case "N-mm":
                    return 10;
                case "Nm":
                case "N-m":
                    return 0.01;
                case "lbf-in":
                case "lbs-in":
                    return 0.0885074545;
                case "lbf-ft":
                case "lbs-ft":
                    return 0.0073756212;
                case "kgf-cm":
                case "kg-cm":
                    return 0.1019716213;
                case "ozf-in":
                case "oz-in":
                    return 1.4161192894;
                default:
                    return 1;
            }
        }
        private static double ConvertModbusCNmTorqueFloat(double value, string desired_torque_units)
        {
            return value * GetConversionFactorCNmToUnits(desired_torque_units);
        }
        private static string ConvertModbusCNmTorqueStr(double value, string desired_torque_units)
        {
            value = ConvertModbusCNmTorqueFloat(value, desired_torque_units);
            if (value > 100)
            {
                return $"{value:0.#}";
            }
            else if (value > 10)
            {
                return $"{value:0.##}";
            }
            else
            {
                return $"{value:0.###}";
            }
        }
    }

}
