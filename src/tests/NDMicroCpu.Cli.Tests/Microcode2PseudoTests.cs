//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Cli;
using NDMicroCpu.Core;
using NUnit.Framework;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Cli.Tests
{
    /// <summary>
    /// Phase 2 validation: the ported Microcode2Pseudo renders real DELILAH-L microcode as
    /// pseudo-C without crashing and with meaningful content, and the CLI PseudoAt/PseudoCurrent
    /// paths work on a booted CPU.
    /// </summary>
    public class Microcode2PseudoTests
    {
        [Test]
        public void RendersEveryDefinedWord_NoCrash_NoEmpty()
        {
            var engine = new CliEngine(CPUType.ND120);
            var cs = engine.Cpu.hw.cs;

            int rendered = 0;
            // DELILAH-L defines microcode up to 012513 octal (5451 dec); scan that range.
            for (ushort a = 0; a <= 0x0AAB; a++)
            {
                ulong word = cs.Read(a);
                if (word == 0) continue; // unused slot
                var mc = new MicroCode();
                mc.DecodeMicroCode(word);
                string pseudo = Microcode2Pseudo.GeneratePseudoCode(mc, a, null);
                Assert.That(pseudo, Is.Not.Null.And.Not.Empty, $"empty render at {System.Convert.ToString(a, 8)}");
                rendered++;
            }
            Assert.That(rendered, Is.GreaterThan(100), "expected many defined microcode words to render");
            TestContext.Out.WriteLine($"rendered {rendered} microcode words as pseudo-C");
        }

        [Test]
        public void PseudoCurrent_AfterBoot_ProducesText()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();
            string p = engine.PseudoCurrent();
            Assert.That(p, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void PseudoAt_KnownRoutine_RendersCommandOrAlu()
        {
            var engine = new CliEngine(CPUType.ND120);
            // 000725 octal = AIIC1 (TRA IIC routine body) in DELILAH-L — a real, non-empty word.
            string p = engine.PseudoAt("725");
            Assert.That(p, Does.StartWith("CS 000725:"));
            Assert.That(p, Does.Not.Contain("NOP; // No operation").Or.Contain("=")); // has some content
        }

        [Test]
        public void Render_LdgprCommand_MentionsGpr()
        {
            // Build a microcode word whose command decodes to LDGPR (command field = 2, bits 32-36).
            var mc = new MicroCode();
            mc.DecodeMicroCode(2UL << 32);
            string p = Microcode2Pseudo.GeneratePseudoCode(mc, 0, null);
            Assert.That(p, Does.Contain("GPR"));
        }
    }
}
