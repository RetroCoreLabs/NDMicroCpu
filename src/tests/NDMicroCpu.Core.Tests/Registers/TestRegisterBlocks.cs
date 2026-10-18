//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Runtime.Intrinsics.X86;
using System.Security.AccessControl;
using System.Text.RegularExpressions;
using NDMicroCpu.Core;

namespace NDMicroCpu.Core.Tests.Registers
{
    public class TestRegisterBlocks
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();
        }

        [Test]
        public void Macro_TRR_PCR_TRA_PCR()
        {

            // TRR Transfer from A register to PCR (on the given level)
            // Paging Control            
            ushort expected_value = 0b11_01_0111_0_11; //PT=3, ATP=01, PIL=7 (0b0111), Ring = 3 // 0x6bb = oct 3273 

            // Run the MacroCode "TRR PGC" 
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode_trr_pcr = (ushort)Utils.Oct2Int("150103"); //TRR PCR
            ushort opcode_tra_pcr = (ushort)Utils.Oct2Int("150014"); // 150003 - TRA PCR // 150014 TRA 


            cpu.WriteALURegister(Enums.RegisterEnum.A, expected_value);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P, opcode_trr_pcr);
            cpu.memory.WritePhysicalMemory(P + 1, opcode_tra_pcr);

            Utils.Log("Executing TRR PCR");
            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.LogEOL();

            //ushort writtenValue = cpu.hw.RF3.Read(7, 0); //PCR level 7

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after TRR ExecuteMacroInstruction");
            //Assert.That(writtenValue, Is.EqualTo(expected_value), "RF3[PIL,0] after TRR ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from TRR ExecuteMacroInstruction");

            P = (ushort)newP;
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x7 << 3); // ask for level 7
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Executing TRA PCR");
            newP = cpu.UnitTest_FetchAndExecute(P);

            Utils.LogEOL();

            ushort regvalue = cpu.ReadALURegister(Enums.RegisterEnum.A);
            //ushort regvalue = cpu.hw.RF3.Read(7, 0); //PCR level 7


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after TRA ExecuteMacroInstruction");
            Assert.That(regvalue, Is.EqualTo(expected_value), "A after TRA ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from TRA ExecuteMacroInstruction");

            Assert.Pass();


            /* NOTE
             *  In microcode 747 it reads from A,PIL - bit PIL is 0. Which is current PIL, not PIL for level 7
             *  
             */
        }






        [Test]
        public void Macro_TRR_PID_TRA_PID()
        {


            //ushort expected_value = 0xF000;
            ushort expected_value = (ushort)Utils.Oct2Int("176000"); //1111_1100_0000_0000

            // Run the MacroCode "TRR PID"  and "TRA PID"
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode_trr_pid = (ushort)Utils.Oct2Int("150106"); // TRR PID            
            ushort opcode_tra_pid = (ushort)Utils.Oct2Int("150006"); // TRA PID


            cpu.WriteALURegister(Enums.RegisterEnum.A, expected_value);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P, opcode_trr_pid);
            cpu.memory.WritePhysicalMemory(P + 1, opcode_tra_pid);


            Utils.Log("Executing TRR PID");
            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.LogEOL();



            ushort regvalue = cpu.hw.RF.Read(15, 12); // AB,PID

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(regvalue, Is.EqualTo(expected_value), "REG[AB,PID] after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            //------------------------------ TRA 

            P = (ushort)newP;
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0); // Clear A
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.Log("Executing TRA PID");
            newP = cpu.UnitTest_FetchAndExecute(P);

            ushort aval = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(aval, Is.EqualTo(expected_value), "A after TRA PID");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Utils.LogEOL();
            Utils.Log($"PID value from TRA is {Utils.Int2Oct(aval)}");
            Utils.LogEOL();
            Assert.Pass();

        }


        [Test]
        public void Macro_TRR_PIE()
        {

            // TRR Transfer from A register to PCR (on the given level)
            //ushort expected_value = 0xF000;
            ushort expected_value = 1 << 14;

            // Run the MacroCode "TRR PIE" 
            ushort P = 0x100;
            ushort opcode_trr_pie = (ushort)Utils.Oct2Int("150107"); //TRR PIE

            cpu.memory.WritePhysicalMemory(P, opcode_trr_pie);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, expected_value);
            cpu.memory.WritePhysicalMemory(P, opcode_trr_pie);
            int newP = cpu.UnitTest_FetchAndExecute(P);



            ushort regvalue = cpu.hw.RF.Read(15, 11); // AB,PIE

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(regvalue, Is.EqualTo(expected_value), "REG[AB,PID] after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.Pass();
        }


        [Test]
        public void Macro_IRW_17_DP()
        {
            // Write the contents of the A register on the current level into the A register of the  program level given by the instruction.

            // NOTE!IRW to P in same level doesn't end up with a write to P. Its reset immediately after with a write to P from R2
            // Looks like its by design... its in the microcode for ND-100 also

            /*
            1058  000420  % ****************************************************************
            1059  000420  % OWN LEVEL
            1060  000420  
            1061  000420            A,A                 B,DEST              ALUF,PASSA          ALUD,B
            1062  000420            IDBS,ALU            T,JMP               T,PUSH
            1063  000420            IRW2;
            1064  000421  
            1065  000421            A,R2                B,P                 ALUF,PASSA          ALUD,B
            1066  000421            IDBS,ALU            T,NEXT              T,HOLD
            1067  000421            CONDENABL;
            */

            // TRR Transfer from A register to IRW (on the given level)            
            ushort expected_value = (ushort)Utils.Oct2Int("1234");

            // Run the MacroCode "IRW 0 DP"
            ushort P = 100;

            //ushort opcode_irw = (ushort)Utils.Oct2Int("153402"); //  153402 = IRW 0 DP (doesnt change P)
            ushort opcode_irw = (ushort)Utils.Oct2Int("153572"); //  153402 = IRW 17 DP (doesnt change P) (17=0x0F)

            cpu.memory.WritePhysicalMemory(P, opcode_irw);
            cpu.hw.mac.LatchMacAddress(P);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, expected_value);

            Utils.LogEOL();

            int newP = cpu.UnitTest_FetchAndExecute(P);

            Utils.LogEOL();

            Assert.That(cpu.hw.mac.FETCH, Is.True, "Expected FETCH to be set");


            //ushort regvalue = cpu.hw.alu.ReadRegisterE(Enums.RegisterEnum.P);
            ushort regvalue = cpu.hw.RF.Read(0x0F, (byte)Enums.RegisterEnum.P);

            Assert.That(regvalue, Is.EqualTo(expected_value), "ALU[p] after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.Pass();
        }
    }
}
