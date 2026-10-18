//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.X86;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using NDMicroCpu.Core;

namespace NDMicroCpu.Core.Tests.NDCPU
{
    public class TestArgument
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }





        #region Argument Instructions


        [Test]
        public void Argument_AAA()
        {
            // Add argument to A

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");

            short expected = 0;
            ushort opcode = (ushort)Utils.Oct2Int("172405"); //AAA 5

            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 1234);


            expected = 1234 + 5;

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected), "A after ExecuteMacroInstruction");

            Assert.Pass();
        }


        [Test]
        public void Argument_AAA_loop()
        {
            // Add argument to A

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");

            short expected = 0;
            ushort opcode = (ushort)Utils.Oct2Int("172400"); //AAA 

            short j = 32767;
            for (short i = 128; i < 256; i++)
            {
                cpu.memory.WritePhysicalMemory(P, (ushort)(opcode | (ushort)i));

                // Setup registers
                cpu.WriteALURegister(Enums.RegisterEnum.P, P);
                cpu.WriteALURegister(Enums.RegisterEnum.A, (ushort)j);

                expected = (short)(j + (sbyte)i);


                // Execute
                int newP = cpu.UnitTest_FetchAndExecute(P);

                Console.WriteLine($"Expected = {expected}, I={i}");

                // Validate
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

                short short_A = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);

                Assert.That(short_A, Is.EqualTo(expected), $"A after ExecuteMacroInstruction, I={i}");
            }

            Assert.Pass();
        }



        #endregion Argument Instructions


    }
}
