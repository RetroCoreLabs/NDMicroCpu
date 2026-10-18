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
    /// DIRECT TRA IIC golden for the ND-120 DELILAH-L, built to settle the Verilog port's
    /// IOX-ERROR / level-14 livelock hypothesis. RUN never runs TRA IIC in the C# (the IOX
    /// error is handled another way), so we drive the AIIC scan (CS 725-742) DIRECTLY: set a
    /// controlled internal-interrupt PIC state, execute a macro TRA IIC (150005 octal), trace
    /// the microcode scan cycle by cycle, and read A = the IIC it returns.
    ///
    /// Verilog hypothesis under test: after the IOX handler, only PID bit 14 (hivec 6, set by
    /// SMPID at CS 740) remains; if the AIIC scan's mask (IIE &amp; 37760 octal = bits 4-13) does
    /// NOT exclude bit 14, the scan reads hivec 6 and encodes IIC = hivec+3 = 9 = MOR (11 oct)
    /// instead of IOX = 7. So the decisive C# facts are:
    ///   Scenario A: only IOX pending (detect bit 10 / hivec 2)          -> expect A = 7 (IOX).
    ///   Scenario B: IOX + level-14 request pending (bit 10 AND bit 14)   -> A should STILL be 7
    ///               if the 37760 mask excludes bit 14; A = 11 oct (MOR) would reproduce the bug.
    ///   Scenario C: only bit 14 pending (the leaked level-14 request)     -> what does it encode?
    ///
    /// Bit map (global 16-bit, HIGH chip = bits 8-15): IOX = bit 10 = hivec 2; MOR = bit 12 =
    /// hivec 4; level-14 request = bit 14 = hivec 6. Detect is active-LOW (0 = pending); Mask is
    /// active-LOW (1 = masked). [Explicit] - long ND-120 boot.
    /// </summary>
    public class DirectTraIicGolden
    {
        const int TICK_PR_20MS = 10550 * 4;
        const int TRA_IIC = 0xD005;          // 150005 octal
        const int AIIC_LO = 0x1D5, AIIC_HI = 0x1E5; // 000725..000745

        static System.Collections.Generic.Dictionary<int, string> _symbols = null!;   // LoadSymbols fills it before any use
        static int[] _symbolAddrs = null!;
        static readonly string SymbolsPath =
            System.IO.Path.Combine(TestPaths.MicrocodeDir(), "ND-120-DELILAH-L.SYMBOLS.TXT");

        static void LoadSymbols()
        {
            if (_symbols != null) return;
            var map = new System.Collections.Generic.Dictionary<int, string>();
            string[] rawLines = System.IO.File.ReadAllLines(SymbolsPath);
            for (int lineIdx = 0; lineIdx < rawLines.Length; lineIdx++)
            {
                string ln = rawLines[lineIdx].Trim();
                if (ln.Length < 7 || ln[6] != '=') continue;
                bool ok = true;
                for (int k = 0; k < 6 && ok; k++) if (ln[k] < '0' || ln[k] > '7') ok = false;
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
            return _symbols[_symbolAddrs[idx]] + "+" + Convert.ToString(csar - _symbolAddrs[idx], 8);
        }

        static string Oct(int v) => Convert.ToString(v & 0xFFFF, 8);
        static string Oct6(int v) => Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0');

        static CPU BootNd120()
        {
            CPU cpu = new CPU(null, CPUType.ND120);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < 6000000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == 0x46E) Assert.Fail("STERR");
                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "expected OPCOM");
            return cpu;
        }

        // Execute one TRA IIC at address p, tracing the microcode CSAR path (esp. the AIIC scan)
        // and returning A plus the step trace. detectBits = the global 16-bit pending pattern to
        // raise (active-high here; converted to the chip's active-low set internally).
        const int TRR_IIE = 0xD105;          // 150405 octal - TRR IIE (writes A -> IIE)

        static (int a, string trace) RunTraIic(CPU cpu, int pendingBits, int iieValue)
        {
            ushort p1 = (ushort)Utils.Oct2Int("1000");
            ushort p2 = (ushort)Utils.Oct2Int("1001");

            // Faithfully program IIE first via a real TRR IIE (150405): A = enable mask -> IIE.
            cpu.memory.WritePhysicalMemory(p1, (ushort)TRR_IIE);
            cpu.WriteALURegister(RegisterEnum.A, (ushort)iieValue);
            cpu.UnitTest_FetchAndExecute(p1);

            // Raise the requested internal-interrupt sources on the PIC (active-high pattern;
            // SetInterruptDetectbits clears the matching active-low detect bits = makes pending).
            cpu.hw.pic.SetInterruptDetectbits((ushort)pendingBits);

            int detBefore = cpu.hw.pic.ReadInterruptDetect();
            int mskBefore = cpu.hw.pic.ReadMask();

            // Execute the single TRA IIC macro instruction (runs the whole AIIC scan microcode).
            cpu.memory.WritePhysicalMemory(p2, (ushort)TRA_IIC);
            cpu.UnitTest_FetchAndExecute(p2);

            int a = cpu.ReadALURegister(RegisterEnum.A);
            int detAfter = cpu.hw.pic.ReadInterruptDetect();
            int mskAfter = cpu.hw.pic.ReadMask();

            StringBuilder tr = new StringBuilder();
            tr.AppendLine($"  IIE set via TRR IIE: A={Oct(iieValue)}");
            tr.AppendLine($"  setup : raised pending={Oct(pendingBits)}  detect={Oct6(detBefore)} Am2914mask={Oct6(mskBefore)}");
            tr.AppendLine($"  after : detect={Oct6(detAfter)} Am2914mask={Oct6(mskAfter)}  (detect change = what TRA IIC cleared)");
            return (a, tr.ToString());
        }

        [Explicit("Direct TRA IIC golden for the ND-120 Verilog port - long boot")]
        [Test]
        public void GenerateDirectTraIicGolden_ND120()
        {
            StringBuilder md = new StringBuilder();
            md.AppendLine("# ND-120 (DELILAH-L) DIRECT TRA IIC golden - IOX vs level-14(MOR) encoding");
            md.AppendLine();
            md.AppendLine("Executes a macro TRA IIC (150005 octal) directly, under a controlled PIC state, and reads");
            md.AppendLine("A = the IIC returned. IOX = detect bit 10 (hivec 2); MOR = bit 12 (hivec 4); level-14");
            md.AppendLine("request = bit 14 (hivec 6). Expected IIC codes: IOX=7, MOR=11 oct(9 dec).");
            md.AppendLine();

            var scenarios = new (string name, int bits)[]
            {
                ("A: IOX only (bit10/hivec2)", 1 << 10),
                ("B: IOX + level-14 req (bit10 + bit14/hivec6)", (1 << 10) | (1 << 14)),
                ("C: level-14 req only (bit14/hivec6)", 1 << 14),
                ("D: MOR only (bit12/hivec4)", 1 << 12),
            };

            for (int scIdx = 0; scIdx < scenarios.Length; scIdx++)
            {
                var s = scenarios[scIdx];
                CPU cpu = BootNd120();
                // IIE = enable everything (0xFFFF); the AIIC scan's own 37760 mask (bits 4-13) is
                // what must exclude the level-14 request (bit 14) - that is exactly what we test.
                var (a, trace) = RunTraIic(cpu, s.bits, 0xFFFF);
                md.AppendLine($"## {s.name}");
                md.AppendLine("```");
                md.Append(trace);
                md.AppendLine($"  => TRA IIC returned A = {Oct(a)} oct = {a} dec  " +
                              $"({(a == 7 ? "IOX" : a == 9 ? "MOR" : "other")})");
                md.AppendLine("```");
                md.AppendLine();
                TestContext.Out.WriteLine($"{s.name}: A = {Oct(a)} oct ({a} dec)");
            }

            string dir = TestPaths.TracesDir();
            string file = System.IO.Path.Combine(dir, "DIRECT-TRA-IIC-ND120.md");
            System.IO.File.WriteAllText(file, md.ToString());
            TestContext.Out.WriteLine($"written: {file}");
        }
    }
}
