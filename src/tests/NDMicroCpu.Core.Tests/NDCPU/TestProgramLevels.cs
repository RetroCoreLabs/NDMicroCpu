//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;

namespace NDMicroCpu.Core.Tests.NDCPU
{
    /// <summary>
    /// Software-driven program-level (PIL) switching.
    ///
    /// On the ND-100/110 the 16 program levels are NOT all handled by the Am2914 PIC pair:
    /// the PIC input pins carry interrupt SOURCES (BINT10-13 -> levels 10-13, BINT14/15,
    /// plus the internal fault lines - see the ND-100 CPU Card 3002 schematic). Levels 1-9
    /// are pure software levels: their pending-ness lives in the microcode's PID scratch
    /// word ("AB,PID" - RegisterFile.cs: "SCR WORD HOLDING MICROPROGRAM-KNOWN BITS OF PID
    /// REGISTER"), and switching to them is done entirely by microcode:
    ///
    ///   TRR/MST PID  -> RPID1 (ND-110-RASK.LISTING.TXT CS 001000-001003) -> CHKIT
    ///   CHKIT        (CS 001062-001071): loads the loop counter with 013 octal
    ///                (IDBS,ARG 100013 + COMM,LDLC at CS 001067), then a one-instruction
    ///                shift loop (CS 001070: B,R3 ALUF,PASSB ALUD,SLD + LCOUNT) shifts
    ///                PID&amp;PIE left once per cycle testing COND,F11 (armed at CS 001066)
    ///                until the highest pending+enabled level bit reaches bit 11 - at which
    ///                point LC holds the level number ("NEW LEVEL -> Q", CS 001071) and
    ///                PLINT/LVSWP (CS 001103/001116) perform the actual level change.
    ///
    /// This is exactly the mechanism INSTRUCTION-VERIFY (INSTRUCTION-B.BPUN) uses for every
    /// single test in its RUN framework: it writes the test address into the target level's
    /// P register (IRW), activates the level (MST PID), lets the test run to its WAIT, and
    /// then reads the level's P back (IRR) and compares it against the address after the
    /// WAIT - printing "WRONG VALUE OF P-REG AFTER TEST" on mismatch (test descriptor at
    /// memory 036774-037040, checker routine at 022542, in the INSTRUCTION-B image).
    /// </summary>
    public class TestProgramLevels
    {
        CPU cpu = null!;   // created in SetUp before every test

        // Macro opcodes (octal), encodings verified against the ND-100 reference data in
        // NDGen/nd100-definitions/specs/instructions/*.yaml:
        //   IRW = 153400 | level<<3 | reg,  IRR = 153600 | level<<3 | reg
        //   reg codes: 0=STS 1=D 2=P 3=B 4=L 5=A 6=T 7=X
        const ushort OPC_IRW_L1_P = 0xD70A;  // 153412 IRW 10 DP  (level-1 P := A)
        const ushort OPC_IRR_L1_P = 0xD78A;  // 153612 IRR 10 DP  (A := level-1 P)
        const ushort OPC_TRR_PIE = 0xD047;  // 150107 TRR PIE    (PIE := A)
        const ushort OPC_ION = 0xD102;  // 150402 ION
        const ushort OPC_MST_PID = 0xD0C6;  // 150306 MST PID    (PID |= A)
        const ushort OPC_SAA_77 = 0xF13F;  // 170477 SAA 77
        const ushort OPC_WAIT = 0xD200;  // 151000 WAIT

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;
            cpu = new CPU();

            // Never log to NATS from tests; keep logging off entirely for speed.
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        /// <summary>
        /// The distilled version of what INSTRUCTION-VERIFY's RUN does for "SAA 0" (SAAOP):
        ///
        ///   level-0 driver:  IRW 10 DP ; TRR PIE ; ION ; MST PID ; (resume) IRR 10 DP
        ///   level-1 test:    SAA 77 ; WAIT
        ///
        /// After MST PID (PID bit 1 set, PIE bits 0+1 enabled, ION on) the CPU must switch
        /// to level 1, execute the test there, and the WAIT must drop back to level 0 with
        /// the level-1 saved P pointing at the word AFTER the WAIT. If the CHKIT scan is
        /// broken the CPU never leaves level 0 and the read-back P is still the IRW value -
        /// which is precisely the "WRONG VALUE OF P-REG AFTER TEST. LEVEL: 1" failure mode
        /// INSTRUCTION-VERIFY reports for every test.
        /// </summary>
        [Test]
        public void MstPid_EntersLevel1_AndWaitReturnsToLevel0()
        {
            // Level-0 driver program at 0400 octal
            ushort p0 = (ushort)Utils.Oct2Int("400");
            cpu.memory.WritePhysicalMemory(p0 + 0, OPC_IRW_L1_P);
            cpu.memory.WritePhysicalMemory(p0 + 1, OPC_TRR_PIE);
            cpu.memory.WritePhysicalMemory(p0 + 2, OPC_ION);
            cpu.memory.WritePhysicalMemory(p0 + 3, OPC_MST_PID);
            cpu.memory.WritePhysicalMemory(p0 + 4, OPC_IRR_L1_P);

            // Level-1 test program at 0500 octal: SAA 77 ; WAIT
            ushort p1 = (ushort)Utils.Oct2Int("500");
            cpu.memory.WritePhysicalMemory(p1 + 0, OPC_SAA_77);
            cpu.memory.WritePhysicalMemory(p1 + 1, OPC_WAIT);

            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(0), "precondition: CPU starts on level 0");

