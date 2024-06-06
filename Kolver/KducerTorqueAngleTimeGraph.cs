// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using System;

namespace Kolver
{
    /// <summary>
    /// Representation of the tightening graph data provided by the KDU controller for each tightening result
    /// Provides convenience methods to get the data
    /// </summary>
    public class KducerTorqueAngleTimeGraph
    {
        private readonly ushort[] torqueSeries, angleSeries; //, timeSeries; for future use with KDU-NT
        private readonly ushort torqueSeriesInterval, angleSeriesInterval;
        /// <summary>
        /// extracts the data from the input register byte arrays into the torque and angle data series
        /// </summary>
        /// <param name="kdu1aTorqueDataSeriesInputRegistersAsByteArray">the 142-byte array from reading input registers 152 through 222</param>
        /// <param name="kdu1aAngleDataSeriesInputRegistersAsByteArray">the 142-byte array from reading input registers 223 through 293</param>
        public KducerTorqueAngleTimeGraph(byte[] kdu1aTorqueDataSeriesInputRegistersAsByteArray, byte[] kdu1aAngleDataSeriesInputRegistersAsByteArray)
        {
            if (kdu1aTorqueDataSeriesInputRegistersAsByteArray == null)
                throw new ArgumentNullException(nameof(kdu1aTorqueDataSeriesInputRegistersAsByteArray));
            if (kdu1aAngleDataSeriesInputRegistersAsByteArray == null)
                throw new ArgumentNullException(nameof(kdu1aTorqueDataSeriesInputRegistersAsByteArray));
            if (kdu1aTorqueDataSeriesInputRegistersAsByteArray.Length != 142)
                throw new ArgumentException("Expecting 142 byte array", nameof(kdu1aTorqueDataSeriesInputRegistersAsByteArray));
            if (kdu1aAngleDataSeriesInputRegistersAsByteArray.Length != 142)
                throw new ArgumentException("Expecting 142 byte array", nameof(kdu1aAngleDataSeriesInputRegistersAsByteArray));

            torqueSeriesInterval = ModbusByteConversions.TwoModbusBigendianBytesToUshort(kdu1aTorqueDataSeriesInputRegistersAsByteArray, 0);
            angleSeriesInterval = ModbusByteConversions.TwoModbusBigendianBytesToUshort(kdu1aAngleDataSeriesInputRegistersAsByteArray, 0);

            // convert to ushort
            ushort[] tmpTorqueArr = new ushort[70];
            ushort[] tmpAngleArr = new ushort[70];
            for (int i = 2; i < kdu1aAngleDataSeriesInputRegistersAsByteArray.Length - 1
                ; i+=2)
            {
                tmpAngleArr[(i - 2) / 2] = ModbusByteConversions.TwoModbusBigendianBytesToUshort(kdu1aAngleDataSeriesInputRegistersAsByteArray, i);
                tmpTorqueArr[(i - 2) / 2] = ModbusByteConversions.TwoModbusBigendianBytesToUshort(kdu1aTorqueDataSeriesInputRegistersAsByteArray, i);
            }

            // copy to internal array, excluding trailing zeros of angle data
            int numberOfTrailingZeroes = 0;
            for (int i = tmpAngleArr.Length - 1; i != 0; i--)
            {
                if (tmpAngleArr[i] == 0)
                    numberOfTrailingZeroes++;
                else
                    break;
            }
            torqueSeries = new ushort[70 - numberOfTrailingZeroes];
            angleSeries = new ushort[70 - numberOfTrailingZeroes];
            for (int i = 0; i < 70 - numberOfTrailingZeroes; i++)
            {
                torqueSeries[i] = tmpTorqueArr[i];
                angleSeries[i] = tmpAngleArr[i];
            }
        }

        /// <summary>returns a copy of the data (original data will not be modified)</summary>
        /// <returns>the angle data series as a ushort array</returns>
        public ushort[] getTorqueSeries()
        {
            ushort[] rtn = new ushort[torqueSeries.Length];
            torqueSeries.CopyTo(rtn, 0);
            return rtn;
        }
        /// <summary>returns a copy of the data (original data will not be modified)</summary>
        /// <returns>the angle data series as a ushort array</returns>
        public ushort[] getAngleSeries()
        {
            ushort[] rtn = new ushort[angleSeries.Length];
            angleSeries.CopyTo(rtn, 0);
            return rtn;
        }

        /// <summary></summary>
        /// <returns>the torque data series as comma separated values</returns>
        public string getTorqueSeriesAsCsv()
        {
            return String.Join(",", torqueSeries);
        }
        /// <summary></summary>
        /// <returns>the angle data series as comma separated values</returns>
        public string getAngleSeriesAsCsv()
        {
            return String.Join(",", angleSeries);
        }

        /// <summary></summary>
        /// <returns>the time interval in ms between each consecutive data point in the torque and angle series</returns>
        public ushort getTimeIntervalBetweenConsecutivePoints()
        {
            return torqueSeriesInterval;
        }

        /// <summary>the returned CSV has exactly 70 columns, trailing values may be empty</summary>
        /// <returns>the angle data series as comma separated values</returns>
        public string getAngleSeriesAsCsvWith70columns()
        {
            if (angleSeries.Length == 70)
                return getAngleSeriesAsCsv();
            else if (angleSeries.Length > 70)
                throw new InvalidOperationException("Cannot fit data in 70 columns"); // placeholder for future integration with highres data

            string data = getAngleSeriesAsCsv();
            string emptyCols = new string(',', 70 - torqueSeries.Length);
            return data + emptyCols;
        }

        /// <summary>the returned CSV has exactly 70 columns, trailing values may be empty</summary>
        /// <returns>the torque data series as comma separated values</returns>
        public string getTorqueSeriesAsCsvWith70columns()
        {
            if (torqueSeries.Length == 70)
                return getTorqueSeriesAsCsv();
            else if (torqueSeries.Length > 70)
                throw new InvalidOperationException("Cannot fit data in 70 columns"); // placeholder for future integration with highres data

            string data = getTorqueSeriesAsCsv();
            string emptyCols = new string(',', 70 - torqueSeries.Length);
            return data + emptyCols;
        }
    }
}
