using Kolver;
using System.Collections;
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
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), 100);
            Assert.IsFalse(kdu.Connected());
            await kdu.ConnectAsync(new CancellationToken());
            Assert.IsTrue(kdu.Connected());
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestCancelConnectToDeadKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.OFF_OR_DC_KDU_IP), 3000);
            CancellationTokenSource src = new CancellationTokenSource();
            Task connectionAttempt = kdu.ConnectAsync(src.Token);
            Assert.IsFalse(kdu.Connected());
            await Task.Delay(100);
            Assert.IsFalse(kdu.Connected());
            src.Cancel();
            await Task.Delay(5);
            Assert.IsTrue(connectionAttempt.IsCanceled);
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestConnectToDeadKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.OFF_OR_DC_KDU_IP), 1000);
            await Assert.ThrowsExceptionAsync<System.TimeoutException>(async () => await kdu.ConnectAsync(new CancellationToken()));
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestReConnectToLiveKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            Assert.IsFalse(kdu.Connected());
            await kdu.ConnectAsync(new CancellationToken());
            Assert.IsTrue(kdu.Connected());
            await kdu.ConnectAsync(new CancellationToken());
            Assert.IsTrue(kdu.Connected());
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestReadHoldingRegisters()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync(new CancellationToken());
            Assert.IsTrue(kdu.Connected());
            Console.WriteLine(await kdu.ReadHoldingRegistersAsync(0, 12));
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestReadInputRegisters()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync(new CancellationToken());
            Assert.IsTrue(kdu.Connected());
            Console.WriteLine(await kdu.ReadInputRegistersAsync(0, 12));
        }

        [TestMethod]
        [Timeout(3000)]
        public async Task TestWriteCoil()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync(new CancellationToken());
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
            Kducer kdu = Kducer.CreateKducerAndStartAsyncComms(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), new CancellationToken());
            await kdu.RunScrewdriverUntilResultAsync(new CancellationToken());
            Assert.IsTrue(kdu.HasNewResult());
            Console.WriteLine(String.Join(",", await kdu.GetResultAsync()));
            kdu.Dispose();
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestGetResultAfterManuallyRunScrewdriver()
        {
            using Kducer kdu = Kducer.CreateKducerAndStartAsyncComms(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), new CancellationToken());
            kdu.lockScrewdriverUntilResultsProcessed = true;
            Console.WriteLine("Manually run screwdriver until result...");
            Console.WriteLine(await kdu.GetResultAsync());
        }
    }

    [TestClass]
    public class KducerTighteningResultsTests
    {
        [TestMethod]
        public void TestGetTorque()
        {
            byte[] res = new byte[] {
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x32,
                0x00, 0x04, 0x00, 0x1E, 0xA2, 0xDD, 0x01, 0x73, 0x03, 0xE8,
                0x00, 0x6E, 0x01, 0x2C, 0x00, 0x49, 0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x52, 0x00, 0x54,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x52, 0x00, 0x14, 0x00, 0x00,
                0x00, 0x32, 0x00, 0x0B, 0x00, 0x00, 0x02, 0x58, 0x00, 0x00,
                0x00, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x18, 0x00, 0x03, 0x00, 0x0E, 0x00, 0x10,
                0x00, 0x0E, 0x00, 0x2F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x30, 0x34
            };
            KducerTighteningResult kdures = new(res);
            Console.WriteLine(kdures.GetTorque());
        }

        [TestMethod]
        public async Task TestGetTorque2()
        {
            using Kducer kdu = Kducer.CreateKducerAndStartAsyncComms(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), new CancellationToken());
            Console.WriteLine("Manually run screwdriver until result...");
            byte[] res = await kdu.GetResultAsync();
            KducerTighteningResult kdures = new(res);
            Console.WriteLine(kdures.GetTorque());
        }
    }

}