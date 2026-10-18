//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Text;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Boot
{
    /// <summary>
    /// Runs INSTRUCTION-VERIFY's per-area test commands one by one, instead of the
    /// all-in-one RUN command. The program's own DOCUMENT-SPECIAL-COMMANDS output
    /// (INSTRUCTION-B, 204384B, 83.11.01) lists the areas:
    ///
    ///   ARGUMENT            SAA/AAA/SAX/AAX/SAT/AAT/SAB/AAB
    ///   MEMORY-REFERENCE    STZ/STA/STT/STX/MIN/LDA/LDT/LDX/LDF/STF/LDD/STD/LBYT/SBYT/ADD/SUB/AND/ORA/MPY
    ///   SEQUENCE            JMP/JPL/JAP/JAN/JAZ/JAF/JXN/JPC/JNC/JXZ/SKP
    ///   REGISTER-OPERATIONS RADD/RSUB/RAND/RORA/REX/SWAP/COPY/RMPY/RDIV/MIX3/EXR
    ///   BIT-OPERATIONS      BLDA/BSTA/BSTC/BLDC/BANC/BORC/BAND/BORA/BSKP/BSET
    ///   SHIFT-INSTRUCTIONS  SHA/SHD/SHT/SAD
    ///   32-BITS-FLOATING    DNZ/NLZ/FMU/FDV/FAD/FSB
    ///   PRIVILEGED          LRB/SRB, MST/MCL PID/PIE
    ///   BYTE-STRING         BFILL/MOVB/MOVBF
    ///   ND100-24BIT         SEX/REX/EXAM/DEPO/LDATX/STATX/LDDTX/STDTX/LDBTX
    ///   BCD / ND100-CX / STACK / SEGMENT
    ///
    /// Each area command runs its tests on level 1 (printing "== END OF TEST ==" when the
    /// level-1 pass completes), then repeats on levels 2..11 forever - so the harness sends
    /// ESC after the level-2 marker appears to abort back to the ">" prompt ("=ESCAPE=").
    /// A failing area prints lines containing "ERROR" and/or "FAILED" instead.
    ///
    /// Splitting per area (a) gives one red test per broken instruction group instead of a
    /// single opaque RUN failure, and (b) exercises the same level-1..n framework that
    /// found the CHKIT loop-counter bug (see TestProgramLevels.cs) with far less noise.
    /// </summary>
    public class TestInstructionVerifyAreas
    {
        // RealTimeClock.TICK_PR_20MS - see TestBootSequence.cs for why this is duplicated here.
        const int TICK_PR_20MS = 10550 * 4;
        const char ESC = (char)0x1B;

        CPU cpu = null!;
        StringBuilder output = null!;

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;

            // Boot a fresh ND-110 to OPCOM (master clear + self-test), same as
            // TestInstructionVerify.BootToOpcom.
            cpu = new CPU(null, CPUType.ND110);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            const ushort sterr = 0x449;
            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < 200000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr)
                {
                    int r2 = cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
                    Assert.Fail($"Self-test failed: hit STERR with R2={Utils.Int2Oct(r2)} after {i} cycles");
                }
                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True,
                "Expected CPU to be stopped at OPCOM after master clear + self-test");

            output = new StringBuilder();
            cpu.hw.uart.OnTextOutput += OnText;

            // "0!" at OPCOM starts INSTRUCTION-VERIFY at address 0; wait for its banner
            // and the interactive ">" prompt.
            TypeString("0!");
            RunCycles(TICK_PR_20MS * 20);
            string text = output.ToString();
            Assert.That(text, Does.Contain("INSTRUCTION"), $"Expected the INSTRUCTION-VERIFY banner. Captured:\n{text}");
            Assert.That(text, Does.Contain(">"), $"Expected the '>' command prompt. Captured:\n{text}");
        }

        [TearDown]
        public void TearDown()
        {
            if (cpu != null && cpu.hw?.uart != null)
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }
        }

        void OnText(char c) => output.Append(c);

        void TypeString(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                cpu.hw.AddConsoleChar(text[i]);
                RunCycles(TICK_PR_20MS * 5);
            }
        }

        void RunCycles(int cycles)
        {
            for (int i = 0; i < cycles; i++) cpu.TickCpu();
        }

        // 48-BITS-FLOATING is intentionally absent: the program banner reports this CPU
        // configuration as "32 BIT FLOATING FORMAT", so the 48-bit list is not the
        // configured instruction set for OTYPE 12 (ND-100 CX, 32F).
        /// <summary>
        /// Diagnostic for the PRIVILEGED area's "MCL/MST/TRA/TRR FAILED": every failed
        /// check in that test JMPs to the error printer at macro address 036270 (octal,
        /// INSTRUCTION-B image). This diag types PRIVILEGED and keeps a rolling ring of
        /// macro-P values; when P first hits 036270 it prints the ring, revealing WHICH
        /// check (the JMP's site) failed - plus A/T/PIL at that moment.
        /// </summary>
        [Explicit("Diagnostic - locates the failing MCL/MST/TRA/TRR check")]
        [Test]
        public void Diag_PrivilegedFirstError()
        {
            const int errSite = 0x3CB8; // 036270 octal - the shared error printer
            output.Clear();
            TypeString("PRIVILEGED\r");

            ushort[] ring = new ushort[64];
            int ringNext = 0, ringCount = 0;
            ushort lastP = 0;
            bool hit = false;

            for (int i = 0; i < TICK_PR_20MS * 400 && !hit; i++)
            {
                cpu.TickCpu();
                ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                if (p != lastP)
                {
                    lastP = p;
                    ring[ringNext] = p;
                    ringNext = (ringNext + 1) % ring.Length;
                    if (ringCount < ring.Length) ringCount++;

                    if (p == errSite)
                    {
                        hit = true;
                        TestContext.Out.WriteLine("--- P ring (oldest first) at first error-printer entry ---");
                        int idx = (ringCount < ring.Length) ? 0 : ringNext;
                        for (int n = 0; n < ringCount; n++)
                        {
                            TestContext.Out.WriteLine($"  P={Utils.Int2Oct(ring[idx])}");
                            idx = (idx + 1) % ring.Length;
                        }
                        TestContext.Out.WriteLine(
                            $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                            $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))} " +
                            $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                            $"PIL={cpu.hw.alu.PIL}");
                    }
                }
            }
            TestContext.Out.WriteLine($"hit={hit} captured:\n{output}");
        }

        /// <summary>
        /// Diagnostic for the BYTE-STRING area's MOVB failure: the framework installs the
        /// source (A/D) and destination (X/T) descriptors into the test level's registers
        /// at runtime (image slots 043110-043117 are zero in the BPUN), then runs
        /// "MOVB ; WAIT ; WAIT" at 043003. This diag drives the area and captures the
        /// descriptors right when P reaches the MOVB, and the end state + destination
        /// memory right after.
        /// </summary>
        [Explicit("Diagnostic - captures MOVB descriptors in the BYTE-STRING area")]
        [Test]
        public void Diag_ByteStringMovbDescriptors()
        {
            const int movbSite = 0x4603;  // 043003 octal - the MOVB under test
            output.Clear();
            TypeString("BYTE-STRING\r");

            ushort lastP = 0;
            bool sawMovb = false;
            int captures = 0;

            for (int i = 0; i < TICK_PR_20MS * 600 && captures < 2; i++)
            {
                cpu.TickCpu();
                ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                if (p == lastP) continue;
                lastP = p;

                if (p == movbSite && !sawMovb)
                {
                    sawMovb = true;
                    captures++;
                    TestContext.Out.WriteLine(
                        $"at MOVB: PIL={cpu.hw.alu.PIL} " +
                        $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                        $"D={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.D))} " +
                        $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                        $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))}");
                }
                else if (sawMovb && captures < 2 && (p == movbSite + 2))
                {
                    captures++;
                    ushort a = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.A);
                    ushort x = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.X);
                    TestContext.Out.WriteLine(
                        $"after MOVB: PIL={cpu.hw.alu.PIL} " +
                        $"A={Utils.Int2Oct(a)} " +
                        $"D={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.D))} " +
                        $"X={Utils.Int2Oct(x)} " +
                        $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))}");
                }
            }

            TestContext.Out.WriteLine($"sawMovb={sawMovb}");
            // Dump the start of the source/destination areas seen in the image constants
            // 043066-043070 (054727 / 004000 / 007776) - likely field addresses/lengths.
            int[] probes = { Convert.ToInt32("4000", 8), Convert.ToInt32("7776", 8), Convert.ToInt32("54727", 8) };
            for (int b = 0; b < probes.Length; b++)
            {
                for (int a = probes[b]; a < probes[b] + 4; a++)
                {
                    TestContext.Out.WriteLine($"  MEM {Utils.Int2Oct(a)} : {Utils.Int2Oct(cpu.memory.ReadPhysicalMemory((ushort)a))}");
                }
            }
        }

        /// <summary>
        /// Diagnostic for the SEGMENT area: typing SEGMENT makes INSTRUCTION-VERIFY
        /// restart (full startup banner) instead of running tests. This watches for the
        /// illegal-instruction / privileged-instruction microcode entries (ILLIN CS
        /// 000216, PRIVI CS 000217 - ND-110-RASK.LISTING.TXT) and records the macro P at
        /// that moment, to identify the opcode the emulator rejects.
        /// </summary>
        /// <summary>
        /// Diagnostic for the SEGMENT restart: keeps a rolling ring of macro-P values and
        /// dumps it the moment the startup banner begins to reprint (first '-' of the
        /// "---------" ruler after the SEGMENT command) - the ring then shows the path
        /// that led back into the monitor's init code.
        /// </summary>
        [Explicit("Diagnostic - P history into the SEGMENT restart")]
        [Test]
        public void Diag_SegmentRestartRing()
        {
            output.Clear();
            TypeString("SEGMENT\r");

            ushort[] ring = new ushort[96];
            int ringNext = 0, ringCount = 0;
            ushort lastP = 0;
            int cmdEchoLen = output.Length;
            bool dumped = false;

            // (Established 2026-07-11: SEGMENT prints NOTHING in >60M cycles - it hangs
            // silently; the banner "restart" seen in the area test only happens once ESC
            // is typed into the hung state. So dump the ring after a modest window - it
            // shows the loop the program hangs in.)
            for (int i = 0; i < TICK_PR_20MS * 100 && !dumped; i++)
            {
                cpu.TickCpu();
                ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                if (p != lastP)
                {
                    lastP = p;
                    ring[ringNext] = p;
                    ringNext = (ringNext + 1) % ring.Length;
                    if (ringCount < ring.Length) ringCount++;
                }
                if (i == TICK_PR_20MS * 100 - 1)
                {
                    dumped = true;
                    TestContext.Out.WriteLine("--- P ring (oldest first) at banner reprint ---");
                    int idx = (ringCount < ring.Length) ? 0 : ringNext;
                    for (int n = 0; n < ringCount; n++)
                    {
                        TestContext.Out.WriteLine($"  P={Utils.Int2Oct(ring[idx])}");
                        idx = (idx + 1) % ring.Length;
                    }
                    TestContext.Out.WriteLine(
                        $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                        $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                        $"B={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.B))} " +
                        $"PIL={cpu.hw.alu.PIL}");
                }
            }
            TestContext.Out.WriteLine($"dumped={dumped}");

            // The ring shows the console input poll loop (052272-052330) - SEGMENT is
            // WAITING FOR INPUT, not hung. Probe what it wants: a bare CR, then see if
            // the monitor answers a normal command afterwards.
            output.Clear();
            TypeString("\r");
            RunCycles(TICK_PR_20MS * 40);
            TestContext.Out.WriteLine($"after CR: [{output}]");

            output.Clear();
            TypeString("LIST-SP-C\r");
            RunCycles(TICK_PR_20MS * 60);
            TestContext.Out.WriteLine($"after LIST-SP-C: [{output}]");
        }

        /// <summary>
        /// Diagnostic: types SEGMENT one character at a time and dumps every byte the
        /// program echoes (as character codes) - to see exactly where the command-line
        /// reader stops cooperating.
        /// </summary>
        [Explicit("Diagnostic - char-level echo dump for the SEGMENT command")]
        [Test]
        public void Diag_SegmentEchoDump()
        {
            output.Clear();
            string cmd = "SEGMENT\r";
            for (int i = 0; i < cmd.Length; i++)
            {
                int before = output.Length;
                cpu.hw.AddConsoleChar(cmd[i]);
                RunCycles(TICK_PR_20MS * 8);
                string echoed = output.ToString(before, output.Length - before);
                System.Text.StringBuilder codes = new System.Text.StringBuilder();
                for (int k = 0; k < echoed.Length; k++)
                {
                    codes.Append(Utils.Int2Oct(echoed[k]));
                    codes.Append(' ');
                }
                TestContext.Out.WriteLine($"typed {Utils.Int2Oct(cmd[i])} '{(cmd[i] >= ' ' ? cmd[i] : '.')}' -> echoed [{codes}]");
            }
            RunCycles(TICK_PR_20MS * 60);
            TestContext.Out.WriteLine($"final extra output: [{output}]");
        }

        /// <summary>
        /// Diagnostic: P-ring into the SEGMENT restart, armed correctly this time - the
        /// restart banner prints DURING the CR processing (see Diag_SegmentEchoDump), so
        /// the ring is collected while the CR is being typed and dumped at the first '-'
        /// (banner ruler) transmitted after the command characters.
        /// </summary>
        [Explicit("Diagnostic - P history into the SEGMENT restart (correct arming)")]
        [Test]
        public void Diag_SegmentRestartRing2()
        {
            TypeString("SEGMENT"); // command chars only - CR comes below, instrumented

            // 4096 deep: the banner trigger fires long after the interesting jump, and
            // the char-output driver alone burns ~100 distinct P values per character.
            ushort[] ring = new ushort[4096];
            int ringNext = 0, ringCount = 0;
            ushort lastP = 0;
            bool dumped = false;
            bool sawEcho = false;

            void OnBanner(char c)
            {
                // The expanded-command echo ("SEGMENT\r\n") comes first; the restart
                // banner's ruler is the first '-' after that.
                if (c == '-' && sawEcho && !dumped)
                {
                    dumped = true;
                    TestContext.Out.WriteLine("--- P ring (oldest first) at first banner '-' ---");
                    int idx = (ringCount < ring.Length) ? 0 : ringNext;
                    for (int n = 0; n < ringCount; n++)
                    {
                        TestContext.Out.WriteLine($"  P={Utils.Int2Oct(ring[idx])}");
                        idx = (idx + 1) % ring.Length;
                    }
                }
                if (c == '\n') sawEcho = true;
            }
            cpu.hw.uart.OnTextOutput += OnBanner;

            cpu.hw.AddConsoleChar('\r');
            for (int i = 0; i < TICK_PR_20MS * 60 && !dumped; i++)
            {
                cpu.TickCpu();
                ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                if (p != lastP)
                {
                    lastP = p;
                    ring[ringNext] = p;
                    ringNext = (ringNext + 1) % ring.Length;
                    if (ringCount < ring.Length) ringCount++;
                }
            }
            cpu.hw.uart.OnTextOutput -= OnBanner;
            TestContext.Out.WriteLine($"dumped={dumped}");
        }

        /// <summary>
        /// Diagnostic: watches the SEGMENT epilogue's continuation pointer [046351] (the
        /// register-restore code at 046411-046424 ends with "JMP I *-55" through it, and
        /// in the failing run it sent execution to the warm-restart at 000267). Logs P
        /// whenever the cell changes while the SEGMENT command runs - identifying who
        /// writes the restart address (or whether it was never set to a proper
        /// continuation in the first place).
        /// </summary>
        [Explicit("Diagnostic - who writes the SEGMENT continuation pointer")]
        [Test]
        public void Diag_SegmentContinuationWatch()
        {
            // 046335 = the cell "JMP I" at 046424 (disp -55 decimal) jumps through;
            // 046332-046334 are the A/T/X save slots restored just before it.
            ushort cell = (ushort)Utils.Oct2Int("46335");
            ushort last = cpu.memory.ReadPhysicalMemory(cell);
            TestContext.Out.WriteLine($"[046351] before SEGMENT: {Utils.Int2Oct(last)}");

            output.Clear();
            string cmd = "SEGMENT\r";
            for (int c = 0; c < cmd.Length; c++)
            {
                cpu.hw.AddConsoleChar(cmd[c]);
                for (int i = 0; i < TICK_PR_20MS * 8; i++)
                {
                    cpu.TickCpu();
                    ushort now = cpu.memory.ReadPhysicalMemory(cell);
                    if (now != last)
                    {
                        TestContext.Out.WriteLine(
                            $"[046351] {Utils.Int2Oct(last)} -> {Utils.Int2Oct(now)} at " +
                            $"P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} PIL={cpu.hw.alu.PIL}");
                        last = now;
                    }
                }
            }
            TestContext.Out.WriteLine($"[046351] after: {Utils.Int2Oct(last)}; captured:\n{output}");
        }

        [Explicit("Diagnostic - catches the instruction that aborts SEGMENT")]
        [Test]
        public void Diag_SegmentIllegalInstruction()
        {
            output.Clear();
            TypeString("SEGMENT\r");

            int hits = 0;
            for (int i = 0; i < TICK_PR_20MS * 200 && hits < 5; i++)
            {
                cpu.TickCpu();
                ushort m = cpu.hw.mic.map;
                if (m == 0x8E || m == 0x8F) // 000216 ILLIN / 000217 PRIVI
                {
                    hits++;
                    ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                    ushort opc0 = cpu.memory.ReadPhysicalMemory((ushort)(p - 1));
                    ushort opc1 = cpu.memory.ReadPhysicalMemory(p);
                    TestContext.Out.WriteLine(
                        $"{(m == 0x8E ? "ILLIN" : "PRIVI")} at P={Utils.Int2Oct(p)} PIL={cpu.hw.alu.PIL} " +
                        $"[P-1]={Utils.Int2Oct(opc0)} [P]={Utils.Int2Oct(opc1)}");
                    // skip ahead so the same event isn't logged every microcycle
                    for (int k = 0; k < 200; k++) cpu.TickCpu();
                }
            }
            TestContext.Out.WriteLine($"hits={hits} captured:\n{output}");
        }

        [TestCase("ARGUMENT")]
        [TestCase("MEMORY-REFERENCE")]
        [TestCase("SEQUENCE")]
        [TestCase("REGISTER-OPERATIONS")]
        [TestCase("BIT-OPERATIONS")]
        [TestCase("SHIFT-INSTRUCTIONS")]
        [TestCase("32-BITS-FLOATING")]
        [TestCase("PRIVILEGED")]
        [TestCase("BYTE-STRING")]
        [TestCase("ND100-24BIT")]
        [TestCase("BCD")]
        [TestCase("ND100-CX")]
        [TestCase("STACK")]
        public void Area(string command)
        {
            RunArea(command);
        }

        /// <summary>
        /// SEGMENT restarts the program - and that is its DESIGNED termination, verified
        /// 2026-07-12. Full annotated disassembly (every address and instruction):
        /// docs\SEGMENT-Command-Disassembly.md in this repository. In short:
        ///
        ///   1. The handler detects hardware features (SEX/REX, PCR, PGS, CSR), fills
        ///      the page-table/shadow area 174000-177777 with a pattern, arms IIE = 020
        ///      (illegal-instruction interrupt only) with a level-14 trap handler at
        ///      054714, then runs a probe battery.
        ///   2. Opcode 142700 at 054601 is the machine-type gate, checked with
        ///      "TRA IIC ; SAT 4 ; SKP IF DA UEQ ST" (054602-054605, UEQ = UNequal):
        ///      IIC == 4 (plain ND-100/CX, 142700 illegal) SKIPS the segment tests;
        ///      IIC != 4 runs them. ND-110 RASK dispatches 142700 (opcode>>6 + 06000 =
        ///      CS 007427) to GECO (customer-specified instruction, GECO1 at CS 004000),
        ///      which executes - so on this emulator the tests RAN: CLEPT at 054613
        ///      (completed without trapping) and the SRB/LRB register-block test at
        ///      054643-054676, all confirmed by the P-ring (Diag_SegmentRestartRing2).
        ///      (An earlier reading of the condition as EQL - commit 1bc4319 - had the
        ///      gate inverted; the UEQ decode and the ring settle it.)
        ///   3. On BOTH machine types the handler ends in the same cleanup (IOF,
        ///      PIE/PID/IIE := 0, registers restored, 054701-054713) and exits through
        ///      the warm-restart vector [046335] = 000267 (installed at startup, never
        ///      modified - watched live, Diag_SegmentContinuationWatch). Failures only
        ///      increment in-memory MIN counters; nothing is printed.
        ///
        /// This test asserts exactly that behavior: banner reprint + prompt, no error.
        /// </summary>
        [Test]
        public void Area_Segment_RestartsViaIllegalOpcodeProbe()
        {
            output.Clear();
            TypeString("SEGMENT\r");
            RunCycles(TICK_PR_20MS * 60);

            string text = output.ToString();
            Assert.That(text, Does.Contain("INSTRUCTION  VERIFY"),
                $"SEGMENT on ND-110 microcode (GECO at 142700) must exit to the monitor, reprinting the banner. Captured:\n{text}");
            Assert.That(text, Does.Contain(">"),
                $"and end back at the '>' prompt. Captured:\n{text}");
            Assert.That(text, Does.Not.Contain("*** ERROR"),
                $"the exit is the program's deliberate bail-out, not an error print. Captured:\n{text}");
        }

        void RunArea(string command)
        {
            output.Clear();
            TypeString(command + "\r");

            // Watch the run: the level-1 pass ends with "== END OF TEST ==" and the
            // framework then starts "== RUNNING TESTS ON LEVEL 2 ==". Stop watching as
            // soon as level 2 starts (level 1 is the assertable unit - the framework
            // repeats the same routines on levels 2..11 forever), or as soon as an error
            // line shows up (fail fast instead of collecting the same error in a loop).
            // NOTE on the error markers: failures print "*** ERROR ***" (or "FAILED").
            // A bare "ERROR" substring is NOT usable - when a fast area reaches the
            // looping stage its stress levels legitimately print "== IOX-ERROR STARTED ==".
            // "THE TESTS ARE NOW LOOPING" is a success marker too (all levels passed).
            const int maxChunks = 120;
            for (int chunk = 0; chunk < maxChunks; chunk++)
            {
                RunCycles(TICK_PR_20MS * 10);
                string sofar = output.ToString();
                if (sofar.Contains("LEVEL 2") || sofar.Contains("NOW LOOPING")) break;
                if (sofar.Contains("*** ERROR") || sofar.Contains("FAILED")) break;
            }

            // Abort the (endless) level loop back to the ">" prompt. Some areas (e.g.
            // BYTE-STRING with its big block moves) poll the console rarely while a
            // level's tests run, so wait in chunks for the "=ESCAPE=" acknowledgement
            // and retry the ESC a few times before giving up.
            int promptMark = output.Length;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                cpu.hw.AddConsoleChar(ESC);
                bool escaped = false;
                for (int chunk = 0; chunk < 20 && !escaped; chunk++)
                {
                    RunCycles(TICK_PR_20MS * 10);
                    escaped = output.ToString(promptMark, output.Length - promptMark).Contains(">");
                }
                if (escaped) break;
            }

            string text = output.ToString();
            Assert.That(text, Does.Not.Contain("*** ERROR"),
                $"{command}: INSTRUCTION-VERIFY reported an error. Captured output:\n{text}");
            Assert.That(text, Does.Not.Contain("FAILED"),
                $"{command}: INSTRUCTION-VERIFY reported a failed test. Captured output:\n{text}");
            Assert.That(text, Does.Contain("== END OF TEST =="),
                $"{command}: expected the level-1 pass to complete with '== END OF TEST =='. Captured output:\n{text}");
            Assert.That(text, Does.Contain(">"),
                $"{command}: expected to get back to the '>' prompt after ESC. Captured output:\n{text}");
        }
    }
}
