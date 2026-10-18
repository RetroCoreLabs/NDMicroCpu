//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NDMicroCpu.Core;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.NDCPU
{
    public class TestALU
    {

        CPU cpu = null!;   // created in SetUp before every test
        CPUType cpuType;

        [SetUp]
        public void Setup()
        {

            cpuType = CPUType.ND110;

            // CPU already loads its own cpuType-appropriate microcode in its constructor.
            cpu = new CPU(null, cpuType);

            // Was LogDestination.Nats + LogLevel.Opcodes: with no NATS server reachable,
            // NatsClient.Send() blocks forever inside NATS.Client.Connection.connect() on the
            // FIRST Opcodes-level Logger.Log call (IDB.Latch logs every micro-cycle), hanging
            // the whole test run - observed 2026-07-11 with RDIV_test stuck in TickCpu.
            // Test runs must never log to NATS.
            Logger.SetDestination(Logger.LogDestination.Disabled);
        }



        #region Arithmetical and Logical Instructions

        [Test]
        public void SUB_test()
        {

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("064001"); // "SUB", "SUB *1");



            ushort a_value = 0x1245;
            ushort memvalue = 0x567;
            ushort expected_res = (ushort)(a_value - memvalue);


            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(address + 1, memvalue);

            cpu.hw.alu.STS_C = false;

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, a_value);
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_res), "A Value after AND operation");
            Assert.That(cpu.hw.alu.STS_C, Is.EqualTo(true), "STS after SUB");

            Assert.Pass();
        }



        [Test]
        public void SUB_test_with_carry()
        {

            // See original reported problem with another emulator
            // https://github.com/CAVISU/NDsim/issues/1

            // SUB should behave identical to RSUB, and "RSUB dr sr" is the same as "RADD CM1 AD1 dr sr" which is "DR + ~SR +1" which gives carry
            // That SUB should work like this is not entirely clear from the documentation, unfortunately, but it's very clear for RSUB, and they have to handle carry the same way.


            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("064001");// "SUB", "SUB *1");

            ushort a_value = 1;
            ushort mem_value = 0;
            ushort expected_res = (ushort)(a_value - mem_value);


            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(address + 1, mem_value);

            //  regs.currentRegisters.STS.C = false;
            cpu.hw.alu.STS_C = false;

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, a_value);
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_res), "A Value after SUB operation");
            Assert.That(cpu.hw.alu.STS_C, Is.EqualTo(true), "STS after SUB");
            Assert.Pass();
        }


        [Test]
        public void AND_test()
        {

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("070001"); // "AND", "AND *1");



            ushort initial_value = 0x1245;
            ushort value = 0x567;
            ushort expected_res = (ushort)(initial_value & value);


            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(address + 1, value);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, initial_value);
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_res), "A Value after AND operation");

            Assert.Pass();
        }


        [Test]
        public void ORA_test()
        {

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("074001");// "ORA", "ORA *1");



            ushort initial_value = 0x1245;
            ushort value = 0x567;
            ushort expected_res = (ushort)(initial_value | value);


            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(address + 1, value);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, initial_value);
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_res), "A Value after OR operation");

            Assert.Pass();
        }
        #endregion Arithmetical and Logical Instructions


        #region Test RMPY

        [Test]
        public void RMPY_test()
        {
            /* integer inter-register multiply, RMPY <sr> <dr> 

            The sr and dr fields are used to specify the two operands to be multiplied (represented as two's complement integers), 
            the codes are the same as for ROP (see Section 3.2.3).

            The result is a 32 - bit signed integer which will be placed in the A and D registers with 
            the 16 most significant bits in the A register and the 16 least significant bits in the D register.


            //  Registers affected A,D 

            */
            // HW 
            //  | A     |      X | => |    A |       D | Carry | 
            // #|-----------------------------------------------
            // 1| 1     |      1 |    | 0000 |     001 | 
            // 2| 010   |    002 |    | 0000 |    0020 | 
            // 3| 177   |   177  |    | 0000 |   37401 | 

            // 4| 17777 |   177  |    | 0001 |  175601 |   <== TEST IS WRONG IN MANUAL
            // 4| 17777 |   177  |    | 0017 |  157601 |   <== THIS IS CORRECT A and D values

            // 5| 17777 | 17777  |    | 1777 |  140001 |   


            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");

            /// RMPY SA DX => Multiply the contents of the A and X registers together, leaving the result in the A and D registers.
            ushort opcode = (ushort)Utils.Oct2Int("141257"); // "RMPY", "RMPY SA DX");

            ushort initialA = 1;
            ushort initialX = 1;
            ushort resultA = 0; // = (ushort)(mul_result & 0xFFFF);
            ushort resultD = 1; // = (ushort)((mul_result >> 16) & 0xFFFF);


            for (int j = 1; j <= 5; j++)
            {

                switch (j)
                {
                    case 1:
                        initialA = 1;
                        initialX = 1;
                        resultA = 0;
                        resultD = 1;
                        break;
                    case 2:
                        initialA = 8; //010
                        initialX = 2;
                        resultA = 0;
                        resultD = 16; //020
                        break;
                    case 3:
                        initialA = 0x7F; //177
                        initialX = 0x7F; //177
                        resultA = 0;
                        resultD = 0x3F01; //37401
                        break;
                    case 4:
                        initialA = 0x1FFF; //17777
                        initialX = 0x7F; //177
                        resultA = 0xf; //17
                        resultD = 0xDF81;  // 157601
                        break;
                    case 5:
                        initialA = 0x1FFF; //17777
                        initialX = 0x1FFF; //17777 
                        resultA = 0x3FF; // 1777
                        resultD = 0xC001; // 140001
                        break;
                    default:
                        break;
                }

                Console.WriteLine($"Setup done for test #{j}");

                uint mul_result = (uint)(initialA * initialX);

#if _autocalc_result_                
                ushort resultA = (ushort)(mul_result & 0xFFFF);
                ushort resultD = (ushort)((mul_result >> 16) & 0xFFFF);
#endif
                // Setup registers

                ushort P = address;
                cpu.memory.WritePhysicalMemory(P, opcode);

                cpu.WriteALURegister(Enums.RegisterEnum.P, P);
                cpu.WriteALURegister(Enums.RegisterEnum.A, initialA);
                cpu.WriteALURegister(Enums.RegisterEnum.X, initialX);
                cpu.WriteALURegister(Enums.RegisterEnum.D, 0);

                Console.WriteLine($"_____ TEST #{j} _____");
                Console.WriteLine($"A=0x{initialA:X4}");
                Console.WriteLine($"D=0x{initialX:X4}");
                Console.WriteLine($"MUL={mul_result:X8}");

                Utils.Log($"Setup done for test #{j}", true);


                // Execute
                int newP = cpu.UnitTest_FetchAndExecute(P);

                Console.WriteLine($"_____ RESULT TEST #{j} _____");

                Console.WriteLine($"A=0x{cpu.ReadALURegister(Enums.RegisterEnum.A):X4}  Expected=0x{resultA:X4}");
                Console.WriteLine($"D=0x{cpu.ReadALURegister(Enums.RegisterEnum.D):X4}  Expected=0x{resultD:X4}");

                // Validate
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(resultA), "A Value after RMPY operation");
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(resultD), "D Value after RMPY operation");
            }

            Assert.Pass();
        }
        #endregion

        #region Test RMPY

        [Test]
        public void RDIV_test()
        {

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("141630");// "RDIV", "RDIV SB");

            ushort P = address;
            int newP = 0;
            short sA = 0;
            short sD = 0;
            short sB = 0;

            cpu.memory.WritePhysicalMemory(P, opcode);

            #region Test #1 => AD = 010, S(B) = 2 => A=4,D=0, Z=0

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD((ushort)Utils.Oct2Int("010"));


            cpu.WriteALURegister(Enums.RegisterEnum.B, 2);

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            // Execute

            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #1 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.That(sA, Is.EqualTo(4), "A Value after RDIV operation");  // MSB
            Assert.That(sD, Is.EqualTo(0), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(false), "STS.Z Value after RDIV operation");

            #endregion AD = 22, SR = 4 => A=5,D=2, Z=0


            #region Test #2 => AD = 22, SR = 4 => A=5,D=2, Z=0

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD(22);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 4);



            // Execute
            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #2 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.That(sA, Is.EqualTo(5), "A Value after RDIV operation");  // MSB
            Assert.That(sD, Is.EqualTo(2), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(false), "STS.Z Value after RDIV operation");

            #endregion AD = 22, SR = 4 => A=5,D=2, Z=0


            #region Test #3 => AD = 378452, SR = -16 => A=-23653,D=4, Z=0

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD(378452);

            int b_minus_16 = -16;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, (ushort)(b_minus_16));



            // Execute
            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #3 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.That(sA, Is.EqualTo(-23653), "A Value after RDIV operation");  // MSB
            Assert.That(sD, Is.EqualTo(4), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(false), "STS.Z Value after RDIV operation");

            #endregion AD = 378452, SR = -16 => A=-23653,D=4, Z=0

            #region Test #4 => AD = 32767, SR = 1 => A=32767,D=0, Z=0

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD(32767);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 1);



            // Execute
            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #4 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.That(sA, Is.EqualTo(32767), "A Value after RDIV operation");  // MSB
            Assert.That(sD, Is.EqualTo(0), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(false), "STS.Z Value after RDIV operation");

            #endregion Test #4 => AD = 32767, SR = 1 => A=32767,D=0, Z=0


            #region Test #5 => AD = 32768, SR = 1 => A/D=?? Z =1

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD(32768);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 1);



            // Execute
            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #5 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            //Assert.That(sA, Is.EqualTo(32767), "A Value after RDIV operation");  // MSB
            //Assert.That(sD, Is.EqualTo(0), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(true), "STS.Z Value after RDIV operation");

            #endregion Test #5 => AD = 32768, SR = 1 => A/D=?? Z =1

            #region Test #6 => AD = 65535, SR = 2 => A=32767,D=1, Z=0

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD(65535);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 2);



            // Execute
            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #6 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            ushort max16 = 32767;

            Assert.That((ushort)sA, Is.EqualTo(max16), "A Value after RDIV operation");  // MSB
            Assert.That(sD, Is.EqualTo(1), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(false), "STS.Z Value after RDIV operation");

            #endregion Test #6 => AD = 65535, SR = 2 => A=32767,D=1, Z=0


            #region Test #7 => AD = 65535, SR = 0 => A=32767,D=32767, Z=1 (Divide by zero)

            cpu.memory.WritePhysicalMemory(P, opcode);

            SetAD(65535);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 0);



            // Execute
            cpu.hw.alu.STS_Z = false; // Clear Z flage in STS (for unit testint). In reality the Z flag will need to be handle by the CPU if it happens.
            newP = cpu.UnitTest_FetchAndExecute(P);



            sA = (short)cpu.ReadALURegister(Enums.RegisterEnum.A);
            sD = (short)cpu.ReadALURegister(Enums.RegisterEnum.D);
            sB = (short)cpu.ReadALURegister(Enums.RegisterEnum.B);

            Console.WriteLine("_____ TEST #7 _____");
            Console.WriteLine($"A=0x{sA:X4}");
            Console.WriteLine($"D=0x{sD:X4}");
            Console.WriteLine($"STS.Z={cpu.hw.alu.STS_Z}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // Handwritten note in the ND-06 manual says that the result is A and D unchanged, and Z=1
            // But Microcode clears sA ?? Bug in emulator ?

            //Assert.That((ushort)sA, Is.EqualTo(max16), "A Value after RDIV operation");  // MSB  <== TODO: Validate what is correct on the ND-110/CX

            Assert.That((ushort)sD, Is.EqualTo(0xFFFF), "D Value after RDIV operation");  // LSB
            Assert.That(cpu.hw.alu.STS_Z, Is.EqualTo(true), "STS.Z Value after RDIV operation");

            #endregion Test #7 => AD = 65535, SR = 0 => A=32767,D=32767, Z=1 (Divide by zero)
            Assert.Pass();
        }

        void SetAD(int AD)
        {
            ushort valA = (ushort)((AD >> 16) & 0xFFFF);
            ushort valD = (ushort)(AD & 0xFFFF);

            cpu.WriteALURegister(Enums.RegisterEnum.A, valA);

            cpu.WriteALURegister(Enums.RegisterEnum.D, valD);
        }
        #endregion

    }
}
