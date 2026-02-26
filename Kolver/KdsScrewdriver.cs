using System;
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("KducerTests")]

namespace Kolver
{
    /// <summary>
    /// screwdriver model identifiers as reported by the Kducer controller
    /// </summary>
    public enum ScrewdriverBaseModelId
    {
        /// <summary>
        /// base model ID when no screwdriver is connected
        /// </summary>
        NoneConnected = 0,
        /// <summary>
        /// KDS-PL6 base model ID, including right angle and pistol variants
        /// </summary>
        KdsPl6 = 1,
        /// <summary>
        /// KDS-PL10 base model ID, including right angle and pistol variants
        /// </summary>
        KdsPl10 = 2,
        /// <summary>
        /// KDS-PL15 base model ID, including right angle and pistol variants
        /// </summary>
        KdsPl15 = 3,
        /// <summary>
        /// KDS-MT1.5 base model ID, including right angle and pistol variants
        /// </summary>
        KdsMt15 = 4,
        /// <summary>
        /// KDS-PL20 base model ID, including right angle variants
        /// </summary>
        KdsPl20 = 5,
        /// <summary>
        /// KDS-PL30/ANG base model ID
        /// </summary>
        KdsPl30 = 6,
        /// <summary>
        /// KDS-PL35 base model ID
        /// </summary>
        KdsPl35 = 7,
        /// <summary>
        /// KDS-PL45/ANG base model ID
        /// </summary>
        KdsPl45 = 8,
        /// <summary>
        /// KDS-PL50 base model ID
        /// </summary>
        KdsPl50 = 9,
        /// <summary>
        /// KDS-PL70/ANG base model ID
        /// </summary>
        KdsPl70 = 10,
        /// <summary>
        /// KDS-PL3 base model ID, including right angle and pistol variants
        /// </summary>
        KdsPl3 = 11,
        /// <summary>
        /// KDS-PL20S base model ID, including right angle and pistol variants
        /// </summary>
        KdsPl20S = 12,
    }

    /// <summary>
    /// represents a base screwdriver model, with its max torque and speed, and ID as reported by the Kducer controller
    /// different physical screwdrivers can share the same model (for example pistol, angle, and inline versions of the same tool)
    /// </summary>
    public sealed class ScrewdriverBaseModel
    {
        internal const string NONE_CONNECTED = "None Connected";
        /// <summary>
        /// a number identifying the screwdriver model, as reported by the Kducer controller
        /// </summary>
        public ScrewdriverBaseModelId Id { get; }
        /// <summary>
        /// the base name, for example "KDS-PL20", does not distinguish between pistol, angle, inline, etc.
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// max value that the torque target/max/reverse program parameter can have, in cNm, for this screwdriver model. note: in reality the "Torque Max" and "Reverse Torque" program parameters can accept values slightly higher than this
        /// if any of these torque parameters are above this value and this screwdriver is connected, the controller will show an error popup in the main screen and the tool will not run
        /// </summary>
        public ushort MaxTorque { get; }
        /// <summary>
        /// max value that the speed/reverse-speed/downshift-speed program parameter can have, in RPM, for this screwdriver model
        /// if any of these speeds are above this value and this screwdriver is connected, the controller will show an error popup in the main screen and the tool will not run
        /// </summary>
        public ushort MaxRpm { get; }
        /// <summary>
        /// min value that the speed/reverse-speed/downshift-speed program parameter can have, in RPM, for this screwdriver model
        /// if any of these speeds are below this value and this screwdriver is connected, the controller will show an error popup in the main screen and the tool will not run
        /// </summary>
        public ushort MinRpm { get; }

        private ScrewdriverBaseModel(ScrewdriverBaseModelId id, string name, ushort maxTorque, ushort maxRpm, ushort minRpm)
        {
            Id = id;
            Name = name;
            MaxTorque = maxTorque;
            MaxRpm = maxRpm;
            MinRpm = minRpm;
        }

