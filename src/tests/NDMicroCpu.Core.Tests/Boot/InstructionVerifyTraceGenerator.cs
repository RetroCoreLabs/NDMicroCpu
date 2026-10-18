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
    /// Reference-trace generator for validating OTHER emulators (e.g. the ND-120 project)
    /// against this one. For a given INSTRUCTION-VERIFY area command it produces a
    /// markdown report at &lt;solution&gt;\traces\TRACE-INSTRUCTION-VERIFY-&lt;AREA&gt;.md:
    ///
    ///  * one section per MACRO instruction: address (octal), opcode word, and the full
    ///    register set at fetch time (A D T X B L P STS PIL + scratch R1-R7 of the
    ///    current level + ALU Q/F, GPR, loop counter)
    ///  * inside each section one row per MICROINSTRUCTION: the control-store address,
    ///    the original ND-110-RASK.LISTING.TXT source text for that address, and every
    ///    watched register that CHANGED during that microinstruction (delta format keeps
    ///    the file readable; the macro header always carries the complete state).
    ///
    /// The capture starts when the area command's CR is submitted and ends at
    /// "== END OF TEST ==" / an error line / the macro-instruction cap - whichever comes
    /// first. All values are OCTAL. [Explicit] - run on demand, writes files.
    /// </summary>
    public class InstructionVerifyTraceGenerator
    {
        const int TICK_PR_20MS = 10550 * 4;

        /// <summary>Full-detail cap: macro instructions traced with microcode rows.</summary>
        const int MACRO_CAP = 400;

        /// <summary>Hard safety cap on micro cycles per run.</summary>
        const int TICK_CAP = TICK_PR_20MS * 400;

        static readonly string UCodeDir = TestPaths.MicrocodeDir() + System.IO.Path.DirectorySeparatorChar;

        // The listing/symbols cross-reference is CPU-type specific: RASK for ND-110,
        // DELILAH-L for ND-120. Both files share the identical "NNNN AAAAAA text"
        // listing and "AAAAAA=LABEL" symbol formats, so only the path differs.
        static string ListingPath(CPUType t) => UCodeDir +
            (t == CPUType.ND110 ? "ND-110-RASK.LISTING.TXT" : "ND-120-DELILAH-L.LISTING.TXT");

        static string SymbolsPath(CPUType t) => UCodeDir +
            (t == CPUType.ND110 ? "ND-110-RASK.SYMBOLS.TXT" : "ND-120-DELILAH-L.SYMBOLS.TXT");

        // Caches keyed by CPU type so ND-110 (RASK) and ND-120 (DELILAH-L) traces can
        // be generated in the same test session without cross-contaminating the maps.
        static readonly System.Collections.Generic.Dictionary<CPUType,
            System.Collections.Generic.Dictionary<int, string>> _listingByType =
            new System.Collections.Generic.Dictionary<CPUType,
                System.Collections.Generic.Dictionary<int, string>>();

        static readonly System.Collections.Generic.Dictionary<CPUType,
            System.Collections.Generic.Dictionary<int, string>> _symbolsByType =
            new System.Collections.Generic.Dictionary<CPUType,
                System.Collections.Generic.Dictionary<int, string>>();

        static readonly System.Collections.Generic.Dictionary<CPUType, int[]> _symbolAddrsByType =
            new System.Collections.Generic.Dictionary<CPUType, int[]>(); // sorted, nearest-preceding lookup

        /// <summary>
        /// Parses ND-110-RASK.SYMBOLS.TXT ("AAAAAA=LABEL" per line). The labels are the
        /// PORTABLE reference between microcode generations: the ND-120 DELILAH listing
        /// has different control-store addresses but (mostly) the same label names, so
        /// every trace row is annotated "LABEL" (exact hit) or "LABEL+n" (offset from the
        /// nearest preceding label, n octal).
        /// </summary>
        static void LoadSymbols(CPUType cpuType)
        {
            if (_symbolsByType.ContainsKey(cpuType)) return;
            System.Collections.Generic.Dictionary<int, string> map =
                new System.Collections.Generic.Dictionary<int, string>();
            string[] lines = System.IO.File.ReadAllLines(SymbolsPath(cpuType));
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                int eq = ln.IndexOf('=');
                if (eq != 6) continue;
                bool ok = true;
                for (int k = 0; k < 6 && ok; k++)
                {
                    if (ln[k] < '0' || ln[k] > '7') ok = false;
                }
                if (!ok) continue;
                int addr = Convert.ToInt32(ln.Substring(0, 6), 8);
                map[addr] = ln.Substring(7).Trim();
            }
            int[] addrs = new int[map.Count];
            map.Keys.CopyTo(addrs, 0);
            Array.Sort(addrs);
            _symbolsByType[cpuType] = map;
            _symbolAddrsByType[cpuType] = addrs;
        }

        /// <summary>Symbolic name for a control-store address: exact label or LABEL+n (octal).</summary>
        static string SymbolFor(int csar, CPUType cpuType)
        {
            LoadSymbols(cpuType);
            var symbols = _symbolsByType[cpuType];
            var symbolAddrs = _symbolAddrsByType[cpuType];
            if (symbols.ContainsKey(csar)) return symbols[csar];
            int idx = Array.BinarySearch(symbolAddrs, csar);
            if (idx < 0) idx = ~idx - 1; // nearest preceding
            if (idx < 0) return "";
            int baseAddr = symbolAddrs[idx];
            return symbols[baseAddr] + "+" + Convert.ToString(csar - baseAddr, 8);
        }

        /// <summary>
        /// Parses the listing once per CPU type: control-store address (octal) ->
        /// compacted source text (all lines of that address joined, comments short).
        /// </summary>
        static System.Collections.Generic.Dictionary<int, string> Listing(CPUType cpuType)
        {
            if (_listingByType.ContainsKey(cpuType)) return _listingByType[cpuType];
            System.Collections.Generic.Dictionary<int, string> map =
                new System.Collections.Generic.Dictionary<int, string>();

            string[] lines = System.IO.File.ReadAllLines(ListingPath(cpuType));
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                // Format: "NNNN  AAAAAA  text..." - line number, 6-digit octal CS address
                if (ln.Length < 14) continue;
                string addrField = ln.Substring(6, 6);
                bool isOctal = addrField.Length == 6;
                for (int k = 0; k < addrField.Length && isOctal; k++)
                {
                    if (addrField[k] < '0' || addrField[k] > '7') isOctal = false;
                }
                if (!isOctal) continue;

                int addr = Convert.ToInt32(addrField, 8);
                string text = ln.Length > 14 ? ln.Substring(14).Trim() : "";
                if (text.Length == 0) continue;
                if (text.StartsWith("%")) continue; // comments excluded from the compact form

                // squeeze runs of spaces
                StringBuilder sq = new StringBuilder(text.Length);
                bool lastSpace = false;
                for (int k = 0; k < text.Length; k++)
                {
                    char c = text[k];
                    if (c == ' ' || c == '\t')
                    {
                        if (!lastSpace) sq.Append(' ');
                        lastSpace = true;
                    }
                    else
                    {
                        sq.Append(c);
                        lastSpace = false;
                    }
                }
                string compact = sq.ToString();

                if (map.ContainsKey(addr)) map[addr] = map[addr] + " " + compact;
                else map[addr] = compact;
            }
            _listingByType[cpuType] = map;
            return map;
        }

        // Watched register set. Order defines the report columns.
        static readonly string[] RegNames =
        {
            "A", "D", "T", "X", "B", "L", "P", "STS",
            "R1", "R2", "R3", "R4", "R5", "R6", "R7",
            "Q", "F", "GPR", "LC"
        };

        static void Snapshot(CPU cpu, int[] into)
        {
            into[0] = cpu.hw.alu.ReadRegisterE(RegisterEnum.A);
            into[1] = cpu.hw.alu.ReadRegisterE(RegisterEnum.D);
            into[2] = cpu.hw.alu.ReadRegisterE(RegisterEnum.T);
            into[3] = cpu.hw.alu.ReadRegisterE(RegisterEnum.X);
            into[4] = cpu.hw.alu.ReadRegisterE(RegisterEnum.B);
            into[5] = cpu.hw.alu.ReadRegisterE(RegisterEnum.L);
            into[6] = cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
            into[7] = cpu.hw.alu.ReadRegisterE(RegisterEnum.STS);
            into[8] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R1);
            into[9] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
            into[10] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R3);
            into[11] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R4);
            into[12] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R5);
            into[13] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R6);
            into[14] = cpu.hw.alu.ReadRegisterE(RegisterEnum.R7);
            into[15] = cpu.hw.alu.Q;
            into[16] = cpu.hw.alu.F & 0xFFFF;
            into[17] = cpu.hw.alu.GPR;
            into[18] = cpu.hw.mic.loopCounter & 0xFFFF;
        }

        static string Oct(int v) => Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0');

        // ND-110 (RASK) trace cases - the original set. ND-120 (DELILAH-L) cases
        // produce address-IDENTICAL traces to the nd120cpuemu structural port (same
        // microcode), so they are the direct cross-validation oracle for that project;
        // 48-BITS-FLOATING is omitted on ND-120 (the machine is 32F - it errors, same
        // as on ND-110).
        [Explicit("Trace generator - writes reference trace files for external validation")]
        [TestCase("RUN", CPUType.ND110)]
        [TestCase("ARGUMENT", CPUType.ND110)]
        [TestCase("MEMORY-REFERENCE", CPUType.ND110)]
        [TestCase("SEQUENCE", CPUType.ND110)]
        [TestCase("REGISTER-OPERATIONS", CPUType.ND110)]
        [TestCase("BIT-OPERATIONS", CPUType.ND110)]
        [TestCase("SHIFT-INSTRUCTIONS", CPUType.ND110)]
        [TestCase("32-BITS-FLOATING", CPUType.ND110)]
        [TestCase("48-BITS-FLOATING", CPUType.ND110)]
        [TestCase("PRIVILEGED", CPUType.ND110)]
        [TestCase("BYTE-STRING", CPUType.ND110)]
        [TestCase("ND100-24BIT", CPUType.ND110)]
        [TestCase("BCD", CPUType.ND110)]
        [TestCase("ND100-CX", CPUType.ND110)]
        [TestCase("STACK", CPUType.ND110)]
        [TestCase("RUN", CPUType.ND120)]
        [TestCase("ARGUMENT", CPUType.ND120)]
        [TestCase("MEMORY-REFERENCE", CPUType.ND120)]
        [TestCase("SEQUENCE", CPUType.ND120)]
        [TestCase("REGISTER-OPERATIONS", CPUType.ND120)]
        [TestCase("BIT-OPERATIONS", CPUType.ND120)]
        [TestCase("SHIFT-INSTRUCTIONS", CPUType.ND120)]
        [TestCase("32-BITS-FLOATING", CPUType.ND120)]
        [TestCase("PRIVILEGED", CPUType.ND120)]
        [TestCase("BYTE-STRING", CPUType.ND120)]
        [TestCase("ND100-24BIT", CPUType.ND120)]
        [TestCase("BCD", CPUType.ND120)]
        [TestCase("ND100-CX", CPUType.ND120)]
        [TestCase("STACK", CPUType.ND120)]
        public void GenerateTrace(string area, CPUType cpuType)
        {
            System.Collections.Generic.Dictionary<int, string> listing = Listing(cpuType);

            // STERR address + boot budget per CPU type (see TestBootSequence.cs): ND-110
            // RASK STERR=002111(0x449), ND-120 DELILAH-L STERR=002156(0x46E); the L
            // binary is bit-exact so its ~4.19M-cycle power-on wait must run for real.
            ushort sterr = cpuType == CPUType.ND110 ? (ushort)0x449 : (ushort)0x46E;
            int bootBudget = cpuType == CPUType.ND110 ? 200000 : 6000000;

            // ---- boot to the INSTRUCTION-VERIFY prompt (same flow as the area tests) ----
            CPU cpu = new CPU(null, cpuType);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < bootBudget; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr)
                    Assert.Fail("Self-test failed (STERR)");
                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "expected OPCOM after boot");

            StringBuilder console = new StringBuilder();
            void OnText(char c) => console.Append(c);
            // ND-110 console is the IM6402 (Uart); ND-120 is the SCN2661 (Uart2661).
            if (cpuType == CPUType.ND110) cpu.hw.uart.OnTextOutput += OnText;
            else cpu.hw.uart2661.OnTextOutput += OnText;

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10); // MOPC input-loop warm-up
                Type(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20); // banner + '>'
                Assert.That(console.ToString(), Does.Contain(">"), "expected the program prompt");

                Type(cpu, area);        // command echo happens char by char
                console.Clear();        // capture starts at the CR

                string microName = cpuType == CPUType.ND110 ? "RASK (ND-110-RASK)" : "DELILAH-L (ND-120-DELILAH-L)";
                string listingFile = cpuType == CPUType.ND110 ? "ND-110-RASK.LISTING.TXT" : "ND-120-DELILAH-L.LISTING.TXT";
                string symbolsFile = cpuType == CPUType.ND110 ? "ND-110-RASK.SYMBOLS.TXT" : "ND-120-DELILAH-L.SYMBOLS.TXT";

                // ---- traced run ----
                StringBuilder md = new StringBuilder(1 << 22);
                md.AppendLine($"# INSTRUCTION-VERIFY reference trace - `{area}` ({cpuType})");
                md.AppendLine();
                md.AppendLine($"- Program: INSTRUCTION-B (204384B, 83.11.01), started with `0!`, command `{area}`");
                md.AppendLine($"- Emulator: NDMicroCpu.Core as {cpuType}, {microName} microcode (`{listingFile}`)");
                md.AppendLine($"- All values OCTAL. Macro registers are the CURRENT level's bank; `PIL` marks the level.");
                md.AppendLine($"- Watched set: {string.Join(" ", RegNames)} (R1-R7 = current-level scratch, Q/F = Am2901 Q register / ALU output latch, GPR = general purpose shift register, LC = loop counter)");
                md.AppendLine($"- One `###` section per macro instruction: `addr : opcode` + full register state at fetch.");
                md.AppendLine($"- Micro rows: `csar | symbol | changed-registers | microcode source`. Only registers that changed during that microinstruction are shown.");
                md.AppendLine(cpuType == CPUType.ND120
                    ? $"- The `symbol` column comes from `{symbolsFile}` - exact label, or `LABEL+n` (octal offset). The control-store ADDRESSES here are the DELILAH-L addresses, so this trace diffs DIRECTLY (address-for-address) against the nd120cpuemu structural port, which runs the same microcode binary."
                    : $"- The `symbol` column comes from `{symbolsFile}` - exact label, or `LABEL+n` (octal offset from the nearest preceding label). These label names are the PORTABLE cross-reference for the ND-120 DELILAH microcode, whose control-store ADDRESSES differ but whose routine labels are (mostly) the same.");
                md.AppendLine($"- Detailed capture is ARMED at the first macro instruction that executes on a TEST level (PIL 1-9) OR is fetched from the test-framework/test-code region (022000-046777 octal) - whichever happens first - and then records the next {MACRO_CAP} macro instructions across all levels (test code, the framework's check code, and any clock-level interrupts that interleave). The command-dispatch/console preamble (047000+) is skipped (count reported in the summary).");
                md.AppendLine($"- Capture stops at END OF TEST / error / cap.");
                md.AppendLine();

                int[] prev = new int[RegNames.Length];
                int[] cur = new int[RegNames.Length];
                Snapshot(cpu, prev);

                int macroCount = 0;
                int skippedPreamble = 0;
                bool detailArmed = false;
                bool done = false;
                string stopReason = "tick cap reached";

                cpu.hw.AddConsoleChar('\r'); // submit the command - capture is live

                for (int tick = 0; tick < TICK_CAP && !done; tick++)
                {
                    bool fetchPending = cpu.hw.mac.FETCH && !cpu.hw.cpu_regs.STOP;
                    ushort pBefore = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);

                    cpu.TickCpu();

                    if (fetchPending && cpu.hw.mac.FETCH == false)
                    {
                        // A macro instruction was just fetched from pBefore.
                        if (!detailArmed)
                        {
                            // Arm at the first instruction that belongs to the actual
                            // test run: either it executes on a TEST LEVEL (PIL 1-9), or
                            // it is fetched from the TEST-FRAMEWORK/TEST-CODE region of
                            // the image (022000-046777 octal - the runner lives at
                            // 022227+, the per-instruction tests at 023400-046000, while
                            // the console/monitor/dispatch preamble lives at 047000+).
                            // The region term matters for areas whose checks run
                            // entirely on level 0 (PRIVILEGED's MCL/MST/TRA/TRR ladder,
                            // BCD's "(NIY)" stub, 32-BITS-FLOATING, ND100-24BIT,
                            // ND100-CX) - with PIL-only arming those produced EMPTY
                            // traces (armed=False, everything counted as preamble).
                            // EXCEPTION - RUN: the all-in-one command spends thousands of
                            // level-0 instructions in the region starting its stress
                            // levels (clock, dummy output, IOX error) before the first
                            // real test; with region arming the 400-instruction window
                            // filled with pure setup (all PIL=0). RUN therefore arms
                            // strictly on the first TEST-LEVEL instruction.
                            int pilNow = cpu.hw.alu.PIL;
                            bool inTestRegion = pBefore >= 0x2400 && pBefore < 0x4E00; // 022000-046777
                            bool pilArm = pilNow >= 1 && pilNow <= 9;
                            if (pilArm || (inTestRegion && area != "RUN"))
                            {
                                detailArmed = true;
                            }
                            else
                            {
                                skippedPreamble++;
                                continue;
                            }
                        }
                        macroCount++;
                        if (macroCount <= MACRO_CAP)
                        {
                            ushort opcode = cpu.memory.ReadPhysicalMemory(pBefore);
                            Snapshot(cpu, prev);
                            md.AppendLine();
                            md.Append($"### #{macroCount}  `{Oct(pBefore)} : {Oct(opcode)}`  PIL={cpu.hw.alu.PIL}");
                            md.AppendLine();
                            md.Append("`");
                            for (int r = 0; r < RegNames.Length; r++)
                            {
                                if (r > 0) md.Append(' ');
                                md.Append(RegNames[r]).Append('=').Append(Oct(prev[r]));
                            }
                            md.AppendLine("`");
                            md.AppendLine();
                            md.AppendLine("| csar | symbol | changed | microcode |");
                            md.AppendLine("|---|---|---|---|");
                        }
                        continue;
                    }

                    if (!detailArmed || macroCount == 0 || macroCount > MACRO_CAP)
                    {
                        // outside the detailed window - still watch for the end marker
                    }
                    else
                    {
                        ushort csar = cpu.hw.mic.map;
                        Snapshot(cpu, cur);
                        StringBuilder delta = new StringBuilder();
                        for (int r = 0; r < RegNames.Length; r++)
                        {
                            if (cur[r] != prev[r])
                            {
                                if (delta.Length > 0) delta.Append(' ');
                                delta.Append(RegNames[r]).Append('=').Append(Oct(cur[r]));
                                prev[r] = cur[r];
                            }
                        }
                        string src = listing.ContainsKey(csar) ? listing[csar].Replace("|", "\\|") : "";
                        string sym = SymbolFor(csar, cpuType);
                        md.Append("| ").Append(Oct(csar)).Append(" | ")
                          .Append(sym)
                          .Append(" | ")
                          .Append(delta.Length > 0 ? delta.ToString() : "-")
                          .Append(" | `").Append(src).Append("` |");
                        md.AppendLine();
                    }

                    if ((tick & 0x3FF) == 0) // check the console periodically, not per tick
                    {
                        string sofar = console.ToString();
                        if (sofar.Contains("END OF TEST")) { done = true; stopReason = "END OF TEST reached"; }
                        else if (sofar.Contains("*** ERROR") || sofar.Contains("FAILED")) { done = true; stopReason = "program reported an error"; }
                    }
                }
                if (!done && macroCount > MACRO_CAP) stopReason = $"macro cap ({MACRO_CAP}) exceeded before END OF TEST";

                md.AppendLine();
                md.AppendLine("## Run summary");
                md.AppendLine();
                md.AppendLine($"- Preamble macro instructions skipped before the first test-level (PIL 1-9) instruction: {skippedPreamble}");
                md.AppendLine($"- Macro instructions from that point: {macroCount} (detailed: {Math.Min(macroCount, MACRO_CAP)})");
                md.AppendLine($"- Detail window armed: {detailArmed}");
                md.AppendLine($"- Stop reason: {stopReason}");
                md.AppendLine($"- Console output during capture:");
                md.AppendLine();
                md.AppendLine("```");
                md.AppendLine(console.ToString());
                md.AppendLine("```");

                string dir = TestPaths.TracesDir();
                // ND-120 traces get a -ND120 suffix so they sit alongside the ND-110
                // set without overwriting it.
                string suffix = cpuType == CPUType.ND110 ? "" : "-ND120";
                string file = System.IO.Path.Combine(dir, $"TRACE-INSTRUCTION-VERIFY-{area}{suffix}.md");
                System.IO.File.WriteAllText(file, md.ToString());
                TestContext.Out.WriteLine($"trace written: {file} ({md.Length} chars, {macroCount} macro instructions, stop: {stopReason})");
            }
            finally
            {
                if (cpuType == CPUType.ND110) cpu.hw.uart.OnTextOutput -= OnText;
                else cpu.hw.uart2661.OnTextOutput -= OnText;
            }
        }

        static void Type(CPU cpu, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                cpu.hw.AddConsoleChar(text[i]);
                RunCycles(cpu, TICK_PR_20MS * 5);
            }
        }

        static void RunCycles(CPU cpu, int cycles)
        {
            for (int i = 0; i < cycles; i++) cpu.TickCpu();
        }

        // ==============================================================================
        //  Golden CS-path capture: OPCOM "0!" on RASK microcode
        // ==============================================================================

        /// <summary>
        /// Golden-oracle capture for the ND-120 emulator's MOPC console blocker
        /// (HANDOFF.md section 2c in the nd120cpuemu repository): records the exact
        /// control-store address stream THIS (INSTRUCTION-B-validated) emulator takes
        /// while sitting at the OPCOM '#' prompt, consuming '0', and consuming '!'
        /// (start execution at address 0). The ND-120 emulator boots the IDENTICAL
        /// RASK microcode, so the first point where its captured CS path diverges from
        /// this stream pinpoints its emulator-side bug (their observed wrong path:
        /// 002267 MRET1 -> 002273 "output stopped by input" -> 002532 RONLY ->
        /// 002533 SPACE -> 002464-466 panel loop, instead of 002350 NOUTP ->
        /// 002351 INPUT).
        ///
        /// Convention note for diffing: each row records hw.mic.map AFTER TickCpu()
        /// returns, i.e. the address of the NEXT microinstruction to execute - the
        /// same "capture after Step()" convention the ND-120 side used, so streams
        /// align 1:1 without a pipeline offset correction.
        ///
        /// Idle/poll loops are run-length compressed (repeating windows up to 64
        /// microinstructions collapse to one "LOOP xN" block), so the file stays
        /// small even though hundreds of thousands of cycles are captured.
        /// [Explicit] - run on demand, writes traces\TRACE-OPCOM-0BANG-CSPATH.md.
        /// </summary>
        [Explicit("Golden CS-path capture for ND-120 emulator cross-validation - writes a trace file")]
        [Test]
        public void GenerateOpcomZeroBangCsPath()
        {
            // This golden capture is RASK-only (the MOPC console blocker it references
            // is on the ND-110 microcode nd120cpuemu currently boots).
            System.Collections.Generic.Dictionary<int, string> listing = Listing(CPUType.ND110);

            // ---- boot to OPCOM, exactly like GenerateTrace ----
            CPU cpu = new CPU(null, CPUType.ND110);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < 200000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == 0x449)
                    Assert.Fail("Self-test failed (STERR)");
                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "expected OPCOM after boot");

            StringBuilder console = new StringBuilder();
            // Output chars are annotated inline in the CS stream: OnTextOutput fires
            // synchronously inside TickCpu, so the capture index identifies exactly
            // which microinstruction produced each echo/prompt character.
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, char>> outEvents =
                new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, char>>();
            int captureLen = 0;
            bool capturing = false;
            void OnText(char c)
            {
                console.Append(c);
                if (capturing) outEvents.Add(new System.Collections.Generic.KeyValuePair<int, char>(captureLen, c));
            }
            cpu.hw.uart.OnTextOutput += OnText;

            StringBuilder md = new StringBuilder(1 << 20);
            md.AppendLine("# Golden CS-path: OPCOM `0!` on ND-110 RASK microcode");
            md.AppendLine();
            md.AppendLine("- Purpose: reference for the ND-120 emulator's MOPC console blocker (nd120cpuemu HANDOFF.md 2c). Same RASK microcode runs there - diff its captured CS path against the phases below; the FIRST divergence is the emulator-side bug.");
            md.AppendLine("- Each row: `cycle | csar | symbol | microcode source`. csar is `hw.mic.map` AFTER the micro-cycle (address of the NEXT microinstruction), matching the ND-120 side's capture-after-Step convention.");
            md.AppendLine("- `LOOP xN (period P)` blocks are N back-to-back repetitions of the P-row window listed under them (idle/poll loops compressed).");
            md.AppendLine("- `>> UART OUT` lines mark console output emitted at that point in the stream.");
            md.AppendLine("- All control-store addresses OCTAL. Symbols from `ND-110-RASK.SYMBOLS.TXT` (exact label or `LABEL+n`).");
            md.AppendLine();

            // Per-phase capture: tick the CPU, recording hw.mic.map every cycle, then
            // emit the loop-compressed stream. stopWhenRunning ends the phase shortly
            // after the STOP flip-flop clears (program launched) so the '!' phase does
            // not balloon into a full program trace.
            void Capture(string label, int budget, bool stopWhenRunning)
            {
                int[] stream = new int[budget];
                outEvents.Clear();
                captureLen = 0;
                capturing = true;
                int tail = -1; // cycles remaining after STOP clears
                for (int i = 0; i < budget; i++)
                {
                    cpu.TickCpu();
                    stream[captureLen++] = cpu.hw.mic.map;
                    if (stopWhenRunning)
                    {
                        if (tail < 0 && cpu.hw.cpu_regs.STOP == false) tail = 400; // keep a little post-launch context
                        if (tail >= 0 && --tail == 0) break;
                    }
                }
                capturing = false;
                EmitCompressed(md, listing, label, stream, captureLen, outEvents);
            }

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10); // MOPC input-loop warm-up (first-char swallow window)

                // Phase 1: the shape of the healthy idle loop, one full 20ms period.
                Capture("Phase 1 - idle at `#` prompt, no input pending", TICK_PR_20MS, false);

                // Phase 2: consume '0'. MOPC must route it via NOUTP(002350)->INPUT(002351)
                // and echo it. This is the exact window where the ND-120 emulator derails.
                cpu.hw.AddConsoleChar('0');
                Capture("Phase 2 - after AddConsoleChar('0')", TICK_PR_20MS * 5, false);

                // Phase 3: consume '!' = start execution at the typed address (0).
                // Ends shortly after the STOP flip-flop clears (program launched).
                cpu.hw.AddConsoleChar('!');
                Capture("Phase 3 - after AddConsoleChar('!'), until macro execution starts", TICK_PR_20MS * 20, true);

                md.AppendLine();
                md.AppendLine("## Result");
                md.AppendLine();
                md.AppendLine($"- STOP after '!': {cpu.hw.cpu_regs.STOP} (False = program launched)");
                md.AppendLine($"- P after capture: {Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} (octal)");
                md.AppendLine($"- Console output over all phases:");
                md.AppendLine();
                md.AppendLine("```");
                md.AppendLine(console.ToString().Replace("\0", ""));
                md.AppendLine("```");

                Assert.That(cpu.hw.cpu_regs.STOP, Is.False,
                    "expected '0!' to launch macro execution (STOP flip-flop cleared)");

                string dir = TestPaths.TracesDir();
                string file = System.IO.Path.Combine(dir, "TRACE-OPCOM-0BANG-CSPATH.md");
                System.IO.File.WriteAllText(file, md.ToString());
                TestContext.Out.WriteLine($"trace written: {file} ({md.Length} chars)");
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }
        }

        /// <summary>Two equal-length windows of the stream match?</summary>
        static bool SegEq(int[] a, int i, int j, int p)
        {
            for (int k = 0; k < p; k++)
            {
                if (a[i + k] != a[j + k]) return false;
            }
            return true;
        }

        /// <summary>
        /// Emits one captured phase with loop compression: at each position, find the
        /// smallest period p (1..64) whose window repeats back-to-back at least 3 times,
        /// collapse it to a "LOOP xN" block; otherwise emit the single row. UART output
        /// events are interleaved at their capture positions.
        /// </summary>
        static void EmitCompressed(StringBuilder md,
            System.Collections.Generic.Dictionary<int, string> listing,
            string label, int[] a, int n,
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, char>> outs)
        {
            md.AppendLine($"## {label}");
            md.AppendLine();
            md.AppendLine($"micro-cycles captured: {n}");
            md.AppendLine();
            md.AppendLine("```");
            int oi = 0;
            int i = 0;
            while (i < n)
            {
                while (oi < outs.Count && outs[oi].Key <= i)
                {
                    char c = outs[oi].Value;
                    string shown = c >= 0x20 && c < 0x7F ? c.ToString() : $"\\x{(int)c:X2}";
                    md.AppendLine($"        >> UART OUT '{shown}' (0x{(int)c:X2}) at cycle {outs[oi].Key}");
                    oi++;
                }
                int bestP = 0, reps = 0;
                for (int p = 1; p <= 64 && i + 2 * p <= n; p++)
                {
                    int r = 1;
                    while (i + (r + 1) * p <= n && SegEq(a, i, i + r * p, p)) r++;
                    if (r >= 3) { bestP = p; reps = r; break; } // smallest repeating period wins
                }
                if (bestP > 0)
                {
                    md.AppendLine($"LOOP x{reps} (period {bestP}) starting cycle {i}:");
                    for (int k = 0; k < bestP; k++)
                    {
                        md.AppendLine($"    {CsRow(a[i + k], listing)}");
                    }
                    i += reps * bestP;
                }
                else
                {
                    md.AppendLine($"{i,8} {CsRow(a[i], listing)}");
                    i++;
                }
            }
            while (oi < outs.Count)
            {
                char c = outs[oi].Value;
                string shown = c >= 0x20 && c < 0x7F ? c.ToString() : $"\\x{(int)c:X2}";
                md.AppendLine($"        >> UART OUT '{shown}' (0x{(int)c:X2}) at cycle {outs[oi].Key}");
                oi++;
            }
            md.AppendLine("```");
            md.AppendLine();
        }

        /// <summary>
        /// One stream row: octal csar, symbol, trimmed listing source. Only used by
        /// EmitCompressed for the RASK-only golden 0! trace, so symbols are RASK.
        /// </summary>
        static string CsRow(int csar, System.Collections.Generic.Dictionary<int, string> listing)
        {
            string src = listing.ContainsKey(csar) ? listing[csar] : "";
            if (src.Length > 90) src = src.Substring(0, 90);
            return $"{Oct(csar)} {SymbolFor(csar, CPUType.ND110),-12} | {src}";
        }
    }
}
