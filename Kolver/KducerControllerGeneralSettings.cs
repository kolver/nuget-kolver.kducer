// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;
using System.Linq;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("KducerTests")]

namespace Kolver
{
    /// <summary>
    /// Representation of the general settings of a Kducer controller
    /// Provides convenience methods to get and set each parameter
    /// You can serialize the settings using getGeneralSettingsModbusHoldingRegistersAsByteArray for storing to a file or database
    /// </summary>
    public class KducerControllerGeneralSettings
    {
        private readonly static byte[] defaultControllerSettingsForKdu1a = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 84, 79, 82, 81, 85, 69, 32, 83, 84, 65, 84, 73, 79, 78, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 7, 161, 32, 0, 0, 0, 0, 0, 0, 0, 0 };

        private readonly byte[] controllerGeneralSettingsHoldingRegistersAsByteArray = new byte[92];
        /// <summary>
        /// the byte array returned can be sent to the KDU-1A to modify the general settings* ,
        /// the byte array can also be used to serialize these settings
        /// *note: for KDU-1A v40+, bytes[88:] are not consecutive with bytes[0:88] in the actual Modbus Map. The Kducer class accounts for this automatically.
        /// </summary>
        /// <returns>the 44 modbus holding registers representing the general settings</returns>
        public byte[] getGeneralSettingsModbusHoldingRegistersAsByteArray() { return controllerGeneralSettingsHoldingRegistersAsByteArray; }

        internal byte[] getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv40_SecondTrancheOnly() { return new ArraySegment<byte>(controllerGeneralSettingsHoldingRegistersAsByteArray, 88, 4).ToArray<byte>(); }
        internal byte[] getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39() { return new ArraySegment<byte>(controllerGeneralSettingsHoldingRegistersAsByteArray, 0, 88).ToArray<byte>(); }
        internal byte[] getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38() { return new ArraySegment<byte>(controllerGeneralSettingsHoldingRegistersAsByteArray, 0, 86).ToArray<byte>(); }
        internal byte[] getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior() { return new ArraySegment<byte>(controllerGeneralSettingsHoldingRegistersAsByteArray, 0, 78).ToArray<byte>(); }

