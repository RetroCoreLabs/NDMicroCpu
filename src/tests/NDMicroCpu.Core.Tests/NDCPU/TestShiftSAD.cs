//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;

namespace NDMicroCpu.Core.Tests.NDCPU
{
    /// <summary>
    /// SAD - shift A and D connected (32-bit AD, A = high word, D = low word).
    /// Distilled from INSTRUCTION-VERIFY's SHIFT-INSTRUCTIONS area:
    ///
    ///   SADOP  (image 034725): A=1, D=1,      SAD 1 -> A=2, D=2 (no cross bits move)
    ///   SAD2OP (image 035004): A=100000, D=0, SAD 1 -> A=0, D=0, bit 15 leaves via M
    ///
    /// SAD2OP is the case that catches a wrong end-input on the double shift: if the
    /// linkage is a 32-bit rotate instead of a plain shift, A's bit 15 wraps into D bit 0
    /// and D reads back 1.
    /// </summary>
    public class TestShiftSAD
    {
        const ushort OPC_SAD_1 = 0xD981;   // 154601 SAD 1      (shift AD left 1)
        const ushort OPC_SAD_SHR_1 = 0xD9FF; // 154777 SAD SHR 1 (shift AD right 1, arithmetic)

        CPU cpu = null!;

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;
            cpu = new CPU();
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        void ExecSad(ushort opcode, ushort a, ushort d)
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            cpu.memory.WritePhysicalMemory(p, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.A, a);
            cpu.WriteALURegister(Enums.RegisterEnum.D, d);
            int newP = cpu.UnitTest_FetchAndExecute(p);
            Assert.That(newP, Is.EqualTo(p + 1), $"P after SAD opcode {Utils.Int2Oct(opcode)}");
        }

        void ExecSad1(ushort a, ushort d) => ExecSad(OPC_SAD_1, a, d);

        [Test]
        public void Sad1_SmallValues_ShiftsBothWords() // SADOP
        {
            ExecSad1(1, 1);
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(2), "A after SAD 1 (A=1 D=1)");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(2), "D after SAD 1 (A=1 D=1)");
        }

        [Test]
        public void Sad1_TopBitLeavesThroughM_NotIntoD() // SAD2OP
        {
            ExecSad1(0x8000, 0);
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0),
                "A after SAD 1 (A=100000 D=0): bit 15 must leave the 32-bit pair");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(0),
                "D after SAD 1 (A=100000 D=0): a plain shift must NOT wrap A bit 15 into D bit 0");
            Assert.That(cpu.hw.alu.STS_M, Is.True,
                "M (last discarded bit) must be 1 after shifting A's bit 15 out");
        }

        /// <summary>
        /// Diagnostic: cycle-by-cycle trace of the SAD 1 microcode (SAD2OP setup) showing
        /// CSAR, ALU mode/destination/shift mode, F, Q, LC and the M flag - for locating
        /// where the disappearing bit is lost.
        /// </summary>
        [Explicit("Diagnostic - traces the SAD microcode")]
        [TestCase((ushort)0xD981, (ushort)0x8000, (ushort)0)] // SAD 1,     A=100000 D=0 (SAD2OP)
        [TestCase((ushort)0xD9FF, (ushort)0x8000, (ushort)0)] // SAD SHR 1, A=100000 D=0 (SAD4OP)
        [TestCase((ushort)0xD9FF, (ushort)1, (ushort)0)]      // SAD SHR 1, A=1 D=0 (link check)
        public void Diag_Sad1MicroTrace(ushort opcode, ushort aStart, ushort dStart)
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            cpu.memory.WritePhysicalMemory(p, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.A, aStart);
            cpu.WriteALURegister(Enums.RegisterEnum.D, dStart);
            cpu.hw.alu.WriteRegisterE(Enums.RegisterEnum.P, p);
            cpu.hw.mac.LatchMacAddress(p);
            cpu.UnitTest_Fetch();

            for (int i = 0; i < 60; i++)
            {
                ushort csar = cpu.hw.mic.map;
                cpu.TickCpu();
                TestContext.Out.WriteLine(
                    $"{i,3}: CSAR={Utils.Int2Oct(csar)} ALUM={cpu.hw.alu.ALUM} ALUD={cpu.hw.alu.aluDestination} " +
                    $"SM={cpu.hw.alu.ShiftMode} F={Utils.Int2Oct(cpu.hw.alu.F & 0xFFFF)} Q={Utils.Int2Oct(cpu.hw.alu.Q)} " +
                    $"LC={cpu.hw.mic.loopCounter} M={(cpu.hw.alu.STS_M ? 1 : 0)}");
                if (cpu.hw.mac.FETCH) break;
            }
            TestContext.Out.WriteLine(
                $"final A={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.A))} " +
                $"D={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.D))} M={cpu.hw.alu.STS_M}");
        }

        [Test]
        public void SadShr1_SignExtends_A() // SAD4OP: A=100000 D=0, SAD SHR 1
        {
            ExecSad(OPC_SAD_SHR_1, 0x8000, 0);
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0xC000),
                "A after SAD SHR 1 (A=100000 D=0): arithmetic right shift must sign-extend (140000)");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(0),
                "D after SAD SHR 1 (A=100000 D=0): A bit 0 (=0) moves into D bit 15");
            Assert.That(cpu.hw.alu.STS_M, Is.False,
                "M after SAD SHR 1 (A=100000 D=0): the discarded bit is D bit 0 = 0");
        }

        [Test]
        public void SadShr1_ALowBit_FallsIntoD() // SAD3OP-style internal link A0 -> D15
        {
            ExecSad(OPC_SAD_SHR_1, 1, 0);
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0),
                "A after SAD SHR 1 (A=1 D=0)");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(0x8000),
                "D after SAD SHR 1 (A=1 D=0): A bit 0 must fall into D bit 15");
            Assert.That(cpu.hw.alu.STS_M, Is.False,
                "M after SAD SHR 1 (A=1 D=0): the discarded bit is D bit 0 = 0");
        }

        [Test]
        public void Sad1_DHighBit_ClimbsIntoA() // internal link D15 -> A0
        {
            ExecSad1(0, 0x8000);
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(1),
                "A after SAD 1 (A=0 D=100000): D bit 15 must climb into A bit 0");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(0),
                "D after SAD 1 (A=0 D=100000)");
        }
    }
}
