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
        private readonly ushort[] torqueSeries70ptRes, angleSeries70ptRes; //, timeSeries; for future use with KDU-NT
        private readonly ushort torqueSeriesInterval70ptRes, angleSeriesInterval70ptRes;
        private readonly ushort[] torqueSeriesHighRes, angleSeriesHighRes;
        private readonly ushort torqueSeriesIntervalHighRes = 1, angleSeriesIntervalHighRes = 1;
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

            torqueSeriesInterval70ptRes = ModbusByteConversions.TwoModbusBigendianBytesToUshort(kdu1aTorqueDataSeriesInputRegistersAsByteArray, 0);
            angleSeriesInterval70ptRes = ModbusByteConversions.TwoModbusBigendianBytesToUshort(kdu1aAngleDataSeriesInputRegistersAsByteArray, 0);

            // convert to ushort
            ushort[] tmpTorqueArr = new ushort[70];
            ushort[] tmpAngleArr = new ushort[70];
            for (int i = 2; i < kdu1aAngleDataSeriesInputRegistersAsByteArray.Length - 1; i+=2)
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
            torqueSeries70ptRes = new ushort[70 - numberOfTrailingZeroes];
            angleSeries70ptRes = new ushort[70 - numberOfTrailingZeroes];
            for (int i = 0; i < 70 - numberOfTrailingZeroes; i++)
            {
                torqueSeries70ptRes[i] = tmpTorqueArr[i];
                angleSeries70ptRes[i] = tmpAngleArr[i];
            }
        }

        /// <summary>
        /// constructor for high-res data series introduced in KDU v38
        /// extracts the data from the input register byte arrays into the torque and angle data series
        /// </summary>
        /// <param name="kdu1aTorqueDataSeriesInputRegistersAsByteArray">the 142-byte array from reading input registers 152 through 222</param>
        /// <param name="kdu1aAngleDataSeriesInputRegistersAsByteArray">the 142-byte array from reading input registers 223 through 293</param>
        /// <param name="kdu1aHighResTorqueDataSeriesAsByteArray">up to 40000 bytes</param>
        /// <param name="kdu1aHighResAngleDataSeriesAsByteArray">up to 40000 bytes</param>
        public KducerTorqueAngleTimeGraph(byte[] kdu1aTorqueDataSeriesInputRegistersAsByteArray, byte[] kdu1aAngleDataSeriesInputRegistersAsByteArray, byte[] kdu1aHighResTorqueDataSeriesAsByteArray, byte[] kdu1aHighResAngleDataSeriesAsByteArray) : this(kdu1aTorqueDataSeriesInputRegistersAsByteArray, kdu1aAngleDataSeriesInputRegistersAsByteArray)
        {
            if (kdu1aHighResAngleDataSeriesAsByteArray == null && kdu1aHighResTorqueDataSeriesAsByteArray == null)
                return; // for convenience when calling from Kducer class

            if (kdu1aHighResTorqueDataSeriesAsByteArray == null)
                throw new ArgumentNullException(nameof(kdu1aHighResTorqueDataSeriesAsByteArray));
            if (kdu1aHighResAngleDataSeriesAsByteArray == null)
                throw new ArgumentNullException(nameof(kdu1aHighResAngleDataSeriesAsByteArray));
            if (kdu1aHighResTorqueDataSeriesAsByteArray.Length != kdu1aHighResAngleDataSeriesAsByteArray.Length)
                throw new ArgumentException("Lengths of high res arrays are not the same", nameof(kdu1aHighResTorqueDataSeriesAsByteArray));

            torqueSeriesHighRes = new ushort[kdu1aHighResTorqueDataSeriesAsByteArray.Length / 2];
            angleSeriesHighRes = new ushort[kdu1aHighResAngleDataSeriesAsByteArray.Length / 2];

            for (int idx = 0; idx < kdu1aHighResTorqueDataSeriesAsByteArray.Length - 1; idx += 2)
            {
                ushort torqueVal, angleVal;
                if (BitConverter.IsLittleEndian)
                {
                    torqueVal = (ushort)((kdu1aHighResTorqueDataSeriesAsByteArray[idx + 1] << 8) | kdu1aHighResTorqueDataSeriesAsByteArray[idx]);
                    angleVal = (ushort)((kdu1aHighResAngleDataSeriesAsByteArray[idx + 1] << 8) | kdu1aHighResAngleDataSeriesAsByteArray[idx]);
                }
                else
                {
                    torqueVal = (ushort)((kdu1aHighResTorqueDataSeriesAsByteArray[idx] << 8) | kdu1aHighResTorqueDataSeriesAsByteArray[idx + 1]);
                    angleVal = (ushort)((kdu1aHighResAngleDataSeriesAsByteArray[idx] << 8) | kdu1aHighResAngleDataSeriesAsByteArray[idx + 1]);
                }

                torqueSeriesHighRes[idx/2] = torqueVal;
                angleSeriesHighRes[idx/2] = angleVal;
            }
        }

        /// <summary>returns a copy of the data (original data will not be modified)</summary>
        /// <returns>the angle data series as a ushort array</returns>
        /// <param name="highResIfAvailable">if true and the high-resolution data series is available, return it</param>
        public ushort[] getTorqueSeries(bool highResIfAvailable = true)
        {
            ushort[] rtn;
            if (highResIfAvailable && torqueSeriesHighRes != null)
            {
                rtn = new ushort[torqueSeriesHighRes.Length];
                torqueSeriesHighRes.CopyTo(rtn, 0);
            }
            else
            {
                rtn = new ushort[torqueSeries70ptRes.Length];
                torqueSeries70ptRes.CopyTo(rtn, 0);
            }
            return rtn;
        }
        /// <summary>returns a copy of the data (original data will not be modified)</summary>
        /// <returns>the angle data series as a ushort array</returns>
        /// <param name="highResIfAvailable">if true and the high-resolution data series is available, return it</param>
        public ushort[] getAngleSeries(bool highResIfAvailable = true)
        {
            ushort[] rtn;
            if (highResIfAvailable && angleSeriesHighRes != null)
            {
                rtn = new ushort[angleSeriesHighRes.Length];
                angleSeriesHighRes.CopyTo(rtn, 0);
            }
            else
            {
                rtn = new ushort[angleSeries70ptRes.Length];
                angleSeries70ptRes.CopyTo(rtn, 0);
            }
            return rtn;
        }

        /// <summary></summary>
        /// <returns>the torque data series as comma separated values</returns>
        public string getTorqueSeriesAsCsv(bool highResIfAvailable = true)
        {
            if (highResIfAvailable && torqueSeriesHighRes != null)
                return String.Join(",", torqueSeriesHighRes);
            else
                return String.Join(",", torqueSeries70ptRes);
        }
        /// <summary></summary>
        /// <returns>the angle data series as comma separated values</returns>
        public string getAngleSeriesAsCsv(bool highResIfAvailable = true)
        {
            if (highResIfAvailable && angleSeriesHighRes != null)
                return String.Join(",", angleSeriesHighRes);
            else
                return String.Join(",", angleSeries70ptRes);
        }

        /// <summary></summary>
        /// <returns>the time interval in ms between each consecutive data point in the torque and angle (low resolution, 70pt) series</returns>
        public ushort getTimeIntervalBetweenConsecutivePoints()
        {
            return torqueSeriesInterval70ptRes;
        }

        /// <summary>the returned CSV has exactly 70 columns, trailing values may be empty</summary>
        /// <returns>the angle data series as comma separated values</returns>
        public string getAngleSeriesAsCsvWith70columns()
        {
            if (angleSeries70ptRes.Length == 70)
                return getAngleSeriesAsCsv(false);
            else if (angleSeries70ptRes.Length > 70)
                throw new InvalidOperationException("Cannot fit data in 70 columns"); // placeholder for future integration with highres data

            string data = getAngleSeriesAsCsv(false);
            string emptyCols = new string(',', 70 - torqueSeries70ptRes.Length);
            return data + emptyCols;
        }

        /// <summary>the returned CSV has exactly 70 columns, trailing values may be empty</summary>
        /// <returns>the torque data series as comma separated values</returns>
        public string getTorqueSeriesAsCsvWith70columns()
        {
            if (torqueSeries70ptRes.Length == 70)
                return getTorqueSeriesAsCsv();
            else if (torqueSeries70ptRes.Length > 70)
                throw new InvalidOperationException("Cannot fit data in 70 columns"); // placeholder for future integration with highres data

            string data = getTorqueSeriesAsCsv();
            string emptyCols = new string(',', 70 - torqueSeries70ptRes.Length);
            return data + emptyCols;
        }
    }
}
