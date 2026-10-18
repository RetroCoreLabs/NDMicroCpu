//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Text;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.UART
{
    /// <summary>
    /// OPCOM (MOPC) UART round-trip regression, for BOTH CPU generations.
    ///
    /// This is the first test in the suite that asserts on actual console OUTPUT CONTENT
    /// produced by the microcode operator communication (MOPC) - everything else that
    /// touches the UART only echoes it to the test console for debugging.
    ///
    /// Scenario (documented in opcom-commands.md): with the CPU sitting stopped at the
    /// OPCOM wait state after master clear + self-test, MOPC polls the console UART during
    /// its 20ms timer loop. Typing an address followed by '/' (SLASH, CS 002776 in RASK)
    /// examines memory: MOPC echoes the typed characters and then prints the addressed
    /// word as a 6-digit octal number. We plant a known value first so the printed number
    /// is a deterministic, asserted round-trip through:
    ///     host key -> UART RX -> MOPC INPUT -> echo TX  (input half)
    ///     MOPC SLASH -> memory read -> octal print -> UART TX  (output half)
    ///
    /// The MOPC console is the on-board UART on both machines but a DIFFERENT chip:
    /// ND-110 uses hw.uart, ND-120 uses hw.uart2661 (SCN2661 EPCI). hw.AddConsoleChar()
    /// already routes to whichever one is active for the CPU type, so only the output
    /// subscription differs.
    /// </summary>
    public class TestOpcomUart
    {
        // RealTimeClock.TICK_PR_20MS - the RTC's private period, mirrored here exactly as
        // TestBootSequence.cs / TestInstructionVerify.cs do. One full period guarantees the
        // MS20-driven MOPC poll loop consumes one queued console character.
        const int TICK_PR_20MS = 10550 * 4;

        static ushort SterrAddress(CPUType cpuType) => cpuType == CPUType.ND110 ? (ushort)0x449 : (ushort)0x46E;
        static int BootCycleBudget(CPUType cpuType) => cpuType == CPUType.ND110 ? 200000 : 6000000;

        /// <summary>
        /// Boots a fresh CPU of the requested type through master clear + the built-in
        /// self-test and returns it sitting stopped at the OPCOM wait state (same sequence
        /// as TestBootSequence.SelfTestPasses / ReachesOpcomWaitState).
        /// </summary>
        static CPU BootToOpcom(CPUType cpuType)
        {
            CPU cpu = new CPU(null, cpuType);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            ushort sterr = SterrAddress(cpuType);
            int maxCycles = BootCycleBudget(cpuType);
            cpu.hw.mic.LatchMap(0);

            bool booted = false;
            for (int i = 0; i < maxCycles; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr)
                {
                    int r2 = cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
                    Assert.Fail($"{cpuType}: self-test failed (STERR) with R2={Utils.Int2Oct(r2)} after {i} cycles");
                }
                if (cpu.hw.mac.FETCH) { booted = true; break; }
            }
            Assert.That(booted, Is.True, $"{cpuType}: boot never reached a FETCH boundary within {maxCycles} cycles");
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, $"{cpuType}: expected CPU stopped at OPCOM after boot");
            return cpu;
        }

        // Types each character into the console, giving MOPC one full 20ms poll period (plus
        // margin) per character to consume and echo it - same cadence TestInstructionVerify uses.
        static void TypeString(CPU cpu, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                cpu.hw.AddConsoleChar(text[i]);
                for (int c = 0; c < TICK_PR_20MS * 5; c++) cpu.TickCpu();
            }
        }

        [TestCase(CPUType.ND110)]
        [TestCase(CPUType.ND120)]
        public void ExamineMemory_EchoesInputAndPrintsOctalContents(CPUType cpuType)
        {
            CPU cpu = BootToOpcom(cpuType);

            // Plant a known, unambiguous value at octal address 1000 so the printed word is
            // deterministic. 123456(oct) is a full 6-octal-digit value with no leading-zero
            // ambiguity, and each octal digit differs, so a byte/bit-order slip would show.
            const string examineAddr = "1000";
            const string plantedOct = "123456";
            ushort addr = (ushort)Utils.Oct2Int(examineAddr);
            ushort value = (ushort)Utils.Oct2Int(plantedOct);
            cpu.memory.WritePhysicalMemory(addr, value);

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            if (cpuType == CPUType.ND110) cpu.hw.uart.OnTextOutput += OnText;
            else cpu.hw.uart2661.OnTextOutput += OnText;

            try
            {
                // Let MOPC settle into its input poll loop before the first keystroke.
                for (int i = 0; i < TICK_PR_20MS * 10; i++) cpu.TickCpu();

                // Type the examine command. '/' (SLASH) triggers the memory read + octal print.
                TypeString(cpu, examineAddr + "/");

                // Give MOPC time to finish transmitting the 6-digit octal result.
                for (int i = 0; i < TICK_PR_20MS * 20; i++) cpu.TickCpu();

                // MOPC pads its serial stream with NULs; strip them before matching (the
                // whole test suite does this - see TestInstructionVerify's transcript checks).
                string text = output.ToString().Replace("\0", "");
                TestContext.Out.WriteLine($"--- {cpuType} OPCOM examine transcript ---\n[{text}]");

                Assert.That(text, Does.Contain(examineAddr),
                    $"{cpuType}: MOPC must echo the typed examine address '{examineAddr}'");
                Assert.That(text, Does.Contain(plantedOct),
                    $"{cpuType}: MOPC must print the examined word ({plantedOct} octal) after '/'; " +
                    "proves the full key->RX->INPUT->SLASH->memory->octal-print->TX round-trip");
            }
            finally
            {
                if (cpuType == CPUType.ND110) cpu.hw.uart.OnTextOutput -= OnText;
                else cpu.hw.uart2661.OnTextOutput -= OnText;
            }
        }
    }
}
