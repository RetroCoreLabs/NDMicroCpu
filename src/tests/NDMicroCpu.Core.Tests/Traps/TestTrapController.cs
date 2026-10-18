//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Traps
{
    /// <summary>
    /// Shared trap-vector dispatch rules, tested against BOTH CPU types (the
    /// TrapController lives in the shared CpuInternals, so a regression hits ND-110 and
    /// ND-120 identically):
    ///
    ///  * Vectors 0x00-0x0D are IMMEDIATE traps: dispatched on the very next micro
    ///    cycle regardless of the FETCH flag (Cpu.cs vector switch, default arm).
    ///  * Vectors 0x0E (panel) and 0x0F (macro interrupt) are INTERRUPTS: they must
    ///    wait for the FETCH boundary - the real ND-100/110/120 combines all
    ///    asynchronous device interrupts into the single PIC-driven request line and
    ///    services them only between macro instructions.
    ///
    /// This is the direct regression suite for the 2026-07 TrapController FETCH-gating
    /// bug (device INT10-13 pins used to map straight to vectors 0x0A-0x0D and forced a
    /// microcode jump on EVERY micro-cycle the pin was asserted).
    /// </summary>
    public class TestTrapController
    {
        static CPU CreateCpu(CPUType cpuType)
        {
            CPU cpu = new CPU(null, cpuType);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
            return cpu;
        }

        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void ImmediateTrap_FiresRegardlessOfFetch(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);

            // Mid-instruction state: FETCH is false right after construction.
            Assert.That(cpu.hw.mac.FETCH, Is.False, "precondition: not at a FETCH boundary");

            cpu.hw.SetMemoryProtectionTrap();

            int vector = cpu.hw.GetTrapVector();
            Assert.That(vector, Is.EqualTo(NDMicroCpu.Core.TrapController.TrapVectors.MEMORY_PROTECT),
                $"{cpuType}: memory-protect must be pending as vector 0x02 immediately, FETCH or not");

            cpu.hw.ClearTrapVector(vector);
            Assert.That(cpu.hw.GetTrapVector(), Is.EqualTo(-1), "trap must clear");
        }

        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void ImmediateTraps_LowerVectorWins(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);

            cpu.hw.SetArithmeticOverflowTrap();                     // 0x05
            cpu.hw.SetPageFaultTrap();                              // 0x01
            Assert.That(cpu.hw.GetTrapVector(),
                Is.EqualTo(NDMicroCpu.Core.TrapController.TrapVectors.PAGE_FAULT),
                $"{cpuType}: lower vector number = higher priority");

            cpu.hw.ClearTrapVector(NDMicroCpu.Core.TrapController.TrapVectors.PAGE_FAULT);
            Assert.That(cpu.hw.GetTrapVector(),
                Is.EqualTo(NDMicroCpu.Core.TrapController.TrapVectors.ARITHMETIC_OVERFLOW),
                $"{cpuType}: remaining trap surfaces after the higher-priority one clears");
            cpu.hw.ClearTrapVector(NDMicroCpu.Core.TrapController.TrapVectors.ARITHMETIC_OVERFLOW);
        }

        /// <summary>
        /// The FETCH-gating regression itself: a device-interrupt pin (here INT12, the
        /// console RX line) must NOT hijack the microcode while an instruction is
        /// executing (FETCH low). It surfaces as the single PIC-driven vector 0x0F and
        /// only at the next FETCH boundary - never as an immediate vector 0x0A-0x0D.
        /// </summary>
        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void DeviceInterruptPin_WaitsForFetchBoundary(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);

            // Raise a device interrupt pin directly on the PIC (the same call the UART
            // RX path makes via CpuInternals.SetInternalInterrupt). Note: with the PIC's
            // mask/enable flip-flops in their reset state the request line stays
            // inactive - which is itself part of the contract: a pin alone must not
            // create a trap vector.
            cpu.hw.pic.SetInterruptPIN(InterruptPin.INT12);

            int vector = cpu.hw.GetTrapVector();
            Assert.That(vector, Is.Not.InRange(0x0A, 0x0D),
                $"{cpuType}: device pins must never surface as immediate vectors 0x0A-0x0D " +
                "(the 2026-07 FETCH-gating bug) - they are PIL/priority inputs to the PIC only");

            // Whatever pends may only be the interrupt-class vector, and the dispatch
            // switch in Cpu.cs must hold it until FETCH:
            if (vector >= 0)
            {
                Assert.That(vector, Is.EqualTo(0x0F).Or.EqualTo(0x0E),
                    $"{cpuType}: a raised device pin may only pend as an interrupt-class vector");
            }

            // Run a couple of microinstructions mid-instruction (FETCH false): the CPU
            // must stay on its normal microcode path, not jump to a trap vector address.
            cpu.hw.mic.LatchMap(0x100); // arbitrary non-vector microcode address
            ushort before = cpu.hw.mic.map;
            cpu.TickCpu();
            Assert.That(cpu.hw.mic.map, Is.Not.InRange(0x0A, 0x0F),
                $"{cpuType}: microcode must not be forced to a vector address mid-instruction " +
                $"(was at {Utils.Int2Oct(before)}, now {Utils.Int2Oct(cpu.hw.mic.map)})");
        }

        /// <summary>
        /// Sticky-bitmap regression (commit b84f1d2 family): vector 0x0F tracks the LIVE
        /// /IRQ line - once the PIC stops requesting, a previously latched 0x0F must not
        /// survive in either the controller or the legacy bitmap.
        /// </summary>
        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void MacroInterruptVector_TracksLiveIrq(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);

            // Latch 0x0F by hand (as the FETCH-path does when /IRQ is low)...
            cpu.hw.SetTrap(0x0F);
            // ...but the live /IRQ line is INACTIVE (PIC reset), so the very next
            // GetTrapVector must have cleared it again - no stale dispatch.
            int vector = cpu.hw.GetTrapVector();
            Assert.That(vector, Is.EqualTo(-1),
                $"{cpuType}: vector 0x0F must track the live /IRQ line, not stick in the bitmap");
            Assert.That(cpu.hw.TrapVector & (1 << 0x0F), Is.EqualTo(0),
                $"{cpuType}: the legacy TrapVector bitmap must be synced (no resurrected 0x0F)");
        }
    }
}
