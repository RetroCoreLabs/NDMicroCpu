//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.NDBus;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.NDBusTests
{
    /// <summary>
    /// Phase-2 tests for the ported floppy DMA controller (NdFloppyDma). Drives it exactly
    /// like the ND-100 LOAD microcode / SINTRAN BFDIS driver would over IOX: program a
    /// command block in memory, load the pointer, write the GO control word, and check that
    /// the controller DMAs the diskette sectors into memory, writes back the status block,
    /// and raises level 11 after its completion delay. Also covers the autoload (boot) path.
    /// </summary>
    public class TestNdFloppyDma
    {
        private const int FloppyBase = 0x370;    // octal 1560 = 0x370
        private const int IdentOct21 = 17;       // octal 021

        private static void Quiet()
        {
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        // A ramp image so each word is predictable: byte i = i & 0xFF.
        private static byte[] RampImage(int size)
        {
            byte[] img = new byte[size];
            for (int i = 0; i < size; i++) img[i] = (byte)(i & 0xFF);
            return img;
        }

        [Test]
        public void FloppyClaimsItsOctalRange_1560_to_1567()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var f = new NdFloppyDma(0, cpu.iobus.Dma, new ByteArrayFloppyMedia(new byte[512]));
            Assert.That(f.DeviceBaseAddress, Is.EqualTo(FloppyBase), "thumbwheel 0 -> octal 1560");
            Assert.That(f.Claims(FloppyBase), Is.True);
            Assert.That(f.Claims(FloppyBase + 7), Is.True);
            Assert.That(f.Claims(FloppyBase + 8), Is.False);
            Assert.That(f.InterruptLevel, Is.EqualTo(11));
        }

        [Test]
        public void ReadData_CommandBlock_DmasSectorsIntoMemory_AndRaisesLevel11()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            byte[] image = RampImage(2048);
            var floppy = new NdFloppyDma(0, cpu.iobus.Dma, new ByteArrayFloppyMedia(image));
            cpu.iobus.Attach(floppy);

            const uint cb = 0x100;         // command-block word address
            const uint target = 0x200;     // where the data lands
            const int words = 4;

            // Build the 12-word command block (see NdFloppyDma / ND-11.021.1 6.3).
            cpu.memory.WritePhysicalMemory(cb + 0, 0x0000);        // cmd: ReadData, drive 0, format 0 (512 B/s)
            cpu.memory.WritePhysicalMemory(cb + 1, 0x0000);        // disk address (logical sector 0)
            cpu.memory.WritePhysicalMemory(cb + 2, 0x0000);        // mem addr hi
            cpu.memory.WritePhysicalMemory(cb + 3, (ushort)target);// mem addr lo
            cpu.memory.WritePhysicalMemory(cb + 4, 0x8000);        // bit15 = word-count mode
            cpu.memory.WritePhysicalMemory(cb + 5, words);         // word count

            // Program the controller over IOX, exactly like the driver.
            floppy.Write(FloppyBase + 5, 0);                       // pointer hi
            floppy.Write(FloppyBase + 7, (ushort)cb);             // pointer lo
            floppy.Write(FloppyBase + 3, (ushort)((1 << 8) | (1 << 1))); // ExecuteCommand + EnableInterrupt

            // Data is DMA'd synchronously on GO. Sector byte i = i, big-endian words.
            for (int i = 0; i < words; i++)
            {
                ushort expected = (ushort)((image[i * 2] << 8) | image[i * 2 + 1]);
                Assert.That(cpu.memory.ReadPhysicalMemory(target + (uint)i), Is.EqualTo(expected),
                    $"word {i} DMA'd from the diskette into memory");
            }

            // Status block written back at +6/+7; error-free -> no OR-of-errors bit.
            ushort status1 = cpu.memory.ReadPhysicalMemory(cb + 6);
            Assert.That((status1 & (1 << 4)), Is.EqualTo(0), "no error in status word 1");

            // Interrupt is deferred: not asserted until the completion delay elapses.
            Assert.That(floppy.InterruptBits, Is.EqualTo(0), "interrupt deferred right after GO");
            for (int i = 0; i < 400; i++) floppy.Clock();
            Assert.That((floppy.InterruptBits & (1 << 11)) != 0, Is.True, "level 11 asserted after completion");

            // IDENT returns octal 21 and clears the interrupt.
            Assert.That(floppy.Ident(11), Is.EqualTo(IdentOct21));
            Assert.That(floppy.InterruptBits, Is.EqualTo(0), "IDENT cleared the interrupt");
        }

        [Test]
        public void Autoload_Success_CallsHostAndCompletesWithoutError()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var floppy = new NdFloppyDma(0, cpu.iobus.Dma, new ByteArrayFloppyMedia(RampImage(2048)));
            cpu.iobus.Attach(floppy);

            bool serviced = false;
            floppy.OnAutoloadRequest = unit =>
            {
                serviced = true;
                // The host would parse BPUN + DMA it in; here just drop a marker and return a boot addr.
                cpu.memory.WritePhysicalMemory(0x50, 0x1234);
                return 0x50;
            };

            floppy.Write(FloppyBase + 3, (ushort)((1 << 2) | (1 << 1))); // ActivateAutoload + EnableInterrupt

            Assert.That(serviced, Is.True, "autoload asked the host to service the boot");
            Assert.That(cpu.memory.ReadPhysicalMemory(0x50), Is.EqualTo(0x1234), "boot image DMA'd in");

            for (int i = 0; i < 400; i++) floppy.Clock();
            Assert.That((floppy.InterruptBits & (1 << 11)) != 0, Is.True, "level 11 after autoload completes");
            ushort hw = floppy.Read(FloppyBase + 2);
            Assert.That((hw & (1 << 4)), Is.EqualTo(0), "no error after successful autoload");
        }

        [Test]
        public void MacroIox_ReadStatus_ReachesFloppyThroughMicrocode()
        {
            // End-to-end: a real macro IOX instruction, executed by the RASK microcode,
            // must reach the attached floppy through the COMM,IOX bus wiring and bring the
            // hardware status word back into A (via DBR). Boot first so the CPU is in a
            // state that runs the general-IOX microcode routine.
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            cpu.hw.mic.LatchMap(0);
            bool booted = false;
            for (int i = 0; i < 200000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == 0x449) Assert.Fail("Self-test failed (STERR)");
                if (cpu.hw.mac.FETCH) { booted = true; break; }
            }
            Assert.That(booted, Is.True, "boot reached FETCH");

            var floppy = new NdFloppyDma(0, cpu.iobus.Dma, new ByteArrayFloppyMedia(new byte[512]));
            cpu.iobus.Attach(floppy);

            // IOX 1562 (octal) = read floppy status register 1. Opcode 164000 | device 1562.
            ushort iox1562 = (ushort)(Convert.ToInt32("164000", 8) | Convert.ToInt32("1562", 8));
            const ushort at = 0x1000;
            cpu.memory.WritePhysicalMemory(at, iox1562);

            cpu.UnitTest_FetchAndExecute(at);

            ushort a = cpu.ReadALURegister(RegisterEnum.A);
            // The floppy hardware status word always has bit 15 (dual-density controller) set;
            // its arrival in A proves the whole macro-IOX -> microcode -> COMM,IOX -> device
            // -> DBR -> A path works.
            Assert.That((a & 0x8000) != 0, Is.True,
                $"floppy status (dual-density bit 15) returned into A via COMM,IOX; got {Utils.Int2Oct(a)}");
        }

        [Test]
        public void Autoload_NoBootstrap_SetsError50()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var floppy = new NdFloppyDma(0, cpu.iobus.Dma, new ByteArrayFloppyMedia(RampImage(2048)));
            cpu.iobus.Attach(floppy);
            floppy.OnAutoloadRequest = unit => -1; // no bootable image

            floppy.Write(FloppyBase + 3, (ushort)(1 << 2)); // ActivateAutoload (no interrupt)

            ushort hw = floppy.Read(FloppyBase + 2);
            Assert.That((hw & (1 << 4)) != 0, Is.True, "OR-of-errors set after no-bootstrap autoload");
        }
    }
}