            // IRW 10 DP: level-1 P := 0500
            cpu.WriteALURegister(Enums.RegisterEnum.A, p1);
            int newP = cpu.UnitTest_FetchAndExecute(p0);
            Assert.That(newP, Is.EqualTo(p0 + 1), "P after IRW 10 DP");
            Assert.That(cpu.hw.RF.Read(1, (ushort)Enums.RegisterEnum.P), Is.EqualTo(p1),
                "IRW 10 DP must write the level-1 P register in the register file");

            // TRR PIE: enable levels 0 and 1
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0b0000_0000_0000_0011);
            newP = cpu.UnitTest_FetchAndExecute((ushort)(p0 + 1));
            Assert.That(newP, Is.EqualTo(p0 + 2), "P after TRR PIE");

            // ION: interrupt system on (CHKIT only switches levels when STS IONI is set -
            // the COND,F15 test on IDBS,STS at CS 001065)
            newP = cpu.UnitTest_FetchAndExecute((ushort)(p0 + 2));
            Assert.That(newP, Is.EqualTo(p0 + 3), "P after ION");

            // MST PID: set PID bit 1 -> level 1 is pending+enabled and higher than the
            // current level 0, so the CPU must switch to it. The FetchAndExecute helper
            // runs microcode until the next FETCH, which must now be at level 1's P.
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0b0000_0000_0000_0010);
            newP = cpu.UnitTest_FetchAndExecute((ushort)(p0 + 3));

            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(1),
                "MST PID (bit 1) with PIE bits 0+1 and ION on must enter program level 1 " +
                "(microcode CHKIT scan, ND-110-RASK.LISTING.TXT CS 001062-001071)");
            Assert.That(newP, Is.EqualTo(p1), "after the level switch the next fetch is at level-1 P");

            // Run the level-1 test: SAA 77, then WAIT. The WAIT clears the level's PID bit
            // and CHKIT drops back to level 0 (highest remaining pending+enabled level).
            // (UnitTest_FetchAndExecute is used - not StepMacroCode - because a bare test
            // CPU still has IOControl bit 6 clear = "OPCOM enabled", which makes TickCpu
            // skip the macro fetch; FetchAndExecute performs the fetch itself. It writes P
            // at the CURRENT level, which is now level 1 - exactly what we want.)
            newP = cpu.UnitTest_FetchAndExecute(p1);
            Assert.That(newP, Is.EqualTo(p1 + 1), "P after SAA 77 on level 1");
            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(1), "still on level 1 after SAA 77");
            newP = cpu.UnitTest_FetchAndExecute((ushort)(p1 + 1)); // WAIT

            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(0), "WAIT on level 1 must fall back to level 0");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(p0 + 4),
                "level 0 resumes after the MST PID instruction");

            // THE check INSTRUCTION-VERIFY performs on every test ("P-REG AFTER TEST"):
            // the level-1 saved P must point past the WAIT.
            Assert.That(cpu.hw.RF.Read(1, (ushort)Enums.RegisterEnum.P), Is.EqualTo(p1 + 2),
                "level-1 saved P after the test must point at the word after the WAIT");

            // And the level-1 A register must show the test actually executed up there.
            Assert.That(cpu.hw.RF.Read(1, (ushort)Enums.RegisterEnum.A), Is.EqualTo(0x3F /* 77 octal */),
                "SAA 77 must have executed on level 1");

            // Finally read the saved P back the same way the test program does: IRR 10 DP.
            newP = cpu.UnitTest_FetchAndExecute((ushort)(p0 + 4));
            Assert.That(newP, Is.EqualTo(p0 + 5), "P after IRR 10 DP");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(p1 + 2),
                "IRR 10 DP must read back the level-1 saved P (the value INSTRUCTION-VERIFY compares)");
        }

        /// <summary>
        /// Diagnostic: microinstruction-by-microinstruction trace of the MST PID execution
        /// (the same setup as MstPid_EntersLevel1_AndWaitReturnsToLevel0) so the CHKIT scan
        /// (CS 001062-001071) can be inspected: CSAR, loop counter, ALU F, latched COND and
        /// the armed test object each cycle.
        /// </summary>
        [Explicit("Diagnostic - dumps the CHKIT micro-trace for MST PID")]
        [Test]
        public void Diag_MstPidMicroTrace()
        {
            ushort p0 = (ushort)Utils.Oct2Int("400");
            cpu.memory.WritePhysicalMemory(p0 + 0, OPC_IRW_L1_P);
            cpu.memory.WritePhysicalMemory(p0 + 1, OPC_TRR_PIE);
            cpu.memory.WritePhysicalMemory(p0 + 2, OPC_ION);
            cpu.memory.WritePhysicalMemory(p0 + 3, OPC_MST_PID);
            cpu.memory.WritePhysicalMemory(p0 + 4, OPC_IRR_L1_P);

            ushort p1 = (ushort)Utils.Oct2Int("500");
            cpu.memory.WritePhysicalMemory(p1 + 0, OPC_SAA_77);
            cpu.memory.WritePhysicalMemory(p1 + 1, OPC_WAIT);

            cpu.WriteALURegister(Enums.RegisterEnum.A, p1);
            cpu.UnitTest_FetchAndExecute(p0);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0b0000_0000_0000_0011);
            cpu.UnitTest_FetchAndExecute((ushort)(p0 + 1));
            cpu.UnitTest_FetchAndExecute((ushort)(p0 + 2));

            // Now trace MST PID by hand (mirrors UnitTest_FetchAndExecute's loop)
            // (STOP is already false here - the earlier UnitTest_FetchAndExecute calls clear it)
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0b0000_0000_0000_0010);
            cpu.hw.alu.WriteRegisterE(Enums.RegisterEnum.P, (ushort)(p0 + 3));
            cpu.hw.mac.LatchMacAddress((ushort)(p0 + 3));
            cpu.UnitTest_Fetch();

            TraceCycles("MST PID", 1);

            // A bare test CPU has IOControl bit 6 clear ("OPCOM enabled"), so TickCpu skips
            // the macro fetch - do each fetch manually (same as UnitTest_FetchAndExecute)
            // at the CURRENT level's P, then trace the microcode.
            ushort lp = cpu.ReadALURegister(Enums.RegisterEnum.P);
            cpu.hw.mac.LatchMacAddress(lp);
            cpu.UnitTest_Fetch();
            TraceCycles($"SAA 77 (PIL={cpu.hw.alu.PIL} P={Utils.Int2Oct(lp)})", 1);

            lp = cpu.ReadALURegister(Enums.RegisterEnum.P);
            cpu.hw.mac.LatchMacAddress(lp);
            cpu.UnitTest_Fetch();
            TraceCycles($"WAIT (PIL={cpu.hw.alu.PIL} P={Utils.Int2Oct(lp)})", 1);

            TestContext.Out.WriteLine($"final PIL={cpu.hw.alu.PIL} P={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.P))}");
        }

        void TraceCycles(string what, int fetchesToSee)
        {
            TestContext.Out.WriteLine($"--- {what} ---");
            int fetches = 0;
            for (int i = 0; i < 600; i++)
            {
                ushort csar = cpu.hw.mic.map;
                cpu.TickCpu();
                TestContext.Out.WriteLine(
                    $"{i,3}: CSAR={Utils.Int2Oct(csar)} LC={cpu.hw.mic.loopCounter,3} " +
                    $"F={Utils.Int2Oct(cpu.hw.alu.F & 0xFFFF)} F11={(cpu.hw.alu.F11 ? 1 : 0)} " +
                    $"COND={(cpu.hw.cpu_regs.COND ? 1 : 0)} obj={cpu.hw.cpu_regs.COND_TestObjects} " +
                    $"PIL={cpu.hw.alu.PIL}{(cpu.hw.mac.FETCH ? " FETCH" : "")}");
                if (cpu.hw.mac.FETCH)
                {
                    fetches++;
                    if (fetches >= fetchesToSee) break;
                }
            }
        }
    }
}
