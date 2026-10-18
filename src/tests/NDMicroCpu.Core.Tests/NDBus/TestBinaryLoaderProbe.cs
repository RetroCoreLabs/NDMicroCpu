//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Collections.Generic;
using System.Text;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.NDBus;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.NDBusTests
{
    /// <summary>
    /// [Explicit] diagnostic (NOT a pass/fail regression) that answers the Phase-3
    /// open questions empirically, without guessing the ALD encoding:
    ///   (a) When the panel LOAD button is pressed, which IOX device-number base N does
    ///       the microcode loader actually emit on the bus, for a given ALD value?
    ///   (b) Which loader path runs - MASS storage (first access = write to N+1 core
    ///       address) or BINARY (first access = write 004005 to N+3, DVACT) - on each
    ///       CPU generation? The ND-110 IDBS,ALD path forces ALD bits 13-15 = 111
    ///       (bit13=1 => MASS) while the ND-120 path inverts to bit13=0 (=> BINARY);
    ///       this probe shows the real behavior rather than trusting either reading.
    ///
    /// Mechanism: a recorder device claims a wide IOX range (octal 1400-1777, safely
    /// above the on-board console UART) and logs every register access the loader makes
    /// while returning "not ready" so the loader reveals its polling pattern. We then
    /// read the device base N and the path off the first few recorded transactions.
    /// </summary>
    public class TestBinaryLoaderProbe
    {
        const int TICK_PR_20MS = 10550 * 4;

        // Records every IOX transaction and never signals ready, so the microcode loader
        // exposes its activate/poll sequence. Claims octal 1400-1777 (0x300-0x3FF).
        sealed class RecorderDevice : INDBusDevice
        {
            public readonly List<string> Log = new List<string>();
            // Claim the whole IOX space during the probe so we catch whatever base N the
            // loader decodes from ALD (boot already finished before we attach, so grabbing
            // the console range here is harmless for the LOAD sequence we are measuring).
            public bool Claims(int ioxAddress) => true;
            public ushort Read(int ioxAddress)
            {
                Log.Add($"R {Utils.Int2Oct((ushort)ioxAddress)}");
                return 0; // status bit3 (010) stays 0 => loader keeps polling
            }
            public void Write(int ioxAddress, ushort data)
            {
                Log.Add($"W {Utils.Int2Oct((ushort)ioxAddress)} = {Utils.Int2Oct(data)}");
            }
            public int Ident(int level) => -1;
            public void Clock() { }
            public int InterruptBits => 0;
        }

        static CPU BootToOpcom(CPUType cpuType)
        {
            CPU cpu = new CPU(null, cpuType);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
            ushort sterr = cpuType == CPUType.ND110 ? (ushort)0x449 : (ushort)0x46E;
            int budget = cpuType == CPUType.ND110 ? 200000 : 6000000;
            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < budget; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr) Assert.Fail($"{cpuType}: STERR during boot");
                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, $"{cpuType}: not at OPCOM");
            return cpu;
        }

        [Explicit("Diagnostic - sweep ALD low nibble to find which selects the floppy binary-load (ALDVC entry 1 = 1560)")]
        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void Probe_AldSweep(CPUType cpuType)
        {
            // Full nibble sweep: ND-120's IDBS,ALD INVERTS bits 0-3, so the ALD that yields
            // ALDVC entry 1 (floppy 1560) differs from ND-110's ALD=1 - only a 0..15 sweep finds it.
            for (ushort ald = 0; ald <= 15; ald++)
                ProbeOne(cpuType, ald);
        }

        [Explicit("Diagnostic - trace the ND-120 LOAD microcode path to find where it fails to select a device")]
        [Test]
        public void Probe_ND120_LoadPath_MapTrace()
        {
            CPU cpu = BootToOpcom(CPUType.ND120);
            var rec = new RecorderDevice();
            cpu.iobus.Attach(rec);

            // NOISE FF (set on MACL) inhibits LOAD until the STOP handler (002203) clears it -
            // press STOP first (PANVC entry 0 -> 002203) so NOISE resets, THEN LOAD. (This is now
            // done automatically by CPU.PanelBinaryLoad for ND-120; kept explicit here as the probe.)
            cpu.hw.panel.PushPanelButton(NDMicroCpu.Core.Panel.PanelButton.STOP);
            for (int i = 0; i < TICK_PR_20MS * 3; i++) cpu.TickCpu();

            cpu.hw.cpu_regs.ALD = 14; // ND-120 inverted ALD: 14 -> LC=1 -> ALDVC entry 1 (floppy 1560)
            cpu.hw.panel.PushPanelButton(NDMicroCpu.Core.Panel.PanelButton.LOAD);

            const ushort LOAD = 0x4AA; // 002252 DELILAH-L
            List<ushort> trace = new List<ushort>();
            ushort lastMap = 0xFFFF;
            bool tracing = false;
            for (int i = 0; i < TICK_PR_20MS * 30 && trace.Count < 120; i++)
            {
                cpu.TickCpu();
                ushort m = cpu.hw.mic.map;
                if (m == LOAD) tracing = true;
                if (tracing && m != lastMap) trace.Add(m);
                lastMap = m;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder("ND120 LOAD maptrace (ALD=14):");
            for (int i = 0; i < trace.Count; i++) sb.Append(' ').Append(Utils.Int2Oct(trace[i]));
            TestContext.Out.WriteLine(sb.ToString());
            TestContext.Out.WriteLine($"bus accesses: {rec.Log.Count}");
        }

        static void ProbeOne(CPUType cpuType, ushort ald)
        {
            CPU cpu = BootToOpcom(cpuType);
            var rec = new RecorderDevice();
            cpu.iobus.Attach(rec);

            cpu.hw.cpu_regs.ALD = ald;
            cpu.hw.panel.PushPanelButton(NDMicroCpu.Core.Panel.PanelButton.LOAD);

            // Run enough cycles for the loader to dispatch (PANVC entry 4 -> LOAD -> ALDLO
            // -> path) and issue its first device transactions. Track whether execution
            // reaches the LOAD (002202) / MASS (002147) / ETLO1 binary (002210) microaddrs
            // so we can tell dispatch-failed apart from wrong-device-number.
            // CPU-aware dispatch addresses: RASK (ND-110) LOAD=002202/MASS=002147/ETLO1=002210;
            // DELILAH-L (ND-120) LOAD=002252/MASS=002217/ETLO1=002262 (read from each listing).
            ushort LOAD = cpuType == CPUType.ND110 ? (ushort)0x482 : (ushort)0x4AA;
            ushort MASS = cpuType == CPUType.ND110 ? (ushort)0x467 : (ushort)0x48F;
            ushort ETLO1 = cpuType == CPUType.ND110 ? (ushort)0x488 : (ushort)0x4B2;
            bool sawLoad = false, sawMass = false, sawEtlo1 = false;
            // Compressed map trace: record the first appearance-run of maps once LOAD is seen,
            // so we can read the exact branch the loader takes. Cap the list length.
            List<ushort> mapTrace = new List<ushort>();
            ushort lastMap = 0xFFFF;
            bool tracing = false;
            for (int i = 0; i < TICK_PR_20MS * 30 && rec.Log.Count < 20; i++)
            {
                cpu.TickCpu();
                ushort m = cpu.hw.mic.map;
                if (m == LOAD) { sawLoad = true; tracing = true; }
                if (m == MASS) sawMass = true;
                if (m == ETLO1) sawEtlo1 = true;
                if (tracing && m != lastMap && mapTrace.Count < 160) mapTrace.Add(m);
                lastMap = m;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"{cpuType} ALD={Utils.Int2Oct(ald)}: LOAD={sawLoad} MASS={sawMass} ETLO1={sawEtlo1} | bus[");
            for (int i = 0; i < rec.Log.Count && i < 6; i++) sb.Append(i == 0 ? "" : ", ").Append(rec.Log[i]);
            sb.Append(']');
            TestContext.Out.WriteLine(sb.ToString());
        }
    }
}