        /// <summary>
        /// creates a settings object from a byte array retrieved from Modbus TCP holding registers
        /// </summary>
        /// <param name="controllerGeneralSettingsHoldingRegistersAsByteArray">the byte array from reading general settings holding registers of the KDU</param>
        public KducerControllerGeneralSettings(byte[] controllerGeneralSettingsHoldingRegistersAsByteArray)
        {
            if (controllerGeneralSettingsHoldingRegistersAsByteArray == null)
                throw new ArgumentNullException(nameof(controllerGeneralSettingsHoldingRegistersAsByteArray));
            if (controllerGeneralSettingsHoldingRegistersAsByteArray.Length < 78 || controllerGeneralSettingsHoldingRegistersAsByteArray.Length > 92)
                throw new ArgumentException("Expected length is 78 to 92 bytes", nameof(controllerGeneralSettingsHoldingRegistersAsByteArray));

            controllerGeneralSettingsHoldingRegistersAsByteArray.CopyTo(this.controllerGeneralSettingsHoldingRegistersAsByteArray, 0);
        }
        /// <summary>
        /// creates a general settings object with default values
        /// </summary>
        public KducerControllerGeneralSettings()
        {
            defaultControllerSettingsForKdu1a.CopyTo(this.controllerGeneralSettingsHoldingRegistersAsByteArray, 0);
        }
        /// <summary>0 = English, 1 = Italian, 2 = German,3 = Spanish, 4 = French, Portougese = 5, Polish = 6, Czeck = 7</summary>
        public ushort GetLanguage()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 0);
        }
        /// <summary>0 = English, 1 = Italian, 2 = German,3 = Spanish, 4 = French, Portougese = 5, Polish = 6, Czeck = 7</summary>
        public void SetLanguage(ushort lang)
        {
            if (lang > 7)
                throw new ArgumentException("0 = English, 1 = Italian, 2 = German,3 = Spanish, 4 = French, Portougese = 5, Polish = 6, Czeck = 7", nameof(lang));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(lang, controllerGeneralSettingsHoldingRegistersAsByteArray, 0);
        }
        /// <summary>password to access menu. 4 digits max. must also set PasswordOnOff to ON to enable the password.</summary>
        public ushort GetPassword()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 4);
        }
        /// <summary>password to access menu. 4 digits max. must also set PasswordOnOff to ON to enable the password.</summary>
        public void SetPassword(ushort password)
        {
            if (password > 9999)
                throw new ArgumentException("4 digits max", nameof(password));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(password, controllerGeneralSettingsHoldingRegistersAsByteArray, 4);
        }
        /// <summary>false = password to access menu is off, true = password to access menu is on</summary>
        public bool GetPasswordOnOff()
        {
            return (ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 6) != 0);
        }
        /// <summary>false = password to access menu is off, true = password to access menu is on</summary>
        public void SetPasswordOnOff(bool enablePassword)
        {
            ushort onOff = 0;
            if (enablePassword) onOff = 1;
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(onOff, controllerGeneralSettingsHoldingRegistersAsByteArray, 6);
        }
        /// <summary>0 = Ext, 1 = Int, 2 = Ext+Int (default)</summary>
        public ushort GetCmdOkEscResetSource()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 8);
        }
        /// <summary>0 = Ext, 1 = Int, 2 = Ext+Int (default)</summary>
        public void SetCmdOkEscResetSource(ushort source)
        {
            if (source > 2)
                throw new ArgumentException("0 = Ext, 1 = Int, 2 = Ext+Int (default)", nameof(source));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(source, controllerGeneralSettingsHoldingRegistersAsByteArray, 8);
        }
        /// <summary>0 = Off (default), 1 = CN3 I/O, 2 = CN5 TCP, 3 = SWBX/CBS, 4 = Barcode</summary>
        public ushort GetRemoteProgramSource()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 10);
        }
        /// <summary>0 = Off (default), 1 = CN3 I/O, 2 = CN5 TCP, 3 = SWBX/CBS, 4 = Barcode</summary>
        public void SetRemoteProgramSource(ushort source)
        {
            if (source > 4)
                throw new ArgumentException("0 = Off (default), 1 = CN3 I/O, 2 = CN5 TCP, 3 = SWBX/CBS, 4 = Barcode", nameof(source));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(source, controllerGeneralSettingsHoldingRegistersAsByteArray, 10);
        }
        /// <summary>0 = Off (default), 1 = CN3 I/O, 2 = CN5 TCP, 3 = SWBX/CBS, 4 = Barcode</summary>
        public ushort GetRemoteSequenceSource()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 12);
        }
        /// <summary>0 = Off (default), 1 = CN3 I/O, 2 = CN5 TCP, 3 = SWBX/CBS, 4 = Barcode</summary>
        public void SetRemoteSequenceSource(ushort source)
        {
            if (source > 4)
                throw new ArgumentException("0 = Off (default), 1 = CN3 I/O, 2 = CN5 TCP, 3 = SWBX/CBS, 4 = Barcode", nameof(source));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(source, controllerGeneralSettingsHoldingRegistersAsByteArray, 12);
        }
        /// <summary>0 = Off (default), 1 = Prg, 2 = Screw, 3 = Seq</summary>
        public ushort GetResetMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 14);
        }
        /// <summary>0 = Off (default), 1 = Prg, 2 = Screw, 3 = Seq</summary>
        public void SetResetMode(ushort mode)
        {
            if (mode > 3)
                throw new ArgumentException("0 = Off (default), 1 = Prg, 2 = Screw, 3 = Seq", nameof(mode));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, controllerGeneralSettingsHoldingRegistersAsByteArray, 14);
        }
        /// <summary>0 = Off (default), 1 = on SN, 2 = on PR, 3 = on SEQ, 4 = on SN + PR, 5 = on SN + SEQ</summary>
        public ushort GetBarcodeMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 16);
        }
        /// <summary>0 = Off (default), 1 = on SN, 2 = on PR, 3 = on SEQ, 4 = on SN + PR, 5 = on SN + SEQ</summary>
        public void SetBarcodeMode(ushort mode)
        {
            if (mode > 5)
                throw new ArgumentException("0 = Off (default), 1 = on SN, 2 = on PR, 3 = on SEQ, 4 = on SN + PR, 5 = on SN + SEQ", nameof(mode));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, controllerGeneralSettingsHoldingRegistersAsByteArray, 16);
        }
        /// <summary>0 = Off (default), 1 = on PR, 2 = on SEQ</summary>
        public ushort GetSwbxAndCbs880Mode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 18);
        }
        /// <summary>0 = Off (default), 1 = on PR, 2 = on SEQ</summary>
        public void SetSwbxAndCbs880Mode(ushort mode)
        {
            if (mode > 2)
                throw new ArgumentException("0 = Off (default), 1 = on PR, 2 = on SEQ", nameof(mode));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, controllerGeneralSettingsHoldingRegistersAsByteArray, 18);
        }
        /// <summary>current sequence (and default sequence when turning unit on). 1 = A, 2 = B, etc</summary>
        public ushort GetCurrentSequenceNumber()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 20);
        }
        /// <summary>current sequence (and default sequence when turning unit on). 1 = A, 2 = B, etc</summary>
        public void SetCurrentSequenceNumber(ushort sequence)
        {
            if (sequence == 0 || sequence > 24)
                throw new ArgumentException("1 = A, 2 = B, etc", nameof(sequence));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(sequence, controllerGeneralSettingsHoldingRegistersAsByteArray, 20);
        }
        /// <summary>current program (and default program when turning unit on)</summary>
        public ushort GetCurrentProgramNumber()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 22);
        }
        /// <summary>current sequence (and default sequence when turning unit on)</summary>
        public void SetCurrentProgramNumber(ushort program)
        {
            if (program == 0 || program > 200)
                throw new ArgumentException("1 to 200", nameof(program));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(program, controllerGeneralSettingsHoldingRegistersAsByteArray, 22);
        }
        /// <summary>0 = Nm, 1= kgf.cm, 2 = lbf.in, 3 = ozf.in, 4 = lbf.ft (KDU v40 only)</summary>
        public ushort GetTorqueUnits()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 24);
        }
        /// <summary>0 = Nm, 1= kgf.cm, 2 = lbf.in, 3 = ozf.in, 4 = lbf.ft (KDU v40 only)</summary>
        public void SetTorqueUnits(ushort units)
        {
            if (units > 4)
                throw new ArgumentException("0 = Nm, 1= kgf.cm, 2 = lbf.in, 3 = ozf.in, 4 = lbf.ft (4 on KDU v40 and newer only)", nameof(units));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(units, controllerGeneralSettingsHoldingRegistersAsByteArray, 24);
        }
        /// <summary>false = sequence mode is off, true = sequence mode is on</summary>
        public bool GetSequenceModeOnOff()
        {
            return (ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 26) != 0);
        }
        /// <summary>false = sequence mode is off, true = sequence mode is on</summary>
        public void SetSequenceModeOnOff(bool sequenceMode)
        {
            ushort onOff = 0;
            if (sequenceMode) onOff = 1;
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(onOff, controllerGeneralSettingsHoldingRegistersAsByteArray, 26);
        }
        /// <summary>false = fast dock05 mode is off, true = fast dock05 mode is on</summary>
        public bool GetFastDock05ModeOnOff()
        {
            return (ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 30) != 0);
        }
        /// <summary>false = fast dock05 mode is off, true = fast dock05 mode is on</summary>
        public void SetFastDock05ModeOnOff(bool fastDock05)
        {
            ushort onOff = 0;
            if (fastDock05) onOff = 1;
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(onOff, controllerGeneralSettingsHoldingRegistersAsByteArray, 30);
        }
        /// <summary>false = controller buzzer sounds off, true = controller buzzer sounds on</summary>
        public bool GetBuzzerSoundsOnOff()
        {
            return (ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 32) != 0);
        }
        /// <summary>false = controller buzzer sounds off, true = controller buzzer sounds on</summary>
        public void SetBuzzerSoundsOnOff(bool buzzer)
        {
            ushort onOff = 0;
            if (buzzer) onOff = 1;
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(onOff, controllerGeneralSettingsHoldingRegistersAsByteArray, 32);
        }
        /// <summary>0 = TXT (legacy format), 1 = CSV (comma separated values)</summary>
        public ushort GetResultsFormat()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 34);
        }
        /// <summary>0 = TXT (legacy format), 1 = CSV (comma separated values)</summary>
        public void SetResultsFormat(ushort mode)
        {
            if (mode > 1)
                throw new ArgumentException("0 = TXT (legacy format), 1 = CSV (comma separated values)", nameof(mode));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, controllerGeneralSettingsHoldingRegistersAsByteArray, 34);
        }
        /// <summary>string of up to 25 ascii characters</summary>
        public string GetStationName()
        {
            return ModbusByteConversions.ModbusBytesToAsciiString(controllerGeneralSettingsHoldingRegistersAsByteArray, 52, 25);
        }
        /// <summary>string of up to 25 ascii characters</summary>
        public void SetStationName(string stationName)
        {
            if (stationName == null)
                throw new ArgumentNullException(nameof(stationName));
            if (stationName.Length > 25)
                throw new ArgumentException("At most 25 characters", nameof(stationName));
            byte[] stationNameBytes = new byte[25];
            System.Text.Encoding.ASCII.GetBytes(stationName).CopyTo(stationNameBytes, 0);
            stationNameBytes.CopyTo(controllerGeneralSettingsHoldingRegistersAsByteArray, 52);
        }
        /// <summary>0 = Off, 1 = Time (days) based interval, 2 = Cycles based interval</summary>
        public ushort GetCalibrationReminderMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 78);
        }
        /// <summary>0 = Off, 1 = Time (days) based interval, 2 = Cycles based interval</summary>
        public void SetCalibrationReminderMode(ushort mode)
        {
            if (mode > 2)
                throw new ArgumentException("0 = Off, 1 = Time (days) based interval, 2 = Cycles based interval", nameof(mode));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, controllerGeneralSettingsHoldingRegistersAsByteArray, 78);
        }
        /// <summary>Number of days or number of screwdriving cycles until calibration reminder is shown on the screen. 32bit value.</summary>
        public uint GetCalibrationReminderInterval()
        {
            return ModbusByteConversions.FourModbusBigendianBytesToUint(controllerGeneralSettingsHoldingRegistersAsByteArray, 80);
        }
        /// <summary>Number of days or number of screwdriving cycles until calibration reminder is shown on the screen. 32bit value.</summary>
        public void SetCalibrationReminderInterval(uint interval)
        {
            ModbusByteConversions.CopyUintToBytesAsModbusBigendian(interval, controllerGeneralSettingsHoldingRegistersAsByteArray, 80);
        }


        /// <summary>part of misc conf register</summary>
        public bool GetLockIfCn5NotConnectedOnOff()
        {
            return GetMiscConfBit(0);
        }
        /// <summary>part of misc conf register</summary>
        public void SetLockIfCn5NotConnectedOnOff(bool onOff)
        {
            SetMiscConfBit(0, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetLockIfUsbNotConnectedOnOff()
        {
            return GetMiscConfBit(1);
        }
        /// <summary>part of misc conf register</summary>
        public void SetLockIfUsbNotConnectedOnOff(bool onOff)
        {
            SetMiscConfBit(1, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetInvertLogicCn3InStopOnOff()
        {
            return GetMiscConfBit(2);
        }
        /// <summary>part of misc conf register</summary>
        public void SetInvertLogicCn3InStopOnOff(bool onOff)
        {
            SetMiscConfBit(2, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetInvertLogicCn3InPieceOnOff()
        {
            return GetMiscConfBit(3);
        }
        /// <summary>part of misc conf register</summary>
        public void SetInvertLogicCn3InPieceOnOff(bool onOff)
        {
            SetMiscConfBit(3, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetSkipScrewButtonOnOff()
        {
            return GetMiscConfBit(4);
        }
        /// <summary>part of misc conf register</summary>
        public void SetSkipScrewButtonOnOff(bool onOff)
        {
            SetMiscConfBit(4, onOff);
        }
        /// <summary>part of misc conf register</summary>
        public bool GetShowReverseTorqueAndAngleOnOff()
        {
            return GetMiscConfBit(5);
        }
        /// <summary>part of misc conf register</summary>
        public void SetShowReverseTorqueAndAngleOnOff(bool onOff)
        {
            SetMiscConfBit(5, onOff);
        }
        /// <summary>0 = binary (default), 1 = discrete, 2..6 = pnp sens x2 .. x6 for tool change accessory (PNP sensors) when connected to CN3</summary>
        public ushort GetCn3BitxPrSeqInputSelectionMode()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 86);
        }
        /// <summary>0 = binary (default), 1 = discrete, 2..6 = pnp sens x2 .. x6 for tool change accessory (PNP sensors) when connected to CN3</summary>
        public void SetCn3BitxPrSeqInputSelectionMode(ushort mode)
        {
            if (mode > 6)
                throw new ArgumentException("0 = binary (default), 1 = discrete, 2..6 = pnp sens x2 .. x6 for tool change accessory (PNP sensors) when connected to CN3", nameof(mode));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(mode, controllerGeneralSettingsHoldingRegistersAsByteArray, 86);
        }
        /// <summary>For KTLS smart positioning arm. 0 = Off, 1 = LINAR, 2 = LINAR-T, 3 = CAR, 4 = SAR</summary>
        public ushort GetKtlsArm1Model()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 88);
        }
        /// <summary>For KTLS smart positioning arm. 0 = Off, 1 = LINAR, 2 = LINAR-T, 3 = CAR, 4 = SAR</summary>
        public void SetKtlsArm1Model(ushort ktlsArmModel)
        {
            if (ktlsArmModel > 4)
                throw new ArgumentException("0 = Off, 1 = LINAR, 2 = LINAR-T, 3 = CAR, 4 = SAR", nameof(ktlsArmModel));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(ktlsArmModel, controllerGeneralSettingsHoldingRegistersAsByteArray, 88);
        }
        /// <summary>For KTLS second smart positioning arm with Dock-05 only. 0 = Off, 1 = LINAR, 2 = LINAR-T, 3 = CAR, 4 = SAR. Not all combinations of arm1 and arm2 are valid.</summary>
        public ushort GetKtlsArm2Model()
        {
            return ModbusByteConversions.TwoModbusBigendianBytesToUshort(controllerGeneralSettingsHoldingRegistersAsByteArray, 90);
        }
        /// <summary>For KTLS second smart positioning arm with Dock-05 only. 0 = Off, 1 = LINAR, 2 = LINAR-T, 3 = CAR, 4 = SAR. Not all combinations of arm1 and arm2 are valid.</summary>
        public void SetKtlsArm2Model(ushort ktlsArmModel)
        {
            if (ktlsArmModel > 4)
                throw new ArgumentException("0 = Off, 1 = LINAR, 2 = LINAR-T, 3 = CAR, 4 = SAR", nameof(ktlsArmModel));
            ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(ktlsArmModel, controllerGeneralSettingsHoldingRegistersAsByteArray, 90);
        }

        private void SetMiscConfBit(int confBitIdx, bool bitState)
        {
            int byteIdx = 85; // index of misc conf bytes in modbus registers
            if (confBitIdx >= 8)
            {
                confBitIdx -= 8;
                byteIdx = 84;
            }
            byte mask = (byte)(1 << confBitIdx);

            if (bitState) // set to 1
                controllerGeneralSettingsHoldingRegistersAsByteArray[byteIdx] |= mask;
            else // Set to zero
                controllerGeneralSettingsHoldingRegistersAsByteArray[byteIdx] &= (byte)(~mask);
        }
        private bool GetMiscConfBit(int confBitIdx)
        {
            int byteIdx = 85; // index of misc conf bytes in modbus registers
            if (confBitIdx >= 8)
            {
                confBitIdx -= 8;
                byteIdx = 84;
            }
            byte mask = (byte)(1 << confBitIdx);

            return (controllerGeneralSettingsHoldingRegistersAsByteArray[byteIdx] & mask) != 0;
        }
    }
}
