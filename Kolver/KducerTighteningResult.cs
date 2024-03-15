using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Kolver
{
    public class KducerTighteningResult
    {
        private readonly byte[] tighteningResultInputRegistersAsByteArray;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="tighteningResultInputRegistersAsByteArray">the byte array from reading 67 input registers starting at address 295</param>
        /// <param name="replaceTimeStampWithCurrentTime">replaces the timestamp in the byte array with the local machine timestamp at the time of creating this object</param>
        public KducerTighteningResult(byte[] tighteningResultInputRegistersAsByteArray, bool replaceTimeStampWithCurrentTime = true)
        {
            this.tighteningResultInputRegistersAsByteArray = new byte[tighteningResultInputRegistersAsByteArray.Length];
            tighteningResultInputRegistersAsByteArray.CopyTo(this.tighteningResultInputRegistersAsByteArray, 0);

            if(replaceTimeStampWithCurrentTime)
            {
                DateTime localDateTime = DateTime.Now;
                byte[] timeStampAsModbusBytes = new byte[12];

                BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((ushort)(localDateTime.Year % 2000))).CopyTo(timeStampAsModbusBytes, 0);
                BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((ushort)localDateTime.Month)).CopyTo(timeStampAsModbusBytes, 2);
                BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((ushort)localDateTime.Day)).CopyTo(timeStampAsModbusBytes, 4);
                BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((ushort)localDateTime.Hour)).CopyTo(timeStampAsModbusBytes, 6);
                BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((ushort)localDateTime.Minute)).CopyTo(timeStampAsModbusBytes, 8);
                BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((ushort)localDateTime.Second)).CopyTo(timeStampAsModbusBytes, 10);

                timeStampAsModbusBytes.CopyTo(tighteningResultInputRegistersAsByteArray, 102);
            }
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>final torque result in cNm. if using running torque modes, this is the clamping torque value</returns>
        public ushort GetTorqueResult()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 46);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>maximum torque measured at any time during the tightening in cNm</returns>
        public ushort GetPeakTorque()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 48);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>running (prevailing) torque in cNm, if running torque mode is active, 0 otherwise</returns>
        public ushort GetRunningTorque()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 52);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>prevailing (running) torque in cNm, if running torque mode is active, 0 otherwise</returns>
        public ushort GetPrevailingTorque()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 52);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the rotational angle result in degrees</returns>
        public ushort GetAngleResult()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 56);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>true if screw result is OK</returns>
        public bool IsScrewOK()
        {
            return ( tighteningResultInputRegistersAsByteArray[16] == 1 );
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>string up to 16 ASCII characters with the barcode, if any was scanned or sent via modbus</returns>
        public string GetBarcode()
        {
            return new string(System.Text.Encoding.ASCII.GetChars(tighteningResultInputRegistersAsByteArray, 0, 16));
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>string up to 30 ASCII characters with the program name</returns>
        public string GetProgramDescription()
        {
            return new string(System.Text.Encoding.ASCII.GetChars(tighteningResultInputRegistersAsByteArray, 72, 30));
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the program number of this result</returns>
        public ushort GetProgramNr()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 18);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the screwdriver motor model</returns>
        public string GetScrewdriverModel()
        {
            return kdsModels[TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 20)];
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the screwdriver serial number</returns>
        public uint GetScrewdriverSerialNr()
        {
            return FourModbusBytesToUint(tighteningResultInputRegistersAsByteArray, 22);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the target torque of this result in cNm</returns>
        public ushort GetTargetTorque()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 30);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the target speed in RPM. final speed, not downshift speed</returns>
        public ushort GetTargetSpeed()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 32);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the tightening time of this result in milliseconds</returns>
        public ushort GetScrewTime()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 34);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the number of OK up to and including this result. NOK screws are not counted, for example: if this is the very screw being tightened and the result is NOK, the value will be zero.</returns>
        public ushort GetScrewsOKcount()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 36);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the target (total) number of screws of the program of this result</returns>
        public ushort GetTargetScrewsOKcount()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 38);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the sequence number of this result, if sequence is being used. 0=None, A=1, B=2 etc</returns>
        public ushort GetSequenceNr()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 40);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>the sequence letter of this result, if sequence is being used. "-" for none, "A", "B", etc</returns>
        public char GetSequence()
        {
            char seq = (char)(64 + GetSequenceNr());
            if (seq == 64)
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
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 42);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>number of programs in the sequence. From 1 up to 32</returns>
        public ushort GetNrProgramsInSequence()
        {
            return TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 44);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns>short descriptive string of the result, for example "Screw OK" or "Over Max Torque"</returns>
        public string GetResultCode()
        {
            return resultNotesByIndex[TwoModbusBytesToUshort(tighteningResultInputRegistersAsByteArray, 40)];
        }
        private ushort TwoModbusBytesToUshort(byte[] mbBytes, int index)
        {
            return (ushort)IPAddress.NetworkToHostOrder((ushort)BitConverter.ToUInt16(mbBytes, index));
        }

        private uint FourModbusBytesToUint(byte[] mbBytes, int index)
        {
            return (uint)IPAddress.NetworkToHostOrder((int)BitConverter.ToUInt32(mbBytes, index));
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
            "Err Overcurrent Protection", "Err Overcurrent Protection", "Err Temperature Protection",
            "Under Min Torque", "Over Max Torque", "", "Over Max Angle", "Under Min Angle", "Pre-Reverse Incomplete",
            "", "Error KDS connection", "Running Torque Incomplete", "Running Torque Under Min", "Running Torque Over Max"
        };


        public const string csvColumnHeader = "Barcode,Result,Program nr,Program descr,Model,S/N,Target,Target units,Duration,Final Speed,OK screw count,Screw qty in program,Sequence,Seq. program index,Seq. program qty,Torque result,Peak torque,Running Torque Mode,Running Torque,Total torque,Angle result,Angle start at mode,Angle start at torque target,Angle start at angle value,Downshift mode,Downshift speed,Downshift threshold,Torque units,Angle units,Speed units,Time units,Date-Time,Notes,Torque chart units,Torque chart x-interval ms,First torque chart point,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,Last torque chart point,Angle chart x-interval ms,First angle chart point,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,Last angle chart point\n";
        public string GetCSVheader() { return csvColumnHeader; }
        /// <summary>
        /// generates a CSV representation of the results. get the column headers via GetCSVheader
        /// </summary>
        /// <param name="torqueUnitsForResStr">choose between "cNm","mNm","Nm","lbf-in","kgf-cm","lbf-ft","ozf-in"</param>
        /// <returns>a CSV (comma separated values) string representation of the results
        /// it follows the same format as the data saved by the KDU controller to a the USB drive
        /// can be read and visualized by programs like K-Graph (assuming the graph data was included)
        /// </returns>
        public string GetResultsAsCSVstring(string torqueUnitsForResStr = "cNm")
        {
            // this function was taken from another program and used a list of ushorts as the modbus data
            ushort[] mbResRegs_ush = new ushort[67];
            for(int i = 0; i < tighteningResultInputRegistersAsByteArray.Length; i+=2)
            {
                mbResRegs_ush[i / 2] = BitConverter.ToUInt16(tighteningResultInputRegistersAsByteArray, i);
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
            double sTime = mbResRegs[17] / 1000.0;
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
                seqPrgIdx = mbResRegs[21].ToString();
                totPrgIdx = mbResRegs[22].ToString();
            }

            string torque = ConvertModbusCNmTorqueStr(mbResRegs[23], torqueUnitsForResStr);
            string maxTorque = ConvertModbusCNmTorqueStr(mbResRegs[24], torqueUnitsForResStr);
            int rtMode = mbResRegs[25];
            string rTorque = ConvertModbusCNmTorqueStr(mbResRegs[26], torqueUnitsForResStr);
            string totalTorque = ConvertModbusCNmTorqueStr(mbResRegs[27], torqueUnitsForResStr);
            int taMode = mbResRegs[57];
            int targetAngle = mbResRegs[58];
            string target = (taMode == 0) ? targetTorque : targetAngle.ToString();

            string targetUnits = torqueUnitsForResStr;

            string angToDSSpeed = "";
            for (int i = 28; i < 28 + 6; i++)
            {
                angToDSSpeed += mbResRegs[i].ToString() + ",";
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
                barcode, okNok, mbResRegs[9].ToString(), progrDesc, model, serialNr.ToString(),
                target.ToString(), torqueUnitsForResStr, sTime.ToString(), finalSpeed.ToString(),
                screwsMade.ToString(), nScrews.ToString(), sequence, seqPrgIdx, totPrgIdx, torque.ToString(),
                maxTorque.ToString(), rtMode.ToString(), rTorque.ToString(), totalTorque.ToString()
            };

            string csvSoFar = string.Join(",", dataInOrder) + "," + angToDSSpeed;

            int modelNr = mbResRegs[10];
            string graphUnits = (modelNr < 5) ? "mNm" : "cNm";

            List<string> dataInOrder2 = new List<string>()
            {
                dsTorque.ToString(), targetUnits, "deg,rpm,sec", dateStr, resultTxt, "deg,1", graphUnits, "1"
            };

            resultStringCSV = csvSoFar + string.Join(",", dataInOrder2);
            return resultStringCSV;
        }

        private double GetConversionFactorCNmToUnits(string desiredTorqueUnits)
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
        private double ConvertModbusCNmTorqueFloat(double value, string desired_torque_units)
        {
            return value * GetConversionFactorCNmToUnits(desired_torque_units);
        }
        private string ConvertModbusCNmTorqueStr(double value, string desired_torque_units)
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
