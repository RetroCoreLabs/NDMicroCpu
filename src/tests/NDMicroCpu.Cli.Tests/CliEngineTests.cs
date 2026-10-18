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
    /// Phase 1 validation: the CLI attaches to the working NDMicroCpu.Core and the CPU boots ND-120
    /// through self-test (the exact thing the old ND120CPUEMU core could not do), plus basic
    /// command plumbing (step, registers, micro-address).
    /// </summary>
    public class CliEngineTests
    {
        [TestCase(CPUType.ND120)]
        [TestCase(CPUType.ND110)]
        public void Boot_ReachesFetch_NoSelfTestError(CPUType cpuType)
        {
            var engine = new CliEngine(cpuType);
            string report = engine.Boot();

            Assert.That(report, Does.StartWith("Boot OK"),
                $"Expected a clean boot to FETCH, got: {report}");
            Assert.That(report, Does.Not.Contain("SELF-TEST FAILED"));
        }

        [Test]
        public void StepMicro_AdvancesAndReports()
        {
            var engine = new CliEngine(CPUType.ND120);
            string r = engine.StepMicro();
            Assert.That(r, Does.Contain("micro"));
            Assert.That(r, Does.Contain("->"));
        }

        [Test]
        public void SetMicroAddress_ThenStatusShowsIt()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.SetMicroAddress("725"); // 000725 octal (TRA IIC routine entry, arbitrary probe)
            Assert.That(engine.CurrentMicroAddressOctal, Is.EqualTo("000725"));
        }

        [Test]
        public void RegDump_AfterBoot_Runs()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();
            string dump = engine.RegDump();
            Assert.That(dump, Does.StartWith("regs:"));
            Assert.That(dump, Does.Contain("P="));
            Assert.That(dump, Does.Contain("A="));
        }

        [Test]
        public void ReadRegister_UnknownName_ReportsError()
        {
            var engine = new CliEngine(CPUType.ND120);
            Assert.That(engine.ReadRegister("QQ"), Does.Contain("Unknown register"));
        }

        [Test]
        public void FieldsCurrent_ShowsCommand()
        {
            var engine = new CliEngine(CPUType.ND120);
            engine.Boot();
            string f = engine.FieldsCurrent();
            Assert.That(f, Does.Contain("command="));
        }
    }
}
