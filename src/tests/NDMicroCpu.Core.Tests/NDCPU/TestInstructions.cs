//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

/*

Unknown:

How should this command really work ?
    Command:Command22 (MIS:3 A:0) - CNEXT,NWP - TRUE
    Doc says: Conditional "next�, if P is not changed in the last cycle.
    
    There is also many other CNEXT,xxxx commands
    There is also many "conditional jump" CJMP 

*/
using NDMicroCpu.Core.Common;

namespace NDMicroCpu.Core.Tests.NDCPU
{
    public class TestInstructions
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;

            cpu = new CPU();

            // Was LogDestination.Nats + LogLevel.Opcodes: with no NATS server reachable,
            // NatsClient.Send() blocks forever inside NATS.Client.Connection.connect() on the
            // FIRST Opcodes-level Logger.Log call (IDB.Latch logs every micro-cycle), hanging
            // the whole test run - observed 2026-07-11 with TestALU.RDIV_test stuck in TickCpu.
            // Test runs must never log to NATS.
            Logger.SetDestination(Logger.LogDestination.Disabled);
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

        #region Direct Microcode testing - simple tests

        [Test]
        public void Uc_TestJmpFrom0()
        {
            // ATM MACL symbol is 001752 (oct) => 0x3EA  // Correct address when all compiles is 001752, so here we are missing a few "end of uc" ;

            ushort MACL_SYMBOL = 0x3EA;

            // Jump to MACL
            // 
            //  0/
            //  0029  000000 % MASTER CLEAR / POWER CLEAR
            //  0030  000000          AB,MACL     ALUD, NONE
            //  0031  000000          IDBS,ARG    COMM,EWRF    T,JMP    T, HOLD
            //  0032  000000          MACL;


            // Latch regs.ARG to idb => idb.Latch(regs.ARG);
            // AB, MACL => Adress for register in   A,B = (12,9) (dec)  (SCRATCH WORD HOLDING RETRY COUNTER FOR LOAD AFTER MACL)
            // COMM,EWRF => IDB -> REGISTER FILE WORD ADDRESSED BY A-OP AND B-OP
            //
            // ==> Write regs.ARG to registerfile at level 12, register 9

            // Stack = HOLD
            // MIC.map (address) = MACL = 0x3EA

            // Return address =?


            cpu.hw.RF.Write(12, 9, 5);
            cpu.hw.cpu_regs.ARG = 0x1234;

            cpu.hw.mic.LatchMap(0);
            cpu.UnitTest_RunUntilFetch(1);
            ushort regval = cpu.hw.RF.Read(12, 9);

            Assert.That(regval, Is.EqualTo(cpu.hw.cpu_regs.ARG), "RegisterValue(12,9) after uc");
            Assert.That(cpu.hw.mic.map, Is.EqualTo(MACL_SYMBOL), "Address after uc");

            Assert.Pass();
        }


        [Test]
        public void Uc_TestJmpFrom1()
        {
            // PVPF=000027 => 0x17

            ushort PVPF_SYMBOL = 0x17;

            // Jump to MACL
            // 
            //  1/

            // A,6                                               ALUD,NONE
            // IDBS,BMG                COMM,SMPID    T,JMP        T,HOLD
            // PVPF;

            // Latch regs.BMG to idb => idb.Latch(regs.ARG);  ( BIT-MASK-GENERATOR -> IDB)
            //
            // A,6 => A operator = 6
            // COMM,SMPID => IDB (BMG)-> Set PIC PID-bits
            // = Write regs.ARG to registerfile at level 12, register 9

            // Stack = HOLD
            // MIC.map (address) = MACL = 0x3e0

            // Return address = 0x3e0


            //cpu.hw.alu.WriteWRF(12, 9, 0);
            //cpu.hw.cpu_regs.ARG = 0x1234;

            //cpu.hw.cpu_regs.BMG = 0x1234;

            cpu.hw.mic.LatchMap(1);
            cpu.TickCpu();
            //ushort regval = cpu.hw.alu.ReadWRF(12, 9);

            //Assert.That(regval, Is.EqualTo(cpu.hw.cpu_regs.ARG), "RegisterValue(9,12) after uc");
            Assert.That(cpu.hw.mic.map, Is.EqualTo(PVPF_SYMBOL), "Address after uc 1");

            // Execute next UC

            // AB,PGS                           ALUD,NONE
            // IDBS,PGS         COMM, EWRF      T,NEXT          T,HOLD
            // COND,FETCH                       F,NEXT          F,HOLD;

            // Write "PGS" to WRF(PGS) 

            cpu.TickCpu();
            Assert.That(cpu.hw.mic.map, Is.EqualTo(PVPF_SYMBOL + 1), "Address after uc 2");

            Assert.Pass();
        }






