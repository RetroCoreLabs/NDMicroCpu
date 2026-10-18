//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NDMicroCpu.Core;
using NUnit.Framework;

namespace NDMicroCpu.Core.Tests.PIC
{
    public class TestMMU
    {

        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }


        [Test]
        public void TEST_BFILL()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("140130"); //BFILL
            cpu.memory.WritePhysicalMemory(P, opcode);



            // Only the destination is used as an operand in this instruction(it is placed in the X and T registers).
            // The lower byte of the A register is then filled with the destination field.

            // Destination A, D
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)Utils.Oct2Int("054566"));  // D1 - Word address (source)  // Rightmost byte is written to memory ('v')
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)Utils.Oct2Int("000000"));  // 


            // Destination X, T
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)Utils.Oct2Int("000001"));  // D1 - Word address (destination)
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)Utils.Oct2Int("000000"));  // 


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate (BFILL with no error will give SKIP-RETURN)
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            // After execution, bit 15 of the T register points to the end of the field(after the last byte position) and the field length equals zero.

            // Check P
            // ADD TEST
        }

        [Test]
        public void TEST_BMOVE()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150307"); //MST PIE
            cpu.memory.WritePhysicalMemory(P, opcode);



            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, (ushort)Utils.Oct2Int("1"));  // Rightmost byte is written to memory ('v')


            // Destination A, D
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)Utils.Oct2Int("054566"));  // D1 - Word address (source)
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)Utils.Oct2Int("000000"));  // 


            // Destination X, T
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)Utils.Oct2Int("000001"));  // D1 - Word address (destination)
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)Utils.Oct2Int("000000"));  // 


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // ADD TEST
        }

        [Test]
        public void TEST_MST_PIE()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150307"); //MST PIE
            cpu.memory.WritePhysicalMemory(P, opcode);

            ushort A_val = 1 << 14; // Set bit 14


            ushort orgSTS = (ushort)(cpu.hw.alu.STS | 1 << 12);
            ushort orgPIE = cpu.hw.RF.Read(15, 11); // Read PIE
            ushort orgPID = cpu.hw.RF.Read(15, 12); // Read PID;
            ushort orgPIL = cpu.hw.alu.PIL;

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, A_val);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            ushort PIE = cpu.hw.RF.Read(15, 11); // Read PIE
            ushort PID = cpu.hw.RF.Read(15, 12); // Read PID
            ushort newPIL = cpu.hw.alu.PIL;

            ushort newSTS = (ushort)(cpu.hw.alu.STS | 1 << 12);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.That(orgPIE, Is.EqualTo(0), "PIE should not be set");
            Assert.That(orgPID, Is.EqualTo(0), "PID should be clear");
            Assert.That(orgPIL, Is.EqualTo(newPIL), "PIL should not change");

            Assert.That(newSTS, Is.EqualTo(orgSTS), "STS should not change");
            Assert.That(PIE, Is.EqualTo(A_val), "New PIE");


        }

        [Test]
        public void TEST_TRR_PCR()
        {

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");



            //ushort j = 0;
            //ushort i= 5;

            for (ushort j = 0; j < 2; j++)
            {
                for (ushort i = 0; i <= 15; i++)
                {
                    ushort opcode = (ushort)Utils.Oct2Int("150103"); //TRR PCR
                    cpu.memory.WritePhysicalMemory(P, opcode);

                    ushort APT = 3; //01-03  or  0x1-0xF
                    ushort PT = 3; //01-03  or  0x1-0xF

                    ushort RING = 01; // 0-3                     
                    ushort PIL = i;
                    ushort A_val = 0;



                    if (j == 0)
                        A_val = (ushort)(RING & 0x03 | (i << 3) | (APT & 0x03) << 7 | (PT & 0x03) << 9 | (1 << 15)); // Set PIL (Four page table mode)                
                    else
                        A_val = (ushort)(RING & 0x03 | (i << 3) | 1 << 2 | (APT & 0x0F) << 7 | (PT & 0x0F) << 11 | (1 << 15)); // Set PIL (Sixteen page table mode)

                    ushort expected_val = (ushort)(A_val & ~(1 << 15));

                    // Setup registers
                    cpu.WriteALURegister(Enums.RegisterEnum.A, A_val);
                    cpu.WriteALURegister(Enums.RegisterEnum.P, P);

                    // Execute
                    int newP = cpu.UnitTest_FetchAndExecute(P);

                    // Validate
                    Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                    Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

                    ushort expected_PIL = cpu.hw.alu.PIL;

                    ushort stored_val = cpu.hw.RF3.Read(PIL, 0);

                    Assert.That(stored_val, Is.EqualTo(expected_val), $"PCR[{PIL}] after ExecuteMacroInstruction");

                }
            }
            Assert.Pass();
        }
    }
}
