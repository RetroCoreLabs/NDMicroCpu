//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core;

namespace NDMicroCpu.Core.Tests.FloatingPoint
{
    /// <summary>
    /// 32-bit FPP exponent-bias regression tests, driven entirely by the REAL microcode
    /// (NLZ / DNZ / FSB) - no hand-packed operands taken from the ND-100 manual's worked
    /// examples, because those examples are what previously led this emulator astray.
    ///
    /// GROUND TRUTH: the bias is 0400 octal = 256 in the "mantissa in [0.5,1)" convention.
    /// The constant is loaded by NLZ itself - ND-110-RASK.uc line 8433 (CS 003451, NLZ3):
    ///     "A,10  ALUF,PASSD  ALUD,Q  IDBS,BMG"  ->  Q = BMG(10 octal) = 1&lt;&lt;8 = 0400 = 256
    /// and DNZ tests the exponent against the same 0400 (line 8526-8530, CS 003473 DNZ2).
    /// So NLZ 20 (= +16 decimal) of integer 1 must leave A = 040100, NOT 040200.
    ///
    /// This file previously RECORDED 040200/040340 - the emulator's own (wrong) output at
    /// the time. That was the one-binade bug fixed in Cpu.cs Execute_4_ConditionControl
    /// (LCOUNT must count on the condition-true exit pass too); see the comment there.
    /// </summary>
    public class TestFppBiasProbe
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }

        #region helpers

        /// <summary>
        /// Reference packer for the ND-100/110 32-bit float, written straight from the
        /// format definition with bias 256 - deliberately INDEPENDENT of the emulator so
        /// the expected values are derived, not copied from a previous run.
        ///
        /// Layout: A = sign(bit15) | exponent(bits14..6) | top 6 mantissa bits(bits5..0),
        /// D = low 16 mantissa bits. Hidden-bit 23-bit mantissa, value = f * 2^(exp-256)
        /// with f in [0.5,1). Sign is sign-magnitude (the microcode negates the magnitude
        /// and ORs 1000 octal into the exponent word at NLZ1, ND-110-RASK.uc 8454-8462,
        /// which lands on bit15 once the word is shifted left 6).
        /// </summary>
        private static void PackBias256(int value, out ushort a, out ushort d)
        {
            if (value == 0) { a = 0; d = 0; return; }

            bool negative = value < 0;
            uint mag = (uint)(negative ? -(long)value : value);

            // Bit length of the magnitude == the binary exponent e, since mag = f * 2^e.
            int e = 0;
            while ((mag >> e) != 0) e++;

            // Left-justify so the leading 1 (the hidden bit) sits at bit 22 of a 23-bit mantissa.
            uint mant = mag << (23 - e);
            uint stored = mant & 0x3FFFFF; // 22 stored bits; bit22 (the hidden 1) is dropped

            ushort expField = (ushort)(e + 256);
            a = (ushort)((((uint)expField & 0x1FF) << 6) | (stored >> 16));
            d = (ushort)(stored & 0xFFFF);
            if (negative) a |= 0x8000;
        }

        /// <summary>
        /// Execute a single NLZ with the given octal scaling against the supplied integer
        /// in A, from a clean T=0 state. NLZ &lt;s&gt; of N yields N * 2^(s-16), so scaling 20
        /// octal (=16) converts the integer straight across.
        /// </summary>
        private void RunNlz(string scalingOctal, ushort aIn, out ushort a, out ushort d, out ushort t)
        {
            RunNlz(scalingOctal, aIn, 0, out a, out d, out t);
        }

        private void RunNlz(string scalingOctal, ushort aIn, ushort dIn, out ushort a, out ushort d, out ushort t)
        {
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)(Utils.Oct2Int("151400") | Utils.Oct2Int(scalingOctal));
            cpu.memory.WritePhysicalMemory(P, opcode);

            cpu.WriteALURegister(Enums.RegisterEnum.T, 0); // SAT 0
            cpu.WriteALURegister(Enums.RegisterEnum.A, aIn); // SAA <aIn>
            cpu.WriteALURegister(Enums.RegisterEnum.D, dIn);

            cpu.UnitTest_FetchAndExecute(P);

            t = cpu.ReadALURegister(Enums.RegisterEnum.T);
            a = cpu.ReadALURegister(Enums.RegisterEnum.A);
            d = cpu.ReadALURegister(Enums.RegisterEnum.D);
        }

        private void RunNlz20(ushort aIn, out ushort a, out ushort d, out ushort t)
        {
            RunNlz("20", aIn, out a, out d, out t);
        }

        /// <summary>
        /// Run FSB (104001, operand at P+1 as two words) with A,D holding the minuend.
        /// </summary>
        private void RunFsb(ushort accA, ushort accD, ushort memA, ushort memD, out ushort a, out ushort d)
        {
            ushort P = (ushort)Utils.Oct2Int("300");
            cpu.memory.WritePhysicalMemory(P, (ushort)Utils.Oct2Int("104001")); // FSB *1
            cpu.memory.WritePhysicalMemory(P + 1, memA);
            cpu.memory.WritePhysicalMemory(P + 2, memD);

            cpu.WriteALURegister(Enums.RegisterEnum.T, 0);
            cpu.WriteALURegister(Enums.RegisterEnum.A, accA);
            cpu.WriteALURegister(Enums.RegisterEnum.D, accD);

            cpu.UnitTest_FetchAndExecute(P);

            a = cpu.ReadALURegister(Enums.RegisterEnum.A);
            d = cpu.ReadALURegister(Enums.RegisterEnum.D);
        }

        #endregion helpers

        /// <summary>
        /// The headline case: NLZ 20 of 1 must be 040100 (bias 256), not 040200 (bias 257).
        /// T unchanged also confirms this build is a 32-bit-FPP machine (ND-100 manual 3.1.2.6).
        /// </summary>
        [Test]
        public void NLZ20_of_1_is_040100_bias256()
        {
            RunNlz20(1, out ushort a, out ushort d, out ushort t);

            TestContext.Progress.WriteLine($"NLZ20(1) -> T={Utils.Int2Oct(t)} A={Utils.Int2Oct(a)} D={Utils.Int2Oct(d)}");

            Assert.That(t, Is.EqualTo(0), "T must be untouched on a 32-bit FPP machine");
            Assert.That(a, Is.EqualTo((ushort)Utils.Oct2Int("040100")), "NLZ 20 of 1: A");
            Assert.That(d, Is.EqualTo((ushort)Utils.Oct2Int("000000")), "NLZ 20 of 1: D");
        }

        /// <summary>
        /// Values 1, 3, 25, 151, 21845 and their negatives, checked against the independent
        /// bias-256 packer. 040100 / 040240 / 040544 / 041013 are additionally confirmed by
        /// RetroCore's offline suite.
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(25)]
        [TestCase(151)]
        [TestCase(21845)]
        [TestCase(-1)]
        [TestCase(-3)]
        [TestCase(-25)]
        [TestCase(-151)]
        [TestCase(-21845)]
        public void NLZ20_matches_bias256_reference(int value)
        {
            PackBias256(value, out ushort expA, out ushort expD);

            RunNlz20((ushort)value, out ushort a, out ushort d, out ushort t);

            TestContext.Progress.WriteLine(
                $"NLZ20({value,6}) -> A={Utils.Int2Oct(a)} D={Utils.Int2Oct(d)}   " +
                $"expected A={Utils.Int2Oct(expA)} D={Utils.Int2Oct(expD)}");

            Assert.That(t, Is.EqualTo(0), $"T must be untouched (value {value})");
            Assert.That(a, Is.EqualTo(expA), $"NLZ 20 of {value}: A");
            Assert.That(d, Is.EqualTo(expD), $"NLZ 20 of {value}: D");
        }

        /// <summary>
        /// NLZ +16 followed by DNZ -16 must be an exact round trip: N back, not 2N.
        /// This is the property that originally exposed the one-binade bug.
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(25)]
        [TestCase(151)]
        [TestCase(21845)]
        public void NLZ_then_DNZ_round_trips_to_N_not_2N(int value)
        {
            RunNlz20((ushort)value, out ushort a, out ushort d, out ushort t);

            // Feed the microcode-produced float straight back in - nothing hand-packed.
            ushort P2 = (ushort)Utils.Oct2Int("200");
            cpu.memory.WritePhysicalMemory(P2, (ushort)Utils.Oct2Int("152360")); // DNZ -20 octal = -16
            cpu.WriteALURegister(Enums.RegisterEnum.T, t);
            cpu.WriteALURegister(Enums.RegisterEnum.A, a);
            cpu.WriteALURegister(Enums.RegisterEnum.D, d);

            cpu.UnitTest_FetchAndExecute(P2);

            ushort back = cpu.ReadALURegister(Enums.RegisterEnum.A);
            TestContext.Progress.WriteLine(
                $"NLZ20({value}) = {Utils.Int2Oct(a)},{Utils.Int2Oct(d)}  -> DNZ-20 -> A={Utils.Int2Oct(back)}");

            Assert.That(back, Is.EqualTo((ushort)value), $"NLZ/DNZ round trip for {value} must return N, not 2N");
        }

        /// <summary>
        /// Scaling other than +16, measured rather than derived from the manual's
        /// "a larger scaling factor gives a higher floating point number" rule.
        /// RetroCore's ND100_TestFPP32 has a case NLZ 021 with A=1, D=177777 whose expected
        /// value was reasoned out rather than read off the microcode - this measures it.
        /// </summary>
        [Test]
        public void NLZ_scaling_21_of_1_with_D_177777()
        {
            RunNlz("21", 1, (ushort)Utils.Oct2Int("177777"), out ushort a, out ushort d, out ushort t);

            TestContext.Progress.WriteLine(
                $"NLZ21(A=1,D=177777) -> T={Utils.Int2Oct(t)} A={Utils.Int2Oct(a)} D={Utils.Int2Oct(d)}");

            Assert.That(t, Is.EqualTo(0), "T must be untouched");
        }

        /// <summary>
        /// FSB with operands the microcode itself produced, covering the cancellation cases
        /// that DO require a left renormalization of the result - which 6-2 did not.
        ///
        /// If the subtract added an extra binade on renormalize, these would each come back
        /// double (040200 / 040200 / 037700 respectively).
        /// </summary>
        [Test]
        public void FSB_cancellation_cases_do_not_gain_a_binade()
        {
            // ---- 4 - 3 = 1 (two normalize shifts) ----
            RunNlz20(4, out ushort a4, out ushort d4, out _);
            RunNlz20(3, out ushort a3, out ushort d3, out _);
            RunFsb(a4, d4, a3, d3, out ushort r1a, out ushort r1d);
            TestContext.Progress.WriteLine(
                $"FSB 4-3      : {Utils.Int2Oct(a4)},{Utils.Int2Oct(d4)} - {Utils.Int2Oct(a3)},{Utils.Int2Oct(d3)}" +
                $" -> A={Utils.Int2Oct(r1a)} D={Utils.Int2Oct(r1d)}");

            // ---- 3 - 2 = 1 (one normalize shift) ----
            RunNlz20(3, out ushort b3, out ushort e3, out _);
            RunNlz20(2, out ushort b2, out ushort e2, out _);
            RunFsb(b3, e3, b2, e2, out ushort r2a, out ushort r2d);
            TestContext.Progress.WriteLine(
                $"FSB 3-2      : {Utils.Int2Oct(b3)},{Utils.Int2Oct(e3)} - {Utils.Int2Oct(b2)},{Utils.Int2Oct(e2)}" +
                $" -> A={Utils.Int2Oct(r2a)} D={Utils.Int2Oct(r2d)}");

            // ---- 4 - 3.875 = 0.125 (deep cancellation) ----
            // 3.875 = 31 * 2^-3, produced by NLZ with scaling 15 octal (= 13 = 16-3).
            RunNlz20(4, out ushort c4, out ushort f4, out _);
            RunNlz("15", 31, out ushort c31, out ushort f31, out _);
            RunFsb(c4, f4, c31, f31, out ushort r3a, out ushort r3d);
            TestContext.Progress.WriteLine(
                $"FSB 4-3.875  : {Utils.Int2Oct(c4)},{Utils.Int2Oct(f4)} - {Utils.Int2Oct(c31)},{Utils.Int2Oct(f31)}" +
                $" -> A={Utils.Int2Oct(r3a)} D={Utils.Int2Oct(r3d)}");

            // Sanity on the constructed 3.875 operand: same mantissa as integer 31, but an
            // exponent 3 binades lower (NLZ scaling 15 octal instead of 20 octal).
            PackBias256(31, out ushort p31a, out ushort p31d);
            ushort expect31Scaled = (ushort)((((p31a >> 6) & 0x1FF) - 3) << 6 | (p31a & 0x3F));
            Assert.That(c31, Is.EqualTo(expect31Scaled), "NLZ 15 of 31 must be 3.875");
            Assert.That(f31, Is.EqualTo(p31d), "NLZ 15 of 31: D");

            Assert.That(r1a, Is.EqualTo((ushort)Utils.Oct2Int("040100")), "FSB 4-3 must be 1 = 040100");
            Assert.That(r1d, Is.EqualTo((ushort)0), "FSB 4-3: D");
            Assert.That(r2a, Is.EqualTo((ushort)Utils.Oct2Int("040100")), "FSB 3-2 must be 1 = 040100");
            Assert.That(r2d, Is.EqualTo((ushort)0), "FSB 3-2: D");
            Assert.That(r3a, Is.EqualTo((ushort)Utils.Oct2Int("037600")), "FSB 4-3.875 must be 0.125 = 037600");
            Assert.That(r3d, Is.EqualTo((ushort)0), "FSB 4-3.875: D");
        }
    }
}