        [Test]
        public void Uc_IncreaseR3_R3INC()
        {
            ushort address = 0x397; // 001627
            ushort value = 1;
            ushort expected_value = 2;

            cpu.WriteALURegister(Enums.RegisterEnum.R3, value);
            // TODO: Sett R3            
            cpu.hw.mic.LatchMap(address);
            cpu.UnitTest_RunUntilFetch();


            // TODO: Validate R3 Inc
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.R3), Is.EqualTo(expected_value), "R3 after uc");
            Assert.Pass();
        }

        [Test]
        public void Uc_DecreaseR3_R3DCR()
        {

            ushort address = 0x398; // 001630
            ushort value = 2;
            ushort expected_value = 1;

            cpu.WriteALURegister(Enums.RegisterEnum.R3, value);
            cpu.hw.mic.LatchMap(address);
            cpu.UnitTest_RunUntilFetch();


            // TODO: Validate R3 Dec
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.R3), Is.EqualTo(expected_value), "R3 after uc DCR");
            Assert.Pass();
        }
        #endregion Direct Microcode testing - simple tests

        #region LOAD instructions

        // TODO: Make tests for all adressing modes: https://www.ndwiki.org/wiki/ND-100_addressing_modes

        [Test]
        public void Load_LDA()
        {

            /* Microcode 
             * 
             * 006440  LDA:      ALUD,NONE           IDBS,ALU            COMM,AREAD,*        T,JMP               T,HOLD              LDA1;
             * 000170  LDA1:     B,A                 ALUF,PASSD          ALUD,B              IDBS,DBR            COMM,CONTINUE       T,JMP               T,HOLD;
            */
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("44000"); //LDA *  (44000) - Load A from Memory
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); // set som random value to A register                                   
            Utils.LogEOL();

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(opcode), "A after ExecuteMacroInstruction");

            Assert.Pass();


            Assert.Pass();
        }

        [Test]
        public void Load_LDA02()
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
        public void Load_LDA_Negtive_Displacement()
        {

            // test value
            ushort test_value = 0xBEEF;

            // Setup memory
            ushort B = (ushort)Utils.Oct2Int("1000");
            ushort P = (ushort)Utils.Oct2Int("400");
            ushort opcode = (ushort)Utils.Oct2Int("044610"); // LDA -170,B



            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(B - 119, (ushort)(test_value - 1));
            cpu.memory.WritePhysicalMemory(B - 120, test_value);
            cpu.memory.WritePhysicalMemory(B - 121, (ushort)(test_value + 1));


            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, B);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x1234); // set som random value to A register                                   
            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(test_value), "A after ExecuteMacroInstruction");

            Assert.Pass();


            Assert.Pass();
        }

        [Test]
        public void Load_A_VIA_EXR()
        {

            /* Microcode 
             * 
             * 006440  LDA:      ALUD,NONE           IDBS,ALU            COMM,AREAD,*        T,JMP               T,HOLD              LDA1;
             * 000170  LDA1:     B,A                 ALUF,PASSD          ALUD,B              IDBS,DBR            COMM,CONTINUE       T,JMP               T,HOLD;
            */
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");

            ushort exr_opcode = (ushort)Utils.Oct2Int("140650"); // EXR SA


            ushort lda1_opcode = (ushort)Utils.Oct2Int("44001"); //LDA *1  (44000) - Load A from Memory
            ushort lda2_opcode = (ushort)Utils.Oct2Int("44002"); //LDA *2  (44000) - Load A from Memory

            cpu.memory.WritePhysicalMemory(P, exr_opcode); // Load in opcode that EXR SA will run
            cpu.memory.WritePhysicalMemory(P + 1, 0xDEAD);
            cpu.memory.WritePhysicalMemory(P + 2, 0xBEEF);
            cpu.memory.WritePhysicalMemory(P + 3, 0xFEAD);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, lda2_opcode);  // execute LDA1 
            Utils.Log("Setup done", true);

            // Execute instruction  EXR (LDA 01) 
            int newP = cpu.UnitTest_FetchAndExecute(P);




            // Validate
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);




            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction 1");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(A, Is.EqualTo(0xBEEF), "A after LDA *2");

            Assert.That(cpu.hw.alu.GPR, Is.EqualTo(lda2_opcode), "GPR should be set");
            Assert.That(cpu.hw.cpu_regs.IR, Is.EqualTo(lda2_opcode & 0x7ff), "IR bits 0-10 should be set");

            //hw.cpu_regs.IR = operand & 0x7ff



            Assert.Pass();
        }

        #endregion LOAD instructions

        #region STORE instructions


        [Test]
        public void Store_STZ()
        {

            //cpu.WriteVirtualMemory(address + 2, test_value, CpuND100.PageTable.StandardPageTable);
            //ushort memval = (ushort)cpu.ReadVirtualMemory(address + 2, CpuND100.PageTable.StandardPageTable);



            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0000002"); // STZ *2
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 1, 0x01);
            cpu.memory.WritePhysicalMemory(P + 2, 0x02);
            cpu.memory.WritePhysicalMemory(P + 3, 0x03);
            cpu.memory.WritePhysicalMemory(P + 4, 0x04);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); // set som random value to A register                                   
            Utils.LogEOL();

            // Execute
            // Execute: store "0" to the memory address where the Opode is +2          
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0xBEEF), "A after ExecuteMacroInstruction"); // Shouldnt change

            ushort m = cpu.memory.ReadPhysicalMemory(P + 2);
            Assert.That(m, Is.EqualTo(0), "Memory value P+2 from ExecuteMacroInstruction");




            Assert.Pass();
        }


        [Test]
        public void Store_STA()
        {

            //cpu.WriteVirtualMemory(address + 2, test_value, CpuND100.PageTable.StandardPageTable);
            //ushort memval = (ushort)cpu.ReadVirtualMemory(address + 2, CpuND100.PageTable.StandardPageTable);

            ushort test_value = 0xBEEF;

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0004002"); // STA *2
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 1, 0x01);
            cpu.memory.WritePhysicalMemory(P + 2, 0x02);
            cpu.memory.WritePhysicalMemory(P + 3, 0x03);
            cpu.memory.WritePhysicalMemory(P + 4, 0x04);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set som random value to A register                                   
            Utils.LogEOL();

            // Execute
            // Execute: store "" to the memory address where the Opode is +2          
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(test_value), "A after ExecuteMacroInstruction"); // Shouldnt change

            ushort m = cpu.memory.ReadPhysicalMemory(P + 2);
            Assert.That(m, Is.EqualTo(test_value), "Memory value P+2 from ExecuteMacroInstruction");


            Assert.Pass();
        }


        #endregion STORE instructions
        #region Register Instructions

        [Test]
        public void Register_SWAP_A_D()
        {
            // Tested OK 02.02.2023

            // Page 58 in ND-06.029.1 EN

            /* Microcode 
MEM WRITE A:000100 V:146773 - MEM WRITE A:000102 V:177652 - ALU.W: R:2 V:000100 - ALU.W: R:7 V:002322 - ALU.W: R:3 V:157255 - MEM READ A:000100 V:146773 - ALU.R: R:2 V:000100 - ALU.R: R:2 V:000100 - Executing operand 146773 at uc-address 007467
[007467] - B=[1]000003 - A=[5]000007 - IDBS.Latch(000100) - IDBS=ALU - IDBS.Read(000100) - ALU.R: R:7 V:002322 - @SM=LIN - ALU.W: R:3 V:175456 - IDBS.Latch(175456) - Command:Command22 (MIS:3 A:7) - CNEXT,NWP - TRUE - StackContol: HOLD - AddressSelect: NEXT - MAP= 007470
[007470] - B=[1]000003 - A=[5]000007 - IDBS.Latch(175456) - IDBS=ALU - IDBS.Read(175456) - ALU.R: R:7 V:002322 - ALU.R: R:3 V:175456 - @SM=LIN - ALU.W: R:3 V:000000 - IDBS.Latch(000000) - Command:Command22 (MIS:3 A:7) - CNEXT,NWP - TRUE - StackContol: HOLD - AddressSelect: NEXT - MAP= 007471
[007471] - B=[1]000003 - A=[5]000007 - IDBS.Latch(000000) - IDBS=ALU - IDBS.Read(000000) - ALU.R: R:7 V:002322 - @SM=LIN - ALU.W: R:3 V:002323 - IDBS.Latch(002323) - Command:Command22 (MIS:3 A:7) - CNEXT,NWP - TRUE - StackContol: HOLD - AddressSelect: NEXT - MAP= 007472
[007472] - B=[1]000003 - A=[5]000007 - IDBS.Latch(002323) - IDBS=ALU - IDBS.Read(002323) - ALU.R: R:7 V:002322 - ALU.R: R:3 V:002323 - @SM=LIN - ALU.W: R:3 V:000000 - IDBS.Latch(000000) - Command:Command22 (MIS:3 A:7) - CNEXT,NWP - TRUE - StackContol: HOLD - AddressSelect: NEXT - MAP= 007473
[007473] - B=[1]000003 - A=[5]000007 - IDBS.Latch(000000) - IDBS=ALU - IDBS.Read(000000) - ALU.R: R:7 V:002322 - @SM=LIN - ALU.W: R:3 V:175456 - IDBS.Latch(175456) - Command:Command22 (MIS:3 A:7) - CNEXT,NWP - TRUE - StackContol: HOLD - AddressSelect: NEXT - MAP= 007474
[007474] - B=[0]000000 - A=[0]000000 - IDBS.Latch(000000) - IDBS=ALU - IDBS.Read(000000) - ALU.R: R:0 V:000000 - @SM=LIN - IDBS.Latch(000100) - Command:JMP_27 (MIS:3 A:0) - CONTINUE - TRUE - StackContol: HOLD - AddressSelect: JUMP - CONTINUE
ALU.W: R:2 V:000101 - ALU.R: R:2 V:000101 - ALU.R: R:3 V:175456 - 

            */
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("144051"); // SWAP SA DD - Exchange A and D register contents.
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xDEAD); // set som random value to A register                                   
            cpu.WriteALURegister(Enums.RegisterEnum.D, 0xBEEF); // set som random value to A register                                   

            Utils.Log("Setup done", true);
            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0xBEEF), "A after ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(0xDEAD), "D after ExecuteMacroInstruction");

            Assert.Pass();
        }


        [Test]
        public void Register_SWAP_X_TO_A()
        {
            // Page 58 in ND-06.029.1 EN

            /* Microcode 
             * 
            */
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("144157"); // SWAP CLD SA DX - Use zero as the destination operand.
                                                             // Exchange the A and X register contents.
                                                             // A register contents are zero, X register contents are the previous contents of A.
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xDEAD); // set som random value to A register                                   
            cpu.WriteALURegister(Enums.RegisterEnum.X, 0xBEEF); // set som random value to A register                                   

            Utils.Log("Setup done", true);
            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0x00), "A after ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.X), Is.EqualTo(0xDEAD), "X after ExecuteMacroInstruction");

            Assert.Pass();
        }

        #endregion

        #region Store Instructions
        [Test]
        public void Store_STA02()
        {

            /* Microcode 
             * 
             * 10994  006040  STA:      A,A                 ALUF,PASSA          ALUD,NONE           IDBS,ALU            COMM,AWRITE,*       T,JMP               T,HOLD              CONT;
             * 
             *  0389  000145  CONT:     B,Z                 ALUF,ZERO           ALUD,B
             *  0390  000145            IDBS,ALU            COMM,CONTINUE       T,JMP               T,HOLD;

            */
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0004002"); //STA *2  - Store A to Memory *2
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xBEEF); // set value to A register                                   
            Utils.Log("Setup done", true);

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(0xBEEF), "A after ExecuteMacroInstruction");
            Assert.That(cpu.memory.ReadPhysicalMemory(P + 2), Is.EqualTo(0xBEEF), "Memory after ExecuteMacroInstruction");

            Assert.Pass();
        }

        #endregion Store Instructions

        #region Shift Instructions


        [Test]
        public void Shift_A_right_1_ARITHM()
        {
            ushort test_value = 0xBEEF;

            // Right shift A 1 positions
            int bits = 1;

            // Sign bit            
            ushort sign_bit = (ushort)(test_value & 0x8000);
            ushort expected_value = ShiftRightArithmetic(test_value, bits);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0154577"); // SHA SHR 1
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set value to A register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "Value after shift operation");


            Assert.Pass();
        }

        [Test]
        public void Shift_A_right_3_ARITHM()
        {

            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr
            // Right shift A 3 positions


            // COMMENT: Something with "keeping the sign bit" in the shift that I havent understood how to implement.

            ushort test_value = 0xBEEF;

            // Right shift A 3 positions
            int bits = 3;

            // Sign bit            
            ushort sign_bit = (ushort)(test_value & 0x8000);
            ushort expected_value = ShiftRightArithmetic(test_value, bits);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0154575"); // "SHA", SHA SHR 3
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set value to A register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "Value after shift operation");


            Assert.Pass();
        }


        [Test]
        public void Shift_A_right_7_ARITHM()
        {

            // COMMENT: Something with "keeping the sign bit" in the shift that I havent understood how to implement.

            ushort test_value = 0xBEEF;

            // Right shift A 7 positions
            int bits = 7;

            // Sign bit            
            ushort sign_bit = (ushort)(test_value & 0x8000);
            ushort expected_value = ShiftRightArithmetic(test_value, bits);



            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0154571"); // SHA SHR 7
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set value to A register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "Value after shift operation");


            Assert.Pass();
        }

        [Test]
        public void Shift_A_Left_1()
        {
            // Left shift A 1 positions
            ushort test_value = 0xBEEF;
            // Sign bit
            ushort expected_value = (ushort)(test_value << 1);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0154401"); // SHA SHL 1
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set value to A register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "Value after shift operation");


            Assert.Pass();
        }

        [Test]
        public void Shift_A_Left_3()
        {

            // Left shift A 3 positions

            //ushort test_value = 0xBEEF;
            ushort test_value = 0x0001;
            ushort expected_value = (ushort)(test_value << 3);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0154403"); // SHA SHL 3
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set value to A register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "Value after shift operation");


            Assert.Pass();
        }

        [Test]
        public void Shift_A_Left_7()
        {

            // Left shift A 7 positions

            ushort test_value = 0x0001;
            ushort expected_value = (ushort)(test_value << 7);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0154407"); // SHA SHL 7
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, test_value); // set value to A register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "Value after shift operation");


            Assert.Pass();
        }



        [Test]
        public void Rotate_T_Left_1()
        {

            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr
            // Rotate T register 6 places to the left



            // Left rotate T 1 positions
            ushort bits = 1;
            ushort test_value = 0x5aF1;

            // For uint
            // left rotate: (original << bits) | (original >> (32 - bits))
            // right rotate: (original >> bits) | (original << (32 - bits))
            //
            ushort expected_value = (ushort)(test_value << bits | test_value >> 16 - bits);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0155001"); // "SHT ROT 1" 

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.T, test_value); // set value to T register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(expected_value), "Value after rotate operation");


            Assert.Pass();
        }

        [Test]
        public void Rotate_T_Left_2()
        {

            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr
            // Rotate T register 6 places to the left



            // Left rotate T 2 positions
            ushort bits = 2;
            ushort test_value = 0x5aF1;

            // For uint
            // left rotate: (original << bits) | (original >> (32 - bits))
            // right rotate: (original >> bits) | (original << (32 - bits))
            //
            ushort expected_value = (ushort)(test_value << bits | test_value >> 16 - bits);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0155002"); // "SHT ROT 2" 

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.T, test_value); // set value to T register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(expected_value), "Value after rotate operation");


            Assert.Pass();
        }

        [Test]
        public void Rotate_T_Left_8()
        {

            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr
            // Rotate T register 6 places to the left



            // Left rotate T 8 positions
            ushort bits = 8;
            ushort test_value = 0x5aF1;

            // For uint
            // left rotate: (original << bits) | (original >> (32 - bits))
            // right rotate: (original >> bits) | (original << (32 - bits))
            //
            ushort expected_value = (ushort)(test_value << bits | test_value >> 16 - bits);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            //ushort opcode = (ushort)Utils.Oct2Int("0155006"); // "SHT ROT 6"
            ushort opcode = (ushort)Utils.Oct2Int("0155010"); // "SHT ROT 10" (10oct = 8 dec)

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.T, test_value); // set value to T register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(expected_value), "Value after rotate operation");


            Assert.Pass();
        }

        [Test]
        public void Rotate_T_Right_1_()
        {

            // Right rotate T 8 positions
            ushort bits = 1;
            ushort test_value = 0x5aF1;

            // For uint
            // left rotate: (original << bits) | (original >> (32 - bits))
            // right rotate: (original >> bits) | (original << (32 - bits))
            //
            ushort expected_value = RotateRight(test_value, bits);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("155077"); // SHT 70 ROT (SHT SHR ROT 10?)

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.T, test_value); // set value to T register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(expected_value), "Value after rotate right operation");


            Assert.Pass();
        }

        [Test]
        public void Rotate_T_Right_8()
        {
            // Shift example 2
            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr

            // Right rotate T 8 positions
            ushort bits = 8;
            ushort test_value = 0x5aF1;

            // For uint
            // left rotate: (original << bits) | (original >> (32 - bits))
            // right rotate: (original >> bits) | (original << (32 - bits))
            //
            ushort expected_value = RotateRight(test_value, bits);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("155070"); // SHT 70 ROT (SHT SHR ROT 10?)

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.T, test_value); // set value to T register        
            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.T), Is.EqualTo(expected_value), "Value after rotate right operation");


            Assert.Pass();
        }

        [Test]
        public void ShiftExample3()
        {

            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr

            // Shift the connected A and D registers 16 positions to the left. Rotate shift is specified which,
            // in this case, will cause the contents of the A and D registers to be exchanged.
            // The same effect may be obtained by means of a SWAP SA DD instruction (the SWAP is faster).

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            // The manual example explicitly says "Rotate shift is specified" - that is
            // SAD ROT 20 (0155620, ROT = bit 9), not plain SAD 20 (0154620). This test
            // used the plain opcode and only passed while the emulator (wrongly) hardwired
            // rotate linkage into every double shift; INSTRUCTION-VERIFY's SAD2OP exposed
            // that (fixed 2026-07-11), so the opcode here is corrected to the ROT form the
            // manual describes. Plain SAD 20 shifts zeros in: A = old D, D = 0.
            ushort opcode = (ushort)Utils.Oct2Int("0155620"); //SAD ROT 20


            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);


            ushort regA = 1234; // 2322 (oct)
            ushort regD = 4567; // 10727

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regA);
            cpu.WriteALURegister(Enums.RegisterEnum.D, regD);

            Utils.Log("Setup done", true);


            // DEBUG: CURRENT BUG: Note: D is unchanged, A content is replaced with D register.

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            Utils.Log("", true);
            Utils.Log($"A={cpu.ReadALURegister(Enums.RegisterEnum.A)}", true);
            Utils.Log($"D={cpu.ReadALURegister(Enums.RegisterEnum.D)}", true);



            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(regD), "Value in A after rotate operation");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(regA), "Value in D after rotate operation");


            Assert.Pass();
        }


        [Test]
        public void ShiftExample4()
        {

            // Page 3-35 in ND-06.014.02_ND-100_Reference_Manual_January_1982_ocr
            // Shift the D register two places to the right. Feed zeros into the left end during the shifting. Bits 15 and 14 in the D register will become zero.

            ushort test_value = 0xABB7;
            ushort bits = 2;
            ushort expected_value = (ushort)(test_value >> bits & 0xFFFF);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0156376"); // SHD ZIN SHR 2


            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.D, test_value);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected_value), "Value in D after rotate operation");

            Assert.Pass();
        }

        [Test]
        public void ShiftExampleLIN()
        {

            // Shift the D register two places to the right. Feed value of M indicator eros into the left end during the shifting. Bits 15 and 14 in the D register will become "M".


            ushort test_value = 0xABB7;
            ushort bits = 2;
            ushort expected_value = (ushort)(test_value >> bits | 0xC000 & 0xFFFF);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("0157376");//SHD LIN SHR 2


            // Set STS.M = true (Will set bit 15 and 14 to 1 during the shift)
            cpu.hw.alu.STS_M = true;

            //ushort sts = cpu.ReadALURegister(Enums.RegisterEnum.STS);
            //sts |= 0x80; // regs.currentRegisters.STS.M = true; 
            //cpu.WriteALURegister(Enums.RegisterEnum.STS, sts);

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.D, test_value);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.D), Is.EqualTo(expected_value), "Value in D after LIN operation");


            Assert.Pass();
        }

        #endregion Shift Instructions


        #region ROP
        [Test]
        public void ROP_ND110_RADD_CM1_CLD_AD1_SX_DB()
        {
            // ND-06-029.1 EN, Page 48

            // RADD CM1 CLD AD1 SX DB

            // (also = COPY CLD AD1 or RSUB CM1)            



            ushort test_value = 1234;
            ushort expected_value = (ushort)-test_value;

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("146773"); // "COPY AD1 CM1 SX DB");

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, test_value);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 0xDEAD);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.B), Is.EqualTo(expected_value), "Value after RADD operation");

            Assert.Pass();
        }




        [Test]
        public void ROP_ND110_RADD_RINC()
        {
            // ND-06-029.1 EN, Page 48

            // RADD AD1 <dr> is equivalent to RINC <dr>, increment <dr> register by one
            // RADD AD1 DX





            ushort test_value = 1234;
            ushort expected_value = (ushort)(test_value + 1);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("146407"); // RADD RINC DX"); // WAS "RADD AD1 DX"

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, test_value);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 0xDEAD);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.X), Is.EqualTo(expected_value), "Value after RADD operation");

