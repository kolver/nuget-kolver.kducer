// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using Kolver;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections;
using System.Linq;
using System.Net;

namespace KducerTests
{
    static class TestConstants
    {
        public static string REAL_LIVE_KDU_IP = "192.168.32.103";
        public static string OFF_OR_DC_KDU_IP = "192.168.32.40";
    }

    [TestClass]
    public class ReducedModbusTcpClientTests
    {
        [TestMethod]
        [Timeout(3000)]
        public async Task TestConnectToLiveKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            Assert.IsFalse(kdu.Connected());
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
        }

        [TestMethod]
        public async Task TestConnectToDeadKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.OFF_OR_DC_KDU_IP));
            await Assert.ThrowsExceptionAsync<System.Net.Sockets.SocketException>(async () => await kdu.ConnectAsync());
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestReConnectToLiveKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            Assert.IsFalse(kdu.Connected());
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestReadHoldingRegisters()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
            Console.WriteLine(await kdu.ReadHoldingRegistersAsync(0, 12));
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestReadInputRegisters()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
            Console.WriteLine(await kdu.ReadInputRegistersAsync(0, 12));
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestWriteCoil()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
            await kdu.WriteSingleCoilAsync(34, true);
            await Task.Delay(100);
            await kdu.WriteSingleCoilAsync(34, false);
        }
    }

    [TestClass]
    public class KducerTests
    {
        [TestMethod]
        [Timeout(5000)]
        public async Task TestSendProgramData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1000);

            await kdu.SendNewProgramDataAsync(1, pr);
            KducerTighteningProgram prRead = await kdu.GetActiveTighteningProgramDataAsync();
            Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(pr.getProgramModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestGetProgramData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram prRead = await kdu.GetTighteningProgramDataAsync(200);

            prRead.SetLeverErrorOnOff(true);

            await kdu.SendNewProgramDataAsync(200, prRead);

            KducerTighteningProgram prReRead = await kdu.GetTighteningProgramDataAsync(200);

            Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prReRead.getProgramModbusHoldingRegistersAsByteArray()));

            KducerTighteningProgramTests.setAllParametersOfKduTighteningProgram(prRead);

            await kdu.SendNewProgramDataAsync(200, prRead);

            prReRead = await kdu.GetTighteningProgramDataAsync(200);

            Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prReRead.getProgramModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task TestSendMultipleProgramData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1000);

            Dictionary<ushort, KducerTighteningProgram> prDic = new();
            for (ushort i = 1; i <= 200; i++)
                prDic.Add(i, pr);

            await kdu.SendMultipleNewProgramsDataAsync(prDic);

            Dictionary<ushort, KducerTighteningProgram> prRead = await kdu.GetAllTighteningProgramDataAsync();
            for (ushort i = 1; i <= 200; i++)
                Assert.IsTrue(prRead[i].getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prDic[i].getProgramModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestRunScrewdriverUntilResult()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1000);

            await kdu.SendNewProgramDataAsync(1, pr);
            await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestGetResultAfterManuallyRunScrewdriver()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1000);
            await kdu.SendNewProgramDataAsync(await kdu.GetProgramNumberAsync(), pr);

            Console.WriteLine("Manually run screwdriver until result...");
            Console.WriteLine(await kdu.GetResultAsync(CancellationToken.None));
        }

        [TestMethod]
        [Timeout(500)]
        public async Task TestCancelGetResultAsync()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            CancellationTokenSource src = new CancellationTokenSource();
            Task waitForResult = kdu.GetResultAsync(src.Token);
            await Task.Delay(100);
            src.Cancel();
            await Assert.ThrowsExceptionAsync<System.Threading.Tasks.TaskCanceledException>(async () => await waitForResult);
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestSelectProgramAsync()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            ushort pr_set = 60;
            await kdu.SelectProgramNumberAsync(pr_set);
            ushort pr_get = await kdu.GetProgramNumberAsync();
            Assert.AreEqual(pr_set, pr_get);
        }

    }

    [TestClass]
    public class KducerTighteningResultsTests
    {
        [TestMethod]
        public void TestGetXYZFromPrebakedresult()
        {
            string strRes = "65 66 67 45 97 98 99 45 49 50 51 52 0 0 0 0 0 0 0 50 0 4 0 30 162 221 1 115 3 232 0 110 1 44 2 75 0 0 0 5 0 0 0 0 0 0 0 63 0 63 0 0 0 0 0 63 0 20 0 0 0 50 3 251 0 0 2 88 0 0 0 34 101 120 97 109 112 108 101 32 100 101 115 99 32 102 111 114 32 116 101 115 116 0 0 0 0 0 0 0 0 0 0 24 0 3 0 15 0 14 0 15 0 33 0 0 0 0 0 0 0 3 0 0 0 0 0 0 0 0 0 0 48 52 \r\n63";
            byte[] res = strRes.Split(' ').Select(byte.Parse).ToArray();
            KducerTighteningResult tighteningRes = new(res, false);
            Assert.AreEqual(tighteningRes.GetTorqueResult(), 63);
            Assert.AreEqual(tighteningRes.GetPeakTorque(), 63);
            Assert.AreEqual(tighteningRes.GetRunningTorque(), 0);
            Assert.AreEqual(tighteningRes.GetPrevailingTorque(), 0);
            Assert.AreEqual(tighteningRes.GetAngleResult(), 20);
            Assert.IsFalse(tighteningRes.IsScrewOK());
            Assert.AreEqual(tighteningRes.GetBarcode(), "ABC-abc-1234");
            Assert.AreEqual(tighteningRes.GetProgramDescription(), "example desc for test");
            Assert.AreEqual(tighteningRes.GetScrewdriverModel(), "KDS-MT1.5");
            Assert.AreEqual(tighteningRes.GetScrewdriverSerialNr(), (uint)2007773);
            Assert.AreEqual(tighteningRes.GetProgramNr(), 50);
            Assert.AreEqual(tighteningRes.GetTargetTorque(), 110);
            Assert.AreEqual(tighteningRes.GetTargetSpeed(), 300);
            Assert.AreEqual(tighteningRes.GetTargetScrewsOKcount(), 5);
            Assert.AreEqual(tighteningRes.GetScrewTime(), 587);
            Assert.AreEqual(tighteningRes.GetScrewsOKcount(), 0);
            Assert.AreEqual(tighteningRes.GetSequence(), '-');
            Assert.AreEqual(tighteningRes.GetSequenceNr(), 0);
            Assert.AreEqual(tighteningRes.GetProgramIdxInSequence(), 0);
            Assert.AreEqual(tighteningRes.GetNrProgramsInSequence(), 0);
            Assert.AreEqual(tighteningRes.GetResultCode().ToLower(), "Over Max Angle".ToLower());
            Assert.AreEqual(tighteningRes.GetResultTimestamp(), "2024-03-15 14:15:33");

            strRes = "65 66 67 45 97 98 99 45 49 50 51 52 0 0 0 0 0 1 0 51 0 4 0 30 162 221 1 115 3 232 0 50 1 44 2 56 0 1 0 1 0 0 0 0 0 0 0 50 0 50 0 8 0 3 0 50 3 231 0 0 0 0 0 0 0 0 2 88 0 0 0 13 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 24 0 3 0 15 0 15 0 15 0 38 0 0 0 0 0 0 0 5 0 0 0 0 0 0 0 0 0 0 48 52";
            res = strRes.Split(' ').Select(byte.Parse).ToArray();
            tighteningRes = new(res, false);
            Assert.AreEqual(tighteningRes.GetTorqueResult(), 50);
            Assert.AreEqual(tighteningRes.GetPeakTorque(), 50);
            Assert.AreEqual(tighteningRes.GetRunningTorque(), 3);
            Assert.AreEqual(tighteningRes.GetPrevailingTorque(), 3);
            Assert.AreEqual(tighteningRes.GetAngleResult(), 999);
            Assert.IsTrue(tighteningRes.IsScrewOK());
            Assert.AreEqual(tighteningRes.GetBarcode(), "ABC-abc-1234");
            Assert.AreEqual(tighteningRes.GetProgramDescription(), "");
            Assert.AreEqual(tighteningRes.GetScrewdriverModel(), "KDS-MT1.5");
            Assert.AreEqual(tighteningRes.GetScrewdriverSerialNr(), (uint)2007773);
            Assert.AreEqual(tighteningRes.GetProgramNr(), 51);
            Assert.AreEqual(tighteningRes.GetTargetTorque(), 50);
            Assert.AreEqual(tighteningRes.GetTargetSpeed(), 300);
            Assert.AreEqual(tighteningRes.GetTargetScrewsOKcount(), 1);
            Assert.AreEqual(tighteningRes.GetScrewTime(), 568);
            Assert.AreEqual(tighteningRes.GetScrewsOKcount(), 1);
            Assert.AreEqual(tighteningRes.GetSequence(), '-');
            Assert.AreEqual(tighteningRes.GetSequenceNr(), 0);
            Assert.AreEqual(tighteningRes.GetProgramIdxInSequence(), 0);
            Assert.AreEqual(tighteningRes.GetNrProgramsInSequence(), 0);
            Assert.AreEqual(tighteningRes.GetResultCode().ToLower(), "Screw OK".ToLower());
            Assert.AreEqual(tighteningRes.GetResultTimestamp(), "2024-03-15 15:15:38");

            strRes = "0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 1 0 55 0 4 0 30 162 221 1 115 3 232 0 50 1 44 3 254 0 1 0 1 0 3 0 2 0 2 0 49 0 49 0 0 0 0 0 49 7 150 0 0 0 0 0 0 0 0 2 88 0 0 0 13 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 24 0 3 0 15 0 15 0 16 0 12 0 0 0 0 0 0 0 9 0 0 0 0 0 0 0 0 0 0 48 52";
            res = strRes.Split(' ').Select(byte.Parse).ToArray();
            tighteningRes = new(res, false);
            Assert.AreEqual(tighteningRes.GetTorqueResult(), 49);
            Assert.AreEqual(tighteningRes.GetPeakTorque(), 49);
            Assert.AreEqual(tighteningRes.GetRunningTorque(), 0);
            Assert.AreEqual(tighteningRes.GetPrevailingTorque(), 0);
            Assert.AreEqual(tighteningRes.GetAngleResult(), 1942);
            Assert.IsTrue(tighteningRes.IsScrewOK());
            Assert.AreEqual(tighteningRes.GetBarcode(), "");
            Assert.AreEqual(tighteningRes.GetProgramDescription(), "");
            Assert.AreEqual(tighteningRes.GetScrewdriverModel(), "KDS-MT1.5");
            Assert.AreEqual(tighteningRes.GetScrewdriverSerialNr(), (uint)2007773);
            Assert.AreEqual(tighteningRes.GetProgramNr(), 55);
            Assert.AreEqual(tighteningRes.GetTargetTorque(), 50);
            Assert.AreEqual(tighteningRes.GetTargetSpeed(), 300);
            Assert.AreEqual(tighteningRes.GetTargetScrewsOKcount(), 1);
            Assert.AreEqual(tighteningRes.GetScrewTime(), 1022);
            Assert.AreEqual(tighteningRes.GetScrewsOKcount(), 1);
            Assert.AreEqual(tighteningRes.GetSequence(), 'C');
            Assert.AreEqual(tighteningRes.GetSequenceNr(), 3);
            Assert.AreEqual(tighteningRes.GetProgramIdxInSequence(), 2);
            Assert.AreEqual(tighteningRes.GetNrProgramsInSequence(), 2);
            Assert.AreEqual(tighteningRes.GetResultCode().ToLower(), "Screw OK".ToLower());
            Assert.AreEqual(tighteningRes.GetResultTimestamp(), "2024-03-15 15:16:12");
        }
    }

    [TestClass]
    public class KducerTighteningProgramTests
    {
        [TestMethod]
        [Timeout(1000)]
        public void TestSetValues()
        {
            KducerTighteningProgram builtPr = new KducerTighteningProgram("KDS-MT1.5");
            Assert.AreEqual(300, builtPr.GetFinalSpeed());

            byte[] prFromKdu = [0, 0, 0, 50, 0, 0, 0, 150, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 2, 88, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 44, 0, 0, 0, 150, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            builtPr.SetSocket(1);
            Assert.IsTrue(prFromKdu.SequenceEqual(builtPr.getProgramModbusHoldingRegistersAsByteArray()));

            builtPr = new KducerTighteningProgram(new byte[230]);
            setAllParametersOfKduTighteningProgram(builtPr);

            prFromKdu = [0, 0, 0, 50, 0, 0, 7, 208, 0, 0, 3, 232, 0, 0, 60, 140, 0, 0, 62, 128, 0, 0, 58, 152, 0, 0, 0, 100, 0, 0, 1, 99, 0, 0, 3, 82, 0, 0, 19, 136, 0, 1, 0, 0, 111, 27, 0, 0, 0, 25, 0, 0, 0, 0, 0, 0, 0, 60, 0, 0, 0, 150, 0, 1, 0, 0, 0, 0, 0, 0, 14, 16, 0, 0, 2, 88, 0, 0, 3, 231, 0, 1, 0, 0, 0, 0, 0, 0, 5, 220, 0, 0, 0, 10, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 255, 116, 101, 115, 116, 32, 112, 114, 111, 103, 114, 97, 109, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 1, 1, 44, 1, 244, 0, 10, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            for(int i = 0; i < prFromKdu.Length; i++)
                if (prFromKdu[i] != builtPr.getProgramModbusHoldingRegistersAsByteArray()[i])
                    Console.WriteLine($"builtPr[{i}]={builtPr.getProgramModbusHoldingRegistersAsByteArray()[i]}, prFromKdu[{i}]={prFromKdu[i]}" );
            Assert.IsTrue(prFromKdu.SequenceEqual(builtPr.getProgramModbusHoldingRegistersAsByteArray()));
        }

        internal static void setAllParametersOfKduTighteningProgram(KducerTighteningProgram builtPr)
        {
            builtPr.SetAngleMin(15000);
            builtPr.SetAngleMax(16000);
            builtPr.SetTorqueAngleMode(1);
            builtPr.SetAngleTarget(15500);
            builtPr.SetTorqueMax(2000);
            builtPr.SetTorqueMin(1000);
            builtPr.SetTorqueTarget(50);
            builtPr.SetDownshiftInitialSpeed(850);
            builtPr.SetDownshiftThreshold(5000);
            builtPr.SetDescription("test program");
            builtPr.SetFinalSpeed(355);
            builtPr.SetMaxPowerPhaseAngle(3600);
            builtPr.SetMaxPowerPhaseMode(1);
            builtPr.SetMaxReverseTorque(999);
            builtPr.SetMaxtime(150);
            builtPr.SetMintime(60);
            builtPr.SetDownshiftAtAngleOnOff(true);
            builtPr.SetRampOnOff(true);
            builtPr.SetMaxTimeOnOff(true);
            builtPr.SetMinTimeOnOff(true);
            builtPr.SetPressOkOnOff(true);
            builtPr.SetPressEscOnOff(true);
            builtPr.SetLeverErrorOnOff(true);
            builtPr.SetReverseAllowedOnOff(true);
            builtPr.SetUseDock05Screwdriver2OnOff(true);
            builtPr.SetNumberOfScrews(255);
            builtPr.SetPreTighteningReverseAngle(1500);
            builtPr.SetPreTighteningReverseDelay(10);
            builtPr.SetPreTighteningReverseMode(1);
            builtPr.SetRamp(25);
            builtPr.SetReverseSpeed(600);
            builtPr.SetRunningTorqueMax(30);
            builtPr.SetRunningTorqueMin(10);
            builtPr.SetRunningTorqueMode(1);
            builtPr.SetRunningTorqueWindowEnd(500);
            builtPr.SetRunningTorqueWindowStart(300);
            builtPr.SetSocket(8);
            builtPr.SetAngleStartAt(100);
            builtPr.SetAngleStartAtMode(0);
            builtPr.SetAfterTighteningReverseTime(5);
            builtPr.SetAfterTighteningReverseDelay(8);
            builtPr.SetAfterTighteningReverseMode(0);
            builtPr.SetTorqueCompensationValue(1000);
        }
    }
}