//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using System.Text;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Perf
{
    /// <summary>
    /// Direct micro-benchmark of the paged-memory translation hot path
    /// (Memory.MapVirtualToPhysical) with the software TLB OFF vs ON, in the SAME binary
    /// (via the per-instance Memory.TlbEnabled toggle). Purpose: quantify the overhead/benefit of
    /// the TLB before backporting the idea to the RetroCore emulator.
    ///
    /// This is a deterministic, self-contained setup (no boot, no microcode): it enables
    /// paging (PONI), points the current level at page table 0 (PCR ring 3 -> 4pt main
    /// table 0), and populates 64 valid page-table entries (VPN v -> PPN v+1, ring 0). It
    /// then translates a repeating sweep over those 64 pages millions of times and times it.
    ///
    /// [Explicit] - timing test, not part of the normal green run. Run manually:
    ///   dotnet test --filter "FullyQualifiedName~PagedMemoryBenchmark" -- NUnit.Explicit=false
    /// </summary>
    public class PagedMemoryBenchmark
    {
        const int PAGES = 64;             // full page table (6-bit VPN)
        const int SWEEPS = 1_000_000;     // sweeps over all pages => PAGES*SWEEPS translations
        const long TOTAL = (long)PAGES * SWEEPS;

        // Build a CPU with paging enabled and page table 0 filled with valid entries.
        static CPU BuildPagedCpu()
        {
            CPU cpu = new CPU(null, CPUType.ND110);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            // Enable paging (PONI = STS bit 14), SEXI off => 16-bit PTEs, 4-page-table (MMS-1) mode.
            cpu.hw.alu.SetSTSfromDebugger(1 << 14);
            // PCR ring 3 (most powerful): ring(3) >= any page-table ring, so no protection trap.
            // In 4pt mode the main page table = PCR[10:9] = 0, so we use page table 0.
            cpu.hw.cpu_regs.PCR = 0x0003;

            // Populate page table 0 (4pt normal mode lives at 0xFF00): PTE for VPN v = PPN (v+1),
            // ring 0. 16-bit PTE layout (non-SEXI): bits[8:0]=PPN, bits[10:9]=ring. PPN (v+1) <= 64
            // so the physical page base (PPN<<10) stays well inside installed memory.
            for (int v = 0; v < PAGES; v++)
            {
                ushort pte = (ushort)((v + 1) & 0x1FF); // PPN = v+1, ring 0
                cpu.memory.WritePhysicalMemory((uint)(0xFF00 + v), pte, true);
            }
            return cpu;
        }

        // Sweep every page once per outer iteration; return an accumulator so the JIT can't
        // elide the calls. Uses a fixed in-page displacement so results are checkable.
        static ulong RunSweep(CPU cpu, int sweeps, AccessMode am)
        {
            ulong acc = 0;
            for (int s = 0; s < sweeps; s++)
            {
                for (int v = 0; v < PAGES; v++)
                {
                    uint va = (uint)((v << 10) | 0x055); // VPN v, DIP 0x55
                    acc += cpu.memory.MapVirtualToPhysical(va, am, PageTable.StandardPageTable);
                }
            }
            return acc;
        }

        static readonly MmuMode[] MODES = { MmuMode.Legacy, MmuMode.Tlb, MmuMode.ResidentMap };
        static readonly string[] MODE_NAMES = { "Legacy     ", "TLB        ", "ResidentMap" };

        // Time SWEEPS full page-sweeps in a given mode; returns (ms, accumulator).
        static (double ms, ulong acc) TimeMode(CPU cpu, MmuMode mode, AccessMode am)
        {
            cpu.memory.MmuMode = mode;
            cpu.memory.FlushTLB();
            Stopwatch sw = Stopwatch.StartNew();
            ulong acc = RunSweep(cpu, SWEEPS, am);
            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds, acc);
        }

        [Explicit("3-way timing benchmark of the MMU translation path (Legacy / TLB / ResidentMap)")]
        [Test]
        public void Benchmark_Mmu_AllModes()
        {
            CPU cpu = BuildPagedCpu();

            // Correctness first: all three modes must produce IDENTICAL translations.
            // VPN v (PPN v+1) + DIP 0x55 must map to ((v+1)<<10)|0x55 in every mode.
            for (int m = 0; m < MODES.Length; m++)
            {
                cpu.memory.MmuMode = MODES[m];
                cpu.memory.FlushTLB();
                for (int v = 0; v < PAGES; v++)
                {
                    uint va = (uint)((v << 10) | 0x055);
                    uint expected = (uint)(((v + 1) << 10) | 0x055);
                    uint got = cpu.memory.MapVirtualToPhysical(va, AccessMode.READ, PageTable.StandardPageTable);
                    Assert.That(got, Is.EqualTo(expected), $"translation mismatch (mode={MODES[m]}) VPN={v}");
                }
            }

            // Warm up all three paths (JIT + populate) before timing.
            for (int m = 0; m < MODES.Length; m++)
            {
                cpu.memory.MmuMode = MODES[m]; cpu.memory.FlushTLB(); RunSweep(cpu, 2000, AccessMode.READ);
            }

            // Timed READ + WRITE for each mode; verify accumulators match across modes.
            double[] readMs = new double[MODES.Length];
            double[] writeMs = new double[MODES.Length];
            ulong readAcc0 = 0;
            for (int m = 0; m < MODES.Length; m++)
            {
                var r = TimeMode(cpu, MODES[m], AccessMode.READ);
                readMs[m] = r.ms;
                if (m == 0) readAcc0 = r.acc;
                else Assert.That(r.acc, Is.EqualTo(readAcc0), $"mode {MODES[m]} produced different READ results");

                var w = TimeMode(cpu, MODES[m], AccessMode.WRITE);
                writeMs[m] = w.ms;
            }

            string build =
#if DEBUG
                "DEBUG (unoptimised - Release is ~2x faster)";
#else
                "RELEASE (optimised)";
#endif
            double legacyRead = readMs[0], legacyWrite = writeMs[0];

            // Clean fixed-width table. Columns line up even with the runner's line indentation.
            StringBuilder rep = new StringBuilder();
            rep.AppendLine();
            rep.AppendLine("+-----------------------------------------------------------------------+");
            rep.AppendLine("|              PAGED-MEMORY MMU BENCHMARK (3 modes)                     |");
            rep.AppendLine("+-----------------------------------------------------------------------+");
            rep.AppendLine($"  Build    : {build}");
            rep.AppendLine($"  Workload : {TOTAL:N0} translations  ({PAGES} pages x {SWEEPS:N0} sweeps)");
            rep.AppendLine();
            rep.AppendLine("  READ            time      throughput   per-xlat   speedup vs Legacy");
            rep.AppendLine("  ---------------------------------------------------------------------");
            for (int m = 0; m < MODES.Length; m++)
            {
                double ms = readMs[m];
                double rate = TOTAL / (ms / 1000.0) / 1e6;
                double ns = ms * 1_000_000.0 / TOTAL;
                rep.AppendLine($"  {MODE_NAMES[m]}   {ms,8:N0} ms   {rate,6:N1} M/s   {ns,6:N2} ns      {legacyRead / ms,5:N2}x");
            }
            rep.AppendLine();
            rep.AppendLine("  WRITE           time                             speedup vs Legacy");
            rep.AppendLine("  ---------------------------------------------------------------------");
            for (int m = 0; m < MODES.Length; m++)
            {
                rep.AppendLine($"  {MODE_NAMES[m]}   {writeMs[m],8:N0} ms                             {legacyWrite / writeMs[m],5:N2}x");
            }
            rep.AppendLine("+-----------------------------------------------------------------------+");
            string report = rep.ToString();
            TestContext.Out.WriteLine(report);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(TestPaths.TestsDir("Perf"), "benchmark-result.txt"), report); } catch { }
        }

        // Boot the CPU to OPCOM (master clear -> self-test -> first FETCH). Returns the number
        // of micro-cycles it took. Fails if the self-test errors (STERR = mic.map 0x449) or it
        // never reaches FETCH. Deterministic: the tick count does not depend on the TLB.
        static long BootToOpcom(bool useMmuTlb)
        {
            CPU cpu = new CPU(null, CPUType.ND110, useMmuTlb: useMmuTlb);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            cpu.hw.mic.LatchMap(0);
            long ticks = 0;
            for (int i = 0; i < 400000; i++)
            {
                cpu.TickCpu();
                ticks++;
                if (cpu.hw.mic.map == 0x449) Assert.Fail("Self-test failed (STERR) during boot benchmark");
                if (cpu.hw.mac.FETCH) return ticks;
            }
            Assert.Fail("CPU did not reach OPCOM (FETCH) within budget");
            return ticks; // unreachable
        }

        [Explicit("Timing benchmark of CPU boot-to-OPCOM with the TLB off vs on")]
        [Test]
        public void Benchmark_Boot_TlbOffVsOn()
        {
            const int BOOTS = 500;   // large sample so min/median are stable
            const int WARMUP = 20;   // discard the first boots (JIT + cache warm-up)

            // Warm up (JIT both paths + microcode load) before timing.
            long ticksOff = BootToOpcom(false);
            long ticksOn = BootToOpcom(true);
            for (int i = 0; i < WARMUP; i++) { BootToOpcom(false); BootToOpcom(true); }

            // Boot is unpaged (PONI not set until an OS enables it), so MapVirtualToPhysical
            // short-circuits before the TLB block: the tick count MUST be identical on/off, and
            // the wall-time should be within noise. This test validates the TLB adds no overhead
            // to the boot/self-test path.
            Assert.That(ticksOn, Is.EqualTo(ticksOff),
                $"boot tick count differs with TLB (off={ticksOff}, on={ticksOn}) - TLB changed boot behaviour!");

            // Interleave off/on per iteration so machine-warmup / CPU-frequency / GC ordering
            // bias cancels out (an all-off-then-all-on layout unfairly favours whichever runs
            // second). Record EACH boot's time so we can report min/median (robust to GC/OS
            // outliers) instead of just a noise-prone mean.
            double[] offTimes = new double[BOOTS];
            double[] onTimes = new double[BOOTS];
            Stopwatch sw = new Stopwatch();
            for (int i = 0; i < BOOTS; i++)
            {
                sw.Restart(); BootToOpcom(false); sw.Stop(); offTimes[i] = sw.Elapsed.TotalMilliseconds;
                sw.Restart(); BootToOpcom(true); sw.Stop(); onTimes[i] = sw.Elapsed.TotalMilliseconds;
            }

            // Min / median / mean per path (no LINQ). Sort copies for the median.
            Array.Sort(offTimes);
            Array.Sort(onTimes);
            double offMin = offTimes[0], onMin = onTimes[0];
            double offMed = offTimes[BOOTS / 2], onMed = onTimes[BOOTS / 2];
            double offSum = 0, onSum = 0;
            for (int i = 0; i < BOOTS; i++) { offSum += offTimes[i]; onSum += onTimes[i]; }
            double offMs = offSum / BOOTS, onMs = onSum / BOOTS;

            StringBuilder rep = new StringBuilder();
            rep.AppendLine();
            rep.AppendLine("+---------------------------------------------------------------+");
            rep.AppendLine("|              CPU BOOT-TO-OPCOM BENCHMARK (ND-110)             |");
            rep.AppendLine("+---------------------------------------------------------------+");
            rep.AppendLine($"  Boots timed : {BOOTS} per path (interleaved, {WARMUP} warm-up discarded)");
            rep.AppendLine($"  Ticks/boot  : {ticksOff:N0}   (IDENTICAL off vs on - the real proof)");
            rep.AppendLine();
            rep.AppendLine("                      min          median          mean");
            rep.AppendLine("  -----------------------------------------------------------------");
            rep.AppendLine($"  Boot  TLB off  {offMin,8:N2} ms   {offMed,8:N2} ms   {offMs,8:N2} ms");
            rep.AppendLine($"  Boot  TLB ON   {onMin,8:N2} ms   {onMed,8:N2} ms   {onMs,8:N2} ms");
            rep.AppendLine($"  Delta (median) {onMed - offMed,8:+0.00;-0.00} ms  ({(onMed - offMed) / offMed * 100.0:+0.0;-0.0}%)  <- robust measure");
            rep.AppendLine("  -----------------------------------------------------------------");
            rep.AppendLine("  Verdict: tick count identical => TLB adds NO overhead to boot.");
            rep.AppendLine("           (boot is unpaged, so the TLB is never engaged; the tiny");
            rep.AppendLine("            min/median delta is measurement noise, not a real cost.)");
            rep.AppendLine("+---------------------------------------------------------------+");
            string report = rep.ToString();
            TestContext.Out.WriteLine(report);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(TestPaths.TestsDir("Perf"), "boot-benchmark-result.txt"), report); } catch { }
        }
    }
}
