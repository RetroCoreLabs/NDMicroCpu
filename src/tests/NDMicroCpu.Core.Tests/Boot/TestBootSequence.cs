//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Boot
{
    /// <summary>
    /// Boots the CPU from microaddress 0 (master clear) exactly like real hardware:
    /// runs the built-in microcode self-test (SELFT/ST1-ST8), then the rest of master
    /// clear, and expects to land in the STOP2/MOPC "operator wait" state - the same
    /// place a real ND-110/120 sits when powered on with no program loaded.
    ///
    /// Self-test / MOPC addresses below were read directly out of the compiled listings
    /// (ND-110-RASK.LISTING.TXT / ND-120-DELILAH-L.LISTING.TXT), not guessed:
    ///   ND-110: SELFT=002006(0x406) STERR=002111(0x449) MACL2=002113(0x44B) MS20=002261(0x4B1) MRET1=002267(0x4B7)
    ///   ND-120: SELFT=002053(0x42B) STERR=002156(0x46E) MACL2=002160(0x470) MS20=002333(0x4DB) MRET1=002341(0x4E1)
    /// </summary>
    public class TestBootSequence
    {
        // RealTimeClock.TICK_PR_20MS - number of micro-cycles the emulated RTC counts
        // before firing the MS20 (20ms) panel interrupt. Kept here as a documented
        // constant since RealTimeClock's own field is private.
        const int TICK_PR_20MS = 10550 * 4;

        static CPU CreateCpu(CPUType cpuType)
        {
            // CPU already loads its own cpuType-appropriate microcode in its constructor.
            CPU cpu = new CPU(null, cpuType);

            // NB: LogDestination.Nats opens a real NatsClient and calls Send() synchronously
            // per log call (Logger.cs) - with no broker running that is extremely slow/blocking.
            // Existing tests use it at low iteration counts where the cost isn't noticeable;
            // these boot/self-test loops run 10^5-10^6 TickCpu() calls, so logging must stay off.
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            return cpu;
        }

        static ushort SterrAddress(CPUType cpuType) => cpuType == CPUType.ND110 ? (ushort)0x449 : (ushort)0x46E;
        static ushort Ms20Address(CPUType cpuType) => cpuType == CPUType.ND110 ? (ushort)0x4B1 : (ushort)0x4DB;

        /// <summary>
        /// Cycle budget for the master-clear + self-test run, per CPU type. These differ
        /// because of the original power-on delay loop in the master clear routine
        /// ("% WAITING LOOP 0,5 - 1 SECOND", MACL3/MACL4):
        ///
        ///   ND-110: the RASK microcode *source* was deliberately patched to skip the wait
        ///           ("B,177777" instead of "B,1" at MACL3 001754/octal - see the comment
        ///           "changed to avoid delay during MACL. Wait not neccesary in emulator
        ///           (Ronny: 01.01.2023)" in ND-110-RASK.LISTING.TXT line 4773). Boot
        ///           therefore reaches FETCH in well under 200000 cycles.
        ///
        ///   ND-120: the DELILAH-L binary is kept bit-exact to the EPROM dumps (no source
        ///           patch), so the original delay loop WOULD run for real: MACL3 (002003)
        ///           seeds Q=1, MACL4 (001777) counts Q+1 until 16-bit wrap = 65536 inner
        ///           cycles, and the outer loop at 002000 decrements R1=64 times (both
        ///           measured by ticking the emulator, matching ND-120-DELILAH-L.LISTING.TXT
        ///           lines 5105-5123). That would be 64 * 65536 = 4,194,304 microcycles of
        ///           pure waiting before SELFT even starts.
        ///
        ///           Instead of patching the bit-exact binary, the emulator short-circuits
        ///           this pure busy-wait at the C# level (Cpu.cs, gated to cpuType==ND120 and
        ///           the exact MACL3 CSAR 0x403): after the MACL3 seed loads Q=1 it forces
        ///           Q=0xFFFF, reproducing EXACTLY RASK's "B,177777" source patch so MACL4's
        ///           first "Q+1" wraps immediately. Each outer pass then costs one inner cycle
        ///           instead of 65536, so ND-120 now reaches FETCH in the same ~200000-cycle
        ///           ballpark as ND-110.
        /// </summary>
        static int BootCycleBudget(CPUType cpuType) => cpuType == CPUType.ND110 ? 200000 : 200000;

        /// <summary>
        /// Runs microcode from address 0 (master clear) until the CPU reaches its first
        /// macro-instruction FETCH boundary - the built-in self-test and the rest of
        /// master clear all run as pure microcode before that point. Fails immediately,
        /// reporting the R2 error code, if execution ever reaches STERR (self-test failure).
        /// </summary>
        static void RunSelfTestToFirstFetch(CPU cpu, CPUType cpuType)
        {
            int maxCycles = BootCycleBudget(cpuType);
            ushort sterr = SterrAddress(cpuType);
            cpu.hw.mic.LatchMap(0);

            for (int i = 0; i < maxCycles; i++)
            {
                cpu.TickCpu();

                if (cpu.hw.mic.map == sterr)
                {
                    int r2 = cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
                    Assert.Fail($"Self-test failed for {cpuType}: hit STERR with R2={Utils.Int2Oct(r2)} after {i} cycles");
                }

                if (cpu.hw.mac.FETCH) return;
            }

            Assert.Fail($"Self-test for {cpuType} did not reach a FETCH boundary within {maxCycles} cycles");
        }

        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void LoadMicrocode(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);
            Assert.That(cpu.hw.cs, Is.Not.Null, "ControlStore should exist after LoadCS");
        }

        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void SelfTestPasses(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);
            RunSelfTestToFirstFetch(cpu, cpuType);
        }

        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void ReachesOpcomWaitState(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);
            RunSelfTestToFirstFetch(cpu, cpuType);

            // EAUTO~ (STS bit 13) defaults set (Uart.cs: EAUTO_n = true), so master clear
            // takes the STOP2 path (operator wait) rather than attempting an auto-boot.
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True,
                $"{cpuType}: expected CPU to be stopped at the OPCOM/STOP2 wait state after master clear + self-test");
        }

        /// <summary>
        /// Cross-check for the nd-120 Verilog project's open question "is LC=o01 the
        /// intended value at the o000016 panel-interrupt dispatch?" (see
        /// Verilog\DELILAH-CPU\CGA_MIC\LDLCN_o000016_investigation.md in the nd-120 repository).
        ///
        /// Both listings put the PANVC jump table at 003760 (0x7F0) with entries
        /// LC=0:STOP 1:MS20 2:PRQ 3:SING2 4:LOAD 5:CONT 6:RSTRT 7:MACL
        /// (ND-120-DELILAH-L.LISTING.TXT lines 10254-10278; RASK has the same table).
        /// The panel/MS20 interrupt path is 000016 "% PANEL INTERRUPT" (IDBS,PANEL
        /// COMM,LDLC) -> PANEL(000050) -> JMPAOPR dispatch into PANVC+LC. So on a
        /// HEALTHY boot the 20ms RTC interrupt must dispatch through PANVC entry 1
        /// (003761 -> MS20) - i.e. LC must hold 1 at the dispatch.
        ///
        /// This test asserts exactly that against the working emulator boot, giving the
        /// Verilog side a confirmed ground truth for both microcode generations.
        /// </summary>
        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void PanelInterruptDispatch_TakesPanvcEntry1_MS20(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);
            RunSelfTestToFirstFetch(cpu, cpuType);
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, $"{cpuType}: precondition - stopped at OPCOM");

            const ushort PANVC = 0x7F0; // 003760 octal, same address in RASK and DELILAH-L
            ushort ms20 = Ms20Address(cpuType);

            int entry = -1;
            int lcAtDispatch = -1;
            bool ms20Reached = false;
            int maxCycles = TICK_PR_20MS + 20000;

            for (int i = 0; i < maxCycles; i++)
            {
                cpu.TickCpu();
                ushort m = cpu.hw.mic.map;

                if (entry < 0 && m >= PANVC && m <= PANVC + 7)
                {
                    entry = m - PANVC;
                    lcAtDispatch = cpu.hw.mic.loopCounter & 0x0F;
                }
                if (entry >= 0 && m == ms20)
                {
                    ms20Reached = true;
                    break;
                }
            }

            Assert.That(entry, Is.EqualTo(1),
                $"{cpuType}: the 20ms panel interrupt must dispatch through PANVC entry 1 (003761 -> MS20); " +
                $"got entry {entry} (LC nibble at dispatch: {lcAtDispatch})");
            Assert.That(lcAtDispatch, Is.EqualTo(1),
                $"{cpuType}: LC low nibble at the PANVC dispatch should be 1 (the MS20 vector loaded by COMM,LDLC at 000016)");
            Assert.That(ms20Reached, Is.True,
                $"{cpuType}: MS20 handler (0x{ms20:X3}) not reached after the PANVC entry-1 dispatch");
        }

        /// <summary>
        /// The scenario the operator actually relies on: with no macro-program running
        /// (CPU sitting stopped at OPCOM), the RTC must keep ticking and periodically fire
        /// the MS20 panel interrupt (trap vector 0x0E) so OPCOM can still service the
        /// console. This only calls TickCpu() - never UnitTest_FetchAndExecute/StepMacroCode -
        /// to prove the timer interrupt fires purely from background ticking, not from any
        /// high-level code being executed.
        /// </summary>
        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void MS20InterruptFiresWhileStopped(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);
            RunSelfTestToFirstFetch(cpu, cpuType);

            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, $"{cpuType}: precondition - CPU must be stopped before this test");

            ushort ms20 = Ms20Address(cpuType);
            bool hitMs20 = false;

            // Give the RTC more than one full 20ms period's worth of cycles, plus margin
            // for the panel-interrupt dispatch (trap 0x0E -> PANEL -> PANVC -> MS20 handler).
            int maxCycles = TICK_PR_20MS + 20000;

            for (int i = 0; i < maxCycles; i++)
            {
                cpu.TickCpu();

                if (cpu.hw.mic.map == ms20)
                {
                    hitMs20 = true;
                    break;
                }
            }

            Assert.That(hitMs20, Is.True,
                $"{cpuType}: MS20 handler (0x{ms20:X3}) was never reached within {maxCycles} idle cycles - " +
                "the 20ms timer interrupt should wake OPCOM even with no macrocode executing");
        }

        /// <summary>
        /// Post-boot macrocode execution: after the FULL master-clear + self-test boot
        /// (not the pristine-CPU state the isolated instruction tests start from), the
        /// machine must still execute ordinary macro instructions correctly. Guards
        /// against boot leaving residue in the ALU/MIC/register file that derails
        /// macrocode - distinct from TestInstructions.cs, which never boots.
        ///
        /// Program: SAA 12 ; AAA 3 ; LDX ; SAT -1 ; WAIT(ION off -> back to STOP/OPCOM).
        /// </summary>
        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void MacrocodeExecutesAfterFullBoot(CPUType cpuType)
        {
            CPU cpu = CreateCpu(cpuType);
            RunSelfTestToFirstFetch(cpu, cpuType);
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, $"{cpuType}: precondition - stopped at OPCOM");

            ushort p = (ushort)Utils.Oct2Int("1000");
            cpu.memory.WritePhysicalMemory(p + 0, (ushort)Utils.Oct2Int("170412")); // SAA 12
            cpu.memory.WritePhysicalMemory(p + 1, (ushort)Utils.Oct2Int("172403")); // AAA 3
            cpu.memory.WritePhysicalMemory(p + 2, (ushort)Utils.Oct2Int("054002")); // LDX *2  (X := [P+2] = data)
            cpu.memory.WritePhysicalMemory(p + 3, (ushort)Utils.Oct2Int("171377")); // SAT -1
            cpu.memory.WritePhysicalMemory(p + 4, (ushort)Utils.Oct2Int("125252")); // data word for the LDX
            cpu.memory.WritePhysicalMemory(p + 5, 0);

            int newP = cpu.UnitTest_FetchAndExecute(p);
            Assert.That(newP, Is.EqualTo(p + 1), $"{cpuType}: P after SAA 12");
            Assert.That(cpu.ReadALURegister(RegisterEnum.A), Is.EqualTo(0x0A),
                $"{cpuType}: A after SAA 12 (octal 12)");

            newP = cpu.UnitTest_FetchAndExecute((ushort)(p + 1));
            Assert.That(newP, Is.EqualTo(p + 2), $"{cpuType}: P after AAA 3");
            Assert.That(cpu.ReadALURegister(RegisterEnum.A), Is.EqualTo(0x0D),
                $"{cpuType}: A after AAA 3 (12+3 = 15 octal)");

            newP = cpu.UnitTest_FetchAndExecute((ushort)(p + 2));
            Assert.That(newP, Is.EqualTo(p + 3), $"{cpuType}: P after LDX *2");
            Assert.That(cpu.ReadALURegister(RegisterEnum.X), Is.EqualTo(Utils.Oct2Int("125252")),
                $"{cpuType}: X after LDX *2 (P-relative load)");

            newP = cpu.UnitTest_FetchAndExecute((ushort)(p + 3));
            Assert.That(newP, Is.EqualTo(p + 4), $"{cpuType}: P after SAT -1");
            Assert.That(cpu.ReadALURegister(RegisterEnum.T), Is.EqualTo(0xFFFF),
                $"{cpuType}: T after SAT -1 (sign extended)");
        }
    }
}
