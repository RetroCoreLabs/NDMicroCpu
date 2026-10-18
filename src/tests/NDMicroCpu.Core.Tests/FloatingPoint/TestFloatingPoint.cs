//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core;

namespace NDMicroCpu.Core.Tests.FloatingPoint
{
    // Test floating point instructions
    public class TestFloatingPoint
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }


        #region Detect chip

        [Test]
        public void FPP_IS_DetectedCorrect()
        {
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("151420"); //NLZ 20
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xBEEF);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.T, 0);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 1);
            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // This tests whether T is changed, if so, the CPU is 48 - bit; otherwise it is 32 - bit.
            var T = cpu.ReadALURegister(Enums.RegisterEnum.T);
            Utils.Log($"T={T}", true);

            if (T != 0)
            {
                Utils.Log("NLZ T 48 Bit", true);
            }
            else
            {
                Utils.Log("NLZ T 32 Bit", true);
            }

#if FPP48
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), !Is.EqualTo(0), "NLZ T 48 Bit");
#endif
#if FPP32
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(0), "NLZ T 32 Bit");
#endif


            Assert.Pass();

        }


        [Test]
        public void FAD_32_1plus3()
        {


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("100001"); // "FAD", "FAD *1");
            cpu.memory.WritePhysicalMemory(P, opcode);


            int expected_value_t = Utils.Oct2Int("0");
            int expected_value_a = Utils.Oct2Int("040300"); // 040300 = 4
            int expected_value_d = Utils.Oct2Int("0");

            // Set the value +1 into TAD
            cpu.WriteALURegister(Enums.RegisterEnum.T, 0);
            cpu.WriteALURegister(Enums.RegisterEnum.A, (ushort)Utils.Oct2Int("040100")); // 1           
            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)Utils.Oct2Int("000000"));


            // Store the value +3 into memory
            cpu.memory.WritePhysicalMemory(P + 1, (ushort)Utils.Oct2Int("040240"));
            cpu.memory.WritePhysicalMemory(P + 2, (ushort)Utils.Oct2Int("000000"));

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            Utils.Log($"T={cpu.ReadALURegister(Enums.RegisterEnum.T)}", true);
            Utils.Log($"A={cpu.ReadALURegister(Enums.RegisterEnum.A)}", true);
            Utils.Log($"D={cpu.ReadALURegister(Enums.RegisterEnum.D)}", true);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(expected_value_t), "FAD T");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value_a), "FAD A");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected_value_d), "FAD D");


            Assert.Pass();
        }


        [Test]
        public void FAD_FAD01()
        {


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("100001"); // "FAD", "FAD *1");
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); // set som random value to A register                                   
            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0xBEEF), "A after ExecuteMacroInstruction");

            Assert.Pass();


            Assert.Pass();
        }


        [Test]
        public void TEST_FMU()
        {


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("100001"); // "FAD", "FAD *1");
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); // set som random value to A register                                   
            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0xBEEF), "A after ExecuteMacroInstruction");

            Assert.Pass();


            Assert.Pass();
        }

        [Test]
        public void TestFMU()
        {
            TestFMU32s("000000", "000000", "000000", "000000", "000000", "000000", "1"); // 0 * 0 = 0


            // This is a test case in the INSTRUCTION tester, with T = "177777"
            // With T = 0, this is: 63147576 = 963.5555419921875 * 65536
            TestFMU32s("041270", "070707", "042100", "000000", "043270", "070707", "2");


            TestFMU32s("040100", "000000", "047770", "070707", "047770", "070707", "3");
            TestFMU32s("040700", "000000", "040152", "125252", "040752", "125252", "4");
            TestFMU32s("040700", "000000", "040225", "052525", "041025", "052525", "5"); // 64 * 2.6666665077209473

            TestFMU32s("040271", "000000", "047770", "000000", "050161", "070000", "6"); // 3.26961332947098E+19 = 3.78125 * 8.646911284551352E+18
            TestFMU32s("142570", "000000", "045271", "000000", "147761", "070000", "7"); //  -8.17403332367745E+18 = -1966080 * 4157528342528

            Assert.Pass();


        }

        public void TestFMU32s(string A, string D, string memA, string memD, string resA, string resD, string comment = "")
        {
            TestFMU32((ushort)Utils.Oct2Int(A), (ushort)Utils.Oct2Int(D), (ushort)Utils.Oct2Int(memA), (ushort)Utils.Oct2Int(memD), (ushort)Utils.Oct2Int(resA), (ushort)Utils.Oct2Int(resD), comment);
        }

        public void TestFMU32(ushort A, ushort D, ushort memA, ushort memD, ushort resA, ushort resD, string comment)
        {

            ushort address = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("110001"); // "FMU", "FMU *1");
            ushort P = address;

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            // Set the value +4 into TAD
            cpu.WriteALURegister(Enums.RegisterEnum.T, 0);
            cpu.WriteALURegister(Enums.RegisterEnum.A, A);
            cpu.WriteALURegister(Enums.RegisterEnum.D, D);


            cpu.memory.WritePhysicalMemory(address, opcode);
            cpu.memory.WritePhysicalMemory(address + 1, memA);
            cpu.memory.WritePhysicalMemory(address + 2, memD);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(0), $"FMU T - {comment}");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(resA), $"FMU A - {comment}");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(resD), $"FMU D - {comment}");
        }


        #endregion





    }
}
