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
    /// Internal-interrupt (IIC) path trace for the ND-120 RUN / IOX-ERROR phase, built to
    /// cross-validate a gate-level DELILAH interrupt-controller port. It answers exactly one
    /// question: through the TRA IIC scan (AIIC1..AIIC2, CS 725-742 octal), what are IID, IIE,
    /// IID&amp;IIE, and the IIC that TRA IIC finally returns into A - aligned to CSAR/symbol - so a
    /// wrong IID bit (source detection) can be told apart from a wrong vector-&gt;code mapping in
    /// the scan.
    ///
    /// SENSE (the C# models the DELILAH two-chip Am2914, HIGH=levels 8-15, LOW=levels 0-7):
    ///   * Detect register is active LOW (bit=0 =&gt; that IREQ input is PENDING). So the ND-100
    ///     style active-high IID = (~detect) &amp; 0xFFFF.
    ///   * Mask register is active LOW (bit=1 =&gt; MASKED). The ND-100 style active-high IIE
    ///     (enabled) = (~mask) &amp; 0xFFFF.
    ///   * IID &amp; IIE (pending AND enabled) = (~detect) &amp; (~mask).
    ///   * The Am2914 IREQ bit positions are NOT the ND-100 IIC code. Known hardware inputs
    ///     (from the C# IOX-error path + the Verilog spec): IOX = IREQ bit 10 (HIGH-chip bit 2,
    ///     hivec 2); MOR = IREQ bit 12 (HIGH-chip bit 4, hivec 4). The AIIC microcode scan is
    ///     what TRANSLATES the winning IREQ/vector to the ND-100 IIC code (IOX-&gt;7, MOR-&gt;11 oct).
    ///   * So the "IIC returned" column is A AFTER TRA IIC completes = the microcode's translated
    ///     code; comparing it to the highest IID&amp;IIE IREQ bit pins where a port diverges:
    ///       - wrong IID bit set        =&gt; the IREQ/pending bits differ from this golden.
    ///       - wrong vector-&gt;code map    =&gt; IID&amp;IIE matches but A (returned IIC) differs.
    ///
    /// Window: from the "DUMMY OUTPUT" console marker, through "IOX-ERROR", to "LEVEL 13".
    /// Writes traces\IIC-PATH-RUN-ND120.md. [Explicit] - long ND-120 boot + RUN.
    /// </summary>
    public class IicPathTrace
    {
        const int TICK_PR_20MS = 10550 * 4;

        // AIIC scan (TRA IIC) microcode window, DELILAH-L: AIIC1=725, AIIC3=737, AIIC2=742 (octal).
        const int AIIC_LO = 0x1D5; // 000725
        const int AIIC_HI = 0x1E5; // 000745 (a little past AIIC2 to catch its tail)

        static readonly string SymbolsPath =
            System.IO.Path.Combine(TestPaths.MicrocodeDir(), "ND-120-DELILAH-L.SYMBOLS.TXT");

        static System.Collections.Generic.Dictionary<int, string> _symbols = null!;   // LoadSymbols fills it before any use
        static int[] _symbolAddrs = null!;

        static void LoadSymbols()
        {
            if (_symbols != null) return;
            var map = new System.Collections.Generic.Dictionary<int, string>();
            string[] lines = System.IO.File.ReadAllLines(SymbolsPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length < 7 || ln[6] != '=') continue;
                bool ok = true;
                for (int k = 0; k < 6 && ok; k++)
                    if (ln[k] < '0' || ln[k] > '7') ok = false;
                if (!ok) continue;
                map[Convert.ToInt32(ln.Substring(0, 6), 8)] = ln.Substring(7).Trim();
            }
            _symbols = map;
            _symbolAddrs = new int[map.Count];
            map.Keys.CopyTo(_symbolAddrs, 0);
            Array.Sort(_symbolAddrs);
        }

        static string SymbolFor(int csar)
        {
            LoadSymbols();
            if (_symbols.ContainsKey(csar)) return _symbols[csar];
            int idx = Array.BinarySearch(_symbolAddrs, csar);
            if (idx < 0) idx = ~idx - 1;
            if (idx < 0) return "";
            int b = _symbolAddrs[idx];
            return _symbols[b] + "+" + Convert.ToString(csar - b, 8);
        }

        static long _aiicTicks, _traIicCount;

        static string Oct6(int v) => Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0');
        static string Oct(int v) => Convert.ToString(v & 0xFFFF, 8);

        // Name the KNOWN internal-interrupt IREQ inputs in the active-high pending word. Only the
        // bits we can source with certainty are named; everything else is shown as bitN.
        static string DecodePending(int pending)
        {
            if ((pending & 0xFFFF) == 0) return "(none)";
            StringBuilder sb = new StringBuilder();
            for (int b = 15; b >= 0; b--)
            {
                if ((pending & (1 << b)) == 0) continue;
                if (sb.Length > 0) sb.Append(',');
                string name = b switch
                {
                    10 => "IOX",      // IREQ bit 10 = HIGH-chip bit 2 = hivec 2  (IIC code 7)
                    12 => "MOR",      // IREQ bit 12 = HIGH-chip bit 4 = hivec 4  (IIC code 11 oct)
                    13 => "POW?",     // power fail region
                    11 => "PTY?",     // parity region
                    _ => "bit" + b
                };
                sb.Append(name).Append('(').Append(b).Append(')');
            }
            return sb.ToString();
        }

        static int HighestBit(int v)
        {
            v &= 0xFFFF;
            for (int b = 15; b >= 0; b--) if ((v & (1 << b)) != 0) return b;
            return -1;
        }

        [Explicit("IIC-path trace for the ND-120 Verilog interrupt-controller port - long run")]
        [Test]
        public void GenerateIicTrace_ND120_Run()
        {
            CPU cpu = new CPU(null, CPUType.ND120);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            const ushort sterr = 0x46E;
            cpu.hw.mic.LatchMap(0);
            bool booted = false;
            for (int i = 0; i < 6000000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr) Assert.Fail("Self-test failed (STERR)");
                if (cpu.hw.mac.FETCH) { booted = true; break; }
            }
            Assert.That(booted, Is.True, "ND-120 boot never reached FETCH");

            StringBuilder console = new StringBuilder();
            StringBuilder rows = new StringBuilder(1 << 20);
            int rowCount = 0;
            const int MAX_ROWS = 4000;

            void OnText(char c) => console.Append(c);
            cpu.hw.uart2661.OnTextOutput += OnText;

            // Emit one markdown table row.
            void Row(string trigger, int csar, int pil, int detect, int mask, int aVal, bool haveA)
            {
                int pend = (~detect) & 0xFFFF;      // active-high IID (pending)
                int en = (~mask) & 0xFFFF;          // active-high IIE (enabled)
                int prio = pend & en;               // IID & IIE
                int hb = HighestBit(prio);
                rows.Append("| ").Append(++rowCount)
                    .Append(" | ").Append(trigger)
                    .Append(" | ").Append(Oct6(csar)).Append(' ').Append(SymbolFor(csar))
                    .Append(" | ").Append(pil)
                    .Append(" | ").Append(Oct6(detect)).Append(" / ").Append(Oct(pend))
                    .Append(" | ").Append(Oct6(mask)).Append(" / ").Append(Oct(en))
                    .Append(" | ").Append(Oct(prio)).Append(" [").Append(DecodePending(prio)).Append(']')
                    .Append(" | ").Append(hb < 0 ? "-" : hb.ToString())
                    .Append(" | ").Append(haveA ? (Oct(aVal) + " oct = " + aVal + " dec") : "")
                    .AppendLine(" |");
            }

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                Type(cpu, "0!");
                for (int chunk = 0; chunk < 400; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    if (console.ToString().Replace("\0", "").Contains(">")) break;
                }
                Assert.That(console.ToString().Replace("\0", ""), Does.Contain(">"), "precondition: prompt");
                console.Clear();

                Type(cpu, "RUN\r");

                // --- Phase A: FAST-FORWARD to just before the window. The ND-120 RUN sweep is many
                //     hundreds of millions of micro-cycles; per-tick sampling all of it is
                //     infeasible. Arm at "CLOCK STARTED" (the marker BEFORE DUMMY OUTPUT) using
                //     SMALL chunks, so fine capture then brackets the whole DUMMY OUTPUT ->
                //     IOX-ERROR -> LEVEL 13 region without overshooting the brief IOX-error pulse. ---
                bool armed = false;
                for (int chunk = 0; chunk < 40000 && !armed; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS);
                    if (console.ToString().Replace("\0", "").Contains("IOX-ERROR")) armed = true;
                }

                // --- Phase B: FINE per-tick capture from DUMMY OUTPUT to LEVEL 13. Console markers
                //     are checked only every N ticks (ToString() is O(n)); everything else is the
                //     per-tick IID/IIE/CSAR sampling the user needs. ---
                if (armed)
                {
                    int prevDetect = cpu.hw.pic.ReadInterruptDetect();
                    int prevMask = cpu.hw.pic.ReadMask();
                    int prevPil = cpu.hw.alu.PIL;
                    bool inAiic = false;                 // currently inside the TRA IIC scan
                    int aiicEntryDetect = 0, aiicEntryMask = 0;

                    // Macro-level TRA IIC (opcode 150005 octal = 0xD005) detection, independent of
                    // the microcode CSAR path - the definitive "did a TRA IIC run and what did it
                    // return in A" signal.
                    const int TRA_IIC_OPCODE = 0xD005;
                    int prevIR = cpu.hw.cpu_regs.IR;
                    bool traPending = false;
                    int traDetect = 0, traMask = 0, traCsar = 0, traPil = 0;
                    long aiicTicks = 0, traIicCount = 0;

                    // Filter out the two big noise sources so only the IIC path shows: the 20ms RTC
                    // clock (level 13, detect bit 3) and the idle level-switch loop (PIL 0<->1).
                    // Bit 3 = the RTC pending bit; everything else in the pending word is a real
                    // internal-interrupt source (IOX bit10, MOR bit12, ...).
                    const int RTC_BIT = 1 << 3;

                    const long FINE_CAP = 900_000_000;   // whole CLOCK..END span, safety-bounded
                    const int CONSOLE_EVERY = 200000;
                    long fineTicks = 0, sinceCheck = 0;
                    bool done = false;

                    while (!done && fineTicks < FINE_CAP && rowCount < MAX_ROWS)
                    {
                        cpu.TickCpu();
                        fineTicks++;

                        int csar = cpu.hw.mic.map;
                        int detect = cpu.hw.pic.ReadInterruptDetect();
                        int mask = cpu.hw.pic.ReadMask();
                        int pil = cpu.hw.alu.PIL;

                        // (1) IID change - but ignore the pure-RTC toggling. Emit only when the
                        //     NON-RTC pending bits change (this is where IOX bit10 / MOR bit12 show).
                        int pendNow = (~detect) & 0xFFFF & ~RTC_BIT;
                        int pendPrev = (~prevDetect) & 0xFFFF & ~RTC_BIT;
                        if (pendNow != pendPrev)
                            Row("IID chg", csar, pil, detect, mask, 0, false);

                        // (2) IIE change (rare, always interesting).
                        if (mask != prevMask)
                            Row("IIE chg", csar, pil, detect, mask, 0, false);

                        // (3) PIL change - only the internal-interrupt level 14 (suppress 0/1/13).
                        if (pil != prevPil && (pil == 14 || prevPil == 14))
                            Row("PIL " + prevPil + "->" + pil, csar, pil, detect, mask, 0, false);

                        // (4) TRA IIC scan (AIIC1..AIIC2): emit each distinct CSAR step, and when the
                        //     scan RETURNS to macro code read A = the IIC it produced.
                        bool nowInAiic = csar >= AIIC_LO && csar <= AIIC_HI;
                        if (nowInAiic)
                        {
                            aiicTicks++;
                            if (!inAiic) { inAiic = true; aiicEntryDetect = detect; aiicEntryMask = mask; }
                            Row("AIIC scan", csar, pil, detect, mask, 0, false);
                        }
                        else if (inAiic)
                        {
                            inAiic = false;
                            int a = cpu.hw.alu.ReadRegisterE(RegisterEnum.A);
                            Row("TRA IIC -> A", csar, pil, aiicEntryDetect, aiicEntryMask, a, true);
                        }

                        // Macro-level TRA IIC: rising edge of IR==0xD005 captures pre-state; when IR
                        // moves on, the instruction has completed and A holds the returned IIC code.
                        int ir = cpu.hw.cpu_regs.IR;
                        if (ir == TRA_IIC_OPCODE && prevIR != TRA_IIC_OPCODE)
                        {
                            traIicCount++;
                            traPending = true;
                            traDetect = detect; traMask = mask; traCsar = csar; traPil = pil;
                        }
                        else if (traPending && ir != TRA_IIC_OPCODE)
                        {
                            traPending = false;
                            int a = cpu.hw.alu.ReadRegisterE(RegisterEnum.A);
                            Row("**TRA IIC(150005)**", traCsar, traPil, traDetect, traMask, a, true);
                        }

                        // Every MACRO IOX instruction (164000-167777 = 0xE800..0xEFFF): this is what
                        // the IOX-ERROR test issues. Log P (fetch addr), device number, PIL, detect -
                        // so we can see whether the IOX is claimed (no detect change) or errors
                        // (detect bit 10 goes pending). "A" column reused to show the device number.
                        if ((ir & 0xF800) == 0xE800 && prevIR != ir)
                        {
                            int dev = ir & 0x7FF;
                            int p = cpu.hw.mac.fetch_address;
                            Row("IOX dev " + Convert.ToString(dev, 8) + " @P=" + Oct6(p), csar, pil, detect, mask, dev, true);
                        }
                        prevIR = ir;

                        prevDetect = detect; prevMask = mask; prevPil = pil;

                        if (++sinceCheck >= CONSOLE_EVERY)
                        {
                            sinceCheck = 0;
                            // Narrow window: just the IOX-ERROR section, up to the LEVEL 13 marker.
                            if (console.ToString().Replace("\0", "").Contains("LEVEL 13")) done = true;
                        }
                    }

                    _aiicTicks = aiicTicks; _traIicCount = traIicCount;
                }
            }
            finally
            {
                cpu.hw.uart2661.OnTextOutput -= OnText;
            }

            StringBuilder head = new StringBuilder();
            head.AppendLine("# ND-120 (DELILAH-L) internal-interrupt (IIC) path trace - RUN / IOX-ERROR window");
            head.AppendLine();
            head.AppendLine("Golden from the C# oracle (RUN passes IOX-ERROR). Window: DUMMY OUTPUT -> IOX-ERROR -> LEVEL 13.");
            head.AppendLine();
            head.AppendLine("Two-chip Am2914: HIGH=levels 8-15, LOW=levels 0-7. Detect active-LOW (0=pending); Mask active-LOW");
            head.AppendLine("(1=masked). Columns show BOTH raw chip value and the ND-100 active-high derivation:");
            head.AppendLine();
            head.AppendLine("- **IID** = `detect(raw octal) / ~detect(pending, octal)`  (pending bits = internal-interrupt IREQ inputs)");
            head.AppendLine("- **IIE** = `mask(raw octal) / ~mask(enabled, octal)`");
            head.AppendLine("- **IID & IIE** = pending AND enabled, with known sources named");
            head.AppendLine("  (IOX = IREQ bit10/hivec2 -> IIC 7; MOR = IREQ bit12/hivec4 -> IIC 11 oct).");
            head.AppendLine("- **hi bit** = highest set bit of IID&IIE (the winning IREQ bit, Am2914 space - NOT the IIC code).");
            head.AppendLine("- **IIC ret (A)** = A register AFTER TRA IIC completes = the microcode's translated ND-100 IIC code.");
            head.AppendLine();
            head.AppendLine("Divergence test: if IID&IIE matches your port but `IIC ret (A)` differs, the bug is the vector->code");
            head.AppendLine("mapping in the AIIC scan; if IID&IIE itself differs (wrong/absent IREQ bit), it is source detection.");
            head.AppendLine();
            head.AppendLine("| # | trigger | CSAR + DELILAH-L sym | PIL | IID detect/pend | IIE mask/en | IID&IIE [src] | hi bit | IIC ret (A) |");
            head.AppendLine("|---|---------|----------------------|-----|-----------------|-------------|---------------|--------|-------------|");

            // Console transcript tail (debug: confirms the RUN markers actually printed).
            string transcript = console.ToString().Replace("\0", "");
            head.AppendLine();
            head.AppendLine("<details><summary>RUN console transcript (tail)</summary>");
            head.AppendLine();
            head.AppendLine("```");
            head.AppendLine(transcript.Length > 4000 ? transcript.Substring(transcript.Length - 4000) : transcript);
            head.AppendLine("```");
            head.AppendLine("</details>");
            head.AppendLine();

            string dir = TestPaths.TracesDir();
            string file = System.IO.Path.Combine(dir, "IIC-PATH-RUN-ND120.md");
            System.IO.File.WriteAllText(file, head.ToString() + rows.ToString());
            TestContext.Out.WriteLine($"IIC path trace written: {file} ({rowCount} rows)");
            TestContext.Out.WriteLine($"COUNTERS: AIIC-range ticks={_aiicTicks}, TRA IIC(150005) macro executions={_traIicCount}");
            TestContext.Out.WriteLine("TRANSCRIPT TAIL: " + (transcript.Length > 600 ? transcript.Substring(transcript.Length - 600) : transcript));
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
    }
}
