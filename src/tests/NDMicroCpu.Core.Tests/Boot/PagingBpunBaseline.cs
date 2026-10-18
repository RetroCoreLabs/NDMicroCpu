//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Text;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.Support.FileLoader;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Boot
{
    /// <summary>
    /// BASELINE harness for the paging test program (NDMicroCpu.Machine\BPUN\paging-b.bpun). Establishes
    /// the golden PASS output of the paging tests on the CURRENT emulator so any CPU hot-path change
    /// (e.g. per-cycle allocation guards, or a future MMU translation cache) can be proven not to
    /// regress paging. Loads the BPUN into RAM, boots to OPCOM, starts it, and captures the console.
    /// [Explicit] - run manually to see/lock the golden.
    /// </summary>
    public class PagingBpunBaseline
    {
        const int TICK_PR_20MS = 10550 * 4;

        static readonly string PagingBpun =
            System.IO.Path.Combine(TestPaths.BpunDir(), "paging-b.bpun");

        // Count non-overlapping occurrences of needle in haystack (no LINQ).
        static int CountOf(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        static void Type(CPU cpu, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                cpu.hw.AddConsoleChar(s[i]);
                for (int c = 0; c < TICK_PR_20MS * 5; c++) cpu.TickCpu();
            }
        }

        // Run the real paging-b program under EVERY MMU mode; all must reproduce the golden
        // byte-for-byte (the three modes are behaviour-identical, this proves it on a real workload).
        [Explicit("Baseline capture of paging-b.bpun output (ND-110)")]
        [TestCase(MmuMode.Legacy)]
        [TestCase(MmuMode.Tlb)]
        [TestCase(MmuMode.ResidentMap)]
        public void Baseline_PagingB_ND110(MmuMode mmuMode)
        {
            Assert.That(System.IO.File.Exists(PagingBpun), Is.True, $"paging BPUN not found: {PagingBpun}");

            // Decode the BPUN to learn its Start address + Action (how it's meant to be started).
            byte[] raw = System.IO.File.ReadAllBytes(PagingBpun);
            BPUN decoded = new BPUN();
            Assert.That(decoded.LoadBPUN(raw), Is.True, "BPUN decoder should parse paging-b.bpun");
            TestContext.Out.WriteLine($"paging-b.bpun: Start={Utils.Int2Oct((ushort)decoded.Start)} " +
                $"Address={Utils.Int2Oct((ushort)decoded.Address)} Count={decoded.Count} Action={decoded.Action}");

            // Exercise the MMU mode under test (Legacy / Tlb / ResidentMap). All three must
            // reproduce the golden byte-for-byte, proving they are behaviour-identical on the
            // real paging-b workload. Production default is Legacy until the others are trusted.
            CPU cpu = new CPU(null, CPUType.ND110);
            cpu.MmuMode = mmuMode;
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            // Boot to OPCOM (master clear + self-test).
            cpu.hw.mic.LatchMap(0);
            bool booted = false;
            for (int i = 0; i < 300000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == 0x449) Assert.Fail("Self-test failed (STERR)");
                if (cpu.hw.mac.FETCH) { booted = true; break; }
            }
            Assert.That(booted, Is.True, "did not reach OPCOM");

            // Load the paging test program into RAM (overwrites the default INSTRUCTION-B).
            NDMicroCpu.Core.Support.FileHelper.LoadBPUN(PagingBpun, cpu.memory.Raw);

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                for (int i = 0; i < TICK_PR_20MS * 10; i++) cpu.TickCpu();

                // Start it at its BPUN Start address ("164316!"), wait for the banner.
                Type(cpu, Utils.Int2Oct((ushort)decoded.Start) + "!");
                for (int chunk = 0; chunk < 30 && !output.ToString().Contains("PAGING"); chunk++)
                    for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();

                // Drive it: RUN <cr>, then it asks which tests -> <cr> for ALL (per the operator).
                Type(cpu, "RUN\r");
                for (int i = 0; i < TICK_PR_20MS * 20; i++) cpu.TickCpu(); // let the "which tests?" prompt print
                Type(cpu, "\r");

                // OBSERVED BASELINE (ND-110, unchanged main, captured 2026-07-16):
                // the program prints NO title banner - it emits, on the SAME terminal line
                // (bare CR, no LF), two sub-tests that each finish with "=== END OF TEST ===":
                //     PAGING AS MEMORY (ADDRESS & DATA BITS)   === END OF TEST ===
                //     PAGING CONTROL REGISTERS, ALL LEVELS     === END OF TEST ===
                // and then it LOOPS these two forever, error-free. A broken paging path would
                // instead print "*** E R R O R ***" (it aborts after 10 errors). So the gate is:
                // both named sub-tests reach END OF TEST and the suite LOOPS at least twice
                // (>=4 END OF TEST) with NO error line. Break as soon as we've seen it loop.
                for (int chunk = 0; chunk < 200; chunk++)
                {
                    for (int i = 0; i < TICK_PR_20MS * 20; i++) cpu.TickCpu();
                    string norm = output.ToString().Replace("\0", "");
                    // Stop as soon as the run has resolved either way: the suite looped clean
                    // (>=4 END OF TEST) OR a routine aborted on error. Avoids ticking the full
                    // budget at the idle menu prompt after an early abort.
                    if (CountOf(norm, "=== END OF TEST ===") >= 4 || norm.Contains("ROUTINE ABORTED"))
                        break;
                }
            }
            finally { cpu.hw.uart.OnTextOutput -= OnText; }

            string text = output.ToString().Replace("\0", "");
            // Dump the raw captured console to a fixed file so the full transcript is always
            // inspectable regardless of whether the test runner surfaces TestContext.Out.
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(TestPaths.TestsDir("Boot"), "paging-b-capture.txt"), text); } catch { }
            TestContext.Out.WriteLine($"--- paging-b.bpun console ({text.Length} chars) ---");
            TestContext.Out.WriteLine(text);
            TestContext.Out.WriteLine($"STOP={cpu.hw.cpu_regs.STOP} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} PIL={cpu.hw.alu.PIL}");

            // GOLDEN SNAPSHOT gate. The paging-b console output is fully DETERMINISTIC (verified:
            // two consecutive runs on unchanged main produced byte-identical 871-char captures),
            // so the regression gate is exact equality against a committed golden file. Any change
            // to the CPU/MMU hot path (per-cycle allocation guards, an MMU translation cache, etc.)
            // that alters paging behaviour by even one character fails this gate.
            //
            // BASELINE (clean): the first four paging sub-tests all pass and the suite loops -
            //     PAGING AS MEMORY (ADDRESS & DATA BITS)   === END OF TEST ===
            //     PAGING CONTROL REGISTERS, ALL LEVELS     === END OF TEST ===
            //     PGU/WIP BITS FOR ALL PITS & ALL ENTRIES  === END OF TEST ===
            //     ALTERNATIVE PIT USAGE ON ALL LEVELS      === END OF TEST ===
            // NOTE: the PGU/WIP and ALTERNATIVE-PIT sub-tests only pass because of the 4-page-table
            // fix in Memory.MapVirtualToPhysical (deriving pageTable from PCR[10:9]/[8:7] in MMS-1
            // mode). Before that fix this run aborted at TEST NR: 2 with "*** E R R O R *** -PGU BIT
            // NOT SET" because PGU/WIP were only ever set on page table 0.
            string goldenPath = System.IO.Path.Combine(TestPaths.TestsDir("Boot"), "paging-b-golden.txt");
            Assert.That(System.IO.File.Exists(goldenPath), Is.True,
                $"golden snapshot missing: {goldenPath}. Re-create it from paging-b-capture.txt after reviewing the diff.");
            string golden = System.IO.File.ReadAllText(goldenPath);

            // Sanity: the golden itself must contain the known structural markers (guards against a
            // truncated/empty golden silently passing).
            Assert.That(golden, Does.Contain("PAGING TEST PROGRAM (PAGING)"), "golden must contain the banner");
            Assert.That(golden, Does.Contain("PROGRAM NUMBER: 203098B"), "golden must contain the program number");

            Assert.That(text, Is.EqualTo(golden),
                "paging-b console output diverged from the golden snapshot. If the change is intentional " +
                "(e.g. a deliberate MMU fix), review paging-b-capture.txt vs paging-b-golden.txt and update " +
                "the golden. Otherwise this is a REGRESSION in the paging path.");
        }
    }
}
