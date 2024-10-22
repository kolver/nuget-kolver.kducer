// Copyright (c) 2024 Kolver Srl www.kolver.com MIT license

using Kolver;
using Microsoft.Extensions.Logging.Abstractions;
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
    public class HandsFree
    {
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
            public async Task TestReadBits()
            {
                using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
                await kdu.ConnectAsync();
                Assert.IsTrue(kdu.Connected());
                for (ushort i = 0; i < 48; i++)
                    await kdu.ReadCoilsAsync(i, (ushort)(48 - i));
                for (ushort i = 0; i < 32; i++)
                    await kdu.ReadDiscreteInputsAsync(i, (ushort)(32 - i));
            }
        }

        [TestClass]
        public class KducerTests
        {
            private async Task CheckSetProgramMode(Kducer kdu)
            {
                // check/set program mode
                KducerControllerGeneralSettings genSetts = await kdu.GetGeneralSettingsDataAsync();
                if (genSetts.GetSequenceModeOnOff() == true)
                {
                    genSetts.SetSequenceModeOnOff(false);
                    if (await kdu.GetKduMainboardVersionAsync() <= 37)
                        genSetts.SetRemoteProgramSource(2);
                    await kdu.SendGeneralSettingsDataAsync(genSetts);
                }
                else if (await kdu.GetKduMainboardVersionAsync() <= 37)
                {
                    genSetts.SetRemoteProgramSource(2);
                    await kdu.SendGeneralSettingsDataAsync(genSetts);
                }
            }

            [TestMethod]
            [Timeout(15000)]
            public async Task TestSendProgramData()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

                await CheckSetProgramMode(kdu);

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

                if (await kdu.GetKduMainboardVersionAsync() < 40)
                    Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().AsSpan(0, 180).SequenceEqual(prReRead.getProgramModbusHoldingRegistersAsByteArray().AsSpan(0, 180)));
                else
                    Assert.IsTrue(prRead.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prReRead.getProgramModbusHoldingRegistersAsByteArray()));

                if (await kdu.GetKduMainboardVersionAsync() >= 40)
                {
                    KducerTighteningProgram pr = await kdu.GetTighteningProgramDataAsync(prNumber);
                    pr.SetKtlsSensor1Tolerance(10);
                    pr.SetKtlsSensor2Tolerance(20);
                    pr.SetKtlsSensor3Tolerance(30);
                    pr.SetSubstituteTorqueUnits(5);
                    await kdu.SendNewProgramDataAsync(prNumber, pr);
                    prReRead = await kdu.GetTighteningProgramDataAsync(prNumber);
                    Assert.IsTrue(pr.getProgramModbusHoldingRegistersAsByteArray().SequenceEqual(prReRead.getProgramModbusHoldingRegistersAsByteArray()));
                }
            }

            [TestMethod]
            [Timeout(30000)]
            public async Task TestSendMultipleProgramData()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

                await CheckSetProgramMode(kdu);

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
            [Timeout(15000)]
            public async Task TestReadCoils()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

                await kdu.IsConnectedWithTimeoutAsync();

                await CheckSetProgramMode(kdu);
                await kdu.SelectProgramNumberAsync(1);

                KducerTighteningProgram pr = new KducerTighteningProgram();
                pr.SetTorqueAngleMode(1);
                pr.SetAngleTarget(1000);
                pr.SetLeverErrorOnOff(false);
                pr.SetNumberOfScrews(1);

                await kdu.SendNewProgramDataAsync(1, pr);
                await kdu.DisableScrewdriver();
                await kdu.EnableScrewdriver();
                bool[] bits = await kdu.GetCn3OutputBitsAsync();
                Assert.IsFalse(bits[0]);
                Assert.IsFalse(bits[1]);
                Assert.IsFalse(bits[6]);
                Assert.IsTrue(bits[7]);
                await kdu.DisableScrewdriver();
                Assert.IsTrue((await kdu.GetCn3OutputBitsAsync())[6]);
                await kdu.EnableScrewdriver();
                Assert.IsFalse((await kdu.GetCn3OutputBitsAsync())[6]);
                await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
                bits = await kdu.GetCn3OutputBitsAsync();
                Assert.IsTrue(bits.SequenceEqual([false, false, true, false, true, false, false, true]));
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
                await kdu.SelectProgramNumberAsync(1);
                await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
                if (await kdu.GetKduMainboardVersionAsync() >= 38)
                    await kdu.SetHighResGraphModeAsync(true);
                await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
            }

            [TestMethod]
            [Timeout(10000)]
            public async Task TestNewKduV40InputRegistersResult()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

                if (await kdu.GetKduMainboardVersionAsync() < 40)
                    return;
                
                await kdu.SetHighResGraphModeAsync(true);

                KducerTighteningProgram pr = new KducerTighteningProgram();
                pr.SetTorqueAngleMode(1);
                pr.SetAngleTarget(1000);
                pr.SetAngleMax(1100);
                pr.SetAngleMin(900);
                pr.SetTorqueMin(0);
                pr.SetTorqueMax(120);
                KducerTighteningProgram pr2 = new KducerTighteningProgram();
                pr2.SetTorqueAngleMode(1);
                pr2.SetAngleTarget(5);
                pr2.SetAngleMax(1150);
                pr2.SetAngleMin(1);
                pr2.SetTorqueMin(300);
                pr2.SetTorqueMax(350);

                await kdu.SendNewProgramDataAsync(1, pr);
                await kdu.SendNewProgramDataAsync(2, pr2);
                await kdu.SelectProgramNumberAsync(1);
                KducerTighteningResult res = await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
                Assert.IsTrue(res.GetAngleLimitMin() == 900);
                Assert.IsTrue(res.GetAngleLimitMax() == 1100);
                Assert.IsTrue(res.GetTorqueLimitMin() == 0);
                Assert.IsTrue(res.GetTorqueLimitMax() == 120);

                await kdu.SelectProgramNumberAsync(2);
                res = await kdu.RunScrewdriverUntilResultAsync(CancellationToken.None);
                Assert.IsTrue(res.GetAngleLimitMin() == 1);
                Assert.IsTrue(res.GetAngleLimitMax() == 1150);
                Assert.IsTrue(res.GetTorqueLimitMin() == 300);
                Assert.IsTrue(res.GetTorqueLimitMax() == 350);
            }

            [TestMethod]
            [Timeout(5000)]
            public async Task TestGetSetDateTimeAsync()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
                ushort version = await kdu.GetKduMainboardVersionAsync();
                if (version >= 39)
                {
                    DateTime dt_kdu = await kdu.GetDateTimeAsync(); // must not throw
                    DateTime dt = new DateTime(2024, 10, 21, 15, 15, 15);
                    await kdu.SetDateTimeAsync(dt);
                    dt_kdu = await kdu.GetDateTimeAsync();
                    Assert.IsTrue((dt - dt_kdu).TotalSeconds < 3);
                    await kdu.SetDateTimeToNowAsync();
                    dt_kdu = await kdu.GetDateTimeAsync();
                    Assert.IsTrue((DateTime.Now - dt_kdu).TotalSeconds < 3);
                }
                else
                {
                    await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => kdu.GetDateTimeAsync());
                }
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

                await CheckSetProgramMode(kdu);

                ushort pr_set = 60;
                await kdu.SelectProgramNumberAsync(pr_set);
                ushort pr_get = await kdu.GetProgramNumberAsync();
                Assert.AreEqual(pr_set, pr_get);
            }

            [TestMethod]
            [Timeout(15000)]
            public async Task TestSelectSequenceAsync()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

                // check/set sequence mode
                KducerControllerGeneralSettings genSetts = await kdu.GetGeneralSettingsDataAsync();
                if (genSetts.GetSequenceModeOnOff() == false)
                {
                    genSetts.SetSequenceModeOnOff(true);
                    if (await kdu.GetKduMainboardVersionAsync() <= 37)
                        genSetts.SetRemoteSequenceSource(2);
                    await kdu.SendGeneralSettingsDataAsync(genSetts);
                }
                else if (await kdu.GetKduMainboardVersionAsync() <= 37)
                {
                    genSetts.SetRemoteSequenceSource(2);
                    await kdu.SendGeneralSettingsDataAsync(genSetts);
                }

                ushort seq_set = 4;
                await kdu.SelectSequenceNumberAsync(seq_set);
                ushort seq_get = await kdu.GetSequenceNumberAsync();
                Assert.AreEqual(seq_set, seq_get);
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
            [Timeout(60000)]
            public async Task TestSendBarcodeWithResults()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
                Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
                Assert.IsTrue(kdu.IsConnected());

                await CheckSetProgramMode(kdu);

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

                await kdu.SelectSequenceNumberAsync(1);
                await kdu.SendNewSequenceDataAsync(1, seq);
                KducerSequenceOfTighteningPrograms seqRead = await kdu.GetActiveSequenceDataAsync();
                byte[] sqRdBytes = seqRead.getSequenceModbusHoldingRegistersAsByteArray();
                byte[] seqWrBytes = seq.getSequenceModbusHoldingRegistersAsByteArray();
                Assert.IsTrue(sqRdBytes.SequenceEqual(seqWrBytes));
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
            [Timeout(20000)]
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

                if (await kdu.GetKduMainboardVersionAsync() >= 40)
                {
                    genSetts = await kdu.GetGeneralSettingsDataAsync();
                    genSetts.SetKtlsArm1Model(2);
                    genSetts.SetKtlsArm2Model(2);
                    await kdu.SendGeneralSettingsDataAsync(genSetts);
                    genSettsRead = await kdu.GetGeneralSettingsDataAsync();
                    Assert.IsTrue(genSetts.getGeneralSettingsModbusHoldingRegistersAsByteArray().SequenceEqual(genSettsRead.getGeneralSettingsModbusHoldingRegistersAsByteArray()));
                }
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
                {
                    byte[] sentPrArray = sentData.Item2[i].getProgramModbusHoldingRegistersAsByteArray();
                    byte[] reReadPrArray = reReadKduData.Item2[i].getProgramModbusHoldingRegistersAsByteArray();
                    if (sentPrArray.SequenceEqual(reReadPrArray) == false)
                        for (int j = 0; j < sentPrArray.Length; i++)
                            if (sentPrArray[j] != reReadPrArray[j])
                                Console.WriteLine($"sentPrArray[{j}]: {sentPrArray[j]}, reReadPrArray[{j}]: {reReadPrArray[j]}");
                    Assert.IsTrue(sentPrArray.SequenceEqual(reReadPrArray));
                }

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

                prFromKdu = [0, 0, 0, 50, 0, 0, 7, 208, 0, 0, 3, 232, 0, 0, 60, 140, 0, 0, 62, 128, 0, 0, 58, 152, 0, 0, 0, 100, 0, 0, 1, 99, 0, 0, 3, 82, 0, 0, 19, 136, 0, 1, 0, 0, 111, 27, 0, 0, 0, 25, 0, 0, 0, 0, 0, 0, 0, 60, 0, 0, 0, 150, 0, 1, 0, 0, 0, 1, 0, 0, 14, 16, 0, 0, 2, 88, 0, 0, 3, 231, 0, 1, 0, 0, 0, 1, 0, 0, 5, 220, 0, 0, 0, 10, 0, 0, 0, 0, 0, 5, 0, 0, 0, 1, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 8, 0, 0, 0, 99, 116, 101, 115, 116, 32, 112, 114, 111, 103, 114, 97, 109, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 232, 0, 1, 1, 44, 1, 244, 0, 10, 0, 30, 0, 10, 0, 20, 0, 250, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
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
                builtPr.SetMaxPowerPhaseTime(1);
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
                builtPr.SetPreTighteningReverseTime(1);
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
                builtPr.SetAfterTighteningReverseAngle(1);
                builtPr.SetAfterTighteningReverseTime(5);
                builtPr.SetAfterTighteningReverseDelay(8);
                builtPr.SetAfterTighteningReverseMode(0);
                builtPr.SetTorqueCompensationValue(1000);
                builtPr.SetKtlsSensor1Tolerance(10);
                builtPr.SetKtlsSensor2Tolerance(20);
                builtPr.SetKtlsSensor3Tolerance(250);
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

                byte[] settingsFromKdu = [0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 84, 79, 82, 81, 85, 69, 32, 83, 84, 65, 84, 73, 79, 78, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 7, 161, 32, 0, 0, 0, 0, 0, 0, 0, 0];
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

                settingsFromKdu = [0, 1, 0, 0, 0, 100, 0, 1, 0, 1, 0, 2, 0, 0, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 1, 0, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 75, 68, 85, 67, 69, 82, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 15, 66, 64, 0, 53, 0, 3, 0, 0, 0, 0];
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

            [TestMethod]
            public void TestKducerHighResGraphs()
            {
                byte[] graph_buf_whole =
                {
                    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x01,0x00,0x01,0x00,0x01,0x00,0x01,0x00,0x02,0x00,0x02,0x00,0x02,0x00,0x03,0x00,0x03,0x00,0x03,0x00,0x03,0x00,0x03,0x00,0x04,0x00,0x04,0x00,0x05,0x00,0x05,0x00,0x06,0x00,0x06,0x00,0x06,0x00,0x07,0x00,0x07,0x00,0x08,0x00,0x09,0x00,0x09,0x00,0x09,0x00,0x0A,0x00,0x0A,0x00,0x0B,0x00,0x0B,0x00,0x0C,0x00,0x0C,0x00,0x0D,0x00,0x0D,0x00,0x0D,0x00,0x0E,0x00,0x0F,0x00,0x0F,0x00,0x10,0x00,0x10,0x00,0x11,0x00,0x11,0x00,0x12,0x00,0x13,0x00,0x13,0x00,0x14,0x00,0x15,0x00,0x15,0x00,0x16,0x00,0x16,0x00,0x17,0x00,0x18,0x00,0x19,0x00,0x19,0x00,0x1A,0x00,0x1B,0x00,0x1C,0x00,0x1D,0x00,0x1E,0x00,0x1E,0x00,0x1F,0x00,0x20,0x00,0x21,0x00,0x22,0x00,0x23,0x00,0x24,0x00,0x25,0x00,0x26,0x00,0x27,0x00,0x27,0x00,0x29,0x00,0x2A,0x00,0x2A,0x00,0x2B,0x00,0x2C,0x00,0x2D,0x00,0x2E,0x00,0x2F,0x00,0x30,0x00,0x31,0x00,0x33,0x00,0x34,0x00,0x35,0x00,0x36,0x00,0x37,0x00,0x38,0x00,0x39,0x00,0x3A,0x00,0x3C,0x00,0x3D,0x00,0x3E,0x00,0x3F,0x00,0x40,0x00,0x41,0x00,0x43,0x00,0x44,0x00,0x45,0x00,0x46,0x00,0x47,0x00,0x48,0x00,0x4A,0x00,0x4B,0x00,0x4C,0x00,0x4D,0x00,0x4F,0x00,0x50,0x00,0x51,0x00,0x53,0x00,0x54,0x00,0x55,0x00,0x57,0x00,0x58,0x00,0x59,0x00,0x5B,0x00,0x5C,0x00,0x5D,0x00,0x5F,0x00,0x60,0x00,0x62,0x00,0x63,0x00,0x65,0x00,0x66,0x00,0x68,0x00,0x69,0x00,0x6B,0x00,0x6D,0x00,0x6E,0x00,0x70,0x00,0x71,0x00,0x73,0x00,0x75,0x00,0x76,0x00,0x78,0x00,0x7A,0x00,0x7C,0x00,0x7D,0x00,0x7F,0x00,0x81,0x00,0x82,0x00,0x84,0x00,0x86,0x00,0x88,0x00,0x8A,0x00,0x8B,0x00,0x8D,0x00,0x8F,0x00,0x91,0x00,0x93,0x00,0x95,0x00,0x96,0x00,0x98,0x00,0x9A,0x00,0x9C,0x00,0x9E,0x00,0xA0,0x00,0xA2,0x00,0xA3,0x00,0xA5,0x00,0xA7,0x00,0xA9,0x00,0xAB,0x00,0xAD,0x00,0xAF,0x00,0xB1,0x00,0xB3,0x00,0xB5,0x00,0xB7,0x00,0xB9,0x00,0xBB,0x00,0xBD,0x00,0xBF,0x00,0xC1,0x00,0xC4,0x00,0xC6,0x00,0xC8,0x00,0xCA,0x00,0xCC,0x00,0xCE,0x00,0xD0,0x00,0xD2,0x00,0xD5,0x00,0xD7,0x00,0xD9,0x00,0xDB,0x00,0xDD,0x00,0xDF,0x00,0xE1,0x00,0xE4,0x00,0xE6,0x00,0xE8,0x00,0xEA,0x00,0xEC,0x00,0xEE,0x00,0xF1,0x00,0xF3,0x00,0xF5,0x00,0xF7,0x00,0xF9,0x00,0xFB,0x00,0xFD,0x00,0x00,0x01,0x02,0x01,0x04,0x01,0x06,0x01,0x08,0x01,0x0A,0x01,0x0C,0x01,0x0F,0x01,0x11,0x01,0x13,0x01,0x15,0x01,0x17,0x01,0x19,0x01,0x1B,0x01,0x1D,0x01,0x20,0x01,0x22,0x01,0x24,0x01,0x26,0x01,0x28,0x01,0x2A,0x01,0x2C,0x01,0x2F,0x01,0x31,0x01,0x33,0x01,0x35,0x01,0x37,0x01,0x39,0x01,0x3B,0x01,0x3E,0x01,0x40,0x01,0x42,0x01,0x44,0x01,0x46,0x01,0x48,0x01,0x4A,0x01,0x4D,0x01,0x4E,0x01,0x51,0x01,0x53,0x01,0x55,0x01,0x57,0x01,0x59,0x01,0x5B,0x01,0x5E,0x01,0x60,0x01,0x62,0x01,0x64,0x01,0x66,0x01,0x68,0x01,0x6A,0x01,0x6C,0x01,0x6F,0x01,0x71,0x01,0x73,0x01,0x75,0x01,0x77,0x01,0x79,0x01,0x7B,0x01,0x7E,0x01,0x80,0x01,0x82,0x01,0x84,0x01,0x86,0x01,0x88,0x01,0x8A,0x01,0x8C,0x01,0x8E,0x01,0x91,0x01,0x93,0x01,0x95,0x01,0x97,0x01,0x99,0x01,0x9B,0x01,0x9D,0x01,0x9F,0x01,0xA2,0x01,0xA4,0x01,0xA6,0x01,0xA8,0x01,0xAA,0x01,0xAC,0x01,0xAE,0x01,0xB1,0x01,0xB3,0x01,0xB5,0x01,0xB7,0x01,0xB9,0x01,0xBB,0x01,0xBD,0x01,0xBF,0x01,0xC2,0x01,0xC3,0x01,0xC6,0x01,0xC8,0x01,0xCA,0x01,0xCC,0x01,0xCE,0x01,0xD0,0x01,0xD2,0x01,0xD5,0x01,0xD7,0x01,0xD9,0x01,0xDB,0x01,0xDD,0x01,0xDF,0x01,0xE1,0x01,0xE3,0x01,0xE5,0x01,0xE7,0x01,0xEA,0x01,0xEC,0x01,0xEE,0x01,0xF0,0x01,0xF2,0x01,0xF4,0x01,0xF6,0x01,0xF8,0x01,0xFB,0x01,0xFD,0x01,0xFF,0x01,0x01,0x02,0x03,0x02,0x05,0x02,0x07,0x02,0x09,0x02,0x0B,0x02,0x0E,0x02,0x10,0x02,0x12,0x02,0x14,0x02,0x16,0x02,0x18,0x02,0x1A,0x02,0x1C,0x02,0x1F,0x02,0x20,0x02,0x23,0x02,0x25,0x02,0x27,0x02,0x29,0x02,0x2B,0x02,0x2D,0x02,0x2F,0x02,0x32,0x02,0x34,0x02,0x36,0x02,0x38,0x02,0x3A,0x02,0x3C,0x02,0x3E,0x02,0x40,0x02,0x42,0x02,0x45,0x02,0x47,0x02,0x49,0x02,0x4B,0x02,0x4D,0x02,0x4F,0x02,0x51,0x02,0x53,0x02,0x55,0x02,0x58,0x02,0x59,0x02,0x5C,0x02,0x5E,0x02,0x60,0x02,0x62,0x02,0x64,0x02,0x66,0x02,0x68,0x02,0x6A,0x02,0x6C,0x02,0x6F,0x02,0x71,0x02,0x73,0x02,0x75,0x02,0x77,0x02,0x79,0x02,0x7B,0x02,0x7D,0x02,0x7F,0x02,0x82,0x02,0x84,0x02,0x86,0x02,0x88,0x02,0x8A,0x02,0x8C,0x02,0x8E,0x02,0x90,0x02,0x92,0x02,0x95,0x02,0x97,0x02,0x99,0x02,0x9B,0x02,0x9D,0x02,0x9F,0x02,0xA1,0x02,0xA3,0x02,0xA6,0x02,0xA7,0x02,0xAA,0x02,0xAC,0x02,0xAE,0x02,0xB0,0x02,0xB2,0x02,0xB4,0x02,0xB6,0x02,0xB8,0x02,0xBA,0x02,0xBC,0x02,0xBF,0x02,0xC1,0x02,0xC3,0x02,0xC5,0x02,0xC7,0x02,0xC9,0x02,0xCB,0x02,0xCD,0x02,0xCF,0x02,0xD1,0x02,0xD4,0x02,0xD6,0x02,0xD8,0x02,0xDA,0x02,0xDC,0x02,0xDE,0x02,0xE0,0x02,0xE3,0x02,0xE5,0x02,0xE7,0x02,0xE9,0x02,0xEB,0x02,0xED,0x02,0xEF,0x02,0xF1,0x02,0xF3,0x02,0xF5,0x02,0xF8,0x02,0xFA,0x02,0xFC,0x02,0xFE,0x02,0x00,0x03,0x02,0x03,0x04,0x03,0x06,0x03,0x08,0x03,0x0A,0x03,0x0D,0x03,0x0F,0x03,0x11,0x03,0x13,0x03,0x15,0x03,0x17,0x03,0x19,0x03,0x1B,0x03,0x1E,0x03,0x20,0x03,0x22,0x03,0x24,0x03,0x26,0x03,0x28,0x03,0x2A,0x03,0x2D,0x03,0x2E,0x03,0x30,0x03,0x33,0x03,0x35,0x03,0x37,0x03,0x39,0x03,0x3B,0x03,0x3D,0x03,0x3F,0x03,0x41,0x03,0x43,0x03,0x46,0x03,0x48,0x03,0x4A,0x03,0x4C,0x03,0x4E,0x03,0x50,0x03,0x52,0x03,0x54,0x03,0x57,0x03,0x59,0x03,0x5B,0x03,0x5D,0x03,0x5F,0x03,0x61,0x03,0x63,0x03,0x65,0x03,0x67,0x03,0x6A,0x03,0x6C,0x03,0x6E,0x03,0x70,0x03,0x72,0x03,0x74,0x03,0x76,0x03,0x78,0x03,0x7B,0x03,0x7D,0x03,0x7F,0x03,0x81,0x03,0x83,0x03,0x85,0x03,0x87,0x03,0x89,0x03,0x8B,0x03,0x8D,0x03,0x90,0x03,0x92,0x03,0x94,0x03,0x96,0x03,0x98,0x03,0x9A,0x03,0x9C,0x03,0x9E,0x03,0xA0,0x03,0xA3,0x03,0xA5,0x03,0xA7,0x03,0xA9,0x03,0xAB,0x03,0xAD,0x03,0xAF,0x03,0xB1,0x03,0xB4,0x03,0xB6,0x03,0xB8,0x03,0xBA,0x03,0xBC,0x03,0xBE,0x03,0xC0,0x03,0xC2,0x03,0xC4,0x03,0xC7,0x03,0xC9,0x03,0xCB,0x03,0xCD,0x03,0xCF,0x03,0xD1,0x03,0xD3,0x03,0xD5,0x03,0xD7,0x03,0xDA,0x03,0xDC,0x03,0xDE,0x03,0xE0,0x03,0xE2,0x03,0xE4,0x03,0xE6,0x03,0xE8,0x03,0xEA,0x03,0xED,0x03,0xEF,0x03,0xF1,0x03,0xF3,0x03,0xF5,0x03,0xF7,0x03,0xF9,0x03,0xFC,0x03,0xFD,0x03,0x00,0x04,0x02,0x04,0x04,0x04,0x06,0x04,0x08,0x04,0x0A,0x04,0x0C,0x04,0x0F,0x04,0x11,0x04,0x13,0x04,0x15,0x04,0x17,0x04,0x19,0x04,0x1B,0x04,0x1D,0x04,0x20,0x04,0x21,0x04,0x24,0x04,0x26,0x04,0x28,0x04,0x2A,0x04,0x2C,0x04,0x2F,0x04,0x31,0x04,0x33,0x04,0x35,0x04,0x37,0x04,0x39,0x04,0x3B,0x04,0x3D,0x04,0x3F,0x04,0x42,0x04,0x44,0x04,0x46,0x04,0x48,0x04,0x4A,0x04,0x4C,0x04,0x4E,0x04,0x51,0x04,0x53,0x04,0x55,0x04,0x57,0x04,0x59,0x04,0x5B,0x04,0x5D,0x04,0x5F,0x04,0x62,0x04,0x64,0x04,0x66,0x04,0x68,0x04,0x6A,0x04,0x6C,0x04,0x6E,0x04,0x71,0x04,0x73,0x04,0x75,0x04,0x77,0x04,0x79,0x04,0x7B,0x04,0x7D,0x04,0x80,0x04,0x81,0x04,0x84,0x04,0x86,0x04,0x88,0x04,0x8A,0x04,0x8C,0x04,0x8E,0x04,0x91,0x04,0x93,0x04,0x95,0x04,0x97,0x04,0x99,0x04,0x9B,0x04,0x9D,0x04,0x9F,0x04,0xA1,0x04,0xA4,0x04,0xA6,0x04,0xA8,0x04,0xAA,0x04,0xAC,0x04,0xAE,0x04,0xB1,0x04,0xB3,0x04,0xB5,0x04,0xB7,0x04,0xB9,0x04,0xBB,0x04,0xBD,0x04,0xC0,0x04,0xC2,0x04,0xC4,0x04,0xC6,0x04,0xC8,0x04,0xCA,0x04,0xCC,0x04,0xCE,0x04,0xD1,0x04,0xD3,0x04,0xD5,0x04,0xD7,0x04,0xD9,0x04,0xDB,0x04,0xDE,0x04,0xE0,0x04,0xE2,0x04,0xE4,0x04,0xE6,0x04,0xE8,0x04,0xEA,0x04,0xEC,0x04,0xEE,0x04,0xF1,0x04,0xF3,0x04,0xF5,0x04,0xF7,0x04,0xF9,0x04,0xFB,0x04,0xFE,0x04,0x00,0x05,0x02,0x05,0x04,0x05,0x06,0x05,0x08,0x05,0x0A,0x05,0x0C,0x05,0x0E,0x05,0x11,0x05,0x13,0x05,0x15,0x05,0x17,0x05,0x19,0x05,0x1B,0x05,0x1D,0x05,0x20,0x05,0x22,0x05,0x24,0x05,0x26,0x05,0x28,0x05,0x2A,0x05,0x2C,0x05,0x2F,0x05,0x31,0x05,0x33,0x05,0x35,0x05,0x37,0x05,0x39,0x05,0x3B,0x05,0x3D,0x05,0x3F,0x05,0x41,0x05,0x44,0x05,0x46,0x05,0x48,0x05,0x4A,0x05,0x4C,0x05,0x4E,0x05,0x50,0x05,0x52,0x05,0x55,0x05,0x57,0x05,0x59,0x05,0x5B,0x05,0x5D,0x05,0x5F,0x05,0x61,0x05,0x64,0x05,0x65,0x05,0x68,0x05,0x6A,0x05,0x6C,0x05,0x6E,0x05,0x70,0x05,0x72,0x05,0x74,0x05,0x77,0x05,0x79,0x05,0x7B,0x05,0x7D,0x05,0x7F,0x05,0x81,0x05,0x83,0x05,0x85,0x05,0x88,0x05,0x8A,0x05,0x8C,0x05,0x8E,0x05,0x90,0x05,0x92,0x05,0x95,0x05,0x97,0x05,0x99,0x05,0x9B,0x05,0x9D,0x05,0x9F,0x05,0xA1,0x05,0xA3,0x05,0xA5,0x05,0xA8,0x05,0xAA,0x05,0xAC,0x05,0xAE,0x05,0xB0,0x05,0xB2,0x05,0xB4,0x05,0xB6,0x05,0xB9,0x05,0xBB,0x05,0xBD,0x05,0xBF,0x05,0xC1,0x05,0xC3,0x05,0xC5,0x05,0xC7,0x05,0xCA,0x05,0xCC,0x05,0xCE,0x05,0xD0,0x05,0xD2,0x05,0xD4,0x05,0xD6,0x05,0xD9,0x05,0xDB,0x05,0xDD,0x05,0xDF,0x05,0xE1,0x05,0xE3,0x05,0xE6,0x05,0xE8,0x05,0xEA,0x05,0xEC,0x05,0xEE,0x05,0xF0,0x05,0xF2,0x05,0xF4,0x05,0xF6,0x05,0xF9,0x05,0xFA,0x05,0xFD,0x05,0xFF,0x05,0x01,0x06,0x03,0x06,0x05,0x06,0x07,0x06,0x09,0x06,0x0C,0x06,0x0E,0x06,0x10,0x06,0x12,0x06,0x14,0x06,0x16,0x06,0x18,0x06,0x1A,0x06,0x1C,0x06,0x1E,0x06,0x21,0x06,0x23,0x06,0x25,0x06,0x27,0x06,0x29,0x06,0x2B,0x06,0x2D,0x06,0x2F,0x06,0x32,0x06,0x34,0x06,0x36,0x06,0x38,0x06,0x3A,0x06,0x3C,0x06,0x3E,0x06,0x40,0x06,0x43,0x06,0x45,0x06,0x47,0x06,0x49,0x06,0x4B,0x06,0x4D,0x06,0x4F,0x06,0x51,0x06,0x53,0x06,0x55,0x06,0x57,0x06,0x59,0x06,0x5C,0x06,0x5E,0x06,0x60,0x06,0x62,0x06,0x64,0x06,0x66,0x06,0x68,0x06,0x6A,0x06,0x6C,0x06,0x6F,0x06,0x70,0x06,0x73,0x06,0x75,0x06,0x77,0x06,0x79,0x06,0x7B,0x06,0x7D,0x06,0x7F,0x06,0x81,0x06,0x83,0x06,0x85,0x06,0x87,0x06,0x89,0x06,0x8B,0x06,0x8D,0x06,0x8F,0x06,0x91,0x06,0x93,0x06,0x95,0x06,0x97,0x06,0x99,0x06,0x9B,0x06,0x9D,0x06,0x9F,0x06,0xA1,0x06,0xA3,0x06,0xA5,0x06,0xA7,0x06,0xA9,0x06,0xAB,0x06,0xAD,0x06,0xAF,0x06,0xB1,0x06,0xB3,0x06,0xB5,0x06,0xB7,0x06,0xB9,0x06,0xBB,0x06,0xBD,0x06,0xC0,0x06,0xC2,0x06,0xC3,0x06,0xC6,0x06,0xC8,0x06,0xCA,0x06,0xCC,0x06,0xCE,0x06,0xD0,0x06,0xD2,0x06,0xD4,0x06,0xD6,0x06,0xD8,0x06,0xDA,0x06,0xDC,0x06,0xDE,0x06,0xE0,0x06,0xE2,0x06,0xE4,0x06,0xE6,0x06,0xE9,0x06,0xEA,0x06,0xEC,0x06,0xEE,0x06,0xF0,0x06,0xF3,0x06,0xF5,0x06,0xF7,0x06,0xF9,0x06,0xFB,0x06,0xFD,0x06,0xFF,0x06,0x01,0x07,0x03,0x07,0x05,0x07,0x07,0x07,0x09,0x07,0x0B,0x07,0x0D,0x07,0x0F,0x07,0x12,0x07,0x14,0x07,0x16,0x07,0x18,0x07,0x1A,0x07,0x1C,0x07,0x1E,0x07,0x20,0x07,0x22,0x07,0x24,0x07,0x26,0x07,0x29,0x07,0x2A,0x07,0x2D,0x07,0x2F,0x07,0x31,0x07,0x33,0x07,0x35,0x07,0x37,0x07,0x39,0x07,0x3B,0x07,0x3D,0x07,0x3F,0x07,0x41,0x07,0x43,0x07,0x46,0x07,0x48,0x07,0x4A,0x07,0x4C,0x07,0x4E,0x07,0x50,0x07,0x52,0x07,0x54,0x07,0x56,0x07,0x58,0x07,0x5A,0x07,0x5D,0x07,0x5F,0x07,0x61,0x07,0x63,0x07,0x65,0x07,0x67,0x07,0x69,0x07,0x6B,0x07,0x6E,0x07,0x6F,0x07,0x72,0x07,0x74,0x07,0x76,0x07,0x78,0x07,0x7A,0x07,0x7C,0x07,0x7E,0x07,0x80,0x07,0x83,0x07,0x85,0x07,0x87,0x07,0x89,0x07,0x8B,0x07,0x8D,0x07,0x8F,0x07,0x91,0x07,0x93,0x07,0x96,0x07,0x98,0x07,0x9A,0x07,0x9C,0x07,0x9E,0x07,0xA0,0x07,0xA2,0x07,0xA4,0x07,0xA6,0x07,0xA8,0x07,0xAB,0x07,0xAD,0x07,0xAF,0x07,0xB1,0x07,0xB3,0x07,0xB5,0x07,0xB7,0x07,0xBA,0x07,0xBC,0x07,0xBE,0x07,0xC0,0x07,0xC2,0x07,0xC4,0x07,0xC6,0x07,0xC8,0x07,0xCA,0x07,0xCD,0x07,0xCF,0x07,0xD1,0x07,0xD3,0x07,0xD5,0x07,0xD7,0x07,0xD9,0x07,0xDB,0x07,0xDD,0x07,0xE0,0x07,0xE2,0x07,0xE4,0x07,0xE6,0x07,0xE8,0x07,0xEA,0x07,0xEC,0x07,0xEF,0x07,0xF1,0x07,0xF3,0x07,0xF5,0x07,0xF7,0x07,0xF9,0x07,0xFB,0x07,0xFE,0x07,0x00,0x08,0x02,0x08,0x04,0x08,0x06,0x08,0x08,0x08,0x0A,0x08,0x0C,0x08,0x0E,0x08,0x11,0x08,0x13,0x08,0x15,0x08,0x17,0x08,0x19,0x08,0x1B,0x08,0x1D,0x08,0x1F,0x08,0x21,0x08,0x23,0x08,0x26,0x08,0x28,0x08,0x2A,0x08,0x2C,0x08,0x2E,0x08,0x30,0x08,0x32,0x08,0x34,0x08,0x36,0x08,0x38,0x08,0x3A,0x08,0x3C,0x08,0x3E,0x08,0x40,0x08,0x42,0x08,0x44,0x08,0x46,0x08,0x48,0x08,0x4A,0x08,0x4C,0x08,0x4E,0x08,0x50,0x08,0x52,0x08,0x53,0x08,0x55,0x08,0x57,0x08,0x59,0x08,0x5B,0x08,0x5C,0x08,0x5E,0x08,0x60,0x08,0x62,0x08,0x63,0x08,0x65,0x08,0x67,0x08,0x68,0x08,0x6A,0x08,0x76,0x08,
                    0x04,0x00,0x05,0x00,0x0F,0x00,0x08,0x00,0x09,0x00,0x09,0x00,0x0A,0x00,0x11,0x00,0x14,0x00,0x41,0x00,0x0A,0x00,0x0C,0x00,0x0F,0x00,0x0C,0x00,0x09,0x00,0x05,0x00,0x11,0x00,0x0A,0x00,0x0D,0x00,0x10,0x00,0x0E,0x00,0x12,0x00,0x08,0x00,0x04,0x00,0x0D,0x00,0x05,0x00,0x06,0x00,0x09,0x00,0x0B,0x00,0x0F,0x00,0x11,0x00,0x11,0x00,0x13,0x00,0x09,0x00,0x06,0x00,0x08,0x00,0x08,0x00,0x05,0x00,0x0E,0x00,0x09,0x00,0x09,0x00,0x0E,0x00,0x11,0x00,0x42,0x00,0x0A,0x00,0x0B,0x00,0x3C,0x00,0x3C,0x00,0x05,0x00,0x47,0x00,0x11,0x00,0x0F,0x00,0x09,0x00,0x0B,0x00,0x3D,0x00,0x05,0x00,0x0D,0x00,0x11,0x00,0x3E,0x00,0x15,0x00,0x0F,0x00,0x10,0x00,0x0B,0x00,0x0B,0x00,0x11,0x00,0x05,0x00,0x02,0x00,0x10,0x00,0x0D,0x00,0x08,0x00,0x11,0x00,0x0A,0x00,0x17,0x00,0x0B,0x00,0x0B,0x00,0x05,0x00,0x3D,0x00,0x0B,0x00,0x0E,0x00,0x07,0x00,0x05,0x00,0x0C,0x00,0x06,0x00,0x06,0x00,0x13,0x00,0x0A,0x00,0x11,0x00,0x0D,0x00,0x08,0x00,0x0A,0x00,0x0A,0x00,0x0D,0x00,0x0E,0x00,0x3A,0x00,0x10,0x00,0x07,0x00,0x14,0x00,0x11,0x00,0x0B,0x00,0x0E,0x00,0x05,0x00,0x0C,0x00,0x04,0x00,0x13,0x00,0x05,0x00,0x0D,0x00,0x16,0x00,0x16,0x00,0x09,0x00,0x05,0x00,0x0E,0x00,0x0A,0x00,0x0C,0x00,0x01,0x00,0x10,0x00,0x0B,0x00,0x08,0x00,0x10,0x00,0x10,0x00,0x13,0x00,0x03,0x00,0x40,0x00,0x04,0x00,0x05,0x00,0x05,0x00,0x0F,0x00,0x0D,0x00,0x40,0x00,0x06,0x00,0x0A,0x00,0x3D,0x00,0x09,0x00,0x1A,0x00,0x08,0x00,0x05,0x00,0x10,0x00,0x08,0x00,0x12,0x00,0x11,0x00,0x09,0x00,0x02,0x00,0x3A,0x00,0x03,0x00,0x0B,0x00,0x01,0x00,0x3C,0x00,0x05,0x00,0x13,0x00,0x0C,0x00,0x11,0x00,0x0B,0x00,0x14,0x00,0x12,0x00,0x07,0x00,0x14,0x00,0x08,0x00,0x08,0x00,0x04,0x00,0x12,0x00,0x05,0x00,0x40,0x00,0x11,0x00,0x06,0x00,0x0B,0x00,0x0B,0x00,0x06,0x00,0x40,0x00,0x08,0x00,0x11,0x00,0x0B,0x00,0x13,0x00,0x10,0x00,0x05,0x00,0x0C,0x00,0x06,0x00,0x10,0x00,0x0D,0x00,0x08,0x00,0x0B,0x00,0x07,0x00,0x17,0x00,0x13,0x00,0x11,0x00,0x0E,0x00,0x0D,0x00,0x0B,0x00,0x0B,0x00,0x08,0x00,0x0C,0x00,0x07,0x00,0x4C,0x00,0x46,0x00,0x0B,0x00,0x10,0x00,0x04,0x00,0x0B,0x00,0x0C,0x00,0x0D,0x00,0x05,0x00,0x03,0x00,0x05,0x00,0x11,0x00,0x06,0x00,0x0B,0x00,0x09,0x00,0x21,0x00,0x10,0x00,0x03,0x00,0x0B,0x00,0x0D,0x00,0x3D,0x00,0x06,0x00,0x0C,0x00,0x0D,0x00,0x43,0x00,0x0B,0x00,0x42,0x00,0x11,0x00,0x0C,0x00,0x05,0x00,0x08,0x00,0x05,0x00,0x03,0x00,0x0A,0x00,0x0F,0x00,0x0C,0x00,0x17,0x00,0x0E,0x00,0x16,0x00,0x0B,0x00,0x0B,0x00,0x06,0x00,0x06,0x00,0x0D,0x00,0x0B,0x00,0x0B,0x00,0x09,0x00,0x0C,0x00,0x0E,0x00,0x08,0x00,0x3C,0x00,0x0C,0x00,0x0F,0x00,0x01,0x00,0x0B,0x00,0x0B,0x00,0x0D,0x00,0x05,0x00,0x0F,0x00,0x16,0x00,0x4A,0x00,0x08,0x00,0x0C,0x00,0x09,0x00,0x0B,0x00,0x00,0x00,0x04,0x00,0x3A,0x00,0x09,0x00,0x0F,0x00,0x05,0x00,0x09,0x00,0x0A,0x00,0x0E,0x00,0x11,0x00,0x3A,0x00,0x11,0x00,0x05,0x00,0x07,0x00,0x0A,0x00,0x10,0x00,0x09,0x00,0x0B,0x00,0x07,0x00,0x08,0x00,0x07,0x00,0x07,0x00,0x06,0x00,0x16,0x00,0x0D,0x00,0x10,0x00,0x11,0x00,0x07,0x00,0x06,0x00,0x06,0x00,0x0F,0x00,0x39,0x00,0x07,0x00,0x41,0x00,0x13,0x00,0x08,0x00,0x06,0x00,0x10,0x00,0x0A,0x00,0x0A,0x00,0x06,0x00,0x0B,0x00,0x06,0x00,0x05,0x00,0x03,0x00,0x05,0x00,0x06,0x00,0x13,0x00,0x0A,0x00,0x0E,0x00,0x3B,0x00,0x0B,0x00,0x0C,0x00,0x05,0x00,0x08,0x00,0x0F,0x00,0x0F,0x00,0x0B,0x00,0x0C,0x00,0x16,0x00,0x01,0x00,0x05,0x00,0x07,0x00,0x08,0x00,0x03,0x00,0x09,0x00,0x43,0x00,0x08,0x00,0x10,0x00,0x0E,0x00,0x08,0x00,0x13,0x00,0x01,0x00,0x07,0x00,0x10,0x00,0x0B,0x00,0x0B,0x00,0x01,0x00,0x0B,0x00,0x0F,0x00,0x0B,0x00,0x0B,0x00,0x0B,0x00,0x09,0x00,0x0A,0x00,0x0A,0x00,0x0A,0x00,0x01,0x00,0x05,0x00,0x08,0x00,0x12,0x00,0x3A,0x00,0x0A,0x00,0x04,0x00,0x06,0x00,0x10,0x00,0x05,0x00,0x06,0x00,0x08,0x00,0x0B,0x00,0x0E,0x00,0x10,0x00,0x09,0x00,0x03,0x00,0x0B,0x00,0x11,0x00,0x11,0x00,0x06,0x00,0x06,0x00,0x0E,0x00,0x04,0x00,0x08,0x00,0x0D,0x00,0x16,0x00,0x0E,0x00,0x07,0x00,0x09,0x00,0x11,0x00,0x09,0x00,0x06,0x00,0x3E,0x00,0x11,0x00,0x0B,0x00,0x17,0x00,0x11,0x00,0x0D,0x00,0x05,0x00,0x09,0x00,0x0B,0x00,0x07,0x00,0x05,0x00,0x39,0x00,0x0C,0x00,0x0C,0x00,0x48,0x00,0x0B,0x00,0x0E,0x00,0x0E,0x00,0x07,0x00,0x3A,0x00,0x0C,0x00,0x38,0x00,0x3A,0x00,0x07,0x00,0x0A,0x00,0x05,0x00,0x05,0x00,0x12,0x00,0x07,0x00,0x09,0x00,0x07,0x00,0x07,0x00,0x0B,0x00,0x09,0x00,0x0F,0x00,0x44,0x00,0x13,0x00,0x04,0x00,0x39,0x00,0x39,0x00,0x09,0x00,0x05,0x00,0x01,0x00,0x08,0x00,0x11,0x00,0x0E,0x00,0x0B,0x00,0x0E,0x00,0x03,0x00,0x09,0x00,0x05,0x00,0x05,0x00,0x05,0x00,0x09,0x00,0x43,0x00,0x07,0x00,0x1A,0x00,0x0B,0x00,0x10,0x00,0x40,0x00,0x0B,0x00,0x05,0x00,0x05,0x00,0x05,0x00,0x00,0x00,0x05,0x00,0x40,0x00,0x0B,0x00,0x08,0x00,0x0B,0x00,0x0C,0x00,0x0B,0x00,0x0A,0x00,0x06,0x00,0x0A,0x00,0x03,0x00,0x3F,0x00,0x07,0x00,0x3C,0x00,0x0A,0x00,0x0C,0x00,0x09,0x00,0x10,0x00,0x0C,0x00,0x0A,0x00,0x0F,0x00,0x07,0x00,0x11,0x00,0x07,0x00,0x11,0x00,0x15,0x00,0x46,0x00,0x0A,0x00,0x11,0x00,0x11,0x00,0x3B,0x00,0x0D,0x00,0x08,0x00,0x13,0x00,0x0E,0x00,0x09,0x00,0x05,0x00,0x06,0x00,0x06,0x00,0x06,0x00,0x3C,0x00,0x11,0x00,0x15,0x00,0x0C,0x00,0x11,0x00,0x0A,0x00,0x0B,0x00,0x10,0x00,0x01,0x00,0x05,0x00,0x04,0x00,0x01,0x00,0x08,0x00,0x0F,0x00,0x0B,0x00,0x09,0x00,0x13,0x00,0x40,0x00,0x11,0x00,0x11,0x00,0x0C,0x00,0x3C,0x00,0x06,0x00,0x3D,0x00,0x02,0x00,0x05,0x00,0x0B,0x00,0x10,0x00,0x0E,0x00,0x41,0x00,0x0B,0x00,0x03,0x00,0x04,0x00,0x06,0x00,0x05,0x00,0x05,0x00,0x0C,0x00,0x12,0x00,0x09,0x00,0x0C,0x00,0x0E,0x00,0x04,0x00,0x3E,0x00,0x07,0x00,0x08,0x00,0x0E,0x00,0x07,0x00,0x06,0x00,0x03,0x00,0x0C,0x00,0x10,0x00,0x11,0x00,0x14,0x00,0x10,0x00,0x0B,0x00,0x07,0x00,0x07,0x00,0x07,0x00,0x0B,0x00,0x05,0x00,0x09,0x00,0x0F,0x00,0x17,0x00,0x11,0x00,0x08,0x00,0x10,0x00,0x0F,0x00,0x04,0x00,0x03,0x00,0x0E,0x00,0x11,0x00,0x07,0x00,0x05,0x00,0x04,0x00,0x05,0x00,0x04,0x00,0x0E,0x00,0x3F,0x00,0x03,0x00,0x03,0x00,0x12,0x00,0x3C,0x00,0x0D,0x00,0x05,0x00,0x04,0x00,0x15,0x00,0x04,0x00,0x3C,0x00,0x0E,0x00,0x08,0x00,0x09,0x00,0x09,0x00,0x0B,0x00,0x04,0x00,0x0D,0x00,0x09,0x00,0x0C,0x00,0x01,0x00,0x0A,0x00,0x0A,0x00,0x0B,0x00,0x07,0x00,0x05,0x00,0x38,0x00,0x0E,0x00,0x11,0x00,0x11,0x00,0x06,0x00,0x0A,0x00,0x0D,0x00,0x08,0x00,0x3B,0x00,0x05,0x00,0x09,0x00,0x0A,0x00,0x11,0x00,0x0B,0x00,0x0A,0x00,0x00,0x00,0x03,0x00,0x05,0x00,0x02,0x00,0x0B,0x00,0x11,0x00,0x06,0x00,0x10,0x00,0x0A,0x00,0x0B,0x00,0x09,0x00,0x0D,0x00,0x0D,0x00,0x3A,0x00,0x38,0x00,0x01,0x00,0x0B,0x00,0x38,0x00,0x0B,0x00,0x40,0x00,0x13,0x00,0x05,0x00,0x0A,0x00,0x07,0x00,0x0A,0x00,0x08,0x00,0x09,0x00,0x0E,0x00,0x0A,0x00,0x0B,0x00,0x0E,0x00,0x0D,0x00,0x11,0x00,0x0A,0x00,0x13,0x00,0x04,0x00,0x0C,0x00,0x00,0x00,0x06,0x00,0x09,0x00,0x09,0x00,0x16,0x00,0x12,0x00,0x0A,0x00,0x0B,0x00,0x0D,0x00,0x0B,0x00,0x05,0x00,0x0C,0x00,0x10,0x00,0x05,0x00,0x12,0x00,0x0A,0x00,0x11,0x00,0x13,0x00,0x40,0x00,0x05,0x00,0x09,0x00,0x06,0x00,0x00,0x00,0x06,0x00,0x0C,0x00,0x09,0x00,0x0E,0x00,0x0D,0x00,0x0F,0x00,0x03,0x00,0x05,0x00,0x10,0x00,0x05,0x00,0x05,0x00,0x13,0x00,0x09,0x00,0x0C,0x00,0x0D,0x00,0x0C,0x00,0x06,0x00,0x0A,0x00,0x0B,0x00,0x0C,0x00,0x02,0x00,0x41,0x00,0x09,0x00,0x0B,0x00,0x13,0x00,0x0B,0x00,0x09,0x00,0x0B,0x00,0x03,0x00,0x0C,0x00,0x07,0x00,0x0F,0x00,0x0D,0x00,0x0E,0x00,0x10,0x00,0x0C,0x00,0x0E,0x00,0x0C,0x00,0x0F,0x00,0x08,0x00,0x08,0x00,0x15,0x00,0x08,0x00,0x08,0x00,0x06,0x00,0x05,0x00,0x0A,0x00,0x3D,0x00,0x0B,0x00,0x0D,0x00,0x3A,0x00,0x08,0x00,0x05,0x00,0x0F,0x00,0x19,0x00,0x06,0x00,0x11,0x00,0x07,0x00,0x12,0x00,0x0B,0x00,0x0B,0x00,0x03,0x00,0x07,0x00,0x0D,0x00,0x0B,0x00,0x0B,0x00,0x10,0x00,0x13,0x00,0x11,0x00,0x11,0x00,0x11,0x00,0x0E,0x00,0x07,0x00,0x03,0x00,0x0A,0x00,0x05,0x00,0x04,0x00,0x03,0x00,0x0D,0x00,0x0E,0x00,0x41,0x00,0x0C,0x00,0x0C,0x00,0x08,0x00,0x04,0x00,0x04,0x00,0x09,0x00,0x0E,0x00,0x06,0x00,0x47,0x00,0x0B,0x00,0x14,0x00,0x10,0x00,0x05,0x00,0x06,0x00,0x08,0x00,0x06,0x00,0x04,0x00,0x07,0x00,0x06,0x00,0x09,0x00,0x17,0x00,0x0F,0x00,0x11,0x00,0x08,0x00,0x02,0x00,0x0A,0x00,0x05,0x00,0x07,0x00,0x04,0x00,0x0B,0x00,0x11,0x00,0x0E,0x00,0x15,0x00,0x10,0x00,0x0B,0x00,0x11,0x00,0x0E,0x00,0x0E,0x00,0x08,0x00,0x04,0x00,0x06,0x00,0x0B,0x00,0x0D,0x00,0x0B,0x00,0x0F,0x00,0x0E,0x00,0x0C,0x00,0x0D,0x00,0x07,0x00,0x09,0x00,0x0B,0x00,0x09,0x00,0x05,0x00,0x11,0x00,0x10,0x00,0x0F,0x00,0x08,0x00,0x11,0x00,0x0B,0x00,0x07,0x00,0x11,0x00,0x0C,0x00,0x0D,0x00,0x0C,0x00,0x0A,0x00,0x39,0x00,0x40,0x00,0x04,0x00,0x0B,0x00,0x05,0x00,0x0B,0x00,0x05,0x00,0x18,0x00,0x0A,0x00,0x11,0x00,0x0F,0x00,0x04,0x00,0x05,0x00,0x07,0x00,0x11,0x00,0x36,0x00,0x06,0x00,0x0B,0x00,0x01,0x00,0x12,0x00,0x0A,0x00,0x11,0x00,0x04,0x00,0x37,0x00,0x0A,0x00,0x0B,0x00,0x0F,0x00,0x0B,0x00,0x0B,0x00,0x3E,0x00,0x49,0x00,0x17,0x00,0x10,0x00,0x11,0x00,0x05,0x00,0x3A,0x00,0x3A,0x00,0x0E,0x00,0x0D,0x00,0x1D,0x00,0x14,0x00,0x44,0x00,0x15,0x00,0x0F,0x00,0x09,0x00,0x0C,0x00,0x40,0x00,0x0B,0x00,0x07,0x00,0x09,0x00,0x0B,0x00,0x18,0x00,0x14,0x00,0x24,0x00,0x27,0x00,0x0D,0x00,0x0E,0x00,0x30,0x00,0x2F,0x00,0x46,0x00,0x2B,0x00,0x5E,0x00,0x3C,0x00,0x87,0x00,0x58,0x00,0x57,0x00,0x29,0x00,0x3A,0x00,0x29,0x00,0x1F,0x00,0x13,0x00,0x40,0x00,0x3F,0x00,0x09,0x00,0x40,0x00,0x1C,0x00,0x05,0x00,0x0B,0x00,0x09,0x00,0x11,0x00,0x14,0x00,0x11,0x00,0x0A,0x00,0x3B,0x00,0x3B,0x00,0x11,0x00,0x1D,0x00,0x09,0x00,0x13,0x00,0x1C,0x00,0x12,0x00,0x04,0x00,0x15,0x00,0x1A,0x00,0x11,0x00,0x0D,0x00,0x16,0x00,0x0A,0x00,0x10,0x00,0x06,0x00,0x0F,0x00,0x0C,0x00,0x06,0x00,0x3D,0x00,0x44,0x00,0x0B,0x00,0x0E,0x00,0x0A,0x00,0x08,0x00,0x13,0x00,0x13,0x00,0x06,0x00,0x05,0x00,0x09,0x00,0x05,0x00,0x42,0x00,0x1D,0x00,0x05,0x00,0x07,0x00,0x13,0x00,0x06,0x00,0x0B,0x00,0x06,0x00,0x0B,0x00,0x08,0x00,0x09,0x00,0x05,0x00,0x0E,0x00,0x0E,0x00,0x0E,0x00,0x13,0x00,0x0A,0x00,0x0C,0x00,0x3A,0x00,0x05,0x00,0x0A,0x00,0x05,0x00,0x03,0x00,0x3D,0x00,0x15,0x00,0x23,0x00,0x1B,0x00,0x0B,0x00,0x0C,0x00,0x10,0x00,0x0C,0x00,0x13,0x00,0x07,0x00,0x06,0x00,0x06,0x00,0x0F,0x00,0x13,0x00,0x11,0x00,0x0F,0x00,0x06,0x00,0x06,0x00,0x0C,0x00,0x0D,0x00,0x0F,0x00,0x05,0x00,0x3B,0x00,0x15,0x00,0x06,0x00,0x1D,0x00,0x09,0x00,0x11,0x00,0x13,0x00,0x41,0x00,0x08,0x00,0x1D,0x00,0x0B,0x00,0x11,0x00,0x0D,0x00,0x43,0x00,0x18,0x00,0x0B,0x00,0x01,0x00,0x0F,0x00,0x17,0x00,0x06,0x00,0x0E,0x00,0x15,0x00,0x12,0x00,0x0B,0x00,0x0F,0x00,0x11,0x00,0x11,0x00,0x0D,0x00,0x0A,0x00,0x13,0x00,0x3D,0x00,0x22,0x00,0x0E,0x00,0x0C,0x00,0x08,0x00,0x12,0x00,0x10,0x00,0x0B,0x00,0x0F,0x00,0x11,0x00,0x0E,0x00,0x12,0x00,0x06,0x00,0x03,0x00,0x12,0x00,0x10,0x00,0x1B,0x00,0x10,0x00,0x0A,0x00,0x09,0x00,0x0B,0x00,0x0E,0x00,0x0E,0x00,0x40,0x00,0x0A,0x00,0x12,0x00,0x17,0x00,0x11,0x00,0x10,0x00,0x0B,0x00,0x0B,0x00,0x0C,0x00,0x07,0x00,0x0A,0x00,0x0E,0x00,0x0F,0x00,0x05,0x00,0x06,0x00,0x0B,0x00,0x11,0x00,0x11,0x00,0x0A,0x00,0x0D,0x00,0x05,0x00,0x0E,0x00,0x15,0x00,0x15,0x00,0x10,0x00,0x46,0x00,0x07,0x00,0x0F,0x00,0x07,0x00,0x18,0x00,0x0E,0x00,0x05,0x00,0x46,0x00,0x13,0x00,0x15,0x00,0x0D,0x00,0x17,0x00,0x42,0x00,0x14,0x00,0x0D,0x00,0x12,0x00,0x0B,0x00,0x13,0x00,0x09,0x00,0x0B,0x00,0x16,0x00,0x24,0x00,0x1C,0x00,0x16,0x00,0x12,0x00,0x16,0x00,0x47,0x00,0x11,0x00,0x1C,0x00,0x13,0x00,0x13,0x00,0x1D,0x00,0x33,0x00,0x38,0x00,0x17,0x00,0x20,0x00,0x2F,0x00,0x4C,0x00,0x55,0x00,0x4F,0x00,0x30,0x00,0x63,0x00,0x3F,0x00,0x75,0x00,0x84,0x00,0x6A,0x00,0x6C,0x00,0x65,0x00,0x78,0x00,0xAE,0x00,0x82,0x00,0x8D,0x00,0xDC,0x00,0xB1,0x00,0xF4,0x00,0x03,0x01,0x04,0x01,0xF3,0x00,0xFD,0x00,0xFF,0x00,0x37,0x01,0x3E,0x01,0x66,0x01,0x53,0x01,0x31,0x01,0x66,0x01,0x4A,0x01,0x5B,0x01,0x9A,0x01,0xB1,0x01,0x73,0x01,0x99,0x01,0xC7,0x01,0x4F,0x02
                };
                byte[] torque_graph_buf = new byte[graph_buf_whole.Length / 2];
                byte[] angle_graph_buf = new byte[graph_buf_whole.Length / 2];
                Array.Copy(graph_buf_whole, graph_buf_whole.Length / 2, torque_graph_buf, 0, graph_buf_whole.Length / 2);
                Array.Copy(graph_buf_whole, 0, angle_graph_buf, 0, graph_buf_whole.Length / 2);

                ushort[] lowres_torque =
                {
                    16, 65, 18, 66, 71, 61, 58, 22, 64, 61, 60, 64, 76, 33, 67, 23, 74, 58, 57, 65, 59, 67, 58, 17, 62, 72, 68, 67, 64, 63, 70, 60, 65, 62, 23, 63, 60, 59, 17, 64, 22, 64, 19, 65, 21, 61, 19, 71, 23, 21, 17, 64, 55, 73, 70, 135, 59, 68, 66, 61, 35, 65, 67, 61, 64, 21, 70, 71, 132, 318, 591
                };
                ushort[] lowres_angle =
                {
                    16, 0, 4, 11, 19, 31, 46, 64, 84, 107, 135, 164, 196, 230, 265, 299, 333, 367, 401, 435, 469, 503, 537, 571, 604, 638, 672, 705, 739, 773, 807, 840, 874, 908, 942, 976, 1009, 1043, 1077, 1112, 1146, 1180, 1214, 1248, 1282, 1316, 1350, 1384, 1418, 1452, 1486, 1521, 1554, 1588, 1622, 1655, 1688, 1720, 1753, 1785, 1818, 1852, 1885, 1919, 1953, 1987, 2020, 2055, 2088, 2120, 2166
                };

                byte[] lowres_torque_asbytes = new byte[142];
                byte[] lowres_angle_asbytes = new byte[142];
                for (int i = 0; i < lowres_torque.Length; i++)
                {
                    ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(lowres_torque[i], lowres_torque_asbytes, i*2);
                    ModbusByteConversions.CopyUshortToBytesAsModbusBigendian(lowres_angle[i], lowres_angle_asbytes, i * 2);
                }

                Assert.ThrowsException<ArgumentException>( () => { new KducerTorqueAngleTimeGraph(lowres_torque_asbytes, lowres_angle_asbytes, new byte[122], new byte[123]); } );
                Assert.ThrowsException<ArgumentException>(() => { new KducerTorqueAngleTimeGraph(new byte[140], lowres_angle_asbytes, new byte[122], new byte[122]); });
                Assert.ThrowsException<ArgumentException>(() => { new KducerTorqueAngleTimeGraph(lowres_angle_asbytes, new byte[140], new byte[122], new byte[122]); });
                Assert.ThrowsException<ArgumentNullException>(() => { new KducerTorqueAngleTimeGraph(null, lowres_angle_asbytes, new byte[122], new byte[122]); });
                Assert.ThrowsException<ArgumentNullException>(() => { new KducerTorqueAngleTimeGraph(lowres_angle_asbytes, null, new byte[122], new byte[122]); });
                Assert.ThrowsException<ArgumentNullException>(() => { new KducerTorqueAngleTimeGraph(lowres_torque_asbytes, lowres_angle_asbytes, null, new byte[122]); });
                Assert.ThrowsException<ArgumentNullException>(() => { new KducerTorqueAngleTimeGraph(lowres_torque_asbytes, lowres_angle_asbytes, new byte[122], null); });

                KducerTorqueAngleTimeGraph ta = new KducerTorqueAngleTimeGraph(lowres_torque_asbytes, lowres_angle_asbytes, torque_graph_buf, angle_graph_buf);

                ushort[] lowres_torque_nointerval =
                {
                    65, 18, 66, 71, 61, 58, 22, 64, 61, 60, 64, 76, 33, 67, 23, 74, 58, 57, 65, 59, 67, 58, 17, 62, 72, 68, 67, 64, 63, 70, 60, 65, 62, 23, 63, 60, 59, 17, 64, 22, 64, 19, 65, 21, 61, 19, 71, 23, 21, 17, 64, 55, 73, 70, 135, 59, 68, 66, 61, 35, 65, 67, 61, 64, 21, 70, 71, 132, 318, 591
                };
                ushort[] lowres_angle_nointerval =
                {
                    0, 4, 11, 19, 31, 46, 64, 84, 107, 135, 164, 196, 230, 265, 299, 333, 367, 401, 435, 469, 503, 537, 571, 604, 638, 672, 705, 739, 773, 807, 840, 874, 908, 942, 976, 1009, 1043, 1077, 1112, 1146, 1180, 1214, 1248, 1282, 1316, 1350, 1384, 1418, 1452, 1486, 1521, 1554, 1588, 1622, 1655, 1688, 1720, 1753, 1785, 1818, 1852, 1885, 1919, 1953, 1987, 2020, 2055, 2088, 2120, 2166
                };

                Assert.IsTrue(lowres_torque_nointerval.SequenceEqual(ta.getTorqueSeries(false)));
                Assert.IsTrue(lowres_angle_nointerval.SequenceEqual(ta.getAngleSeries(false)));

                ushort[] highres_torque =
                {
                    4,5,15,8,9,9,10,17,20,65,10,12,15,12,9,5,17,10,13,16,14,18,8,4,13,5,6,9,11,15,17,17,19,9,6,8,8,5,14,9,9,14,17,66,10,11,60,60,5,71,17,15,9,11,61,5,13,17,62,21,15,16,11,11,17,5,2,16,13,8,17,10,23,11,11,5,61,11,14,7,5,12,6,6,19,10,17,13,8,10,10,13,14,58,16,7,20,17,11,14,5,12,4,19,5,13,22,22,9,5,14,10,12,1,16,11,8,16,16,19,3,64,4,5,5,15,13,64,6,10,61,9,26,8,5,16,8,18,17,9,2,58,3,11,1,60,5,19,12,17,11,20,18,7,20,8,8,4,18,5,64,17,6,11,11,6,64,8,17,11,19,16,5,12,6,16,13,8,11,7,23,19,17,14,13,11,11,8,12,7,76,70,11,16,4,11,12,13,5,3,5,17,6,11,9,33,16,3,11,13,61,6,12,13,67,11,66,17,12,5,8,5,3,10,15,12,23,14,22,11,11,6,6,13,11,11,9,12,14,8,60,12,15,1,11,11,13,5,15,22,74,8,12,9,11,0,4,58,9,15,5,9,10,14,17,58,17,5,7,10,16,9,11,7,8,7,7,6,22,13,16,17,7,6,6,15,57,7,65,19,8,6,16,10,10,6,11,6,5,3,5,6,19,10,14,59,11,12,5,8,15,15,11,12,22,1,5,7,8,3,9,67,8,16,14,8,19,1,7,16,11,11,1,11,15,11,11,11,9,10,10,10,1,5,8,18,58,10,4,6,16,5,6,8,11,14,16,9,3,11,17,17,6,6,14,4,8,13,22,14,7,9,17,9,6,62,17,11,23,17,13,5,9,11,7,5,57,12,12,72,11,14,14,7,58,12,56,58,7,10,5,5,18,7,9,7,7,11,9,15,68,19,4,57,57,9,5,1,8,17,14,11,14,3,9,5,5,5,9,67,7,26,11,16,64,11,5,5,5,0,5,64,11,8,11,12,11,10,6,10,3,63,7,60,10,12,9,16,12,10,15,7,17,7,17,21,70,10,17,17,59,13,8,19,14,9,5,6,6,6,60,17,21,12,17,10,11,16,1,5,4,1,8,15,11,9,19,64,17,17,12,60,6,61,2,5,11,16,14,65,11,3,4,6,5,5,12,18,9,12,14,4,62,7,8,14,7,6,3,12,16,17,20,16,11,7,7,7,11,5,9,15,23,17,8,16,15,4,3,14,17,7,5,4,5,4,14,63,3,3,18,60,13,5,4,21,4,60,14,8,9,9,11,4,13,9,12,1,10,10,11,7,5,56,14,17,17,6,10,13,8,59,5,9,10,17,11,10,0,3,5,2,11,17,6,16,10,11,9,13,13,58,56,1,11,56,11,64,19,5,10,7,10,8,9,14,10,11,14,13,17,10,19,4,12,0,6,9,9,22,18,10,11,13,11,5,12,16,5,18,10,17,19,64,5,9,6,0,6,12,9,14,13,15,3,5,16,5,5,19,9,12,13,12,6,10,11,12,2,65,9,11,19,11,9,11,3,12,7,15,13,14,16,12,14,12,15,8,8,21,8,8,6,5,10,61,11,13,58,8,5,15,25,6,17,7,18,11,11,3,7,13,11,11,16,19,17,17,17,14,7,3,10,5,4,3,13,14,65,12,12,8,4,4,9,14,6,71,11,20,16,5,6,8,6,4,7,6,9,23,15,17,8,2,10,5,7,4,11,17,14,21,16,11,17,14,14,8,4,6,11,13,11,15,14,12,13,7,9,11,9,5,17,16,15,8,17,11,7,17,12,13,12,10,57,64,4,11,5,11,5,24,10,17,15,4,5,7,17,54,6,11,1,18,10,17,4,55,10,11,15,11,11,62,73,23,16,17,5,58,58,14,13,29,20,68,21,15,9,12,64,11,7,9,11,24,20,36,39,13,14,48,47,70,43,94,60,135,88,87,41,58,41,31,19,64,63,9,64,28,5,11,9,17,20,17,10,59,59,17,29,9,19,28,18,4,21,26,17,13,22,10,16,6,15,12,6,61,68,11,14,10,8,19,19,6,5,9,5,66,29,5,7,19,6,11,6,11,8,9,5,14,14,14,19,10,12,58,5,10,5,3,61,21,35,27,11,12,16,12,19,7,6,6,15,19,17,15,6,6,12,13,15,5,59,21,6,29,9,17,19,65,8,29,11,17,13,67,24,11,1,15,23,6,14,21,18,11,15,17,17,13,10,19,61,34,14,12,8,18,16,11,15,17,14,18,6,3,18,16,27,16,10,9,11,14,14,64,10,18,23,17,16,11,11,12,7,10,14,15,5,6,11,17,17,10,13,5,14,21,21,16,70,7,15,7,24,14,5,70,19,21,13,23,66,20,13,18,11,19,9,11,22,36,28,22,18,22,71,17,28,19,19,29,51,56,23,32,47,76,85,79,48,99,63,117,132,106,108,101,120,174,130,141,220,177,244,259,260,243,253,255,311,318,358,339,305,358,330,347,410,433,371,409,455,591
                };
                ushort[] highres_angle =
                {
                    0,0,0,0,0,0,0,0,0,0,0,1,1,1,1,1,2,2,2,3,3,3,3,3,4,4,5,5,6,6,6,7,7,8,9,9,9,10,10,11,11,12,12,13,13,13,14,15,15,16,16,17,17,18,19,19,20,21,21,22,22,23,24,25,25,26,27,28,29,30,30,31,32,33,34,35,36,37,38,39,39,41,42,42,43,44,45,46,47,48,49,51,52,53,54,55,56,57,58,60,61,62,63,64,65,67,68,69,70,71,72,74,75,76,77,79,80,81,83,84,85,87,88,89,91,92,93,95,96,98,99,101,102,104,105,107,109,110,112,113,115,117,118,120,122,124,125,127,129,130,132,134,136,138,139,141,143,145,147,149,150,152,154,156,158,160,162,163,165,167,169,171,173,175,177,179,181,183,185,187,189,191,193,196,198,200,202,204,206,208,210,213,215,217,219,221,223,225,228,230,232,234,236,238,241,243,245,247,249,251,253,256,258,260,262,264,266,268,271,273,275,277,279,281,283,285,288,290,292,294,296,298,300,303,305,307,309,311,313,315,318,320,322,324,326,328,330,333,334,337,339,341,343,345,347,350,352,354,356,358,360,362,364,367,369,371,373,375,377,379,382,384,386,388,390,392,394,396,398,401,403,405,407,409,411,413,415,418,420,422,424,426,428,430,433,435,437,439,441,443,445,447,450,451,454,456,458,460,462,464,466,469,471,473,475,477,479,481,483,485,487,490,492,494,496,498,500,502,504,507,509,511,513,515,517,519,521,523,526,528,530,532,534,536,538,540,543,544,547,549,551,553,555,557,559,562,564,566,568,570,572,574,576,578,581,583,585,587,589,591,593,595,597,600,601,604,606,608,610,612,614,616,618,620,623,625,627,629,631,633,635,637,639,642,644,646,648,650,652,654,656,658,661,663,665,667,669,671,673,675,678,679,682,684,686,688,690,692,694,696,698,700,703,705,707,709,711,713,715,717,719,721,724,726,728,730,732,734,736,739,741,743,745,747,749,751,753,755,757,760,762,764,766,768,770,772,774,776,778,781,783,785,787,789,791,793,795,798,800,802,804,806,808,810,813,814,816,819,821,823,825,827,829,831,833,835,838,840,842,844,846,848,850,852,855,857,859,861,863,865,867,869,871,874,876,878,880,882,884,886,888,891,893,895,897,899,901,903,905,907,909,912,914,916,918,920,922,924,926,928,931,933,935,937,939,941,943,945,948,950,952,954,956,958,960,962,964,967,969,971,973,975,977,979,981,983,986,988,990,992,994,996,998,1000,1002,1005,1007,1009,1011,1013,1015,1017,1020,1021,1024,1026,1028,1030,1032,1034,1036,1039,1041,1043,1045,1047,1049,1051,1053,1056,1057,1060,1062,1064,1066,1068,1071,1073,1075,1077,1079,1081,1083,1085,1087,1090,1092,1094,1096,1098,1100,1102,1105,1107,1109,1111,1113,1115,1117,1119,1122,1124,1126,1128,1130,1132,1134,1137,1139,1141,1143,1145,1147,1149,1152,1153,1156,1158,1160,1162,1164,1166,1169,1171,1173,1175,1177,1179,1181,1183,1185,1188,1190,1192,1194,1196,1198,1201,1203,1205,1207,1209,1211,1213,1216,1218,1220,1222,1224,1226,1228,1230,1233,1235,1237,1239,1241,1243,1246,1248,1250,1252,1254,1256,1258,1260,1262,1265,1267,1269,1271,1273,1275,1278,1280,1282,1284,1286,1288,1290,1292,1294,1297,1299,1301,1303,1305,1307,1309,1312,1314,1316,1318,1320,1322,1324,1327,1329,1331,1333,1335,1337,1339,1341,1343,1345,1348,1350,1352,1354,1356,1358,1360,1362,1365,1367,1369,1371,1373,1375,1377,1380,1381,1384,1386,1388,1390,1392,1394,1396,1399,1401,1403,1405,1407,1409,1411,1413,1416,1418,1420,1422,1424,1426,1429,1431,1433,1435,1437,1439,1441,1443,1445,1448,1450,1452,1454,1456,1458,1460,1462,1465,1467,1469,1471,1473,1475,1477,1479,1482,1484,1486,1488,1490,1492,1494,1497,1499,1501,1503,1505,1507,1510,1512,1514,1516,1518,1520,1522,1524,1526,1529,1530,1533,1535,1537,1539,1541,1543,1545,1548,1550,1552,1554,1556,1558,1560,1562,1564,1566,1569,1571,1573,1575,1577,1579,1581,1583,1586,1588,1590,1592,1594,1596,1598,1600,1603,1605,1607,1609,1611,1613,1615,1617,1619,1621,1623,1625,1628,1630,1632,1634,1636,1638,1640,1642,1644,1647,1648,1651,1653,1655,1657,1659,1661,1663,1665,1667,1669,1671,1673,1675,1677,1679,1681,1683,1685,1687,1689,1691,1693,1695,1697,1699,1701,1703,1705,1707,1709,1711,1713,1715,1717,1719,1721,1723,1725,1728,1730,1731,1734,1736,1738,1740,1742,1744,1746,1748,1750,1752,1754,1756,1758,1760,1762,1764,1766,1769,1770,1772,1774,1776,1779,1781,1783,1785,1787,1789,1791,1793,1795,1797,1799,1801,1803,1805,1807,1810,1812,1814,1816,1818,1820,1822,1824,1826,1828,1830,1833,1834,1837,1839,1841,1843,1845,1847,1849,1851,1853,1855,1857,1859,1862,1864,1866,1868,1870,1872,1874,1876,1878,1880,1882,1885,1887,1889,1891,1893,1895,1897,1899,1902,1903,1906,1908,1910,1912,1914,1916,1918,1920,1923,1925,1927,1929,1931,1933,1935,1937,1939,1942,1944,1946,1948,1950,1952,1954,1956,1958,1960,1963,1965,1967,1969,1971,1973,1975,1978,1980,1982,1984,1986,1988,1990,1992,1994,1997,1999,2001,2003,2005,2007,2009,2011,2013,2016,2018,2020,2022,2024,2026,2028,2031,2033,2035,2037,2039,2041,2043,2046,2048,2050,2052,2054,2056,2058,2060,2062,2065,2067,2069,2071,2073,2075,2077,2079,2081,2083,2086,2088,2090,2092,2094,2096,2098,2100,2102,2104,2106,2108,2110,2112,2114,2116,2118,2120,2122,2124,2126,2128,2130,2131,2133,2135,2137,2139,2140,2142,2144,2146,2147,2149,2151,2152,2154,2166
                };

                Assert.IsTrue(highres_torque.SequenceEqual(ta.getTorqueSeries(true)));
                Assert.IsTrue(highres_angle.SequenceEqual(ta.getAngleSeries(true)));
                Assert.IsTrue(String.Join(',',highres_torque).SequenceEqual(ta.getTorqueSeriesAsCsv(true)));
                Assert.IsTrue(String.Join(',', highres_angle).SequenceEqual(ta.getAngleSeriesAsCsv(true)));
            }
        }

        [TestClass]
        public class KducerKduDataFileReaderTests
        {
            [TestMethod]
            public async Task TestReadFile()
            {
                await ReadFileTests("../../../v40 file.kdu", 40);
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
                else if (kduVersion == 38)
                    settingsbytesRead = data.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38();
                else
                    settingsbytesRead = data.Item1.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39();
                for (int i = 0; i < settingsbytesRead.Length; i++)
                {
                    Assert.AreEqual(settingsbytes[i], settings.getGeneralSettingsModbusHoldingRegistersAsByteArray()[i]);
                    if (settingsbytes[i] != settingsbytesRead[i])
                        Console.WriteLine($"built[{i}]={settingsbytes[i]}, fromFile[{i}]={settingsbytesRead[i]}");
                }
                if (kduVersion == 37)
                    settingsbytes = settings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv37andPrior();
                else if (kduVersion == 38)
                    settingsbytes = settings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv38();
                else
                    settingsbytes = settings.getGeneralSettingsModbusHoldingRegistersAsByteArray_KDUv39();
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

    [TestClass]
    public class ToolInHandTests
    {
        [TestClass]
        public class ReducedModbusTcpClientTests
        {
            [TestMethod]
            [Timeout(10000)]
            public async Task TestPullCableFromKdu()
            {
                using ReducedModbusTcpClientAsync kdu = new ReducedModbusTcpClientAsync(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP));
                await kdu.ConnectAsync();
                Assert.IsTrue(kdu.Connected());
                System.Diagnostics.Debug.WriteLine("Pull the cord in the next 5 seconds");
                await Task.Delay(5000);
                System.Diagnostics.Debug.WriteLine("Now checking if Exceptions are thrown");
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

                System.Diagnostics.Debug.WriteLine("Run the screwdriver...");
                await Task.Delay(5000);
                Assert.IsTrue(kdu.HasNewResult());

                kdu.ClearResultsQueue();
                Assert.IsFalse(kdu.HasNewResult());
            }

            [TestMethod]
            [Timeout(20000)]
            public async Task TestGetResultAfterManuallyRunScrewdriver()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);

                KducerTighteningProgram pr = new KducerTighteningProgram();
                pr.SetTorqueAngleMode(1);
                pr.SetAngleTarget(1000);
                await kdu.SendNewProgramDataAsync(await kdu.GetProgramNumberAsync(), pr);

                System.Diagnostics.Debug.WriteLine("Manually run screwdriver until result...");
                KducerTighteningResult res = await kdu.GetResultAsync(CancellationToken.None);
                System.Diagnostics.Debug.WriteLine(res.GetResultsAsCSVstringSingleLine());

                if (await kdu.GetKduMainboardVersionAsync() >= 40)
                    await kdu.SetHighResGraphModeAsync(true);
                System.Diagnostics.Debug.WriteLine("Manually run screwdriver until result...");
                res = await kdu.GetResultAsync(CancellationToken.None);
                System.Diagnostics.Debug.WriteLine(res.GetResultsAsCSVstringSingleLine());
            }

            [TestMethod]
            [Timeout(20000)]
            public async Task TestPullCableFromKdu()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
                Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
                Assert.IsTrue(kdu.IsConnected());
                System.Diagnostics.Debug.WriteLine("Pull the cord in the next 5 seconds");
                await Task.Delay(5000);
                System.Diagnostics.Debug.WriteLine("Now checking if exceptions are thrown");
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
                System.Diagnostics.Debug.WriteLine("Pull the cord in the next 5 seconds"); 
                await Task.Delay(5000);
                Assert.IsFalse(kdu.IsConnected());
                Assert.IsFalse(kdu.IsConnectedWithTimeoutBlocking(250));
                Assert.IsFalse(await kdu.IsConnectedWithTimeoutAsync(250));
                System.Diagnostics.Debug.WriteLine("Plug the cord back in");
                Assert.IsTrue(kdu.IsConnectedWithTimeoutBlocking(5000));
                Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
                Assert.IsTrue(kdu.IsConnected());
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
                    System.Diagnostics.Debug.WriteLine($"Look at the barcode, should be {barcode}");
                    await kdu.SendBarcodeAsync(barcode);
                    await Task.Delay(1000);
                }
            }

            [TestMethod]
            public async Task TestKtlsPositions()
            {
                using Kducer kdu = new Kducer(IPAddress.Parse(TestConstants.REAL_LIVE_KDU_IP), NullLoggerFactory.Instance);
                Assert.IsTrue(await kdu.IsConnectedWithTimeoutAsync(500));
                Assert.IsTrue(kdu.IsConnected());
                for (int i = 0; i < 100; i++)
                {
                    Tuple<ushort[], ushort[], ushort[]> ktls = await kdu.GetKtlsPositionsAsync();
                    System.Diagnostics.Debug.WriteLine($"Positions: {ktls.Item1[0]},{ktls.Item1[1]},{ktls.Item1[2]},{ktls.Item2[0]},{ktls.Item2[1]},{ktls.Item2[2]}; Target: {ktls.Item3[0]},{ktls.Item3[1]},{ktls.Item3[2]}");
                    await Task.Delay(100);
                }
                
            }
        }
    }
}