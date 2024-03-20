// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using Kolver;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace KducerTests
{
    static class TestConstants
    {
        public static string REAL_LIVE_KDU_IP = "192.168.32.103";
        public static string OFF_OR_DC_KDU_IP = "192.168.32.69";
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
        public async Task TestRunScrewdriverUntilResult()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestGetResultAfterManuallyRunScrewdriver()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
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

        [TestMethod]
        public async Task TestGetTorque2()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            Console.WriteLine("Manually run screwdriver until result...");
            KducerTighteningResult res = await kdu.GetResultAsync(CancellationToken.None);
            Console.WriteLine(res.GetResultTimestamp());
        }
    }

}