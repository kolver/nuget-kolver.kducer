// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using Kolver;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Sockets;

// note: some of these tests require a real KDU-1A controller to be connected to pass. some require using the screwdriver to pass.
// all the tests are run and must pass before every commit to the main branch
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

        [TestMethod]
        [Timeout(10000)]
        public async Task TestPullCableFromKdu()
        {
            using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
            await kdu.ConnectAsync();
            Assert.IsTrue(kdu.Connected());
            Console.WriteLine("Pull the cord in the next 5 seconds");
            await Task.Delay(5000);
            Console.WriteLine("Now checking if Exceptions are thrown");
            await Assert.ThrowsExceptionAsync<System.Net.Sockets.SocketException>(async () => await kdu.ReadInputRegistersAsync(0, 10));
        }
    }

    [TestClass]
    public class KducerTests
    {
        [TestMethod]
        [Timeout(10000)]
        public async Task TestClearResultsQueue()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            await kdu.IsConnectedWithTimeoutAsync();

            kdu.ClearResultsQueue();
            Assert.IsFalse(kdu.HasNewResult());

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1);

            await kdu.SelectProgramNumberAsync(1);
            await kdu.SendNewProgramDataAsync(1, pr);
            await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);

            Assert.IsFalse(kdu.HasNewResult());

            Console.WriteLine("Run the screwdriver...");
            await Task.Delay(5000);
            Assert.IsTrue(kdu.HasNewResult());

            kdu.ClearResultsQueue();
            Assert.IsFalse(kdu.HasNewResult());
        }

        [TestMethod]
        [Timeout(10000)]
        public async Task TestSendProgramData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1000);

            await kdu.SendNewProgramDataAsync(1, pr);
            KducerTighteningProgram prRead = await kdu.GetActiveTighteningProgramDataAsync();
            Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(pr.getProgramModbusHoldingRegistersAsByteArray()));

            pr.SetTorqueTarget(60000); // invalid torque value is refused by KDU
            await Assert.ThrowsExceptionAsync<ModbusException>(async () => await kdu.SendNewProgramDataAsync(1, pr));
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task TestGetProgramData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            ushort prNumber = 200;
            if (await kdu.GetKduMainboardVersionAsync() < 38)
                prNumber = 64;

            KducerTighteningProgram prRead = await kdu.GetTighteningProgramDataAsync(prNumber);

            prRead.SetLeverErrorOnOff(true);

            await kdu.SendNewProgramDataAsync(prNumber, prRead);

            KducerTighteningProgram prReRead = await kdu.GetTighteningProgramDataAsync(prNumber);

            Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prReRead.getProgramModbusHoldingRegistersAsByteArray()));

            KducerTighteningProgramTests.setAllParametersOfKduTighteningProgram(prRead);

            await kdu.SendNewProgramDataAsync(prNumber, prRead);

            prReRead = await kdu.GetTighteningProgramDataAsync(prNumber);

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
            ushort maxProg = 200;
            if (await kdu.GetKduMainboardVersionAsync() < 38)
                maxProg = 64;

            for (ushort i = 1; i <= maxProg; i++)
                prDic.Add(i, pr);

            await kdu.SendMultipleNewProgramsDataAsync(prDic);

            Dictionary<ushort, KducerTighteningProgram> prRead = await kdu.GetAllTighteningProgramDataAsync();
            for (ushort i = 1; i <= maxProg; i++)
                Assert.IsTrue(prRead[i].getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prDic[i].getProgramModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(10000)]
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
        [Timeout(10000)]
        public async Task TestGetResultAfterManuallyRunScrewdriver()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1000);
            await kdu.SendNewProgramDataAsync(await kdu.GetProgramNumberAsync(), pr);

            Console.WriteLine("Manually run screwdriver until result...");
            Console.WriteLine((await kdu.GetResultAsync(CancellationToken.None)).GetResultsAsCSVstringSingleLine());
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
        [Timeout(15000)]
        public async Task TestSelectProgramAsync()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            ushort pr_set = 60;
            await kdu.SelectProgramNumberAsync(pr_set);
            ushort pr_get = await kdu.GetProgramNumberAsync();
            Assert.AreEqual(pr_set, pr_get);
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task TestSelectSequenceAsync()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            ushort seq_set = 4;
            await kdu.SelectSequenceNumberAsync(seq_set);
            ushort seq_get = await kdu.GetSequenceNumberAsync();
            Assert.AreEqual(seq_set, seq_get);
        }

        [TestMethod]
        [Timeout(20000)]
        public async Task TestPullCableFromKdu()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
            Assert.IsTrue(kdu.IsConnected());
            Console.WriteLine("Pull the cord in the next 5 seconds");
            await Task.Delay(5000);
            Console.WriteLine("Now checking if exceptions are thrown");
            Assert.IsFalse(kdu.IsConnected());
            Assert.IsFalse(kdu.IsConnectedWithTimeoutBlocking(250));
            Assert.IsFalse(await kdu.IsConnectedWithTimeoutAsync(250));
            await Assert.ThrowsExceptionAsync<SocketException>(async () => await kdu.GetResultAsync(CancellationToken.None, true));
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task TestAutoReconnectToKdu()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
            Assert.IsTrue(kdu.IsConnected());
            Console.WriteLine("Pull the cord in the next 5 seconds");
            await Task.Delay(5000);
            Assert.IsFalse(kdu.IsConnected());
            Assert.IsFalse(kdu.IsConnectedWithTimeoutBlocking(250));
            Assert.IsFalse(await kdu.IsConnectedWithTimeoutAsync(250));
            Console.WriteLine("Plug the cord back in");
            Assert.IsTrue(kdu.IsConnectedWithTimeoutBlocking(5000));
            Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
            Assert.IsTrue(kdu.IsConnected());
            await kdu.GetResultAsync(CancellationToken.None, true);
        }

        [TestMethod]
        [Timeout(1000)]
        public async Task TestGetResultThrowsExceptionWhenDisconnected()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.OFF_OR_DC_KDU_IP), NullLoggerFactory.Instance);
            Assert.IsFalse(kdu.IsConnected());
            Assert.IsFalse(kdu.IsConnectedWithTimeoutBlocking(250));
            Assert.IsFalse(await kdu.IsConnectedWithTimeoutAsync(250));
            await Assert.ThrowsExceptionAsync<System.Net.Sockets.SocketException>(async () => await kdu.GetResultAsync(CancellationToken.None, true));
        }
        [TestMethod]
        [Timeout(10000)]
        public async Task TestSendBarcode()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
            Assert.IsTrue(kdu.IsConnected());
            List<string> valid_barcodes = [" ", "ABcd", "1234567890123456", "!@#$%^&*()_-=|\\-", ",,,,", "my unique code"];
            foreach (string barcode in valid_barcodes)
            {
                Console.WriteLine($"Look at the barcode, should be {barcode}");
                await kdu.SendBarcodeAsync(barcode);
                await Task.Delay(1000);
            }
        }


        [TestMethod]
        [Timeout(20000)]
        public async Task TestSendBarcodeWithResults()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
            Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
            Assert.IsTrue(kdu.IsConnected());
            // send an angle control program for quick testing
            KducerTighteningProgram pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1);
            pr.SetFinalSpeed(50);
            await kdu.SendNewProgramDataAsync(1, pr);
            await kdu.SelectProgramNumberAsync(1);

            List<string> valid_barcodes = ["", "ABcd", "1234567890123456", "!@#$%^&*()_-=|\\-", "/?,.:;\'\"{}[]", "my unique code"]; // note: commas are replaced with dots because CSV results!

            await Assert.ThrowsExceptionAsync<ArgumentException>(async () => await kdu.SendBarcodeAsync("this barcode is too long!"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(async () => await kdu.SendBarcodeAsync("12345678901234567")); // 17 chars, too long

            foreach (string barcode in valid_barcodes)
            {
                await kdu.SendBarcodeAsync(barcode);
                KducerTighteningResult res = await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
                string bc = res.GetBarcode();
                if (string.IsNullOrEmpty(barcode))
                    Assert.IsTrue(bc.Equals(" "));
                else
                    Assert.IsTrue(bc.Equals(barcode.Replace(',', '.')));
                // do twice, barcode should stay if program didn't change
                res = await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
                bc = res.GetBarcode();
                if (string.IsNullOrEmpty(barcode))
                    Assert.IsTrue(bc.Equals(" "));
                else
                    Assert.IsTrue(bc.Equals(barcode.Replace(',', '.')));
            }
            // verify barcode goes away when changing programs
            pr = new KducerTighteningProgram();
            pr.SetTorqueAngleMode(1);
            pr.SetAngleTarget(1);
            pr.SetFinalSpeed(50);
            await kdu.SendNewProgramDataAsync(2, pr);
            KducerTighteningResult res2 = await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
            Assert.IsTrue(res2.GetBarcode().Equals(""));
        }

        [TestMethod]
        [Timeout(10000)]
        public async Task TestSendSequenceData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            KducerSequenceOfTighteningPrograms seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [3, 50, 3, 3], "barcode", await kdu.GetKduMainboardVersionAsync());

            await kdu.SendNewSequenceDataAsync(1, seq);
            KducerSequenceOfTighteningPrograms seqRead = await kdu.GetActiveSequenceDataAsync();
            Assert.IsTrue(seqRead.getSequenceModbusHoldingRegistersAsByteArray().SequenceEqual(seq.getSequenceModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task TestGetSequenceData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            ushort sequenceNumber = 24;
            if (await kdu.GetKduMainboardVersionAsync() < 38)
                sequenceNumber = 8;

            KducerSequenceOfTighteningPrograms seqRead = await kdu.GetSequenceDataAsync(sequenceNumber);

            await kdu.SendNewSequenceDataAsync(sequenceNumber, seqRead);

            KducerSequenceOfTighteningPrograms seqReRead = await kdu.GetSequenceDataAsync(sequenceNumber);

            Assert.IsTrue(seqRead.getSequenceModbusHoldingRegistersAsByteArray().SequenceEqual(seqReRead.getSequenceModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(30000)]
        public async Task TestSendMultipleSequencesData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            KducerSequenceOfTighteningPrograms seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [3, 50, 3, 3]);

            Dictionary<ushort, KducerSequenceOfTighteningPrograms> seqDic = new();
            ushort maxSeq = 24;
            if (await kdu.GetKduMainboardVersionAsync() < 38)
            {
                maxSeq = 8;
                seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [3, 50, 3, 3], "", 37);
            }

            for (ushort i = 1; i <= maxSeq; i++)
                seqDic.Add(i, seq);

            await kdu.SendMultipleNewSequencesDataAsync(seqDic);

            Dictionary<ushort, KducerSequenceOfTighteningPrograms> seqRead = await kdu.GetAllSequencesDataAsync();
            for (ushort i = 1; i <= maxSeq; i++)
                Assert.IsTrue(seqRead[i].getSequenceModbusHoldingRegistersAsByteArray().SequenceEqual(seqDic[i].getSequenceModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(10000)]
        public async Task TestGetGeneralSettingsData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            KducerControllerGeneralSettings genSetts = await kdu.GetGeneralSettingsDataAsync();

            await kdu.SendGeneralSettingsDataAsync(genSetts);

            KducerControllerGeneralSettings genSettsReRead = await kdu.GetGeneralSettingsDataAsync();

            Assert.IsTrue(genSetts.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(genSettsReRead.getGeneralSettingsModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(20000)]
        public async Task TestSendGeneralSettingsData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            KducerControllerGeneralSettings genSetts = await kdu.GetGeneralSettingsDataAsync();

            byte[] settingsFromKdu = [0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 84, 79, 82, 81, 85, 69, 32, 83, 84, 65, 84, 73, 79, 78, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 7, 161, 32, 0, 0, 0, 0];
            if (await kdu.GetKduMainboardVersionAsync() <= 37)
                settingsFromKdu = settingsFromKdu.ToList().GetRange(0, 78).ToArray();
            KducerControllerGeneralSettings newGenSetts = new KducerControllerGeneralSettings(settingsFromKdu);
            await kdu.SendGeneralSettingsDataAsync(newGenSetts);
            KducerControllerGeneralSettings genSettsRead = await kdu.GetGeneralSettingsDataAsync();
            Assert.IsTrue(newGenSetts.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(genSettsRead.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            settingsFromKdu = [0, 1, 0, 0, 0, 100, 0, 1, 0, 1, 0, 2, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 75, 68, 85, 67, 69, 82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 15, 66, 64, 0, 53, 0, 3];
            if (await kdu.GetKduMainboardVersionAsync() <= 37)
                settingsFromKdu = settingsFromKdu.ToList().GetRange(0, 78).ToArray();
            newGenSetts = new KducerControllerGeneralSettings(settingsFromKdu);
            if (await kdu.GetKduMainboardVersionAsync() <= 37)
                newGenSetts.SetSwbxAndCbs880Mode(0);
            if (await kdu.GetKduMainboardVersionAsync() <= 38)
                newGenSetts.SetCn3BitxPrSeqInputSelectionMode(0);
            await kdu.SendGeneralSettingsDataAsync(newGenSetts);
            genSettsRead = await kdu.GetGeneralSettingsDataAsync();
            byte[] read = genSettsRead.getGeneralSettingsModbusHoldingRegistersAsByteArray();
            byte[] sent = newGenSetts.getGeneralSettingsModbusHoldingRegistersAsByteArray();
            for (int i = 0; i < read.Length; i++)
                if (read[i] != sent[i])
                    Console.WriteLine($"read[{i}]={read[i]}, sent[{i}]={sent[i]}");
            Assert.IsTrue(sent.SequenceEqual(read));

            await kdu.SendGeneralSettingsDataAsync(genSetts);
            genSettsRead = await kdu.GetGeneralSettingsDataAsync();
            Assert.IsTrue(genSetts.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(genSettsRead.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            settingsFromKdu = [0, 100, 0, 0, 0, 100, 0, 1, 0, 1, 0, 2, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 75, 68, 85, 67, 69, 82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 15, 66, 64, 0, 53, 0, 3];
            if (await kdu.GetKduMainboardVersionAsync() <= 37)
                settingsFromKdu = settingsFromKdu.ToList().GetRange(0, 78).ToArray(); 
            newGenSetts = new KducerControllerGeneralSettings(settingsFromKdu); // these settings have 100 for language, invalid value
            await Assert.ThrowsExceptionAsync<ModbusException>(async () => await kdu.SendGeneralSettingsDataAsync(newGenSetts));
        }
        [TestMethod]
        [Timeout(60000)]
        public async Task TestSendAllDataGetAllData()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            // read, write, compare
            Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> sentData = await kdu.GetSettingsAndProgramsAndSequencesDataAsync();
            await kdu.SendAllSettingsProgramsAndSequencesDataAsync(sentData);
            Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> reReadKduData = await kdu.GetSettingsAndProgramsAndSequencesDataAsync();

            Assert.IsTrue(sentData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(reReadKduData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            Assert.IsTrue(sentData.Item2.Count == reReadKduData.Item2.Count);
            for (ushort i = 1; i <= sentData.Item2.Keys.Max(); i++)
                Assert.IsTrue(sentData.Item2[i].getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(sentData.Item2[i].getProgramModbusHoldingRegistersAsByteArray()));

            Assert.IsTrue(sentData.Item3.Count == reReadKduData.Item3.Count);
            for (ushort i = 1; i < sentData.Item3.Keys.Max(); i++)
                Assert.IsTrue(sentData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray().SequenceEqual(sentData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray()));
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestSendFile()
        {
            using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));

            Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> sentData, reReadKduData;
            
            // send file, read, compare
            sentData = await KducerKduDataFileReader.ReadKduDataFile("../../../v37 file.kdu");

            await kdu.SendAllSettingsProgramsAndSequencesDataAsync(sentData);
            reReadKduData = await kdu.GetSettingsAndProgramsAndSequencesDataAsync();

            Assert.IsTrue(sentData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(reReadKduData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            // Assert.IsTrue(sentData.Item2.Count == reReadKduData.Item2.Count); this is not true if file is v37 and KDU is v38
            for (ushort i = 1; i <= sentData.Item2.Keys.Max(); i++)
                Assert.IsTrue(sentData.Item2[i].getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(reReadKduData.Item2[i].getProgramModbusHoldingRegistersAsByteArray()));

            // Assert.IsTrue(sentData.Item3.Count == reReadKduData.Item3.Count); this is not true if file is v37 and KDU is v38
            for (ushort i = 1; i < sentData.Item3.Keys.Max(); i++)
            {
                byte[] sentArray = sentData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray();
                if (await kdu.GetKduMainboardVersionAsync() >= 38)
                    sentArray = sentData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater();
                Assert.IsTrue(sentArray.SequenceEqual(reReadKduData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray()));
            }

            // send file, read, compare
            sentData = await KducerKduDataFileReader.ReadKduDataFile("../../../v38 file.kdu");
            if (await kdu.GetKduMainboardVersionAsync() <= 37)
            {
                for (ushort i = 65; i <= 200; i++)
                    sentData.Item2.Remove(i);
                for (ushort i = 9; i <= 24; i++)
                    sentData.Item3.Remove(i);
                Dictionary<ushort, KducerSequenceOfTighteningPrograms> seqs_reduced = new();
                for (ushort i = 1; i <= 8; i++)
                    seqs_reduced.Add(i, new KducerSequenceOfTighteningPrograms(sentData.Item3[i].getProgramsAsByteArray().ToList().GetRange(0, 16), sentData.Item3[i].getLinkModesAsByteArray().ToList().GetRange(0, 16), sentData.Item3[i].getLinkTimesAsByteArray().ToList().GetRange(0, 16), "", 37));
                sentData = Tuple.Create(sentData.Item1, sentData.Item2, seqs_reduced);
            }
            await kdu.SendAllSettingsProgramsAndSequencesDataAsync(sentData);
            reReadKduData = await kdu.GetSettingsAndProgramsAndSequencesDataAsync();

            if (await kdu.GetKduMainboardVersionAsync() <= 37)
                Assert.IsTrue(sentData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior().SequenceEqual(reReadKduData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior()));
            else
                Assert.IsTrue(sentData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(reReadKduData.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            Assert.IsTrue(sentData.Item2.Count == reReadKduData.Item2.Count);
            for (ushort i = 1; i <= sentData.Item2.Keys.Max(); i++)
                Assert.IsTrue(sentData.Item2[i].getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(reReadKduData.Item2[i].getProgramModbusHoldingRegistersAsByteArray()));

            Assert.IsTrue(sentData.Item3.Count == reReadKduData.Item3.Count);
            for (ushort i = 1; i < sentData.Item3.Keys.Max(); i++)
                Assert.IsTrue(sentData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray().SequenceEqual(reReadKduData.Item3[i].getSequenceModbusHoldingRegistersAsByteArray()));
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

            prFromKdu = [0, 0, 0, 50, 0, 0, 7, 208, 0, 0, 3, 232, 0, 0, 60, 140, 0, 0, 62, 128, 0, 0, 58, 152, 0, 0, 0, 100, 0, 0, 1, 99, 0, 0, 3, 82, 0, 0, 19, 136, 0, 1, 0, 0, 111, 27, 0, 0, 0, 25, 0, 0, 0, 0, 0, 0, 0, 60, 0, 0, 0, 150, 0, 1, 0, 0, 0, 0, 0, 0, 14, 16, 0, 0, 2, 88, 0, 0, 3, 231, 0, 1, 0, 0, 0, 0, 0, 0, 5, 220, 0, 0, 0, 10, 0, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 99, 116, 101, 115, 116, 32, 112, 114, 111, 103, 114, 97, 109, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 1, 1, 44, 1, 244, 0, 10, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            for (int i = 0; i < prFromKdu.Length; i++)
                if (prFromKdu[i] != builtPr.getProgramModbusHoldingRegistersAsByteArray()[i])
                    Console.WriteLine($"builtPr[{i}]={builtPr.getProgramModbusHoldingRegistersAsByteArray()[i]}, prFromKdu[{i}]={prFromKdu[i]}");
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
            builtPr.SetNumberOfScrews(99);
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

    [TestClass]
    public class KducerSequenceOfTighteningProgramsTests
    {
        [TestMethod]
        public void TestConstructors()
        {
            byte[] defaultSeq = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3];
            KducerSequenceOfTighteningPrograms seq = new KducerSequenceOfTighteningPrograms(defaultSeq);
            byte[] seqBytesFromseq = seq.getSequenceModbusHoldingRegistersAsByteArray();
            Assert.IsTrue(defaultSeq.SequenceEqual<byte>(seqBytesFromseq));

            byte[] progs = [1];
            byte[] links = [1];
            seq = new KducerSequenceOfTighteningPrograms(progs.ToList(), links.ToList());
            seqBytesFromseq = seq.getSequenceModbusHoldingRegistersAsByteArray();
            Assert.IsTrue(defaultSeq.SequenceEqual<byte>(seqBytesFromseq));
            Assert.IsTrue(new List<byte>(progs).SequenceEqual(seq.getProgramsAsByteArray()));
            Assert.IsTrue(new List<byte>(links).SequenceEqual(seq.getLinkModesAsByteArray()));
            Assert.IsTrue(new List<byte>([3]).SequenceEqual(seq.getLinkTimesAsByteArray()));

            byte[] mixedSeq = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 50, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3];
            seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [0, 50, 0, 0]);
            seqBytesFromseq = seq.getSequenceModbusHoldingRegistersAsByteArray();
            Assert.IsTrue(mixedSeq.SequenceEqual<byte>(seqBytesFromseq));
            Assert.IsTrue(new List<byte>([3, 50, 3, 3]).SequenceEqual(seq.getLinkTimesAsByteArray()));
            seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [0, 50, 0, 0]);
            seqBytesFromseq = seq.getSequenceModbusHoldingRegistersAsByteArray();
            Assert.IsTrue(mixedSeq.SequenceEqual<byte>(seqBytesFromseq));
            seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [0, 50, 0, 0, 3, 3]);
            seqBytesFromseq = seq.getSequenceModbusHoldingRegistersAsByteArray();
            Assert.IsTrue(mixedSeq.SequenceEqual<byte>(seqBytesFromseq));

            seq = new KducerSequenceOfTighteningPrograms([1, 2, 3, 6], [0, 0, 1, 2], [0, 0, 0, 0]);
            seqBytesFromseq = seq.getSequenceModbusHoldingRegistersAsByteArray();
            Assert.IsFalse(mixedSeq.SequenceEqual<byte>(seqBytesFromseq));
            Assert.IsTrue(new List<byte>([1, 2, 3, 6]).SequenceEqual(seq.getProgramsAsByteArray()));
            Assert.IsTrue(new List<byte>([0, 0, 1, 2]).SequenceEqual(seq.getLinkModesAsByteArray()));

            // must throw no exception
            seq = new KducerSequenceOfTighteningPrograms(new List<byte>([1, 2, 3, 60]));
            Assert.IsTrue(seq.GetBarcode().Equals(""));
            seq = new KducerSequenceOfTighteningPrograms(new List<byte>([1, 2, 3, 6, 0, 35]));
            seq = new KducerSequenceOfTighteningPrograms(new List<byte>([200, 200, 200]));
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], null, "");
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], null, null, "");
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [], "");
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [], [], "");
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [], "");
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], null, [], "");
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [], null, "");
            Assert.IsTrue(seq.GetBarcode().Equals(""));
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [], [], null);
            Assert.IsTrue(seq.GetBarcode().Equals(""));
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], "1234567890123456");
            Assert.IsTrue(seq.GetBarcode().Equals("1234567890123456"));
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [2, 1, 0], [3]);
            seq = new KducerSequenceOfTighteningPrograms([200, 200, 200], [2, 1, 0], [3, 3, 3, 3, 3]);
            seq = new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 16).ToList());
            seq.getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior();
            seq.getSequenceModbusHoldingRegistersAsByteArray_KDUv38andLater();
            seq.getSequenceModbusHoldingRegistersAsByteArray();

            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms(new List<byte>([0])));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms(new List<byte>([0, 1, 2])));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms(new List<byte>([])));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([0]));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([0, 1, 2]));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([]));
            Assert.ThrowsException<ArgumentNullException>(() => new KducerSequenceOfTighteningPrograms(null));
            Assert.ThrowsException<ArgumentNullException>(() => new KducerSequenceOfTighteningPrograms(null, null, null, null));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([200], "", 37));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([200], "this barcode is too long"));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([200], "12345678901234567"));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([1], [3]));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([1], [2, 2]));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([1, 1], [2]));
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms([1, 1], [2], [3]));

            // list constructor
            seq = new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 16).ToList());
            seq = new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 32).ToList());
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 33).ToList()));

            // array constructor
            seq = new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 64).ToList().ToArray());
            seq = new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 112).ToList().ToArray());
            Assert.ThrowsException<ArgumentException>(() => new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 65).ToList().ToArray()));

            // asking for v37 sequence with more than 16 programs
            Assert.ThrowsException<InvalidOperationException>(() => new KducerSequenceOfTighteningPrograms(Enumerable.Repeat<byte>(2, 112).ToList().ToArray()).getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior());

        }
    }

    [TestClass]
    public class KducerControllerGeneralSettingsTests
    {
        [TestMethod]
        [Timeout(1000)]
        public void TestSetValues()
        {
            KducerControllerGeneralSettings builtSettings = new KducerControllerGeneralSettings();
            Assert.AreEqual((uint)500000, builtSettings.GetCalibrationReminderInterval());

            byte[] settingsFromKdu = [0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 84, 79, 82, 81, 85, 69, 32, 83, 84, 65, 84, 73, 79, 78, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 7, 161, 32, 0, 0, 0, 0];
            builtSettings.SetCmdOkEscResetSource(2);
            Assert.IsTrue(settingsFromKdu.SequenceEqual(builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            builtSettings = new KducerControllerGeneralSettings(new byte[88]);
            setDefaultParametersOfKduGeneralSettings(builtSettings);
            for (int i = 0; i < settingsFromKdu.Length; i++)
                if (settingsFromKdu[i] != builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()[i])
                    Console.WriteLine($"built[{i}]={builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()[i]}, fromKdu[{i}]={settingsFromKdu[i]}");
            Assert.IsTrue(settingsFromKdu.SequenceEqual(builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()));

            builtSettings = new KducerControllerGeneralSettings(new byte[88]);
            setMiscParametersOfKduGeneralSettings(builtSettings);

            settingsFromKdu = [0, 1, 0, 0, 0, 100, 0, 1, 0, 1, 0, 2, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 75, 68, 85, 67, 69, 82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 15, 66, 64, 0, 53, 0, 3];
            for (int i = 0; i < settingsFromKdu.Length; i++)
                if (settingsFromKdu[i] != builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()[i])
                    Console.WriteLine($"built[{i}]={builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()[i]}, fromKdu[{i}]={settingsFromKdu[i]}");
            Assert.IsTrue(settingsFromKdu.SequenceEqual(builtSettings.getGeneralSettingsModbusHoldingRegistersAsByteArray()));
        }

        internal static void setDefaultParametersOfKduGeneralSettings(KducerControllerGeneralSettings builtGenSett)
        {
            builtGenSett.SetLanguage(0);
            builtGenSett.SetPasswordOnOff(false);
            builtGenSett.SetPassword(0);
            builtGenSett.SetCmdOkEscResetSource(2);
            builtGenSett.SetRemoteProgramSource(0);
            builtGenSett.SetRemoteSequenceSource(0);
            builtGenSett.SetResetMode(0);
            builtGenSett.SetBarcodeMode(0);
            builtGenSett.SetSwbxAndCbs880Mode(0);
            builtGenSett.SetCurrentSequenceNumber(1);
            builtGenSett.SetCurrentProgramNumber(1);
            builtGenSett.SetTorqueUnits(0);
            builtGenSett.SetSequenceModeOnOff(false);
            builtGenSett.SetFastDock05ModeOnOff(false);
            builtGenSett.SetBuzzerSoundsOnOff(true);
            builtGenSett.SetResultsFormat(1);
            builtGenSett.SetStationName("TORQUE STATION");
            builtGenSett.SetCalibrationReminderMode(2);
            builtGenSett.SetCalibrationReminderInterval(500000);
            builtGenSett.SetLockIfCn5NotConnectedOnOff(false);
            builtGenSett.SetLockIfUsbNotConnectedOnOff(false);
            builtGenSett.SetInvertLogicCn3InStopOnOff(false);
            builtGenSett.SetInvertLogicCn3InPieceOnOff(false);
            builtGenSett.SetSkipScrewButtonOnOff(false);
            builtGenSett.SetShowReverseTorqueAndAngleOnOff(false);
            builtGenSett.SetCn3BitxPrSeqInputSelectionMode(0);
        }

        internal static void setMiscParametersOfKduGeneralSettings(KducerControllerGeneralSettings builtGenSett)
        {
            builtGenSett.SetLanguage(1);
            builtGenSett.SetPasswordOnOff(true);
            builtGenSett.SetPassword(100);
            builtGenSett.SetCmdOkEscResetSource(1);
            builtGenSett.SetRemoteProgramSource(2);
            builtGenSett.SetRemoteSequenceSource(0);
            builtGenSett.SetResetMode(1);
            builtGenSett.SetBarcodeMode(1);
            builtGenSett.SetSwbxAndCbs880Mode(1);
            builtGenSett.SetCurrentSequenceNumber(1);
            builtGenSett.SetCurrentProgramNumber(1);
            builtGenSett.SetTorqueUnits(1);
            builtGenSett.SetSequenceModeOnOff(false);
            builtGenSett.SetFastDock05ModeOnOff(true);
            builtGenSett.SetBuzzerSoundsOnOff(true);
            builtGenSett.SetResultsFormat(1);
            builtGenSett.SetStationName("KDUCER");
            builtGenSett.SetCalibrationReminderMode(2);
            builtGenSett.SetCalibrationReminderInterval(1000000);
            builtGenSett.SetLockIfCn5NotConnectedOnOff(true);
            builtGenSett.SetLockIfUsbNotConnectedOnOff(false);
            builtGenSett.SetInvertLogicCn3InStopOnOff(true);
            builtGenSett.SetInvertLogicCn3InPieceOnOff(false);
            builtGenSett.SetSkipScrewButtonOnOff(true);
            builtGenSett.SetShowReverseTorqueAndAngleOnOff(true);
            builtGenSett.SetCn3BitxPrSeqInputSelectionMode(3);
        }
    }

    [TestClass]
    public class KducerTorqueAngleTimeGraphTests
    {
        [TestMethod]
        public void TestKducerTorqueAngleTimeGraph()
        {
            KducerTorqueAngleTimeGraph ta = new KducerTorqueAngleTimeGraph(new byte[142], new byte[142]);
            // should not throw exceptions:
            string csv = ta.getAngleSeriesAsCsv();
            csv = ta.getTorqueSeriesAsCsv();
            csv = ta.getAngleSeriesAsCsvWith70columns();
            csv = ta.getTorqueSeriesAsCsvWith70columns();
            ushort[] arr = ta.getAngleSeries();
            arr = ta.getTorqueSeries();

            string torqueRegBytes = "0, 27, 0, 25, 0, 52, 0, 41, 0, 55, 0, 57, 0, 76, 0, 98, 0, 83, 0, 105, 0, 120, 0, 144, 0, 143, 0, 140, 0, 147, 0, 147, 0, 144, 0, 140, 0, 121, 0, 131, 0, 86, 0, 67, 0, 79, 0, 164, 0, 201, 0, 224, 0, 224, 0, 185, 0, 119, 0, 53, 0, 66, 0, 147, 0, 135, 0, 112, 0, 104, 0, 36, 0, 32, 0, 47, 0, 92, 0, 144, 0, 144, 0, 143, 0, 126, 0, 116, 0, 96, 0, 115, 0, 114, 0, 118, 0, 111, 0, 117, 0, 87, 0, 77, 0, 124, 0, 173, 0, 224, 0, 249, 0, 218, 0, 189, 0, 126, 0, 157, 0, 217, 0, 232, 0, 193, 0, 141, 0, 92, 0, 143, 0, 238, 1, 136, 1, 244, 0, 0, 0, 0";
            byte[] torqueData = torqueRegBytes.Split(", ").Select(byte.Parse).ToArray();

            string angleRegBytes = "0, 27, 0, 2, 0, 12, 0, 29, 0, 55, 0, 88, 0, 130, 0, 180, 0, 238, 1, 37, 1, 93, 1, 149, 1, 206, 2, 5, 2, 60, 2, 119, 2, 176, 2, 230, 3, 33, 3, 89, 3, 144, 3, 201, 4, 1, 4, 56, 4, 110, 4, 163, 4, 219, 5, 19, 5, 75, 5, 131, 5, 186, 5, 242, 6, 42, 6, 99, 6, 156, 6, 213, 7, 13, 7, 68, 7, 124, 7, 179, 7, 235, 8, 35, 8, 91, 8, 147, 8, 205, 9, 7, 9, 63, 9, 120, 9, 177, 9, 232, 10, 33, 10, 89, 10, 143, 10, 198, 10, 253, 11, 53, 11, 109, 11, 164, 11, 220, 12, 19, 12, 74, 12, 130, 12, 186, 12, 242, 13, 43, 13, 98, 13, 153, 13, 208, 14, 36, 0, 0, 0, 0";
            byte[] angleData = angleRegBytes.Split(", ").Select(byte.Parse).ToArray();

            ta = new KducerTorqueAngleTimeGraph(torqueData, angleData);
            // should not throw exceptions:
            csv = ta.getAngleSeriesAsCsv();
            csv = ta.getTorqueSeriesAsCsv();
            csv = ta.getAngleSeriesAsCsvWith70columns();
            csv = ta.getTorqueSeriesAsCsvWith70columns();
            arr = ta.getAngleSeries();
            arr = ta.getTorqueSeries();
            Assert.AreEqual((ushort)27, ta.getTimeIntervalBetweenConsecutivePoints());

            arr = [25, 52, 41, 55, 57, 76, 98, 83, 105, 120, 144, 143, 140, 147, 147, 144, 140, 121, 131, 86, 67, 79, 164, 201, 224, 224, 185, 119, 53, 66, 147, 135, 112, 104, 36, 32, 47, 92, 144, 144, 143, 126, 116, 96, 115, 114, 118, 111, 117, 87, 77, 124, 173, 224, 249, 218, 189, 126, 157, 217, 232, 193, 141, 92, 143, 238, 392, 500];
            ushort[] taArr = ta.getTorqueSeries();
            Assert.IsTrue(arr.SequenceEqual(taArr));
            arr = [2, 12, 29, 55, 88, 130, 180, 238, 293, 349, 405, 462, 517, 572, 631, 688, 742, 801, 857, 912, 969, 1025, 1080, 1134, 1187, 1243, 1299, 1355, 1411, 1466, 1522, 1578, 1635, 1692, 1749, 1805, 1860, 1916, 1971, 2027, 2083, 2139, 2195, 2253, 2311, 2367, 2424, 2481, 2536, 2593, 2649, 2703, 2758, 2813, 2869, 2925, 2980, 3036, 3091, 3146, 3202, 3258, 3314, 3371, 3426, 3481, 3536, 3620];
            taArr = ta.getAngleSeries();
            Assert.IsTrue(arr.SequenceEqual(taArr));

            csv = "25,52,41,55,57,76,98,83,105,120,144,143,140,147,147,144,140,121,131,86,67,79,164,201,224,224,185,119,53,66,147,135,112,104,36,32,47,92,144,144,143,126,116,96,115,114,118,111,117,87,77,124,173,224,249,218,189,126,157,217,232,193,141,92,143,238,392,500";
            Assert.AreEqual(csv, ta.getTorqueSeriesAsCsv());
            csv += ",,";
            Assert.AreEqual(csv, ta.getTorqueSeriesAsCsvWith70columns());

            csv = "2,12,29,55,88,130,180,238,293,349,405,462,517,572,631,688,742,801,857,912,969,1025,1080,1134,1187,1243,1299,1355,1411,1466,1522,1578,1635,1692,1749,1805,1860,1916,1971,2027,2083,2139,2195,2253,2311,2367,2424,2481,2536,2593,2649,2703,2758,2813,2869,2925,2980,3036,3091,3146,3202,3258,3314,3371,3426,3481,3536,3620";
            Assert.AreEqual(csv, ta.getAngleSeriesAsCsv());
            csv += ",,";
            Assert.AreEqual(csv, ta.getAngleSeriesAsCsvWith70columns());

            torqueRegBytes = "0, 27, 0, 25, 0, 52, 0, 41, 0, 55, 0, 57, 0, 76, 0, 98, 0, 83, 0, 105, 0, 120, 0, 144, 0, 143, 0, 140, 0, 147, 0, 147, 0, 144, 0, 140, 0, 121, 0, 131, 0, 86, 0, 67, 0, 79, 0, 164, 0, 201, 0, 224, 0, 224, 0, 185, 0, 119, 0, 53, 0, 66, 0, 147, 0, 135, 0, 112, 0, 104, 0, 36, 0, 32, 0, 47, 0, 92, 0, 144, 0, 144, 0, 143, 0, 126, 0, 116, 0, 96, 0, 115, 0, 114, 0, 118, 0, 111, 0, 117, 0, 87, 0, 77, 0, 124, 0, 173, 0, 224, 0, 249, 0, 218, 0, 189, 0, 126, 0, 157, 0, 217, 0, 232, 0, 193, 0, 141, 0, 92, 0, 143, 0, 238, 1, 136, 1, 244, 1, 245, 1, 246";
            torqueData = torqueRegBytes.Split(", ").Select(byte.Parse).ToArray();

            angleRegBytes = "0, 27, 0, 2, 0, 12, 0, 29, 0, 55, 0, 88, 0, 130, 0, 180, 0, 238, 1, 37, 1, 93, 1, 149, 1, 206, 2, 5, 2, 60, 2, 119, 2, 176, 2, 230, 3, 33, 3, 89, 3, 144, 3, 201, 4, 1, 4, 56, 4, 110, 4, 163, 4, 219, 5, 19, 5, 75, 5, 131, 5, 186, 5, 242, 6, 42, 6, 99, 6, 156, 6, 213, 7, 13, 7, 68, 7, 124, 7, 179, 7, 235, 8, 35, 8, 91, 8, 147, 8, 205, 9, 7, 9, 63, 9, 120, 9, 177, 9, 232, 10, 33, 10, 89, 10, 143, 10, 198, 10, 253, 11, 53, 11, 109, 11, 164, 11, 220, 12, 19, 12, 74, 12, 130, 12, 186, 12, 242, 13, 43, 13, 98, 13, 153, 13, 208, 14, 36, 14, 37, 14, 38";
            angleData = angleRegBytes.Split(", ").Select(byte.Parse).ToArray();

            ta = new KducerTorqueAngleTimeGraph(torqueData, angleData);

            arr = [25, 52, 41, 55, 57, 76, 98, 83, 105, 120, 144, 143, 140, 147, 147, 144, 140, 121, 131, 86, 67, 79, 164, 201, 224, 224, 185, 119, 53, 66, 147, 135, 112, 104, 36, 32, 47, 92, 144, 144, 143, 126, 116, 96, 115, 114, 118, 111, 117, 87, 77, 124, 173, 224, 249, 218, 189, 126, 157, 217, 232, 193, 141, 92, 143, 238, 392, 500, 501, 502];
            taArr = ta.getTorqueSeries();
            Assert.IsTrue(arr.SequenceEqual(taArr));
            arr = [2, 12, 29, 55, 88, 130, 180, 238, 293, 349, 405, 462, 517, 572, 631, 688, 742, 801, 857, 912, 969, 1025, 1080, 1134, 1187, 1243, 1299, 1355, 1411, 1466, 1522, 1578, 1635, 1692, 1749, 1805, 1860, 1916, 1971, 2027, 2083, 2139, 2195, 2253, 2311, 2367, 2424, 2481, 2536, 2593, 2649, 2703, 2758, 2813, 2869, 2925, 2980, 3036, 3091, 3146, 3202, 3258, 3314, 3371, 3426, 3481, 3536, 3620, 3621, 3622];
            taArr = ta.getAngleSeries();
            Assert.IsTrue(arr.SequenceEqual(taArr));

            csv = "25,52,41,55,57,76,98,83,105,120,144,143,140,147,147,144,140,121,131,86,67,79,164,201,224,224,185,119,53,66,147,135,112,104,36,32,47,92,144,144,143,126,116,96,115,114,118,111,117,87,77,124,173,224,249,218,189,126,157,217,232,193,141,92,143,238,392,500,501,502";
            Assert.AreEqual(csv, ta.getTorqueSeriesAsCsv());
            Assert.AreEqual(csv, ta.getTorqueSeriesAsCsvWith70columns());

            csv = "2,12,29,55,88,130,180,238,293,349,405,462,517,572,631,688,742,801,857,912,969,1025,1080,1134,1187,1243,1299,1355,1411,1466,1522,1578,1635,1692,1749,1805,1860,1916,1971,2027,2083,2139,2195,2253,2311,2367,2424,2481,2536,2593,2649,2703,2758,2813,2869,2925,2980,3036,3091,3146,3202,3258,3314,3371,3426,3481,3536,3620,3621,3622";
            Assert.AreEqual(csv, ta.getAngleSeriesAsCsv());
            Assert.AreEqual(csv, ta.getAngleSeriesAsCsvWith70columns());

            torqueRegBytes = "0, 27, 0, 25, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0";
            torqueData = torqueRegBytes.Split(", ").Select(byte.Parse).ToArray();

            angleRegBytes = "0, 27, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0";
            angleData = angleRegBytes.Split(", ").Select(byte.Parse).ToArray();

            ta = new KducerTorqueAngleTimeGraph(torqueData, angleData);

            arr = [25];
            taArr = ta.getTorqueSeries();
            Assert.IsTrue(arr.SequenceEqual(taArr));
            arr = [2];
            taArr = ta.getAngleSeries();
            Assert.IsTrue(arr.SequenceEqual(taArr));

            csv = "25";
            Assert.AreEqual(csv, ta.getTorqueSeriesAsCsv());
            string emptyCols = new string(',', 69);
            Assert.AreEqual(csv + emptyCols, ta.getTorqueSeriesAsCsvWith70columns());

            csv = "2";
            Assert.AreEqual(csv, ta.getAngleSeriesAsCsv());
            Assert.AreEqual(csv + emptyCols, ta.getAngleSeriesAsCsvWith70columns());
        }
    }

    [TestClass]
    public class KducerKduDataFileReaderTests
    {
        [TestMethod]
        public async Task TestReadFile()
        {
            await ReadFileTests("../../../v38 file.kdu", 38);
            await ReadFileTests("../../../v37 file.kdu", 37);
        }
        private async Task ReadFileTests(string path, ushort kduVersion)
        {
            // just testing for exceptions
            Tuple<KducerControllerGeneralSettings, Dictionary<ushort, KducerTighteningProgram>, Dictionary<ushort, KducerSequenceOfTighteningPrograms>> data = await KducerKduDataFileReader.ReadKduDataFile(path);

            // settings
            byte[] settingsbytes = [0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 84, 79, 82, 81, 85, 69, 32, 83, 84, 65, 84, 73, 79, 78, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 7, 161, 32, 0, 0, 0, 0];
            KducerControllerGeneralSettings settings = new KducerControllerGeneralSettings(settingsbytes);
            byte[] settingsbytesRead;
            if (kduVersion == 37)
                settingsbytesRead = data.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior();
            else
                settingsbytesRead = data.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38();
            for (int i = 0; i < settingsbytesRead.Length; i++)
            {
                Assert.AreEqual(settingsbytes[i], settings.getGeneralSettingsModbusHoldingRegistersAsByteArray()[i]);
                if (settingsbytes[i] != settingsbytesRead[i])
                    Console.WriteLine($"built[{i}]={settingsbytes[i]}, fromFile[{i}]={settingsbytesRead[i]}");
            }
            if (kduVersion == 37)
                settingsbytes = settings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior();
            else
                settingsbytes = settings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38();
            Assert.IsTrue(settingsbytesRead.SequenceEqual(settingsbytes));

            // programs
            byte[] programBytes = [0, 0, 0, 100, 0, 0, 0, 110, 0, 0, 0, 90, 0, 0, 0, 0, 0, 0, 117, 48, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 44, 0, 0, 1, 64, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 64, 0, 0, 0, 200, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            KducerTighteningProgram prog = new KducerTighteningProgram(programBytes);
            Assert.IsTrue(programBytes.SequenceEqual(prog.getProgramModbusHoldingRegistersAsByteArray()));
            if (kduVersion <= 37)
            {
                prog.SetTorqueTarget(50);
                prog.SetTorqueMax(150);
                prog.SetTorqueMin(0);
                prog.SetDownshiftInitialSpeed(600);
                prog.SetLeverErrorOnOff(false);
                prog.SetReverseSpeed(300);
                prog.SetMaxReverseTorque(150);
            }
            for (ushort i = 1; i <= 8; i++)
            {
                if (kduVersion >= 38) prog.SetSocket(i);
                else prog.SetSocket(0);
                byte[] built = prog.getProgramModbusHoldingRegistersAsByteArray();
                byte[] read = data.Item2[i].getProgramModbusHoldingRegistersAsByteArray();
                Assert.IsTrue(built.Length == read.Length);
                for (int j = 0; j < built.Length; j++)
                    if (built[j] != read[j])
                        Console.WriteLine($"built[{j}]={built[j]}, fromFile[{j}]={read[j]}");
                Assert.IsTrue(built.SequenceEqual(read));
            }
            prog.SetSocket(0);
            ushort maxprog = 200;
            if (kduVersion <= 37)
                maxprog = 64;
            for (ushort i = 9; i <= maxprog; i++)
                Assert.IsTrue(prog.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(data.Item2[i].getProgramModbusHoldingRegistersAsByteArray()));

            // sequences
            KducerSequenceOfTighteningPrograms seq = new([1], [1]);
            ushort maxseq = 24;
            if (kduVersion <= 37)
                maxseq = 8;
            for (ushort i = 1; i <= maxseq; i++)
            {
                byte[] built, read;
                if (kduVersion >= 38)
                {
                    built = seq.getSequenceModbusHoldingRegistersAsByteArray();
                    read = data.Item3[i].getSequenceModbusHoldingRegistersAsByteArray();
                }
                else
                {
                    built = seq.getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior();
                    read = data.Item3[i].getSequenceModbusHoldingRegistersAsByteArray_KDUv37andPrior();
                }
                Assert.IsTrue(built.SequenceEqual(read));
            }
        }
    }
}