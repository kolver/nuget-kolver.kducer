using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kolver
{
    /// <summary>
    /// used to read the .kdu files saved to a usb drive via the KDU controller USB menu
    /// </summary>
    public static class KducerKduDataFileReader
    {
        /// <summary>
        /// reads a .kdu data file from KDU-1A v37 or later
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns>a tuple containing settings,sequences, and programs that can be passed directly to the corresponding Kducer method</returns>
        /// <exception cref="InvalidDataException">passed an invalid file</exception>
        /// <exception cref="IOException">system failure reading file</exception>
        public static async Task<Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>>> ReadKduDataFile(string filePath)
        {
            byte[] data;
            using (FileStream kduFs = File.OpenRead(filePath))
            {
                FileInfo fileInfo = new FileInfo(filePath);

                long len = fileInfo.Length;
                if (len != 15072 && len != 48096)
                    throw new InvalidDataException($"{filePath} does not appear to be a valid kdu file originating from KDU-1A v36 or newer");

                data = new byte[len];
                int bytesRead = 0;
                while (bytesRead < len)
                {
                    int nBytesChunkRead = await kduFs.ReadAsync(data, bytesRead, (int)len - bytesRead).ConfigureAwait(false);
                    bytesRead += nBytesChunkRead;
                    if (nBytesChunkRead == 0)
                        throw new IOException($"Failed to read bytes from {filePath}");
                }
            }

            // check version
            if (data[0] < 0x09)
                throw new InvalidDataException($"{filePath} does not appear to be a valid kdu file originating from KDU-1A v36 or newer");

            return ParseKduConfBytes(data);
        }

        internal static Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> ParseKduConfBytes(byte[] data)
        {
            // read settings
            KducerControllerGeneralSettings settings = new KducerControllerGeneralSettings();
            settings.SetLanguage(ReadUshortFromBytes(data, 30));
            if (data[0] < 0x0d) // v41
                settings.SetPassword(ReadUshortFromBytes(data, 33));
            else
                settings.SetPasswordLong(ReadUintFromBytes(data, 33));
            settings.SetPasswordOnOff(data[37] == 1);
            settings.SetCmdOkEscResetSource(data[38]);
            settings.SetRemoteProgramSource(data[39]);
            settings.SetRemoteSequenceSource(data[40]);
            settings.SetResetMode(data[42]);
            settings.SetBarcodeMode(data[43]);
            settings.SetSwbxAndCbs880Mode(data[44]);
            settings.SetLockIfCn5NotConnectedOnOff(data[110] == 1);
            settings.SetFastDock05ModeOnOff(data[111] == 1);
            settings.SetBuzzerSoundsOnOff(data[112] == 1);
            settings.SetResultsFormat(data[113]);
            settings.SetStationName(ReadStringFromBytes(data, 114, 25));
            settings.SetCalibrationReminderMode(data[139]);
            settings.SetCalibrationReminderInterval(ReadUintFromBytes(data, 140));
            settings.SetLockIfUsbNotConnectedOnOff(data[145] == 1);
            settings.SetInvertLogicCn3InStopOnOff(data[146] == 1);
            settings.SetInvertLogicCn3InPieceOnOff(data[147] == 1);
            settings.SetSkipScrewButtonOnOff(data[148] == 1);
            settings.SetShowReverseTorqueAndAngleOnOff(data[149] == 1);
            settings.SetCn3BitxPrSeqInputSelectionMode(data[150]);
            settings.SetKtlsArm1Model((byte)(data[151] & 0xF));
            settings.SetKtlsArm1Model((byte)(data[151] >> 4));
            if (data[0] < 0x0d) // v41
                settings.SetAllowProgSeqChangeWithoutPasscodeOnOff(false);
            else
                settings.SetAllowProgSeqChangeWithoutPasscodeOnOff(data[176] == 1);

            // read sequences
            int seqLen = 32;
            int numSeqs = 24;
            int seqChunkLen = 128;
            int seqSkipLen = 16;
            ushort kduVersion = 38;
            if (data[0] < 0x0b)
            {
                seqLen = 16;
                numSeqs = 8;
                seqChunkLen = 64;
                seqSkipLen = 0;
                kduVersion = 37;
            }
            Dictionary<ushort, KducerSequenceOfTighteningPrograms> seqDict = new Dictionary<ushort, KducerSequenceOfTighteningPrograms>(numSeqs);
            List<byte> progs;
            List<byte> linkModes;
            List<byte> linkTimes;
            for (ushort seqIdx = 0; seqIdx < numSeqs; seqIdx++)
            {
                progs = new List<byte>(new byte[seqLen]);
                linkModes = new List<byte>(new byte[seqLen]);
                linkTimes = Enumerable.Repeat<byte>(3, seqLen).ToList();
                progs[0] = 1;

                string seqBarcode = ReadStringFromBytes(data, 224 + seqIdx * seqChunkLen, 16);
                int baseIdx = 224 + seqIdx * seqChunkLen + seqSkipLen + 16;
                for (int progIdx = 0; progIdx < seqLen; progIdx++)
                {
                    progs[progIdx] = data[baseIdx + progIdx];
                    linkModes[progIdx] = data[baseIdx + progIdx + seqLen];
                    linkTimes[progIdx] = data[baseIdx + progIdx + seqLen + seqLen];
                    if (linkTimes[progIdx] < 3 || linkTimes[progIdx] > 100)
                        linkTimes[progIdx] = 3;
                    if (linkModes[progIdx] > 2)
                        linkModes[progIdx] = 0;
                    if ( (kduVersion == 38 && progs[progIdx] > 200) || (kduVersion == 37 && progs[progIdx] > 64) || progs[progIdx] == 0)
                    {
                        progs[progIdx] = 0;  // sanitize corrupt file
                        break;
                    }
                }
                seqDict.Add((ushort)(seqIdx + 1), new KducerSequenceOfTighteningPrograms(progs, linkModes, linkTimes, seqBarcode, kduVersion));
            }

            // read programs
            int firstProgIdx = 224 + seqChunkLen * numSeqs;
            int numProgs = 200;
            if (data[0] < 0x0b) { numProgs = 64; }
            Dictionary<ushort, KducerTighteningProgram> progDict = new Dictionary<ushort, KducerTighteningProgram>(numProgs);
            for (ushort progIdx = 0; progIdx < numProgs; progIdx++)
            {
                KducerTighteningProgram prog = new KducerTighteningProgram(new byte[230]);
                prog.SetTorqueTarget(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 0));
                prog.SetTorqueMax(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 4));
                prog.SetTorqueMin(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 8));
                prog.SetAngleTarget(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 12));
                prog.SetAngleMax(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 16));
                prog.SetAngleMin(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 20));
                prog.SetAngleStartAt(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 24));
                prog.SetFinalSpeed(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 28));
                prog.SetDownshiftInitialSpeed(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 32));
                prog.SetDownshiftThreshold(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 36));
                prog.SetTorqueAngleMode(data[firstProgIdx + 224 * progIdx + 40]);
                prog.SetDownshiftAtTorqueOnOff(data[firstProgIdx + 224 * progIdx + 41] >= 1);
                prog.SetDownshiftAtAngleOnOff(data[firstProgIdx + 224 * progIdx + 41] == 2);
                prog.SetAngleStartAtMode(data[firstProgIdx + 224 * progIdx + 42]);
                prog.SetRampOnOff(data[firstProgIdx + 224 * progIdx + 43] == 1);
                prog.SetRunTimeOnOff(data[firstProgIdx + 224 * progIdx + 44] == 1);
                prog.SetMinTimeOnOff(data[firstProgIdx + 224 * progIdx + 45] == 1);
                prog.SetMaxTimeOnOff(data[firstProgIdx + 224 * progIdx + 46] == 1);
                prog.SetRamp(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 47));
                prog.SetRuntime(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 51));
                prog.SetMintime(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 55));
                prog.SetTotalAngleMin(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 57));
                prog.SetMaxtime(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 59));
                prog.SetTotalAngleMax(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 61));
                prog.SetMaxPowerPhaseMode(data[firstProgIdx + 224 * progIdx + 63]);
                prog.SetMaxPowerPhaseTime(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 64));
                prog.SetMaxPowerPhaseAngle(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 68));
                prog.SetReverseSpeed(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 72));
                prog.SetMaxReverseTorque(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 76));
                prog.SetPreTighteningReverseMode(data[firstProgIdx + 224 * progIdx + 80]);
                prog.SetPreTighteningReverseTime(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 81));
                prog.SetPreTighteningReverseAngle(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 85));
                prog.SetPreTighteningReverseDelay(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 89));
                prog.SetAfterTighteningReverseMode(data[firstProgIdx + 224 * progIdx + 94]);
                prog.SetAfterTighteningReverseTime(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 95));
                prog.SetAfterTighteningReverseAngle(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 99));
                prog.SetKtlsSensor1Tolerance(data[firstProgIdx + 224 * progIdx + 103]);
                prog.SetKtlsSensor2Tolerance(data[firstProgIdx + 224 * progIdx + 104]);
                prog.SetKtlsSensor3Tolerance(data[firstProgIdx + 224 * progIdx + 105]);
                prog.SetAfterTighteningReverseDelay(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 106));
                prog.SetSerialPrintOnOff(data[firstProgIdx + 224 * progIdx + 111] == 1);
                prog.SetSubstituteTorqueUnits((byte)(data[firstProgIdx + 224 * progIdx + 112] >> 4));
                prog.SetUseDock05Screwdriver2OnOff(data[firstProgIdx + 224 * progIdx + 113] == 1);
                prog.SetBarcode(ReadStringFromBytes(data, firstProgIdx + 224 * progIdx + 117, 16));
                prog.SetSocket(data[firstProgIdx + 224 * progIdx + 133]);
                prog.SetPressOkOnOff(data[firstProgIdx + 224 * progIdx + 137] == 1);
                prog.SetPressEscOnOff(data[firstProgIdx + 224 * progIdx + 138] == 1);
                prog.SetLeverErrorOnOff(data[firstProgIdx + 224 * progIdx + 139] == 1);
                prog.SetReverseAllowedOnOff(data[firstProgIdx + 224 * progIdx + 140] == 1);
                prog.SetCounterclockwiseTighteningOnOff(data[firstProgIdx + 224 * progIdx + 141] == 1);
                prog.SetNumberOfScrews(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 142));
                prog.SetDescription(ReadStringFromBytes(data, firstProgIdx + 224 * progIdx + 146, 30));
                prog.SetTorqueCompensationValue(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 176));
                prog.SetRunningTorqueMode(data[firstProgIdx + 224 * progIdx + 180]);
                prog.SetRunningTorqueWindowStart(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 181));
                prog.SetRunningTorqueWindowEnd(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 183));
                prog.SetRunningTorqueMin(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 185));
                prog.SetRunningTorqueMax(ReadUshortFromBytes(data, firstProgIdx + 224 * progIdx + 189));
                progDict.Add((ushort)(progIdx + 1), prog);
            }

            for (ushort progIdx = 1; progIdx <= numProgs; progIdx++)
                if (progDict[progIdx].GetTorqueTarget() == ushort.MaxValue)
                    progDict[progIdx] = new KducerTighteningProgram(); // sanitize corrupt file

            return new Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>>(settings, progDict, seqDict);
        }

        private static ushort ReadUshortFromBytes(byte[] source, int index)
        {
            if (BitConverter.IsLittleEndian)
                return (ushort)(source[index] | (source[index + 1] << 8));
            else
                return (ushort)((source[index] << 8) | source[index + 1]);
        }

        private static uint ReadUintFromBytes(byte[] source, int index)
        {
            if (BitConverter.IsLittleEndian)
                return (uint)((source[index]) |
                      (source[index + 1] << 8) |
                      (source[index + 2] << 16) |
                       source[index + 3] << 24);
            else
                return (uint)((source[index] << 24) |
                      (source[index + 1] << 16) |
                      (source[index + 2] << 8) |
                       source[index + 3]);
        }

        private static string ReadStringFromBytes(byte[] source, int index, int count)
        {
            char[] stringchars = Encoding.ASCII.GetChars(source, index, count);
            return new string(stringchars);
        }
    }
}