#if _PART_2_

            P = (ushort)newP;
            test_value = 0x7FFF;
            expected_value = (ushort)(test_value + 1);

            cpu.memory.Write(P, opcode);
            cpu.memory.Write(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, test_value);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 0xDEAD);

            // Execute
            newP = cpu.FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.X), Is.EqualTo(expected_value), "Value after RADD operation");

#endif

            Assert.Pass();


        }



        [Test]
        public void ROP_ND110_RAND_SL_DX()
        {
            // ND-06-029.1 EN, Page 49
            // RAND SL DX
            // AND the contens of the L and X registers. Store the result in X register



            ushort regs_X = 1234;
            ushort regs_L = 0xDEAD;
            ushort expected_value = (ushort)(regs_X & regs_L);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("144447"); // "RAND", "RAND SL DX"

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, regs_X);
            cpu.WriteALURegister(Enums.RegisterEnum.L, regs_L);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.X), Is.EqualTo(expected_value), "Value after RADD operation");


            Assert.Pass();
        }

        #endregion ROP

        #region TRA (Transfer from register to A)


        [Test]
        public void TRA_PANS()
        {
            // TRA PANS
            // Copy PANS value to A

            ushort regs_A = 0;
            ushort expected_value = 1 << 14; // Bit 14 = Panel present

            //TODO: Write to PANS a known value first with TRR MIPANS

            // until that is done, expect 0 on the TRA
            expected_value = 0;



            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150000"); //TRA PANS

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PANS operation");

            Assert.Pass();
        }
        [Test]
        public void TRA_STS()
        {
            // TRA STS
            // Copy STS value to A

            ushort regs_A = 0xDEAD;
            cpu.hw.alu.STS_C = true;
            cpu.hw.alu.PIL = 3;
            ushort expected_value = cpu.hw.alu.STS;


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150001"); // TRA STS

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA OPR operation");

            Assert.Pass();
        }


        [Test]
        public void TRA_OPR()
        {
            // TRA OPR
            // Copy OPR value to A

            ushort regs_A = 0;
            ushort expected_value = 0xDEAD; ;

            // Write to OPR a known value first ?
            cpu.hw.RF.Write(14, 9, expected_value);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150002"); // TRA OPR ( RF1[14,9]

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA OPR operation");


            Assert.Pass();
        }

        [Test]
        public void TRA_PGS()
        {
            // TRA PGS
            // Copy PGS value to A

            ushort regs_A = 0;
            ushort expected_value = 0xDEAD; ;

            //Write to PGS a known value first ?            
            cpu.hw.RF.Write(10, 13, expected_value);

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150003"); // TRA PGS 

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PGS operation");


            Assert.Pass();
        }


        [Test]
        public void TRA_PVL()
        {
            // TRA PVL
            // Copy PVL value to A
#if _ND110_
  // PVL
            regs.currentRegisters.A = 0xDEAD;
            regs.PVL = 15;
            // IRR bit mask explained in "ND-06026-1-EN ND-110 Functional Description.pdf" 
            // MAC "IRR 170 DP" => 153772
            expected_value = regs.PVL << 3 | Numeric.ParseIntValue("153772"); // IRR (PVL) DP  =>  IRR 17 DP  // 153772  


            TestOpCode(address, (ushort)Numeric.ParseIntValue("150004"), "TRA", "TRA PVL");
            regs.decodedOpCode.instr.Execute.Invoke();
            Assert.That(regs.currentRegisters.A, Is.EqualTo(expected_value), "A Value after TRA PVL operation");

#endif
            ushort regs_A = 220;
            ushort PVL = 1;

            PVL &= 0x0F;
            ushort expected_value = (ushort)(PVL << 3 | Utils.Oct2Int("153602")); // IRR (PVL) DP  =>  IRR 17 DP  // 153772   110101111_0000_010=153602

            // Write to PVL a known value first! (its stored shifted 3 positions to the left)
            cpu.hw.RF.Write(15, 13, (ushort)(PVL << 3));

            // NOTE: Something wrong with the calculations ??


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150004"); // TRA PVL 

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PVL operation");


            Assert.Pass();
        }


        void DoIIE(ushort bits)
        {
            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("1000");
            // TRR IIE is register code 5 => opcode 150105.
            // Ground truth: the TRR dispatch "TRR: ... T,JMP0-3 ... VECT3" (ND-110-RASK.LISTING.TXT
            // CS 000755) vectors on instruction bits 0-3 into the table at CS 003700, where offset
            // 5 = TRIIE (003705) and offset 7 = TRPIE (003707). The old opcode 150107 therefore
            // executed TRR PIE, IIE was never written, and TRA IIC scanned with all sources masked.
            ushort opcode = (ushort)Utils.Oct2Int("150105"); // TRR IIE

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, bits);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

        }

        [Test]
        public void TRA_IIC()
        {
            // TRA PGS
            // Copy PGS value to A

            ushort regs_A = 0xDEAD;
            // Ground truth for the expected IIC code (ND-110-RASK.LISTING.TXT, AIIC1 at CS 000676):
            // TRA IIC scans the status fence downwards (PIC,LOSTS 25..14 octal, CS 000702-000706)
            // and computes IIC = fence - 13 octal (Q was decremented once, then "ALUF,Q-D ... 12"
            // at AIIC3/CS 000710). Fence encoding is pin + 8, so pin = IIC + 3:
            //   PAGEF (PF, pin 6)  -> first /IRQ at fence 16 octal -> IIC = 3
            //   POWFAIL (POW, pin 13) -> first /IRQ at fence 25 octal -> IIC = 10 (octal 12)
            // The old expectation of 10 belonged to the (commented-out) powerfail variant below.
            ushort expected_value = 3;


            DoIIE(0xFFFF);

            //TODO: Write to PGS a known value first ?
            //cpu.hw.RF.Write(0, 0, expected_value);

            cpu.hw.pic.SetInterruptPIN(Enums.InterruptPin.PAGEF);
            //cpu.hw.pic.SetInterruptPIN(13); // Powerfail, should give IIC = 10
            //cpu.hw.pic.SetInterruptPIN(5); // Memory Protection, should give IIC = 2

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150005"); // TRA IIC

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA IIC operation");


            Assert.Pass();
        }


        [Test]
        public void TRA_PID()
        {
            // TRA PID
            // Copy PID value to A


            ushort opcode_trr_pid = (ushort)Utils.Oct2Int("150106"); // TRR PID            
            ushort opcode_tra_pid = (ushort)Utils.Oct2Int("150006"); // TRA PID


            //ushort expected_value = 0xF000;
            ushort expected_value = (ushort)Utils.Oct2Int("176000"); //1111_1100_0000_0000


            #region Need to do TRR PID first

            // Run the MacroCode "TRR PID"  and "TRA PID"
            ushort P1 = (ushort)Utils.Oct2Int("100");


            cpu.WriteALURegister(Enums.RegisterEnum.A, expected_value);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P1);
            cpu.memory.WritePhysicalMemory(P1, opcode_trr_pid);

            Utils.Log("Executing TRR PID");
            int newP1 = cpu.UnitTest_FetchAndExecute(P1);
            Utils.LogEOL();

            #endregion Need to do TRR PID first

            ushort regs_A = 0xDEAD;


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("101");
            ushort opcode = opcode_tra_pid;

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PID operation");


            Assert.Pass();
        }

        [Test]
        public void TRA_PIE()
        {
            // TRA PIE
            // Copy PIE value to A

            ushort regs_A = 0xDEAD;
            ushort expected_value = 0xBEEF; ;

            //TODO: Write to PIE a known value first ?
            cpu.hw.RF.Write(15, 11, expected_value);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150007"); // TRA PIE

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PIE operation");


            Assert.Pass();
        }

        [Test]
        public void TRA_CSR()
        {
            // TRA CSR
            // Copy CSR value to A (cache status)

            ushort regs_A = 0xDEAD;
            ushort expected_value = 1 << 2; // Bit 2 means CSR disabled

            cpu.hw.cpu_regs.CSR = expected_value;

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150010"); // TRA CSR

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA CSR operation");


            Assert.Pass();
        }


        [Test]
        public void TRA_ACTL()
        {
            // TRA ACTL
            // Copy ACTL value to A 

            ushort regs_A = 0xDEAD;
            ushort expected_value = (ushort)(1 << cpu.hw.alu.PIL);


            //TODO: Test with different levels in PIL


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150011"); // TRA ACTL

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA ACTL operation");


            Assert.Pass();
        }


        [Test]
        public void TRA_ALD()
        {
            // TRA ALD
            // Copy ALD value to A 

            /*
              
             0 => 0
             1 => 1560
             2 => 20500;
             3 => 21540
            .. see uc-memory 003560 -> 003577
            */
            ushort regs_A = 0xDEAD;
            ushort expected_value = (ushort)Utils.Oct2Int("21540");

            cpu.hw.cpu_regs.ALD = 3; // Set known value to ALD

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150012"); // TRA ALD

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA ALD operation");


            Assert.Pass();
        }

        [Test]
        public void TRA_PES()
        {
            // TRA PES
            // Copy PES value to A 

            ushort regs_A = 0xDEAD;
            ushort expected_value = 0xBEEF;

            cpu.hw.cpu_regs.PES = expected_value;

            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150013");//TRA PES

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PES operation");


            Assert.Pass();
        }

        [Test]
        public void TRA_PGC()
        {
            // TRA PGC / TRA PCR (alias)
            // Copy PGC value to A 

            #region Setup PGC value


            // TEST WRITE and READ PGC on all 16 levels (0-15)
            for (ushort PIL = 0; PIL <= 15; PIL++)
            {
                // TRR Transfer from A register to PCR (on the given level)
                // Paging Control            
                ushort pgc_value = 0b11_01_0000_0_11; //PT=3, ATP=01, PIL=0 (0b0000), 4pt, Ring = 3 // 0x6bb = oct 3273 
                pgc_value = (ushort)(pgc_value | (PIL << 3));


                // Run the MacroCode "TRR PGC" 
                ushort P1 = (ushort)Utils.Oct2Int("100");
                ushort opcode_trr_pcr = (ushort)Utils.Oct2Int("150103"); //TRR PCR



                cpu.WriteALURegister(Enums.RegisterEnum.A, pgc_value);
                cpu.WriteALURegister(Enums.RegisterEnum.P, P1);
                cpu.memory.WritePhysicalMemory(P1, opcode_trr_pcr);

                Utils.Log("Executing TRR PCR");
                int newP1 = cpu.UnitTest_FetchAndExecute(P1);
                Utils.LogEOL();

                #endregion Setup PGC value


                ushort regs_A = (ushort)(PIL << 3);
                ushort expected_value = pgc_value;

                // Setup memory
                ushort P = (ushort)Utils.Oct2Int("101");
                ushort opcode = (ushort)Utils.Oct2Int("150014");//TRA PGC - PAGING CONTROL

                cpu.memory.WritePhysicalMemory(P, opcode);
                cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

                cpu.WriteALURegister(Enums.RegisterEnum.P, P);
                cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


                // Execute
                int newP = cpu.UnitTest_FetchAndExecute(P);

                // Validate
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
                Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
                Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PGC operation");
            }

            Assert.Pass();
        }

        [Test]
        public void TRA_PEA()
        {
            // TRA PEA (Parity Error Address)
            // Copy PEA value to A 

            ushort regs_A = 0xDEAD;
            ushort expected_value = 0;

            //TODO: Write to ALD a known value first ?
            //cpu.RF1.Write(15, 11, expected_value);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150015");//TRA PEA 

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA PEA operation");


            Assert.Pass();
        }

        [Test]
        public void TRA_CS()
        {

            /*
            2133  000717  % TRA CS
            2134  000717  
            2135  000717  ACS:      A,X                 ALUF,ANDAQ          ALUD,NONE
            2136  000717            IDBS,ALU            COMM,ADCS           T,NEXT              T,HOLD;
            2137  000720  
            2138  000720            B,A                 ALUF,PASSD          ALUD,B
            2139  000720            IDBS,RCS            COMM,RWCS           T,RETURN            T,HOLD;
             */

            // TRA CS
            // Copy CS value to A 

            ushort regs_X = (ushort)Utils.Oct2Int("100");
            ushort regs_A = 0xAA;
            ushort expected_value = (ushort)Utils.Oct2Int("13");

            //TODO: Write to ALD a known value first ?
            //cpu.RF1.Write(15, 11, expected_value);


            // Setup memory
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150017");//TRA CS 

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 2, 0xFFAA);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.X, regs_X);
            cpu.WriteALURegister(Enums.RegisterEnum.A, regs_A);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.A), Is.EqualTo(expected_value), "A Value after TRA CS operation");


            Assert.Pass();
        }
        #endregion


        #region Stack Instructions

        [Test]
        [TestCase(false)]
        public void Test_INIT(bool called = false)
        {

            // Load the addresses pointed to by B with the stack frame addresses.
            //
            // Note:

            // Stack overflow and flag error causes an error return, that is the program continues at the address following the stack demand value.

            // In all other cases, the program skips this address to find the return address from the stack.
            // (Flag bit 0 # STS register bit O is a flag error.)

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("103115");  // 103124
            ushort opcode = (ushort)Utils.Oct2Int("140134");// INIT


            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 1, (ushort)Utils.Oct2Int("10"));  //words_allocated_to_stack             101656
            cpu.memory.WritePhysicalMemory(P + 2, (ushort)Utils.Oct2Int("101656")); // address_of_stack_start            101656
            cpu.memory.WritePhysicalMemory(P + 3, (ushort)Utils.Oct2Int("5000")); // max_stack_size                     500
            cpu.memory.WritePhysicalMemory(P + 4, 0x0000); // stack_flag
            cpu.memory.WritePhysicalMemory(P + 5, 0x0000); // left empty

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, 0x8653); // (ushort)Utils.Oct2Int("103123"));
            cpu.WriteALURegister(Enums.RegisterEnum.L, 0x8654); // (ushort)Utils.Oct2Int("103124"));

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            Console.WriteLine($"B={cpu.ReadALURegister(Enums.RegisterEnum.B):X4}");

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 7), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 7), "Return value from ExecuteMacroInstruction");


            // B = 700
            if (!called)
                Assert.Pass();
        }


        [Test]
        [TestCase(false)]
        public void Test_ENTR(bool called = false)
        {

            // Init stack!
            Utils.Log("Init Stack first!", true);
            Test_INIT(true);
            Utils.Log("Init OK!!", true);


            // Now execute ENTR

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("103125");
            ushort opcode = (ushort)Utils.Oct2Int("140135");// ENTR


            // Setup registers

            ushort P = address;
            ushort B_FRAME = 0x842E;

            /*    ADDR: ENTR
                  ADDR + 1: Stack demand
                  ADDR + 2: Error return
                  ADDR + 3: Normal return
            */

            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 1, 0xD200); //Stack demand        151000


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            // The B register will always point to a "stack-frame"

            cpu.WriteALURegister(Enums.RegisterEnum.B, B_FRAME); // (ushort)Utils.Oct2Int("700")); 700

            //cpu.WriteALURegister(Enums.RegisterEnum.B, 0x1c0); // (ushort)Utils.Oct2Int("700")); 700

            cpu.WriteALURegister(Enums.RegisterEnum.L, 0x8653); // (ushort)Utils.Oct2Int("103124"));

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            Console.WriteLine($"B={cpu.ReadALURegister(Enums.RegisterEnum.B):X4}");
            Console.WriteLine($"P={cpu.ReadALURegister(Enums.RegisterEnum.P):X4}");

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 3), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 3), "Return value from ExecuteMacroInstruction");



            // B=102564, P=103127, L=103124
            if (!called)
                Assert.Pass();
        }


        [Test]
        public void Test_ENTR_1()
        {

            // Init stack!
            Utils.Log("Init Stack first!", true);
            Test_INIT(true);
            Utils.Log("Init OK!!", true);



            // Setup memory
            ushort address = 0x8655;
            ushort opcode = (ushort)Utils.Oct2Int("140135");// ENTR

            ushort input_B = 0x842E;
            ushort correct_B_retur = 0x843C;
            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);
            cpu.memory.WritePhysicalMemory(P + 1, 0x8);    //Stack demand       (less than 151000)
                                                           //P+2 error return
                                                           //P+3 return address

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, input_B);
            cpu.WriteALURegister(Enums.RegisterEnum.L, 0x8657);

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            Console.WriteLine($"B={cpu.ReadALURegister(Enums.RegisterEnum.B):X4}");

            // Validate that return address is "OK address"
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 3), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 3), "Return value from ExecuteMacroInstruction");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.B), Is.EqualTo(correct_B_retur), "B after ExecuteMacroInstruction");



            // B=102564, P=103127, L=103124
            Assert.Pass();
        }

        [Test]
        public void Test_LEAVE()
        {

            // Init stack!
            Utils.Log("Init Stack first!", true);
            Test_INIT(true);
            Utils.Log("Init OK!!", true);



            // Enter
            Test_ENTR(true);

            // This saves the previous stack pointer in LINK.
            // The B register is restored to its previous value(PREVB) and the stack left by loading the P register(program counter) with the return address(LINK).

            // Setup memory
            ushort address = (ushort)Utils.Oct2Int("103127");
            ushort opcode = (ushort)Utils.Oct2Int("140136");// LEAVE


            // Setup registers

            ushort P = address;
            cpu.memory.WritePhysicalMemory(P, opcode);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            //cpu.WriteALURegister(Enums.RegisterEnum.B, 0x58ab); // (ushort)Utils.Oct2Int("102564"));
            //cpu.WriteALURegister(Enums.RegisterEnum.L, 0x8653); // (ushort)Utils.Oct2Int("103124"));

            Utils.Log("Setup done", true);


            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);

            // Validate
            // Ground truth (ND-110-RASK.LISTING.TXT): LEAVE is a RETURN instruction, not a
            // sequential one - P is loaded from the stack frame's LINK cell, not with P+2.
            //  * ENTRC (CS 001175): "B,L  ALUF,B+1 ... COMM,WRRQ" - ENTR stores L+1 as LINK.
            //  * LEAV  (CS 001224-001232): reads the frame and ends with "B,P ALUF,PASSD ...
            //    IDBS,DBR COMM,LDGPR" (CS 001232) - P := LINK; BCHNG then restores B (PREVB).
            // Test_ENTR (called above) executed ENTR with L = 0x8653, so LINK = L+1 = 0x8654
            // and LEAVE must return with P = 0x8654 (= 103124 octal).
            ushort expected_P = 0x8653 + 1; // LINK stored by ENTR = L+1
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_P), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(expected_P), "Return value from ExecuteMacroInstruction");


            // P = 103125
            // B = 00700
            // L = 103124


            Assert.Pass();
        }
        #endregion

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
