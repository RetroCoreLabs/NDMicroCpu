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
    /// Byte-string instructions (BFILL, MOVB, MOVBF) - distilled from INSTRUCTION-VERIFY's
    /// BYTE-STRING area, which reported:
    ///
    ///   *** ERROR ***  "MOVB" FAILED
    ///   FIRST ERROR AT : 000000 / NO. WRONG WORDS: 000377
    ///   EXPECTED VALUE : 025052 ('**') / FOUND VALUE : 062121
    ///
    /// Register conventions (ND-100 Reference Manual, byte instructions):
    ///   source field:      A = word address, D = descriptor (bit 15 = start byte
    ///                      0=left/1=right, bits 11-0 = field length in bytes)
    ///   destination field: X = word address, T = descriptor (same layout)
    ///   After MOVB: D field length = 0, T field length = number of bytes moved,
    ///   bit 15 of D and T point to the end of the moved field.
    /// </summary>
    public class TestByteString
    {
        const ushort OPC_BFILL = 0xC058; // 140130 BFILL
        const ushort OPC_MOVB = 0xC059;  // 140131 MOVB
        const ushort OPC_MOVBF = 0xC05A; // 140132 MOVBF

        CPU cpu = null!;

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;
            cpu = new CPU();
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        [Test]
        public void Movb_CopiesLeftAlignedByteField()
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            ushort src = (ushort)Utils.Oct2Int("1000");
            ushort dst = (ushort)Utils.Oct2Int("2000");
            const int bytes = 8;

            cpu.memory.WritePhysicalMemory(p, OPC_MOVB);
            for (int i = 0; i < bytes / 2; i++)
            {
                cpu.memory.WritePhysicalMemory(src + i, 0x2A2A); // '**' - the pattern the original test uses
                cpu.memory.WritePhysicalMemory(dst + i, 0x6451); // junk to be overwritten
            }

            cpu.WriteALURegister(Enums.RegisterEnum.A, src);
            cpu.WriteALURegister(Enums.RegisterEnum.D, bytes);          // left-aligned, 8 bytes
            cpu.WriteALURegister(Enums.RegisterEnum.X, dst);
            cpu.WriteALURegister(Enums.RegisterEnum.T, bytes);          // left-aligned, 8 bytes

            // MOVB always takes the SKIP return (P+2) - ground truth: nd100x DoMOVB
            // (src/cpu/cpu_instr.c) and RetroCore ND100_ByteInstructionTests.
            int newP = cpu.UnitTest_FetchAndExecute(p);
            Assert.That(newP, Is.EqualTo(p + 2), "P after MOVB (skip return)");

            for (int i = 0; i < bytes / 2; i++)
            {
                Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + i)), Is.EqualTo(0x2A2A),
                    $"destination word {i} after MOVB");
            }

            // Post conditions per the reference manual
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D) & 0x0FFF, Is.EqualTo(0),
                "D field length must be 0 after MOVB");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T) & 0x0FFF, Is.EqualTo(bytes),
                "T field length must equal number of bytes moved");
        }

        /// <summary>
        /// MOVB must copy min(source length, dest length) bytes - the case INSTRUCTION-
        /// VERIFY's BYTE-STRING area exercises (source 954 bytes into a 4094-byte dest;
        /// the emulator moved the DEST length and ran far past the source field). Values
        /// here mirror RetroCore's ND100_ByteInstructionTests reference case (nd100x
        /// DoMOVB is the C source of truth): 6-byte source into an 8-byte destination.
        /// </summary>
        [Test]
        public void Movb_SourceShorterThanDest_CopiesMinLength()
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            ushort src = (ushort)Utils.Oct2Int("1000");
            ushort dst = (ushort)Utils.Oct2Int("2000");

            cpu.memory.WritePhysicalMemory(p, OPC_MOVB);
            cpu.memory.WritePhysicalMemory(src + 0, 0x4552); // 'E','R'
            cpu.memory.WritePhysicalMemory(src + 1, 0x524F); // 'R','O'
            cpu.memory.WritePhysicalMemory(src + 2, 0x5221); // 'R','!'
            cpu.memory.WritePhysicalMemory(dst + 0, 0x5555);
            cpu.memory.WritePhysicalMemory(dst + 1, 0x5555);
            cpu.memory.WritePhysicalMemory(dst + 2, 0x5555);
            cpu.memory.WritePhysicalMemory(dst + 3, 0x5555);

            cpu.WriteALURegister(Enums.RegisterEnum.A, src);
            cpu.WriteALURegister(Enums.RegisterEnum.D, 6); // source: 6 bytes, left-aligned
            cpu.WriteALURegister(Enums.RegisterEnum.X, dst);
            cpu.WriteALURegister(Enums.RegisterEnum.T, 8); // dest: room for 8

            int newP = cpu.UnitTest_FetchAndExecute(p);
            Assert.That(newP, Is.EqualTo(p + 2), "P after MOVB (skip return)");

            Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + 0)), Is.EqualTo(0x4552), "dest word0 = 'E','R'");
            Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + 1)), Is.EqualTo(0x524F), "dest word1 = 'R','O'");
            Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + 2)), Is.EqualTo(0x5221), "dest word2 = 'R','!'");
            Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + 3)), Is.EqualTo(0x5555),
                "dest word3 must be untouched - only min(6,8) bytes move");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D) & 0x0FFF, Is.EqualTo(0),
                "D field length must be 0 after MOVB");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T) & 0x0FFF, Is.EqualTo(6),
                "T field length must equal the 6 bytes moved");
        }

        /// <summary>
        /// The EXACT descriptor values INSTRUCTION-VERIFY's BYTE-STRING area uses (captured
        /// live via Diag_ByteStringMovbDescriptors): source length 001672 octal = 954
        /// bytes, destination length 007776 octal = 4094 bytes. With these the emulator
        /// moved 4094 bytes (the DEST length, overrunning the 954-byte source field by
        /// 3140 bytes of neighbouring memory) and left T's count un-updated - while the
        /// small-length case above behaves. Source/dest here are placed far apart so the
        /// overrun is detectable but harmless.
        /// </summary>
        /// <summary>
        /// Diagnostic: traces the MOVB microcode setup (CS 001360-001435) for a given
        /// descriptor set - CSAR path plus ALU F and the loop-relevant state, to see where
        /// the min(srcLen,destLen) selection (MINTD, CS 001374) goes wrong for large
        /// lengths while small ones work.
        /// </summary>
        [Explicit("Diagnostic - traces MOVB microcode setup")]
        [TestCase((ushort)6, (ushort)8)]
        [TestCase((ushort)954, (ushort)4094)]
        public void Diag_MovbMicroTrace(ushort srcLen, ushort dstLen)
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            ushort src = 0x4000;
            ushort dst = 0x6000;
            cpu.memory.WritePhysicalMemory(p, OPC_MOVB);
            for (int i = 0; i < 2100; i++)
            {
                cpu.memory.WritePhysicalMemory(src + i, 0x2A2A);
                cpu.memory.WritePhysicalMemory(dst + i, 0x5555);
            }
            cpu.WriteALURegister(Enums.RegisterEnum.A, src);
            cpu.WriteALURegister(Enums.RegisterEnum.D, srcLen);
            cpu.WriteALURegister(Enums.RegisterEnum.X, dst);
            cpu.WriteALURegister(Enums.RegisterEnum.T, dstLen);
            cpu.hw.alu.WriteRegisterE(Enums.RegisterEnum.P, p);
            cpu.hw.mac.LatchMacAddress(p);
            cpu.UnitTest_Fetch();

            // Trace the fast-word-loop burst (CS 001705-001725): per cycle show the
            // A/B operand register indices resolved from A,LC / B,LC, the live LC value
            // and the ALU F - to expose the register-ring off-by-one.
            int printed = 0;
            for (int i = 0; i < 300000 && printed < 70; i++)
            {
                ushort csar = cpu.hw.mic.map;
                cpu.TickCpu();
                if (csar >= 0x3C5 && csar <= 0x3D5) // 001705-001725
                {
                    printed++;
                    TestContext.Out.WriteLine(
                        $"{i,5}: CSAR={Utils.Int2Oct(csar)} Aop={cpu.hw.alu.A_Operand} Bop={cpu.hw.alu.B_Operand} " +
                        $"LC={cpu.hw.mic.loopCounter,2} F={Utils.Int2Oct(cpu.hw.alu.F & 0xFFFF)} " +
                        $"Y={Utils.Int2Oct(cpu.hw.alu.Y)} dst0={Utils.Int2Oct(cpu.memory.ReadPhysicalMemory(dst))}");
                }
                if (cpu.hw.mac.FETCH) break;
            }
            TestContext.Out.WriteLine(
                $"final A={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.A))} " +
                $"D={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.D))} " +
                $"X={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.X))} " +
                $"T={Utils.Int2Oct(cpu.ReadALURegister(Enums.RegisterEnum.T))} " +
                $"dst0={Utils.Int2Oct(cpu.memory.ReadPhysicalMemory(dst))}");
        }

        [Test]
        public void Movb_AreaCase_954BytesInto4094ByteDest()
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            ushort src = 0x4000;
            ushort dst = 0x6000;
            const int srcBytes = 954;   // 001672 octal
            const int dstBytes = 4094;  // 007776 octal
            const int srcWords = srcBytes / 2;   // 477
            const int dstWords = dstBytes / 2;   // 2047

            cpu.memory.WritePhysicalMemory(p, OPC_MOVB);
            for (int i = 0; i < srcWords; i++)
            {
                cpu.memory.WritePhysicalMemory(src + i, 0x2A2A); // '**' like the original test
            }
            // canary words after the source field - must never be read as source data
            for (int i = srcWords; i < dstWords + 4; i++)
            {
                cpu.memory.WritePhysicalMemory(src + i, 0x6451);
            }
            for (int i = 0; i < dstWords + 4; i++)
            {
                cpu.memory.WritePhysicalMemory(dst + i, 0x5555);
            }

            cpu.WriteALURegister(Enums.RegisterEnum.A, src);
            cpu.WriteALURegister(Enums.RegisterEnum.D, srcBytes);
            cpu.WriteALURegister(Enums.RegisterEnum.X, dst);
            cpu.WriteALURegister(Enums.RegisterEnum.T, dstBytes);

            int newP = cpu.UnitTest_FetchAndExecute(p);
            Assert.That(newP, Is.EqualTo(p + 2), "P after MOVB (skip return)");

            for (int i = 0; i < srcWords; i++)
            {
                Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + i)), Is.EqualTo(0x2A2A),
                    $"dest word {i} must be the copied '**' pattern");
            }
            Assert.That(cpu.memory.ReadPhysicalMemory((ushort)(dst + srcWords)), Is.EqualTo(0x5555),
                "first dest word past min(6,8)=954 bytes must be untouched");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D) & 0x0FFF, Is.EqualTo(0),
                "D field length must be 0 after MOVB");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T) & 0x0FFF, Is.EqualTo(srcBytes),
                "T field length must equal the 954 bytes moved");
        }
    }
}
