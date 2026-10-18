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
    /// Phase-1 tests for the transaction-level Norsk Data I/O bus (NDMicroCpu.Core/NDBus).
    /// Exercises the whole INDBusDevice contract end to end against a fake DMA device:
    /// IOX register read/write dispatch, DMA into real machine memory through ISystemBus,
    /// a command that completes a few Clock() ticks later and raises its interrupt, and
    /// IDENT returning the code + clearing the interrupt. No microcode is involved here -
    /// the bus is driven directly, so this validates the seam itself.
    /// </summary>
    public class TestNDBus
    {
        // A minimal DMA device: claims an 8-register octal range, and on the "GO" write
        // DMAs a known pattern into memory at a previously loaded word address, then
        // (after a delay) asserts level 11. Models the shape of NDBusFloppyDMA without its
        // full command-block protocol - enough to prove the bus contract.
        private sealed class FakeDmaDevice : INDBusDevice
        {
            public const int Base = 0x370;          // arbitrary IOX register base
            public const int IdentCode = 0x11;      // octal 021, like the floppy
            public const int Level = 11;            // mass-storage level
            public const ushort Pattern = 0xBEEF;

            private readonly ISystemBus _bus;
            private ushort _targetWord;             // word address loaded via a register write
            private int _delay = -1;                // >=0 = transfer in flight
            private int _interruptBits = 0;
            public int LastStatusReads;

            public FakeDmaDevice(ISystemBus bus) { _bus = bus; }

            public bool Claims(int ioxAddress) => ioxAddress >= Base && ioxAddress < Base + 8;

            public ushort Read(int ioxAddress)
            {
                LastStatusReads++;
                // +0 status: bit 0 = transfer complete (interrupt pending)
                if (ioxAddress == Base + 0) return (ushort)(_interruptBits != 0 ? 1 : 0);
                return 0;
            }

            public void Write(int ioxAddress, ushort data)
            {
                if (ioxAddress == Base + 1) _targetWord = data;   // load target word address
                else if (ioxAddress == Base + 3) StartTransfer();  // GO
            }

            private void StartTransfer()
            {
                // Immediate DMA of the pattern into memory (word address -> byte<<1 done
                // by the device, matching NDBusFloppyDMA's convention).
                _bus.WriteMemory16((uint)_targetWord << 1, Pattern);
                _delay = 3; // completes 3 Clock() ticks later
            }

            public void Clock()
            {
                if (_delay < 0) return;
                if (_delay == 0) { _interruptBits |= (1 << Level); _delay = -1; }
                else _delay--;
            }

            public int InterruptBits => _interruptBits;

            public int Ident(int level)
            {
                if (level == Level && _interruptBits != 0)
                {
                    _interruptBits &= ~(1 << Level); // acknowledged
                    return IdentCode;
                }
                return -1;
            }
        }

        private static void Quiet()
        {
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        [Test]
        public void BareBus_HasNoDevices_AndIoxIsUnclaimed()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            Assert.That(cpu.iobus, Is.Not.Null, "CPU must construct an I/O bus");
            Assert.That(cpu.iobus.DeviceCount, Is.EqualTo(0), "a fresh bus has no devices");

            bool claimed = cpu.iobus.Iox(FakeDmaDevice.Base, isWrite: false, writeData: 0, out ushort _);
            Assert.That(claimed, Is.False, "no device => IOX unclaimed (caller raises IOX error)");
            Assert.That(cpu.iobus.PendingInterruptBits(), Is.EqualTo(0));
            Assert.That(cpu.iobus.Ident(11), Is.EqualTo(-1), "no device answers IDENT");
        }

        [Test]
        public void Iox_DispatchesReadAndWrite_ToClaimingDevice()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var dev = new FakeDmaDevice(cpu.iobus.Dma);
            cpu.iobus.Attach(dev);

            // Write a register, then read status back through the bus.
            bool wrote = cpu.iobus.Iox(FakeDmaDevice.Base + 1, isWrite: true, writeData: 0x0200, out ushort _);
            Assert.That(wrote, Is.True, "device claims its range on write");

            bool read = cpu.iobus.Iox(FakeDmaDevice.Base + 0, isWrite: false, writeData: 0, out ushort status);
            Assert.That(read, Is.True, "device claims its range on read");
            Assert.That(status, Is.EqualTo(0), "no transfer yet => status 0");
            Assert.That(dev.LastStatusReads, Is.EqualTo(1), "read reached the device");
        }

        [Test]
        public void Device_DmaWritesRealMemory_ThroughSystemBus()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var dev = new FakeDmaDevice(cpu.iobus.Dma);
            cpu.iobus.Attach(dev);

            const ushort targetWord = 0x0200; // octal 01000, safe low RAM
            // (RAM is not guaranteed zero on a fresh CPU - control-store/clear leave data -
            // so we just prove the DMA changed the word to the exact pattern.)
            int before = cpu.memory.ReadPhysicalMemory((uint)targetWord);

            cpu.iobus.Iox(FakeDmaDevice.Base + 1, isWrite: true, writeData: targetWord, out ushort _); // load address
            cpu.iobus.Iox(FakeDmaDevice.Base + 3, isWrite: true, writeData: 1, out ushort _);           // GO

            int after = cpu.memory.ReadPhysicalMemory((uint)targetWord);
            Assert.That(after, Is.EqualTo(FakeDmaDevice.Pattern),
                $"the device DMA'd the pattern into physical memory via ISystemBus (was {before})");
        }

        [Test]
        public void Transfer_RaisesInterrupt_AfterClockDelay_AndIdentClearsIt()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var dev = new FakeDmaDevice(cpu.iobus.Dma);
            cpu.iobus.Attach(dev);

            cpu.iobus.Iox(FakeDmaDevice.Base + 1, isWrite: true, writeData: 0x0200, out ushort _);
            cpu.iobus.Iox(FakeDmaDevice.Base + 3, isWrite: true, writeData: 1, out ushort _); // GO, delay=3

            Assert.That(cpu.iobus.PendingInterruptBits(), Is.EqualTo(0), "interrupt not raised immediately");

            for (int i = 0; i < 4; i++) cpu.iobus.Clock(); // let the delay elapse

            int bits = cpu.iobus.PendingInterruptBits();
            Assert.That((bits & (1 << FakeDmaDevice.Level)) != 0, Is.True,
                "level-11 interrupt asserted after the transfer completes");

            // Status register now reports complete.
            cpu.iobus.Iox(FakeDmaDevice.Base + 0, isWrite: false, writeData: 0, out ushort status);
            Assert.That(status, Is.EqualTo(1), "status bit 0 = transfer complete");

            // IDENT at level 11 returns the code and clears the interrupt.
            int code = cpu.iobus.Ident(FakeDmaDevice.Level);
            Assert.That(code, Is.EqualTo(FakeDmaDevice.IdentCode), "IDENT returns the device code");
            Assert.That(cpu.iobus.PendingInterruptBits(), Is.EqualTo(0), "IDENT cleared the interrupt");
            Assert.That(cpu.iobus.Ident(FakeDmaDevice.Level), Is.EqualTo(-1), "second IDENT: nothing pending");
        }

        [Test]
        public void CpuTick_ClocksBus_AndFoldsInterruptIntoPic()
        {
            Quiet();
            CPU cpu = new CPU(null, CPUType.ND110);
            var dev = new FakeDmaDevice(cpu.iobus.Dma);
            cpu.iobus.Attach(dev);

            cpu.iobus.Iox(FakeDmaDevice.Base + 1, isWrite: true, writeData: 0x0200, out ushort _);
            cpu.iobus.Iox(FakeDmaDevice.Base + 3, isWrite: true, writeData: 1, out ushort _); // GO

            // TickCpu() must clock the bus each cycle; after enough ticks the device's
            // interrupt is folded into the PIC (INT11 -> pic detect / request pin).
            bool irqSeen = false;
            for (int i = 0; i < 50 && !irqSeen; i++)
            {
                cpu.TickCpu();
                if (dev.InterruptBits != 0) irqSeen = true;
            }
            Assert.That(irqSeen, Is.True, "device interrupt asserted while the CPU was ticking the bus");
            // The PIC should now be requesting (the fold called SetInternalInterrupt(INT11)).
            Assert.That(cpu.hw.pic.ReadInterruptRequestPin, Is.False,
                "PIC interrupt-request pin asserted (active low) after the bus folded INT11");
        }
    }
}
