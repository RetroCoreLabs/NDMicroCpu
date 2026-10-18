//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Text;
using NDMicroCpu.Core.AMD;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Boot
{
    /// <summary>
    /// PIC (AMD 2914) command-trace generator, built for cross-validating a gate-level
    /// ND-120 DELILAH Verilog port of the interrupt controller. On every PIC
    /// microinstruction (InterruptSubSystem.Exec -> both chips) it records one block:
    /// the live CSAR + DELILAH-L symbol + PIL, the instruction name, and a full
    /// before->after snapshot of BOTH chips (high = levels 8-15, low = levels 0-7):
    /// StatusRegister, MaskRegister, InterruptDetectRegister, PriorityEncoder, Vector,
    /// VectorHoldingRegister, and the flip-flops VectorClear / StatusOverflow /
    /// IrqEnable / GroupEnable, plus the derived outputs IRQ and VectorClaim.
    ///
    /// The capture window spans "CLOCK STARTED" (the clean level-13 RTC control case)
    /// through "DUMMY OUTPUT" and the first IOX-error interrupts - exactly the region a
    /// Verilog port livelocks on if its status fence is mis-wired. Our Amd2914PIC.cs
    /// implements the fence correctly and RUN completes, so this trace is the healthy
    /// ground truth. Written to traces\PIC-TRACE-RUN-ND120.md. [Explicit] - long run.
    ///
    /// Sense conventions (captured raw, as the chip stores them):
    ///   Mask       active LOW  (bit=1 -> that level MASKED)
    ///   Detect     active LOW  (bit=0 -> that level PENDING)
    ///   IRQ        active LOW  (0 -> interrupt request ASSERTED by this chip)
    ///   VClaim     active LOW  (0 -> this chip supplies the vector on a read)
    ///   SOvf       active LOW  (0 -> status overflowed = ALL interrupts fenced)
    ///   GrpE       active LOW  (Output_GroupSignal / LGE - the group-enable the port
    ///                           calls HIGE/LOGE; question 2 asks what this gates)
    /// Register values are OCTAL; flip-flops are 0/1.
    /// </summary>
    public class PicCommandTrace
    {
        const int TICK_PR_20MS = 10550 * 4;

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

        static string Oct(int v) => Convert.ToString(v & 0xFFFF, 8).PadLeft(3, '0');
        static string Csar6(int v) => Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0');

        /// <summary>One chip's before-&gt;after line; changed fields annotated "(wasX)".</summary>
        static string Chip(string tag, Am2914.Snapshot b, Am2914.Snapshot a)
        {
            string F(string name, int bv, int av, bool oct) =>
                name + "=" + (oct ? Oct(av) : av.ToString()) + (bv != av ? "(was" + (oct ? Oct(bv) : bv.ToString()) + ")" : "");
            string B(string name, bool bv, bool av) =>
                name + "=" + (av ? "1" : "0") + (bv != av ? "(was" + (bv ? "1" : "0") + ")" : "");

            return $"  {tag} " +
                F("Sta", b.Status, a.Status, true) + " " +
                F("Msk", b.Mask, a.Mask, true) + " " +
                F("Det", b.Detect, a.Detect, true) + " " +
                F("Pri", b.Priority, a.Priority, false) + " " +
                F("Vec", b.Vector, a.Vector, false) + " " +
                F("VHld", b.VectorHold, a.VectorHold, false) + " " +
                B("VClr", b.VectorClearFF, a.VectorClearFF) + " " +
                B("SOvf", b.StatusOvfFF, a.StatusOvfFF) + " " +
                B("IrqE", b.IrqEnableFF, a.IrqEnableFF) + " " +
                B("GrpE", b.GroupEnableFF, a.GroupEnableFF) + " " +
                B("IRQ", b.Irq, a.Irq) + " " +
                B("VClm", b.VectorClaim, a.VectorClaim);
        }

        [Explicit("PIC command trace for the ND-120 Verilog interrupt-controller port - long run")]
        [Test]
        public void GeneratePicTrace_ND120_Run()
        {
            // ---- boot ND-120 (DELILAH-L) to OPCOM ----
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
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "expected OPCOM after boot");

            StringBuilder console = new StringBuilder();
            StringBuilder md = new StringBuilder(1 << 22);
            int picLines = 0;
            const int MAX_PIC_LINES = 8000;
            bool capturing = false;
            string lastMarker = "(none)";

            // First RDVC observed while capturing - used for the computed answer block.
            bool firstRdvcSaved = false;
            Am2914.Snapshot rdHiB = default, rdHiA = default, rdLoB = default, rdLoA = default;
            int rdCsar = 0, rdPil = 0;

            void OnText(char c) => console.Append(c);
            cpu.hw.uart2661.OnTextOutput += OnText;

            cpu.hw.pic.OnPicExec += (instr, hb, ha, lb, la) =>
            {
                if (!capturing || picLines >= MAX_PIC_LINES) return;
                int csar = cpu.hw.mic.map;
                int pil = cpu.hw.alu.PIL;
                picLines++;

                md.Append('#').Append(picLines)
                  .Append(" CSAR=").Append(Csar6(csar))
                  .Append(" SYM=").Append(SymbolFor(csar))
                  .Append(" PIL=").Append(pil)
                  .Append(" INSTR=").Append(instr.ToString())
                  .AppendLine();
                md.AppendLine(Chip("HI", hb, ha));
                md.AppendLine(Chip("LO", lb, la));

                if (!firstRdvcSaved && instr == Am2914.Am2914Instruction.RDVC)
                {
                    firstRdvcSaved = true;
                    rdHiB = hb; rdHiA = ha; rdLoB = lb; rdLoA = la; rdCsar = csar; rdPil = pil;
                }
            };

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

                // Arm capture BEFORE submitting RUN, so the first MAX_PIC_LINES PIC ops
                // are exactly the setup window the Verilog port livelocks on: CLOCK
                // STARTED (clean level-13 RTC control case) -> DUMMY OUTPUT -> the first
                // IOX-error interrupts. Fine chunks (TICK_PR_20MS/2) so the inline
                // console markers land in order relative to the PIC ops, not in a
                // single coarse burst.
                capturing = true;
                Type(cpu, "RUN\r");

                string[] marks = { "CLOCK STARTED", "DUMMY OUTPUT", "IOX-ERROR",
                    "LEVEL 13", "ARGUMENT INSTRUCTIONS", "END OF TEST" };
                var seenMarks = new System.Collections.Generic.HashSet<string>();
                for (int chunk = 0; chunk < 4000 && picLines < MAX_PIC_LINES; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS / 2);
                    string norm = console.ToString().Replace("\0", "");

                    for (int m = 0; m < marks.Length; m++)
                    {
                        if (norm.Contains(marks[m]) && !seenMarks.Contains(marks[m]))
                        {
                            seenMarks.Add(marks[m]);
                            lastMarker = marks[m];
                            md.AppendLine().AppendLine($"----- console marker: {marks[m]} -----").AppendLine();
                        }
                    }
                    if (norm.Contains("END OF TEST")) break;
                }
            }
            finally
            {
                cpu.hw.uart2661.OnTextOutput -= OnText;
            }

            // ---- header + computed answers to the two open questions ----
            StringBuilder head = new StringBuilder();
            head.AppendLine("# PIC (AMD 2914) command trace - INSTRUCTION-B RUN on ND-120 (DELILAH-L)");
            head.AppendLine();
            head.AppendLine("Ground truth for a gate-level DELILAH interrupt-controller port. Our Amd2914PIC.cs");
            head.AppendLine("implements the status fence correctly and RUN completes (no livelock), so every line");
            head.AppendLine("below is the HEALTHY behavior the Verilog must reproduce.");
            head.AppendLine();
            head.AppendLine("Two chips: HI = Am2914 for levels 8-15, LO = levels 0-7 (cascaded).");
            head.AppendLine("Per line: `#n CSAR=<oct> SYM=<DELILAH-L label> PIL=<level> INSTR=<PIC op>`, then HI and LO");
            head.AppendLine("with each field `Name=after(wasBefore)` - `(wasX)` shown only when the field changed.");
            head.AppendLine("Registers OCTAL; flip-flops 0/1. Sense (active-LOW signals captured raw):");
            head.AppendLine("  Sta=StatusRegister(fence)  Msk=Mask(1=masked)  Det=Detect(0=pending)  Pri=PriorityEncoder");
            head.AppendLine("  Vec=VectorOutput  VHld=VectorHoldingRegister  VClr=VectorClearFF  SOvf=StatusOverflow(0=overflowed)");
            head.AppendLine("  IrqE=IrqEnableFF  GrpE=GroupEnable/LGE(active-LOW)  IRQ=Output(0=asserted)  VClm=VectorClaim(0=this chip wins)");
            head.AppendLine();
            head.AppendLine("PIC instruction names: MCLR CLRIN CLRMB CLRMR CLRVC RDVC RDSTA RDM SETM LDSTA BCLRM BSETM CLRM DISIN LDM ENIN.");
            head.AppendLine();
            head.AppendLine("## Direct answers to the two open questions (from the first RDVC observed)");
            head.AppendLine();
            if (firstRdvcSaved)
            {
                head.AppendLine($"First RDVC at CSAR={Csar6(rdCsar)} ({SymbolFor(rdCsar)}), PIL={rdPil}:");
                head.AppendLine("```");
                head.AppendLine(Chip("HI", rdHiB, rdHiA));
                head.AppendLine(Chip("LO", rdLoB, rdLoA));
                head.AppendLine("```");
                head.AppendLine("Q1 (StatusRegister after RDVC = vector+1? on which chip?):");
                head.AppendLine($"  HI Status {Oct(rdHiB.Status)} -> {Oct(rdHiA.Status)} (vector was {rdHiB.Vector}, VClaim after={(rdHiA.VectorClaim ? "1/no-win" : "0/WINS")})");
                head.AppendLine($"  LO Status {Oct(rdLoB.Status)} -> {Oct(rdLoA.Status)} (vector was {rdLoB.Vector}, VClaim after={(rdLoA.VectorClaim ? "1/no-win" : "0/WINS")})");
                head.AppendLine("  -> the chip whose Status advanced to vector+1 is the one that supplied the vector;");
                head.AppendLine("     the other chip's Status (unchanged) shows the load is on the winner only.");
                head.AppendLine("Q2 (what gates the load - GroupEnable/LGE at that moment):");
                head.AppendLine($"  HI GrpE={(rdHiB.GroupEnableFF ? 1 : 0)} (before) / {(rdHiA.GroupEnableFF ? 1 : 0)} (after)");
                head.AppendLine($"  LO GrpE={(rdLoB.GroupEnableFF ? 1 : 0)} (before) / {(rdLoA.GroupEnableFF ? 1 : 0)} (after)");
                head.AppendLine("  Cross-reference GrpE against which chip's Status loaded to confirm whether LGE qualifies it.");
            }
            else
            {
                head.AppendLine("(No RDVC captured in the window - widen MAX_PIC_LINES or the marker start.)");
            }
            head.AppendLine();
            head.AppendLine($"Captured {picLines} PIC micro-ops. Last console marker reached: {lastMarker}.");
            head.AppendLine();
            head.AppendLine("---");
            head.AppendLine();

            string dir = TestPaths.TracesDir();
            string file = System.IO.Path.Combine(dir, "PIC-TRACE-RUN-ND120.md");
            System.IO.File.WriteAllText(file, head.ToString() + md.ToString());
            TestContext.Out.WriteLine($"PIC trace written: {file} ({picLines} PIC ops, {(head.Length + md.Length)} chars)");

            Assert.That(picLines, Is.GreaterThan(0), "expected at least one PIC micro-op in the RUN window");
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
