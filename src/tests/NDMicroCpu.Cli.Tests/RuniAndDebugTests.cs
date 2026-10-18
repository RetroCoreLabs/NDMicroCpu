//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Cli;
using NUnit.Framework;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Cli.Tests
{
    /// <summary>
    /// Phase 3 validation: `runi` really executes ONE macro instruction on the working
    /// NDMicroCpu.Core core (register presets in, opcode planted at P, result registers out) —
    /// the thing the abandoned ND120CPUEMU could never verify because its core failed
    /// self-test. Plus register/memory write plumbing and microcode breakpoints.
    ///
    /// Opcodes used (ND-100 argument instructions, all octal):
    ///   SAA n = 170400|n  (A := n)
    ///   AAA n = 172400|n  (A := A + n)
    /// </summary>
    public class RuniAndDebugTests
    {
        [Test]
        public void Runi_SAA5_SetsA_AndAdvancesP()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();

            // SAA 5 at P=002000: A := 5, next FETCH at P=002001.
            string r = engine.RunInstruction(new[] { "runi", "170405", "P=2000" });

            Assert.That(r, Does.Contain("executed one instruction"), r);
            Assert.That(r, Does.Contain("A=000005"), r);
            Assert.That(r, Does.Contain("next FETCH at P=002001"), r);
        }

        [Test]
        public void Runi_AAA3_WithPresetA_Adds()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();

            // A preset to 2, AAA 3 -> A = 5.
            string r = engine.RunInstruction(new[] { "runi", "172403", "A=2", "P=2000" });

            Assert.That(r, Does.Contain("set A = 000002"), r);
            Assert.That(r, Does.Contain("A=000005"), r);
        }

        [Test]
        public void Runi_AutoBoots_WhenNotBooted()
        {
            var engine = new CliEngine(CPUType.ND120);
            // No explicit Boot(): runi must boot the CPU itself before executing.
            string r = engine.RunInstruction(new[] { "runi", "170405", "P=2000" });

            Assert.That(r, Does.Contain("(auto-boot) Boot OK"), r);
            Assert.That(r, Does.Contain("A=000005"), r);
        }

        /// <summary>
        /// `runit` (traced runi): every microcode step of the macro instruction is listed
        /// with its CS address, pseudo-C, and a "// actual:" comment carrying the REAL
        /// post-step IDB value and register changes. AAA 3 with A=2 executes as a single
        /// micro-step whose IDB is the sign-extended argument (3) and whose effect is
        /// A: 000002->000005.
        /// </summary>
        [Test]
        public void Runit_Traced_ShowsPseudoAndActualValues()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();

            string r = engine.RunInstruction(new[] { "runit", "172403", "A=2", "P=2000" }, trace: true);

            Assert.That(r, Does.Contain("--- microcode trace"), r);
            Assert.That(r, Does.Contain("CS 0"), r);                       // at least one CS address line
            Assert.That(r, Does.Contain("// actual: IDB=000003"), r);      // real IDB value (the argument)
            Assert.That(r, Does.Contain("A: 000002->000005"), r);          // real register change
            Assert.That(r, Does.Contain("A=000005"), r);                   // final register dump
        }

        [Test]
        public void Runi_NoArgs_ShowsUsage()
        {
            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.RunInstruction(new[] { "runi" }), Does.Contain("Usage: runi"));
        }

        [Test]
        public void Runi_BadOpcode_Rejected()
        {
            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.RunInstruction(new[] { "runi", "999" }), Does.Contain("Invalid opcode"));
        }

        [Test]
        public void Runi_BadRegisterArg_Rejected()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();
            Assert.That(engine.RunInstruction(new[] { "runi", "170405", "QQ=5" }),
                Does.Contain("Invalid register 'QQ'"));
        }

        [Test]
        public void WriteRegister_ThenReadBack()
        {
            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.WriteRegister("X", "1234"), Is.EqualTo("X=001234"));
            Assert.That(engine.ReadRegister("X"), Is.EqualTo("X=001234"));
        }

        [Test]
        public void WriteMemory_ThenReadBack()
        {
            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.WriteMemory("2000", "123456"), Is.EqualTo("mem[002000] = 123456"));
            Assert.That(engine.ReadMemory("2000"), Is.EqualTo("mem[002000] = 123456"));
        }

        [Test]
        public void Breakpoints_AddListRemoveClear()
        {
            var engine = new CliEngine(CPUType.ND120);

            Assert.That(engine.ListBreakpoints(), Is.EqualTo("no breakpoints set"));
            Assert.That(engine.AddBreakpoint("725"), Does.Contain("added at micro addr 000725"));
            Assert.That(engine.AddBreakpoint("725"), Does.Contain("already set"));
            Assert.That(engine.AddBreakpoint("20"), Does.Contain("added at micro addr 000020"));

            string list = engine.ListBreakpoints();
            Assert.That(list, Does.Contain("2 breakpoint(s)"));
            Assert.That(list, Does.Contain("000020"));
            Assert.That(list, Does.Contain("000725"));

            Assert.That(engine.RemoveBreakpoint("20"), Does.Contain("removed"));
            Assert.That(engine.RemoveBreakpoint("20"), Does.Contain("no breakpoint at"));
            Assert.That(engine.ClearBreakpoints(), Is.EqualTo("cleared 1 breakpoint(s)"));
            Assert.That(engine.ListBreakpoints(), Is.EqualTo("no breakpoints set"));
        }

        /// <summary>
        /// StepMacro must stop ON a breakpoint instead of running through to FETCH.
        /// Determinism trick: boot two engines identically; engine A single-steps once from
        /// the post-boot FETCH state to learn which micro address comes next; engine B puts
        /// a breakpoint there and runs StepMacro — it must report the breakpoint, not FETCH.
        /// </summary>
        [Test]
        public void StepMacro_StopsOnBreakpoint()
        {
            var probe = new CliEngine(CPUType.ND120);
            Assert.That(probe.Boot(), Does.StartWith("Boot OK"));
            probe.StepMicro();
            string nextAddrOctal = probe.CurrentMicroAddressOctal;

            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.Boot(), Does.StartWith("Boot OK"));
            engine.AddBreakpoint(nextAddrOctal);

            string r = engine.StepMacro();
            Assert.That(r, Does.Contain("BREAKPOINT"), r);
            Assert.That(r, Does.Contain(nextAddrOctal), r);
        }

        [Test]
        public void PseudoRange_ListsMultipleWords()
        {
            var engine = new CliEngine(CPUType.ND120);
            string r = engine.PseudoRange("0", "4");
            Assert.That(r, Does.Contain("CS 000000:"));
            Assert.That(r, Does.Contain("CS 000001:"));
            Assert.That(r, Does.Contain("CS 000002:"));
            Assert.That(r, Does.Contain("CS 000003:"));
            Assert.That(r, Does.Not.Contain("CS 000004:"));
        }

        [Test]
        public void PseudoRange_DefaultCountIsEight()
        {
            var engine = new CliEngine(CPUType.ND120);
            string r = engine.PseudoRange("20");
            Assert.That(r, Does.Contain("CS 000020:"));
            Assert.That(r, Does.Contain("CS 000027:"));
            Assert.That(r, Does.Not.Contain("CS 000030:"));
        }

        [Test]
        public void PseudoRange_BadArgs_Rejected()
        {
            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.PseudoRange("9"), Does.Contain("Invalid start address"));
            Assert.That(engine.PseudoRange("20", "zero"), Does.Contain("Invalid count"));
        }

        [Test]
        public void PseudoAt_RendersControlStoreWord()
        {
            var engine = new CliEngine(CPUType.ND120);
            string r = engine.PseudoAt("20");
            Assert.That(r, Does.StartWith("CS 000020:"));
            Assert.That(r.Length, Is.GreaterThan("CS 000020:\n".Length));
        }
    }
}
