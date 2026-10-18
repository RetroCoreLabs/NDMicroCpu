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
    public class TestBits
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }





        #region Bit Instructions


        [Test]
        public void BSET_ZRO()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");

            ushort expected = 0;


            // BSET should according to doc only work on low 8 bits..
            for (int i = 0; i <= 7; i++)
            //for (int i = 0; i <= 15; i++)
            {
                ushort opcode = (ushort)Utils.Oct2Int("174000"); //BSET ZRO

                opcode = (ushort)(opcode | (i << 3)); // bit number to set
                expected = (ushort)~(1 << i);
                cpu.hw.alu.SetSTSfromDebugger(0xFFFF);
                cpu.memory.WritePhysicalMemory(P, opcode);

                // Setup registers
                cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                // Execute
                int newP = cpu.UnitTest_FetchAndExecute(P);

                // Validate
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                Assert.That(cpu.hw.alu.STS, Is.EqualTo(expected), $"STS after ExecuteMacroInstruction - bit {i}");

            }

            Assert.Pass();
        }


        [Test]
        public void BSET_ONE()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");

            ushort expected = 0;



            // Documentation says that "Bset works on bit 0-7" but SINTRAN code actually sets bits in the high 8
            // ND-120 design documents shows that it only sets bits 0-7.. confused

            HashSet<int> ignoredValues = new HashSet<int> { 8, 9, 10, 11, 12, 13, 14, 15 };

            // BSET should according to doc only work on low 8 bits..
            //for (int i = 0; i <= 15; i++)
            for (int i = 0; i <= 7; i++)
            {

                // if (ignoredValues.Contains(i)) continue;

                ushort opcode = (ushort)Utils.Oct2Int("174200"); //BSET ONE

                opcode = (ushort)(opcode | (i << 3)); // bit number to set
                expected = (ushort)(1 << i);
                cpu.hw.alu.SetSTSfromDebugger(0);
                cpu.memory.WritePhysicalMemory(P, opcode);

                // Setup registers
                cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                // Execute
                int newP = cpu.UnitTest_FetchAndExecute(P);

                // Validate
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                Assert.That(cpu.hw.alu.STS, Is.EqualTo(expected), $"STS after ExecuteMacroInstruction - bit {i}");

            }

            Assert.Pass();
        }

        [Test]
        public void BSET_BCM()
        {
            // BCM on register 1 (D)
            // Invert bit

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");

            ushort expected = 0;


            for (int i = 0; i <= 15; i++)
            {

                // if (ignoredValues.Contains(i)) continue;

                ushort opcode = (ushort)Utils.Oct2Int("174401"); //BCM ONE                
                opcode = (ushort)(opcode | (i << 3)); // bit number to set
                cpu.memory.WritePhysicalMemory(P, opcode);

                expected = (ushort)(0xFFFF & ~(1 << i));
                cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);




                // Setup registers
                cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                // Execute
                int newP = cpu.UnitTest_FetchAndExecute(P);

                // Validate
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected), $"D after ExecuteMacroInstruction - bit {i}");

            }

            Assert.Pass();
        }


        [Test]
        public void BSET_BAC()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort expected = 0;



            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("174601"); //BSET BAC D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // K = TRUE
                        cpu.hw.alu.STS_K = true;
                        expected = (ushort)(1 << i);
                    }
                    else
                    {
                        // K = FALSE
                        cpu.hw.alu.STS_K = false;
                        expected = 0;
                    }

                    cpu.WriteALURegister(Enums.RegisterEnum.D, 0);

                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected), $"D after ExecuteMacroInstruction - bit {i}");

                }
            }

            Assert.Pass();
        }


        [Test]
        public void BSKP_ZRO()
        {

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            ushort expected_address = 0;



            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("175001"); //BSKP ZRO D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SKIP
                        expected_address = (ushort)(address + 2);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0);
                    }
                    else
                    {
                        // NO SKIP                        
                        expected_address = (ushort)(address + 1);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);
                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
                    Assert.That(newP, Is.EqualTo(expected_address), "Return value from ExecuteMacroInstruction");

                }
            }

            Assert.Pass();
        }

        [Test]
        public void BSKP_ONE()
        {

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            ushort expected_address = 0;



            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("175201"); //BSKP ONE D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SKIP
                        expected_address = (ushort)(address + 2);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);
                    }
                    else
                    {
                        // NO SKIP                        
                        expected_address = (ushort)(address + 1);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0);

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(expected_address), "Return value from ExecuteMacroInstruction");

                }
            }

            Assert.Pass();
        }


        [Test]
        public void BSKP_BCM()
        {
            // // Skip next instruction if bito != K 

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            ushort expected_address = 0;



            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("175401"); //BSKP BCM D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SKIP
                        expected_address = (ushort)(address + 2);
                        cpu.hw.alu.STS_K = true;
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0);
                    }
                    else
                    {
                        // NO SKIP                        
                        expected_address = (ushort)(address + 1);
                        cpu.hw.alu.STS_K = false;
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0);

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(expected_address), "Return value from ExecuteMacroInstruction");

                }
            }

            Assert.Pass();
        }



        [Test]
        public void BSKP_BAC()
        {
            // // Skip next instruction if bito != K 

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            ushort expected_address = 0;



            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("175601"); //BSKP BAC D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SKIP
                        expected_address = (ushort)(address + 2);
                        cpu.hw.alu.STS_K = true;
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);
                    }
                    else
                    {
                        // NO SKIP                        
                        expected_address = (ushort)(address + 1);
                        cpu.hw.alu.STS_K = false;
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(expected_address), "Return value from ExecuteMacroInstruction");

                }
            }

            Assert.Pass();
        }


        [Test]
        public void BSTC()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            ushort expected_D = 0;
            bool expected_K = true;


            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("176001"); //BSTC D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SET bit = 0 (/K)
                        cpu.hw.alu.STS_K = true;

                        expected_D = (ushort)~(1 << i);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);
                    }
                    else
                    {
                        // SET bit = 1 (/K)
                        cpu.hw.alu.STS_K = false;

                        expected_D = (ushort)(1 << i);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0);

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected_D), $"D after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS after ExecuteMacroInstruction - bit {i}");

                }
            }

            Assert.Pass();
        }



        [Test]
        public void BSTA()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            ushort expected_D = 0;
            bool expected_K = false;


            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("176201"); //BSTA D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SET bit = 0 (K)
                        cpu.hw.alu.STS_K = false;

                        expected_D = (ushort)~(1 << i);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0xFFFF);
                    }
                    else
                    {
                        // SET bit = 1 (K)
                        cpu.hw.alu.STS_K = true;

                        expected_D = (ushort)(1 << i);
                        cpu.WriteALURegister(Enums.RegisterEnum.D, 0);

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected_D), $"D after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS after ExecuteMacroInstruction - bit {i}");

                }
            }

            Assert.Pass();
        }



        [Test]
        public void BLDC()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            bool expected_K = false;


            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("176401"); //BLDC D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SET K = /bit = 0 
                        cpu.hw.alu.STS_K = true;
                        expected_K = false;

                        cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));
                    }
                    else
                    {
                        // SET K = /bit = 1 
                        cpu.hw.alu.STS_K = false;
                        expected_K = true;

                        cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS.K after ExecuteMacroInstruction - bit {i}");

                }
            }

            Assert.Pass();
        }

        [Test]
        public void BLDA()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            bool expected_K = false;


            for (int j = 0; j <= 1; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("176601"); //BLDA D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    if (j == 0)
                    {
                        // SET K = bit = 1 
                        cpu.hw.alu.STS_K = false;
                        expected_K = true;

                        cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));
                    }
                    else
                    {
                        // SET K = bit = 0 
                        cpu.hw.alu.STS_K = true;
                        expected_K = false;

                        cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                    }


                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS.K after ExecuteMacroInstruction - bit {i}");

                }
            }

            Assert.Pass();
        }

        [Test]
        public void BANC()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            bool expected_K = false;


            for (int j = 0; j <= 3; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("177001"); //BANC D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);


                    // Expected K = (/bit AND K)

                    switch (j)
                    {
                        case 0:  // K =0, Bit = 0
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = false;

                            break;
                        case 1:  // K = 0, Bit = 1
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));
                            expected_K = false;

                            break;
                        case 2:   // K = 1, Bit =0
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = true; // <== TRUE (one and only where K= true)

                            break;
                        case 3:   // K= 1, Bit = 1
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));

                            expected_K = false;
                            break;

                        default:
                            break;
                    }

                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS.K after ExecuteMacroInstruction - I={i}, J={j}");

                }
            }

            Assert.Pass();
        }

        [Test]
        public void BAND()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            bool expected_K = false;


            for (int j = 0; j <= 3; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("177201"); //BAND D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);


                    // Expected K = (bit AND K)

                    switch (j)
                    {
                        case 0:  // K =0, Bit = 0
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = false;

                            break;
                        case 1:  // K = 0, Bit = 1
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));
                            expected_K = false;

                            break;
                        case 2:   // K = 1, Bit =0
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = false;

                            break;
                        case 3:   // K= 1, Bit = 1
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));

                            expected_K = true; // <== TRUE (one and only where K= true)
                            break;

                        default:
                            break;
                    }

                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS.K after ExecuteMacroInstruction - I={i}, J={j}");

                }
            }

            Assert.Pass();
        }


        [Test]
        public void BORC()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            bool expected_K = false;


            for (int j = 0; j <= 3; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("177401"); //BORC D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);


                    // Expected K = (/bit OR K)

                    switch (j)
                    {
                        case 0:  // K =0, Bit = 0
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = true;

                            break;
                        case 1:  // K = 0, Bit = 1
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));
                            expected_K = false; // <== one and only false

                            break;
                        case 2:   // K = 1, Bit =0
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = true;

                            break;
                        case 3:   // K= 1, Bit = 1
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));

                            expected_K = true;
                            break;

                        default:
                            break;
                    }

                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS.K after ExecuteMacroInstruction - I={i}, J={j}");

                }
            }

            Assert.Pass();
        }


        [Test]
        public void BORA()
        {
            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("100");
            ushort P = address;
            bool expected_K = false;


            for (int j = 0; j <= 3; j++)
            {
                for (int i = 0; i <= 15; i++)
                {

                    ushort opcode = (ushort)Utils.Oct2Int("177601"); //BORA D

                    opcode = (ushort)(opcode | (i << 3)); // bit number to set
                    cpu.memory.WritePhysicalMemory(P, opcode);


                    // Expected K = (bit OR K)

                    switch (j)
                    {
                        case 0:  // K =0, Bit = 0
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = false; // <== one and only false

                            break;
                        case 1:  // K = 0, Bit = 1
                            cpu.hw.alu.STS_K = false;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));
                            expected_K = true;

                            break;
                        case 2:   // K = 1, Bit =0
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)~(1 << i));

                            expected_K = true;

                            break;
                        case 3:   // K= 1, Bit = 1
                            cpu.hw.alu.STS_K = true;
                            cpu.WriteALURegister(Enums.RegisterEnum.D, (ushort)(1 << i));

                            expected_K = true;
                            break;

                        default:
                            break;
                    }

                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), $"P after ExecuteMacroInstruction I={i}, J={j}");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                    Assert.That(cpu.hw.alu.STS_K, Is.EqualTo(expected_K), $"STS.K after ExecuteMacroInstruction - I={i}, J={j}");

                }
            }

            Assert.Pass();
        }
        #endregion Bit Instructions


    }
}
