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
    public class TestAdressing
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }


        #region Boot CPU - Master Clear
        //[Test]
        public void Boot_RunMasterClear()
        {
            ushort address = 0;
            //int cnt = 0;
            int ExpectedAddress = 123;

            // Run micro code to see if we end up at expected address. But cancel after max 100 instructions
            cpu.hw.mic.LatchMap(address);
            cpu.UnitTest_RunUntilFetch(500000);

            //Assert.That(cnt, Is.EqualTo(ExpectedCnt), "Number of uc instructions executed");
            Assert.That(cpu.hw.mac.FETCH, Is.True, "FETCH should be true");
            Assert.That(cpu.hw.mic.map, Is.EqualTo(ExpectedAddress), "Address after MCL");


            Assert.Pass();
        }

        #endregion Boot CPU


        #region Test Memory Adressing Modes - LOAD

        [Test]
        public void Test_P_Relative()
        {

            /* Microcode 
             * 
             * 006440  LDA:      ALUD,NONE           IDBS,ALU            COMM,AREAD,*        T,JMP               T,HOLD              LDA1;
             * 000170  LDA1:     B,A                 ALUF,PASSD          ALUD,B              IDBS,DBR            COMM,CONTINUE       T,JMP               T,HOLD;
            */
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("44002"); //LDA *2  (44000) - Load A from Memory
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xBEEF);

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
        public void Test_B_Relative()
        {

            // Setup memory
            ushort expected_val = 0xA5B7;

            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0044774"); //"LDA -4,B"
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); //Some random value to A
            cpu.WriteALURegister(Enums.RegisterEnum.B, (ushort)Utils.Oct2Int("10035"));

            cpu.memory.WritePhysicalMemory(cpu.ReadALURegister(Enums.RegisterEnum.B) - 4, expected_val);


            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_val), "A after ExecuteMacroInstruction");

            Assert.Pass();
        }

        [Test]
        public void Test_P_Indirect()
        {
            // Setup memory
            ushort expected_val = 0xA5B7;

            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0045002"); //LDA I *2
            ushort mem_address = (ushort)Utils.Oct2Int("016003");
            ushort B = (ushort)Utils.Oct2Int("10035");

            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); //Some random value to A
            cpu.WriteALURegister(Enums.RegisterEnum.B, B);

            cpu.memory.WritePhysicalMemory(mem_address, expected_val);
            cpu.memory.WritePhysicalMemory(P + 2, mem_address);


            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_val), "A after ExecuteMacroInstruction");

            Assert.Pass();
        }


        [Test]
        public void Test_B_Indirect()
        {
            // Setup memory
            ushort expected_val = 0xA5B7;

            ushort P = (ushort)Utils.Oct2Int("0600");
            //ushort opcode = (ushort)Utils.Oct2Int("135403"); // "JPL", "JPL I 3,B");
            ushort opcode = (ushort)Utils.Oct2Int("045403"); // LDA I 3,B


            ushort subr_address_pointer = (ushort)Utils.Oct2Int("0400");
            ushort subr_address = (ushort)Utils.Oct2Int("020000");
            ushort mem_address = (ushort)Utils.Oct2Int("016003");

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(subr_address_pointer + 3, subr_address);
            cpu.memory.WritePhysicalMemory(subr_address, expected_val);


            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); //Some semi-random value to A
            cpu.WriteALURegister(Enums.RegisterEnum.B, subr_address_pointer);



            cpu.memory.WritePhysicalMemory(mem_address, expected_val);

            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_val), "A after ExecuteMacroInstruction");
            //Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(subr_address), "New Program Counter (P) after JPL");
            Assert.Pass();
        }

        [Test]
        public void Test_B_Indirect_JMP()
        {
            // Setup memory
            ushort expected_val = 0xA5B7;

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("135403"); // "JPL", "JPL I 3,B");            


            ushort subr_address_pointer = (ushort)Utils.Oct2Int("0400");
            ushort subr_address = (ushort)Utils.Oct2Int("020000");
            ushort mem_address = (ushort)Utils.Oct2Int("016003");

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(subr_address_pointer + 3, subr_address);


            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, expected_val); //Some semi-random value to A
            cpu.WriteALURegister(Enums.RegisterEnum.B, subr_address_pointer);



            cpu.memory.WritePhysicalMemory(mem_address, expected_val);

            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(subr_address), "New Program Counter (P) after JPL");
            Assert.That(newP, Is.EqualTo(subr_address), "New Program Counter (P) after JPL");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_val), "A after ExecuteMacroInstruction"); // a shouldnt change
            Assert.Pass();
        }


        #endregion Test Memory Addressing Modes


        #region SKIP instructions

        // The next instruction is skipped if a specified condition is true.
        // SKP <dr> <cond> <sr>


        /*
        SKP can be qualified by eight different mnemonics which the flags set by the following expression:

        (dr) - (sr)
        
        Four flags are affected by this calculation
            S sign
            Z zero result (error)
            C carry
            O overflow

        */

        /*
       
        All SKP microcode looks like this (this is for mnemonic MGRE (011))

        12348  007414  
        12349  007414  SKMGR:
        12350  007414            A,SRCE              B,DEST              ALUF,B-A            ALUD,NONE           IDBS,ALU            COMM,CNEXT,NCRY     T,JMP               T,HOLD              SKIP1;
        0435   000215  SKIP1:    ALUF,ZERO           ALUD,NONE           IDBS,ALU            COMM,CNEXT,F=0      T,JMP               T,HOLD;

         */



        [Test]
        public void SKP_Test_0_DX_EQL_SB()
        {
            // Test EQL opcode (condition bits 000 = 0)
            // Flag: Z (Zero) = 1
            // UC: CNEXT,NF=0
            //
            // SKP IF DX EQL SB  <= Skip if X==B

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("140037"); // SKP IF DX EQL SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");


            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 100);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }

        //[Test]
        public void SKP_Test_0_DX_EQL_SB_INVERTED()
        {
            // Test EQL opcode (condition bits 000 = 0)
            // Flag: Z (Zero) = 1
            // UC: CNEXT,NF=0
            //
            // SKP IF DX EQL SB  <= Skip if X==B

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("140037"); // SKP IF DX EQL SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 1);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");


            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        [Test]
        public void SKP_Test_2_DX_GRE_SB_()
        {
            // Test GRE opcode (condition bits 001 = 1)
            // sr and dr are treated as signed numbers.
            //
            // SKP IF DX GRE SB => Skip if X >= B 

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("141037"); // SKP IF DX GRE SB

            // TODO: ADD NEGATIVE NUMBERS FOR TEST!

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 12); // X is bigger than B, should get P+2
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 4); // X is smaller than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        [Test]
        public void SKP_Test_1_DX_GEQ_SB()
        {
            // Test GEQ opcode (condition bits 001 = 1). 
            // sr and dr are treated as signed numbers.
            // Flag: S (Sign) = 0
            // uc: CNEXT,F15 
            //
            // SKP IF DX GEQ SB => Skip if X >= B 

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("140437"); // SKP IF DX GEQ SB


            #region Positive 1 - GREATER

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is bigger than B, should get P+2 (checks F15..)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            #endregion Positive 1

            #region Positive 2 - EQUAL

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is bigger than B, should get P+2 (checks F15..)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            #endregion Positive  2 - EQUAL

            #region Negative 
            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is smaller than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 12);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);

            #endregion Negative 


            Assert.Pass();

        }


        //[Test]
        public void SKP_Test_1_DX_GEQ_SB_INVERTED()
        {
            // Test GRE opcode (condition bits 001 = 1). 
            // sr and dr are treated as signed numbers.
            // Flag: S (Sign) = 0
            // uc: CNEXT,F15 
            //
            // SKP IF DX GEQ SB => Skip if X >= B 

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("140437"); // SKP IF DX GEQ SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is bigger than B, should get P+2 (checks F15..)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 15);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 15); // X is smaller than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        [Test]
        public void SKP_Test_3_DL_MGRE_ST()
        {
            // Test MGRE opcode (condition bits 011 = 3).  (Magnitude greater or equal to)
            // Overflow is taken care of.
            // 
            // SKP IF DL MGRE ST => Skip if L >= T

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("141464"); //SKP IF DL MGRE ST


            #region Positive 1 - GREATER
            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 12);
            cpu.WriteALURegister(Enums.RegisterEnum.T, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done  - 1", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            #endregion Positive 1 - GREATER



            #region Positive 2 - EQUAL
            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.T, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done  - 2", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            #endregion Positive 2 - EQUAL


            #region NEGATIVE - LESS
            Utils.Log("Passed positive test, not setting up for negative test", true);



            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.T, 12);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup 2 done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);

            #endregion NEGATIVE - LESS

            Assert.Pass();

        }

        [Test]
        public void SKP_Test_4_DL_UEQ_O()
        {
            // Test UEQ opcode (condition bits 100 = 4)            
            //
            // SKP IF DL UEQ 0 <= Skip if L register != 0

            ushort P = (ushort)Utils.Oct2Int("0600");
            //ushort opcode = (ushort)Utils.Oct2Int("142004"); // SKP IF DL UEQ 0
            ushort opcode = (ushort)Utils.Oct2Int("142040"); // SKP IF SL UEQ 0


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");


            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 0);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        //[Test]
        public void SKP_Test_4_DL_UEQ_O_INVERTED()
        {
            // Test UEQ opcode (condition bits 100 = 4)            
            //
            // SKP IF DL UEQ 0 <= Skip if L register != 0

            ushort P = (ushort)Utils.Oct2Int("0600");
            //ushort opcode = (ushort)Utils.Oct2Int("142004"); // SKP IF DL UEQ 0
            ushort opcode = (ushort)Utils.Oct2Int("142040"); // SKP IF SL UEQ 0


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 0);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");


            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }
        // Comparing two registers

        [Test]
        public void SKP_Test_4_DT_UEQ_SX()
        {
            // Test UEQ opcode (condition bits 100 = 4). Second test variant of UEQ
            //
            // SKP IF DT UEQ SX

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("142076"); //  SKP IF DT UEQ SX


            ushort test_value = 0xF5A7;
            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)(test_value));
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)(test_value + 5));

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);

            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)(test_value));
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)(test_value));
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }

        //[Test]
        public void SKP_Test_4_DT_UEQ_SX_INVERTED()
        {
            // Test UEQ opcode (condition bits 100 = 4). Second test variant of UEQ
            //
            // SKP IF DT UEQ SX

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("142076"); //  SKP IF DT UEQ SX


            ushort test_value = 0xF5A7;
            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)(test_value));
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)(test_value));

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);

            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.T, (ushort)(test_value));
            cpu.WriteALURegister(Enums.RegisterEnum.X, (ushort)(test_value + 5));
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }

        // Comparing the arithmetic value of the contents of two registers:



        [Test]
        public void SKP_Test_5_DX_LSS_SB()
        {
            // Test LSS opcode (condition bits 101 = 5). 
            //
            // SKP IF DX GRE SB =<  Skip if X < B

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("142437"); // SKP IF DX LSS SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 1); // X is less than B, should get P+2  (checks CNEXT,NF15 then P++)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 15);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is greater than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        //[Test]
        public void SKP_Test_5_DX_LSS_SB_INVERTED()
        {
            // Test LSS opcode (condition bits 101 = 5). 
            //
            // SKP IF DX GRE SB =<  Skip if X < B

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("142437"); // SKP IF DX LSS SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is less than B, should get P+2  (checks CNEXT,NF15 then P++)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 1); // X is greater than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 15);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }

        [Test]
        public void SKP_Test_6_DX_LST_SB()
        {
            // Test MLST opcode (condition bits 110 = 6).  (Less than)
            // Overflow is taken care of. sr and dr are treated as signed numbers.
            //
            // SKP IF DX LST SB => Skip if X < B


            //TODO: Add test for negative numbers also

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("143037"); // SKP IF DX LST SB


            #region POSITIVE TEST
            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 1); // X is less than B, should get P+2  (checks CNEXT,NF15 then P++)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 15);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");


            #endregion POSITIVE TEST

            Utils.Log("Passed positive test, now setting up for negative test", true);

            #region NEGATIVE - greater
            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is greater than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            #endregion NEGATIVE - greater


            #region NEGATIVE - equal
            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 5); // X is equal to B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            #endregion NEGATIVE - equal


            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        //[Test]
        public void SKP_Test_6_DX_LST_SB_INVERTED()
        {
            // Test GRE opcode (condition bits 010 = 2). 
            // Overflow is taken care of. sr and dr are treated as signed numbers.
            //
            // SKP IF DX GEQ SB => Skip if X >= B 

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("143037"); // SKP IF DX LST SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is bigger than B, should get P+2 (checks F15..)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is smaller than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 12);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        [Test]
        public void SKP_Test_7_DX_MLST_SB()
        {
            // Test MLST opcode (condition bits 111 = 7).  (Magnitude less than)
            // Overflow is taken care of
            //
            // SKP IF DX MLST SB => Skip if X < B

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("143437"); // SKP IF DX MLST SB


            #region POSITIVE test
            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 1); // X is less than B, should get P+2  (checks CNEXT,NF15 then P++)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 15);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            #endregion POSITIVE test

            Utils.Log("Passed positive test, now setting up for negative test", true);

            #region NEGATIVE TEST - greater
            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is greater than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done - greater", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            #endregion NEGATIVE TEST - greater

            #region NEGATIVE TEST - equal
            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is greater than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 10);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            #endregion NEGATIVE TEST - equal

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }


        //[Test]
        public void SKP_Test_7_DX_MLST_SB_INVERTED()
        {
            // Test MLST opcode (condition bits 111 = 7).  (Magnitude less than)
            // Overflow is taken care of
            //
            // SKP IF DX MLST SB => Skip if X < B

            ushort P = (ushort)Utils.Oct2Int("0600");
            ushort opcode = (ushort)Utils.Oct2Int("143437"); // SKP IF DX MLST SB


            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 10); // X is less than B, should get P+2  (checks CNEXT,NF15 then P++)
            cpu.WriteALURegister(Enums.RegisterEnum.B, 5);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 2), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 2), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed positive test, now setting up for negative test", true);


            // Setup registers
            P = (ushort)Utils.Oct2Int("0600");
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 1); // X is greater than B, should get P+1
            cpu.WriteALURegister(Enums.RegisterEnum.B, 15);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Utils.Log("Passed negative test!!", true);
            Assert.Pass();

        }



        // Comparing two magnitude numbers:
        // The magnitude tests are especially useful when comparing the relationship between memory addresses which are represented as magnitude numbers in a computer with more than 32K memory.


        #endregion


        #region MIN instruction

        [Test]
        public void TEST_MIN()
        {

            // increment memory and skip if zero (ea)
            // The contents of the memory location pointed to by the effective address are incremented by one.
            // If the new memory location when incremented becomes zero, the next instruction is skipped.

            ushort address = (ushort)Utils.Oct2Int("0600");


            // JAP - Jump if A register is positive or zero, A bit 15 = 0.
            ushort opcode = (ushort)Utils.Oct2Int("040002"); // MIN *2
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFFF); //Increment will give 0, so skip

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFFE); //Increment will NOT give 0, so no skip

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }

        #endregion

        #region Sequencing instructions
        [Test]
        public void JAP()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // JAP - Jump if A register is positive or zero, A bit 15 = 0.
            ushort opcode = (ushort)Utils.Oct2Int("130002"); // "JAP", "JAP 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x123);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFE);

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }


        [Test]
        public void JAN()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // Jump if A register is negative, A bit 15 = 1.
            ushort opcode = (ushort)Utils.Oct2Int("130402"); //"JAN", "JAN 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFE);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // Jump if A register is negative, A bit 15 = 1.
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 123);

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }


        [Test]
        public void JAZ()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // Jump if A register is zero.
            ushort opcode = (ushort)Utils.Oct2Int("131002"); // "JAZ", "JAZ 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // Jump if A register is zero.
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 123);

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }



        [Test]
        public void JAF()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // Jump if A register is filled (not zero).
            ushort opcode = (ushort)Utils.Oct2Int("131402"); // "JAF", "JAF 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 123);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // Jump if A register is filled (not zero).
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0);

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }

        [Test]
        public void JXN()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // JXN - Jump if X register is negative. X bit 15 = 1.
            ushort opcode = (ushort)Utils.Oct2Int("133402"); // "JXN", "JXN 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0xFFFE);// Negative number, expect jump

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // JXN - Jump if X register is negative. X bit 15 = 1.
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0x77);// Positive number, no jump

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }


        [Test]
        public void JXZ()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // JXZ - Jump if X register is zero.
            ushort opcode = (ushort)Utils.Oct2Int("133002"); // "JXZ", "JXZ 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // JXZ - Jump if X register is zero.
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0x77);// Positive number, no jump

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }


        [Test]
        public void JPC()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // JPC - Count and jump if X register is positive or zero.
            ushort opcode = (ushort)Utils.Oct2Int("132002"); // "JPC", "JPC 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region jump, after counting from -1 to 0
            expected_address = address + 2;

            // Setup registers            
            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0xFFFF);

            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // JPC - Count and jump if X register is positive or zero.
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0xFFFE);// huge negative, no jump

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }



        [Test]
        public void JNC()
        {
            ushort address = (ushort)Utils.Oct2Int("0600");


            // JNC - Count and jump if (X) < 0 (jump if negative and count)
            ushort opcode = (ushort)Utils.Oct2Int("132402"); // "JNC", "JNC 2");
            ushort P = address;

            #region Jump
            int expected_address = address + 2;

            // Setup registers
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0xFFFA);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region jump, after counting from -1 to 0
            expected_address = address + 2;

            // Setup registers            
            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 32767);

            Utils.Log("Setup done", true);


            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");
            #endregion Jump


            #region NoJump
            // JPC - Count and jump if X register is positive or zero.
            expected_address = address + 1;

            P = address;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0);// 

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_address), "P after ExecuteMacroInstruction");

            #endregion NoJump

            Assert.Pass();
        }
        #endregion Sequencing instructions


        #region Helper Functions for bit operations

        ushort ShiftRightArithmetic(ushort value, int n)
        {
            ushort sign = 0;
            // Keep sign bit
            for (int i = 0; i < n; i++)
            {
                sign = (ushort)(value & 0x8000);
                value = (ushort)(value >> 1);

                value |= sign;
            }
            return value;
        }


        ushort RotateRight(ushort value, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((value & 1) != 0)
                {
                    value = (ushort)(value >> 1 | 1 << 15);
                }
                else
                {
                    value = (ushort)(value >> 1);
                }
            }
            return value;
        }


        ushort RotateLeft(ushort value, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if ((value & 1 << 15) != 0)
                {
                    value = (ushort)(value << 1 | 1);
                }
                else
                {
                    value = (ushort)(value << 1);
                }
            }
            return value;
        }

        #endregion
    }
}