        /// <summary>
        /// base model when no screwdriver is connected
        /// </summary>
        public static readonly ScrewdriverBaseModel NoneConnected = new ScrewdriverBaseModel(ScrewdriverBaseModelId.NoneConnected, NONE_CONNECTED, 7000, 1800, 10);
        /// <summary>
        /// KDS-PL6 base model, including right angle and pistol variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl6        = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl6,        "KDS-PL6",     600,  850,  50);
        /// <summary>
        /// KDS-PL10 base model, including right angle and pistol variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl10       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl10,       "KDS-PL10",   1000,  600,  50);
        /// <summary>
        /// KDS-PL15 base model, including right angle and pistol variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl15       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl15,       "KDS-PL15",   1500,  320,  50);
        /// <summary>
        /// KDS-MT1.5 base model, including right angle and pistol variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsMt15       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsMt15,       "KDS-MT1.5",   150,  850,  50);
        /// <summary>
        /// KDS-PL20 base model, including right angle variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl20       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl20,       "KDS-PL20",   2000,  210,  20);
        /// <summary>
        /// KDS-PL30/ANG base model
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl30       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl30,       "KDS-PL30",   3000,  140,  20);
        /// <summary>
        /// KDS-PL35 base model
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl35       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl35,       "KDS-PL35",   3500,  140,  20);
        /// <summary>
        /// KDS-PL45/ANG base model
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl45       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl45,       "KDS-PL45",   4500,   90,  20);
        /// <summary>
        /// KDS-PL50 base model
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl50       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl50,       "KDS-PL50",   5000,   90,  20);
        /// <summary>
        /// KDS-PL70/ANG base model
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl70       = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl70,       "KDS-PL70",   7000,   50,  10);
        /// <summary>
        /// KDS-PL3 base model, including right angle and pistol variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl3        = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl3,        "KDS-PL3",     300, 1800,  50);
        /// <summary>
        /// KDS-PL20S base model, including right angle and pistol variants
        /// </summary>
        public static readonly ScrewdriverBaseModel KdsPl20S      = new ScrewdriverBaseModel(ScrewdriverBaseModelId.KdsPl20S,      "KDS-PL20S",  2000,  240,  20);

        // get a screwdriver object from its enum value
        private static readonly ScrewdriverBaseModel[] _map = new ScrewdriverBaseModel[]
        {
            NoneConnected, // 0
            KdsPl6,        // 1
            KdsPl10,       // 2
            KdsPl15,       // 3
            KdsMt15,       // 4
            KdsPl20,       // 5
            KdsPl30,       // 6
            KdsPl35,       // 7
            KdsPl45,       // 8
            KdsPl50,       // 9
            KdsPl70,       // 10
            KdsPl3,        // 11
            KdsPl20S,      // 12
        };

        /// <summary>
        /// gets a ScrewdriverBaseModel from its enum value
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static ScrewdriverBaseModel Get(ScrewdriverBaseModelId id)
        {
            return _map[(int)id];
        }
    }

    /// <summary>
    /// Represents a KDS screwdriver, providing information about its model, usage cycles, and calibration data.
    /// </summary>
    public sealed class KdsScrewdriver
    {
        /// <summary>
        /// represents a base screwdriver model, with its max torque and speed, and ID as reported by the Kducer controller
        /// different physical screwdrivers can share the same model (for example pistol, angle, and inline versions of the same tool)
        /// </summary>
        public ScrewdriverBaseModel BaseModel { get; }

        /// <summary>
        /// the serial number of the tool
        /// </summary>
        public uint SerialNumber { get; }

        /// <summary>
        /// nullable value. the total number of cycles (screws) this tool has done, if available
        /// information is available only if the KDU has firmware version 41c or higher
        /// </summary>
        public uint? Cycles { get; }

        /// <summary>
        /// nullable value. the number of cycles (screws) this tool had done at the time of the last calibration
        /// information is available only if the tool has firmware version 14 or higher, and if the KDU has firmware version 41c or higher
        /// </summary>
        public uint? CyclesAtLastCalibration { get; }

        /// <summary>
        /// nullable value. the date recorded at the time of the last calibration. Its accuracy depends on whether the Kducer controller's clock was set correctly at the time that the FatC setting was changed.
        /// information is available only if the tool has firmware version 14 or higher, and if the KDU has firmware version 41c or higher
        /// </summary>
        public DateTime? LastCalibrationDate { get; }

        /// <summary>
        /// the calibration factor (FatC) of the tool
        /// </summary>
        public ushort CalibrationFactorFatC { get; }

        /// <summary>
        /// Information about the base model, including max torque and speed limits
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"{base.ToString()}; Base model: {BaseModel.Name}; Serial Number: {SerialNumber}; FatC: {CalibrationFactorFatC}. If available: Cycles: {Cycles}; Cycles at last calibration: {CyclesAtLastCalibration}; Date of last calibration: {LastCalibrationDate?.ToString("yyyy-MM-dd")};";
        }

        internal KdsScrewdriver(ushort model, uint serialNumber, ushort FatC, uint cycles, uint cyclesAtLastCalibration, ushort lastCalibrationDate_YY, ushort lastCalibrationDate_MM, ushort lastCalibrationDate_DD)
        {
            BaseModel = ScrewdriverBaseModel.Get((ScrewdriverBaseModelId)model);
            SerialNumber = serialNumber;
            CalibrationFactorFatC = FatC;

            if (cycles == 0xFFFFFFFF)
                Cycles = null;
            else
                Cycles = cycles;

            if(cyclesAtLastCalibration == 0xFFFFFFFF)
                CyclesAtLastCalibration = null;
            else
                CyclesAtLastCalibration = cyclesAtLastCalibration;

            try
            {
                LastCalibrationDate = new DateTime(2000 + lastCalibrationDate_YY, lastCalibrationDate_MM, lastCalibrationDate_DD);
            }
            catch (ArgumentOutOfRangeException)
            {
                LastCalibrationDate = null;
            }
        }
    }
}
