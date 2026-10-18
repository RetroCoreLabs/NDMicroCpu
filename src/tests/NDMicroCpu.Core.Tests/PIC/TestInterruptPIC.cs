//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Reflection.Emit;
using NDMicroCpu.Core.AMD;
using static NDMicroCpu.Core.AMD.Am2914;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.PIC
{
    public class TestInterruptPIC
    {
        CPU cpu = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            cpu = new CPU();

            //cpu.hw.cpu_regs.STOP = false;
            cpu.hw.uart.OnTextOutput += Uart_OnTextOutput;
            NDMicroCpu.Core.Utils.DisableLog = true;

            // Initialize microcode boot up
            cpu.UnitTest_RunUntilFetch();


        }

        void Uart_OnTextOutput(char out_char)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(out_char);
            Console.ResetColor();
        }

        [TearDown]
        public void TearDown()
        {
            cpu.hw.uart.OnTextOutput -= Uart_OnTextOutput;
        }



        #region Test PIC chip



        void PIC_MasterClear()
        {
            // Initialize PIC
            cpu.hw.pic.Exec(Am2914Instruction.MCLR); // Master Clear
        }

        void PIC_ClearMaskRegister()
        {
            // Clear Mask register (enables all priorities)
            cpu.hw.pic.Exec(Am2914Instruction.CLRM);
        }
        void PIC_EnableInterruptRequest()
        {
            // Enable Interrupt Request
            cpu.hw.pic.Exec(Am2914Instruction.ENIN);
        }


        void PIC_ReadVectorIfIrq()
        {
            // Needs to read vector to set staus register and vector info
            if (cpu.hw.pic.ReadInterruptRequestPin == false)
            {
                // Read Vector
                cpu.hw.pic.Exec(Am2914Instruction.RDVC);
            }
        }

        void PIC_SetIrqPin(int pin)
        {
            cpu.hw.pic.SetInterruptPIN(pin);
        }
        [Test]
        public void PIC_TestInterrupt12()
        {
            // REWRITTEN 2026-07-11: the old version of this test encoded pre-fix emulator behavior:
            //  - It expected "no IRQ / vector 0" right after MCLR + request, which was only true while
            //    MasterClear (wrongly) left the mask register = 0xFF (all inhibited). Per the AMD Am2914
            //    datasheet, Master Clear CLEARS the mask register (all priorities enabled) and clears the
            //    status register, so a pending request MUST produce an IRQ and its vector immediately.
            //  - Its LOSTS/status expectations (status "13", final vector 7) matched an older, incorrect
            //    status/fence implementation, not the datasheet.
            // The sequence below only asserts behavior that follows directly from the Am2914 datasheet:
            //   RDVC : vector = highest priority unmasked pending request; status <- vector+1;
            //          vector-hold <- vector; "vector clear enable" FF set.
            //   CLRVC: clears the interrupt latch/register bit of the last vector read.
            //   LDSTA: loads the status (fence) register; IRQ only for requests >= status.
            //   /IRQ is active LOW.
            // Cascading (ND-100 CPU card, chips 21A/B=high, 23A/B=low): ExecuteReadVector returns
            // bits 0-2 = vector within winning chip, bit 3 = high chip /GS, bit 4 = low chip /GS.

            // Initialize PIC
            cpu.hw.pic.Exec(Am2914Instruction.MCLR); // Master Clear: all interrupts enabled, fence 0

            // Trigger Interrupt 12 (BINT12 = pin 2 on the low chip 23A/B)
            cpu.hw.pic.SetInterruptPIN(InterruptPin.INT12);

            // Datasheet: after MCLR everything is enabled, so the pending pin-2 request asserts /IRQ
            bool irq = cpu.hw.pic.ReadInterruptRequestPin; // active LOW
            Assert.That(irq, Is.EqualTo(false), "IRQ must be asserted right after MCLR + request (mask cleared by MCLR)");

            // Read vector: pin 2 on the LOW chip wins -> bits 0-2 = 2, bit 4 = low chip /GS asserted
            ushort v1 = cpu.hw.pic.ExecuteReadVector();
            Assert.That(v1 & 0x07, Is.EqualTo(2), "Vector must be 2 (pin 2 = BINT12 pending, unmasked, fence 0)");
            Assert.That(v1, Is.EqualTo(2 | 0b10000), "Low chip /GS (bit 4) must be asserted, high chip /GS (bit 3) clear");

            // Datasheet: RDVC loads vector+1 into the status register (fence now 3 on the low chip)
            Assert.That(cpu.hw.pic.ReadStatus() & 0xFF, Is.EqualTo(3), "Low chip status must be vector+1 = 3 after RDVC");

            // Trigger Interrupt 11 (BINT11 = pin 1): below the fence (1 < 3) -> no new IRQ
            cpu.hw.pic.SetInterruptPIN(InterruptPin.INT11);
            irq = cpu.hw.pic.ReadInterruptRequestPin;
            Assert.That(irq, Is.EqualTo(true), "Pin 1 request is below the fence (status 3) - no IRQ");

            // Clear the interrupt belonging to the last vector read (pin 2)
            cpu.hw.pic.Exec(Am2914Instruction.CLRVC);
            Assert.That(cpu.hw.pic.ReadInterruptDetect(), Is.EqualTo((ushort)0xFFFD), // all high except pin 1
                "CLRVC must clear exactly the last-read vector's latch (pin 2); pin 1 still pending (active low)");

            // Lower the fence back to 0 and re-enable both groups (LOSTS 0: status=0, both /GE asserted)
            cpu.hw.pic.ExecuteLoadStatus(0);
            irq = cpu.hw.pic.ReadInterruptRequestPin;
            Assert.That(irq, Is.EqualTo(false), "With fence 0 the pending pin-1 request must assert IRQ");

            // Masking demonstration, per datasheet LDM semantics (mask bit 1 = inhibited)
            cpu.hw.pic.LatchMaskBus(0xFFFF);
            cpu.hw.pic.Exec(Am2914Instruction.LDM);
            Assert.That(cpu.hw.pic.ReadInterruptRequestPin, Is.EqualTo(true), "All masked -> no IRQ");

            cpu.hw.pic.LatchMaskBus(0x0000);
            cpu.hw.pic.Exec(Am2914Instruction.LDM);
            Assert.That(cpu.hw.pic.ReadInterruptRequestPin, Is.EqualTo(false), "All unmasked -> IRQ again");

            // Read vector: now pin 1 (BINT11) wins
            ushort v2 = cpu.hw.pic.ExecuteReadVector();
            Assert.That(v2, Is.EqualTo(1 | 0b10000), "Vector must be 1 (pin 1) with low chip /GS asserted");

            // Clear it: no requests left at all
            cpu.hw.pic.Exec(Am2914Instruction.CLRVC);
            Assert.That(cpu.hw.pic.ReadInterruptDetect(), Is.EqualTo((ushort)0xFFFF), "All latches clear (active low)");
            Assert.That(cpu.hw.pic.ReadInterruptRequestPin, Is.EqualTo(true), "No requests -> no IRQ");

            Assert.Pass();
        }




        [Test]
        public void TestMaskAndInterrupt()
        {
            // Note: This is a hand-written C# transcription of the probe loop inside the TRA PID
            // microcode routine APID1/APID3 (ND-110-RASK.LISTING.TXT, CS 000653-000672), which the
            // microcode power-up self-test "TEST 8 : TEST OF PIC" (CS 002073-002103) exercises via
            // SMPID 140017 + TAPID and expects 176000 octal as the final PID value.
            //
            // EXPECTATION FIXED 2026-07-11: the old expected value 176000 (bit 15 set on the first
            // probe) was copied from that self-test, but this transcription is NOT the same algorithm:
            // the real routine's FIRST probe loads the mask from ~R2 (CS 000664, PIC,LMSK B,R2
            // ALUF,INVB - R2 = caller context, many pins enabled), while this transcription's first
            // probe uses mask = ~(1<<4), i.e. ONLY pin 4 enabled. Pin 4 (MON trap) is never requested
            // here (SetInterruptDetectbits(140017) sets pins 15,14,3,2,1,0 only), so per the Am2914
            // datasheet the interrupt-request output MUST stay inactive on that probe: /IRQ is only
            // asserted for a pending AND unmasked request at or above the status fence. Hence bit 15
            // of 'detected' stays 0 and the correct expectation for THIS loop is 076000 octal
            // (probes 2-6 detect pins 3,2,1,0,15 -> bits 14,13,12,11,10).
            // The real self-test expectation 176000 is separately covered by the microcode-driven
            // test Macro_TRA_PID_INT10_15 (which runs the genuine TAPID routine).
            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_EnableInterruptRequest();


            int R3 = ~(1 << 4);
            R3 = R3 & 0xFFFF;

            int detected = 0;

            ushort expected = (ushort)Utils.Oct2Int("076000"); // 0x7C00 - see comment above
            ushort interruptbits = (ushort)Utils.Oct2Int("140017"); // 0xC00F

            int mask = R3;
            int GPR = 1 << 15;

            cpu.hw.pic.ExecuteLoadStatus(0x08);

            // 0 = enabled, 1 = disabled
            cpu.hw.pic.SetInterruptDetectbits(interruptbits); // 0ct 140017
            for (int loopcounter = 5; loopcounter >= 0; loopcounter--)
            {
                cpu.hw.pic.ClearPendingIRQ();

                cpu.hw.pic.LatchMaskBus((ushort)mask);
                cpu.hw.pic.Exec(Am2914Instruction.LDM); // Load mask register from M bus

                if (cpu.hw.pic.ReadInterruptRequestPin == false)
                {
                    detected |= GPR;
                }


                // Rotate right
                mask = mask >> 1 | (mask & 0x01) << 15;
                GPR = GPR >> 1;

                cpu.hw.pic.ExecuteLoadStatus(0x00);
            }

            Console.WriteLine();
            Console.WriteLine($"detected value = {Utils.Int2Oct(detected)}");

            Assert.That(detected, Is.EqualTo(expected), "Detected bits");


            Assert.Pass();
        }



        #endregion


        #region Level Change


        [Test]
        public void UcLevelChange()
        {
            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_EnableInterruptRequest();

            // Setup memory
            // Run the MacroCode "TRA IIC" to read out the current interrupt level (after uC has executed level shift)
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode_tra_iic = (ushort)Utils.Oct2Int("150005"); //TRA IIC (150005)
            cpu.memory.WritePhysicalMemory(P, opcode_tra_iic);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x00);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.R1, 0);

            // Write values to level 12
            cpu.hw.RF.Write(12, (ushort)Enums.RegisterEnum.P, P);
            cpu.hw.RF.Write(12, (ushort)Enums.RegisterEnum.X, P); // Save to X also to validate

            // Trigger Interrupt 12
            cpu.hw.pic.SetInterruptPIN(InterruptPin.INT12);

            Utils.LogEOL();


            // Fake interrupt handling (jump to 00017, macro interrupt - to read vector to R5)            
            cpu.hw.mic.LatchMap((ushort)Utils.Oct2Int("00017"));
            cpu.UnitTest_RunUntilFetch();

            Utils.LogEOL();

            Assert.That(cpu.hw.mac.FETCH, Is.True, "Expected FETCH to be set");

            // Check if level shift has occurred as expected
            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(12), "New level should be 12");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.X), Is.EqualTo(P), "X before ExecuteMacroInstruction");

            // TODO: Research why P register is incremented +1 during level shift
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P), "P before ExecuteMacroInstruction");

            // Execute                        
            int newP = cpu.UnitTest_FetchAndExecute(P);

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            // EXPECTATION FIXED 2026-07-11: the old assertion "IIC should be 12" confused the IIC
            // (Internal Interrupt Code) with the interrupt LEVEL. Per the ND-100 reference manual,
            // TRA IIC returns the code of the highest INTERNAL interrupt source (1=MC .. 10=POW);
            // an EXTERNAL level-12 interrupt (BINT12, PIC pin 2) sets no internal interrupt source.
            // The TRA IIC microcode (AIIC1, ND-110-RASK.LISTING.TXT CS 000676-000715) only scans
            // PIC pins 4-13 (IIE & 37760 octal), and pin 2 is outside that window, so it returns 0.
            // The level change itself is already verified above via the PIL == 12 assertion.
            Assert.That(A, Is.EqualTo(0), "IIC must be 0 - external BINT12 is not an internal interrupt source");

            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");


            Assert.Pass();
        }



        [Test]
        public void TestLevelChange()
        {

            #region SETUP
            PIC_MasterClear();
            PIC_ClearMaskRegister();

            // Set start memory address
            ushort P = (ushort)Utils.Oct2Int("100");

            // Run the MacroCode "TRR PIE"  + "TRR PID" + "TRA PID"
            // Transfer the A register contents into the priority interrupt enable register (PIE)
            ushort opcode = (ushort)Utils.Oct2Int("150107"); //TRR PIE
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Transfer the A register contents into the priority interrupt detect register (PID) <= clear PID
            opcode = (ushort)Utils.Oct2Int("150106"); // TRR PID
            cpu.memory.WritePhysicalMemory(P + 1, opcode);

            // Read the PID register back to register A 
            opcode = (ushort)Utils.Oct2Int("150006"); // TRA PID
            cpu.memory.WritePhysicalMemory(P + 2, opcode);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFF00);

            Utils.LogEOL();
            #endregion SETUP

            // Execute TRR PIE
            Utils.Log("Executing TRR PIE");
            Utils.LogEOL();

            int newP = cpu.UnitTest_FetchAndExecute(P);


            ushort sts = cpu.ReadALURegister(Enums.RegisterEnum.Zero);
            Utils.Log($"STS={sts:X4}");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            //// move to next instruction
            //P = (ushort)newP;
            //cpu.WriteALURegister(Enums.RegisterEnum.A, 0x00); // Clear A before calling TRR PID

            //// Execute "TRR PID"
            //Utils.Log("Executing TRR PID");
            //Utils.LogEOL();

            //newP = cpu.FetchAndExecute(P);            
            //Utils.LogEOL();


            //Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            //Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // FIXED 2026-07-11: the "TRR PID" execution step above was commented out, but newP was
            // still set to P+1, which is where the TRR PID opcode lives - so the final step of this
            // test fetched and executed TRR PID (clearing PID, leaving A=0) instead of the intended
            // TRA PID at P+2. Skip over the unexecuted TRR PID opcode.
            newP = P + 2;

            Utils.Log("Trigger IRQ 11");
            Utils.LogEOL();


            // Trigger Interrupt 11 (BINT11 = PIC pin 1 -> program level 11)
            // (The old comment here said "IRQ 12" but the code has always requested INT11.)
            cpu.hw.pic.SetInterruptPIN(InterruptPin.INT11);


            Utils.Log("Executing IRQ Mapping");
            Utils.LogEOL();


            // Do the IRQ mapping
            // Fake interrupt handling (jump to 00017, macro interrupt - to read vector to R5)            
            cpu.hw.mic.LatchMap((ushort)Utils.Oct2Int("00017"));
            cpu.UnitTest_RunUntilFetch();

            Utils.LogEOL();




            // Run micro code to see if we end up at expected address. But cancel after max 50000 instructions            
            cpu.UnitTest_RunUntilFetch(50000);

            Utils.LogEOL();
            Utils.Log($"PIL = {cpu.hw.alu.PIL}");
            Utils.LogEOL();




            Utils.Log("Executing TRA PID");
            Utils.LogEOL();


            // Fetch PID
            P = (ushort)newP;
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            // Execute "TRA PID"
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);
            Utils.LogEOL();


            //Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // EXPECTATION FIXED 2026-07-11: the interrupt requested above is INT11 (BINT11 = PIC
            // pin 1 = program level 11), so the PID bit that must be set is bit 11, not bit 12.
            // Ground truth from the real microcode (ND-110-RASK.LISTING.TXT): the level-entry
            // routine PLINT (CS 001105-001107) executes "AB,PID ALUF,ORDQ" with Q = BMG(new level),
            // i.e. PID |= 1 << 11 when level 11 is entered; the TRA PID routine APID1 then returns
            // the probed hardware bits OR'ed with that PID scratch register (CS 000671).
            Assert.That(A, Is.EqualTo(1 << 11), "PID bit 11 must be set after the level-11 interrupt was taken");


        }
        #endregion Level Change


        #region Interrupt instructions



        [Test]
        public void Macro_TRA_PID()
        {

            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_EnableInterruptRequest();
            PIC_SetIrqPin(15); // 1 = 11, 14 => 14
            ushort expected_a = 1 << 15;

            PIC_ReadVectorIfIrq();

            Utils.LogEOL();
            Utils.LogEOL();

            // Run the MacroCode "PIOF" 
            ushort P = 0x100;
            ushort opcode_tra_pid = (ushort)Utils.Oct2Int("150006"); // TRA PID

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 1 << 12);

            Utils.LogEOL();
            Utils.LogEOL();


            cpu.memory.WritePhysicalMemory(P, opcode_tra_pid);
            int newP = cpu.UnitTest_FetchAndExecute(P);



            Utils.LogEOL();

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.That(A, Is.EqualTo(expected_a), "Return value from ExecuteMacroInstruction");

        }

        [Test]
        public void Macro_TRA_PID_INT10_15()
        {

            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_EnableInterruptRequest();
            //PIC_SetIrqPin(12);
            PIC_ReadVectorIfIrq();

            Utils.LogEOL();
            Utils.LogEOL();

            cpu.hw.pic.SetInterruptDetectbits(0xC00F); // 0140017 (Set interrupt 15-10)

            // Run the MacroCode "TRA PID" 
            ushort P = 0x100;
            ushort opcode_tra_pid = (ushort)Utils.Oct2Int("150006"); // TRA PID

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 1 << 12);

            Utils.LogEOL();
            Utils.LogEOL();


            cpu.memory.WritePhysicalMemory(P, opcode_tra_pid);
            int newP = cpu.UnitTest_FetchAndExecute(P);



            Utils.LogEOL();

            ushort expected_a = 0xFC00;  // 176000    (Int 15-10)

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.That(A, Is.EqualTo(expected_a), "Return value from ExecuteMacroInstruction");
        }


        [Test]
        public void Macro_WAIT()
        {

            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_SetIrqPin(12);


            // Run the MacroCode "WAIT"             
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode_wait = (ushort)Utils.Oct2Int("0151000"); // WAIT

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xF0F0);

            cpu.memory.WritePhysicalMemory(P, opcode_wait);

            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.Log("WAIT done!");
            Utils.LogEOL();

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.Pass();
        }

        [Test]
        public void Macro_TRA_PIE()
        {

            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_SetIrqPin(12);


            // Run the MacroCode "PIOF" 
            // Transfer the priority interrupt enable register (PIE) contents into the A register 
            ushort P = 0x100;
            ushort opcode = (ushort)Utils.Oct2Int("150007"); // TRA PIE RF[15,11]
            cpu.memory.WritePhysicalMemory(P, opcode);

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xF0F0);


            int newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.Pass();
        }


        [Test]
        public void Macro_TRR_PIE_TRA_PIE()
        {

            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_SetIrqPin(12);


            ushort PIE_value = 1 << 12; // Enable int 12
            ushort expected_PIE = (ushort)(PIE_value);

            // Transfer the A register contents into the priority interrupt enable register (PIE)
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode_trr = (ushort)Utils.Oct2Int("150107"); // TRR PIE
            ushort opcode_tra = (ushort)Utils.Oct2Int("150007"); // TRA PIE


            cpu.memory.WritePhysicalMemory(P, opcode_trr);
            cpu.memory.WritePhysicalMemory(P + 1, opcode_tra);

            cpu.WriteALURegister(Enums.RegisterEnum.A, PIE_value);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.LogEOL();
            Utils.Log("TRR PIE done!", true);
            Utils.LogEOL();

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // move to next instruction
            P = (ushort)newP;
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x00); // Clear A before read

            // Execute "TRA PIE"
            Utils.LogEOL();
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);
            Utils.Log($"PIE={A:X4}");

            Utils.Log("TRA PIE done!");
            Utils.LogEOL();

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(A, Is.EqualTo(expected_PIE), "PIE bits should be identical to the set bits from TRR PIE");

            ushort maskreg = cpu.hw.pic.ExecuteReadMask();
            Utils.Log($"PIC MASK: {maskreg:X4}");

            Assert.Pass();

        }

        [Test]
        public void Macro_TRR_TRA_PID()
        {

            #region SETUP
            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_SetIrqPin(12);

            // Set start memory address
            ushort P = (ushort)Utils.Oct2Int("100");

            // Run the MacroCode "TRR PID" 
            // Transfer the A register contents into the priority interrupt detect register (PID)
            ushort opcode = (ushort)Utils.Oct2Int("150106"); //TRR PID
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Read the PID register back to register A 
            opcode = (ushort)Utils.Oct2Int("150006"); // TRA PID
            cpu.memory.WritePhysicalMemory(P + 1, opcode);


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xF0);


            Utils.LogEOL();
            #endregion SETUP

            // Execute TRR PID
            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.Log("TRR PID done!");
            Utils.LogEOL();

            ushort sts = cpu.ReadALURegister(Enums.RegisterEnum.Zero);
            Utils.Log($"STS={sts:X4}");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");



            // move to next instruction
            P = (ushort)newP;
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x00); // Clear A before read

            // Execute "TRA PID"
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Utils.Log("TRA PID done!");
            Utils.LogEOL();


            Utils.LogEOL();


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.That(A, Is.EqualTo(0xF0), "PID bits should be identical to the set bits from TRR PID");

            Assert.Pass();
        }


        [Test]
        public void Macro_PIOF()
        {
            // Run the MacroCode "PIOF" 
            ushort P = 0x100;
            ushort opcode = (ushort)Utils.Oct2Int("150405"); //PION


            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P, opcode);
            int newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // READ  STS

            opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();

            // Execute            
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Utils.LogEOL();
            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");


            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set
            bool isPONI = (A & 1 << 14) != 0; // check if PONI is set (Memory management)
            bool isION = (A & 1 << 15) != 0; // check if ION flag

            // Validate            
            Assert.That(isPONI, Is.EqualTo(false), "STS PONI indicator");
            Assert.That(isION, Is.EqualTo(false), "STS ION indicator");
            //Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.Pass();
        }


        [Test]
        public void Macro_PION()
        {
            // Run the MacroCode "PION" 
            ushort P = 0x100;
            ushort opcode = (ushort)Utils.Oct2Int("150412"); //PION
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P, opcode);

            Utils.LogEOL();

            int newP = cpu.UnitTest_FetchAndExecute(P);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // READ  STS

            opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            Utils.LogEOL();

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Utils.LogEOL();
            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");




            // Validate
            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set
            bool isPONI = (A & 1 << 14) != 0; // check if PONI is set (Memory management)
            bool isION = (A & 1 << 15) != 0; // check if ION flag


            Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");
            Assert.That(isPONI, Is.EqualTo(true), "STS PONI indicator");
            Assert.That(isION, Is.EqualTo(true), "STS ION indicator");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            Assert.Pass();
        }

        [Test]
        public void Macro_ION()
        {
            // Run the MacroCode "ION" 
            ushort P = 0x100;
            ushort opcode = (ushort)Utils.Oct2Int("150402"); //ION 

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Execute            

            int newP = cpu.UnitTest_FetchAndExecute(P);

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            // READ  STS

            opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();

            // Execute            
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set
            bool isION = (A & 1 << 15) != 0; // check if ION flag

            Utils.LogEOL();
            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");

            // Validate
            //Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");
            Assert.That(isION, Is.EqualTo(true), "STS ION indicator");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");




            Assert.Pass();
        }


        [Test]
        public void Macro_Paging_ON()
        {
            // Run the MacroCode "PON" 
            ushort P = 0x100;
            ushort opcode = (ushort)Utils.Oct2Int("0150410"); //PON

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.memory.WritePhysicalMemory(P, opcode);

            int newP = cpu.UnitTest_FetchAndExecute(P);


            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.That(cpu.hw.alu.STS_PONI, Is.EqualTo(true), "Memory Management enabled");

            Utils.LogEOL();
            Utils.Log("Paging enabled / PONI = TRUE");
            Utils.LogEOL();
            // READ  STS

            opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();

            // Execute
            newP = cpu.UnitTest_FetchAndExecute(P);

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set
            bool isPONI = (A & 1 << 14) != 0; // check if PONI flag is set (bit 14)

            Utils.LogEOL();
            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");

            // Validate
            Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");
            Assert.That(isPONI, Is.EqualTo(true), "STS PONI indicator");

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");




            Assert.Pass();
        }
        [Test]
        public void Test_TRA_IIC()
        {
            //int pin = 13; // 13=PageFault Pin 6=PageFault
            //int expected_iic = 3; // caluclated IIC = PageFault 

            int pin = 14; // Pin 4 (lo chip) = Monitor
            int expected_iic = 0; // caluclated IIC = 1 = Monitor 


            PIC_MasterClear();
            PIC_ClearMaskRegister();
            PIC_SetIrqPin(pin);


            Assert.That((ushort)(~cpu.hw.pic.ReadInterruptDetect()), Is.EqualTo(1 << pin), "Checking Interrupt Detect bits");


            // ENABLE ALL INTERRUPS FIRST
            // Setup memory
            // Run the MacroCode "TRR IIE" to read out the current interrupt level
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort trr_iie_opcode = (ushort)Utils.Oct2Int("150105"); //TRR IIE (150005)

            cpu.memory.WritePhysicalMemory(P, trr_iie_opcode);

            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF); // ENABLE EVERYTHING
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            int newP1 = cpu.UnitTest_FetchAndExecute(P);




            // Setup memory
            // Run the MacroCode "TRA IIC" to read out the current interrupt level
            P = (ushort)Utils.Oct2Int("100");
            ushort tra_iic_opcode = (ushort)Utils.Oct2Int("150005"); //TRA IIC (150005)

            cpu.memory.WritePhysicalMemory(P, tra_iic_opcode);

            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.LogEOL();
            Utils.Log("Memory setup done");
            Utils.LogEOL();

            // Run the MacroCode "TRA IIC" to read out the IIC vector
            int newP = cpu.UnitTest_FetchAndExecute(P);
            Assert.That(newP, Is.EqualTo(P + 1), "P after TRA IIC");

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);
            Assert.That(A, Is.EqualTo(expected_iic), $"IIC should be {expected_iic}");
            Utils.LogEOL();



            Assert.Pass();
        }

        [Test]
        public void Test_TRR_IIE()
        {
            PIC_MasterClear();
            PIC_ClearMaskRegister();

            // Setup memory
            // Run the MacroCode "TRA IIC" to read out the current interrupt level
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort trr_pid_opcode = (ushort)Utils.Oct2Int("150105"); //TRR IIE (150105)
            ushort tra_iic_opcode = (ushort)Utils.Oct2Int("150005"); //TRA IIC (150005)

            cpu.memory.WritePhysicalMemory(P, trr_pid_opcode);
            cpu.memory.WritePhysicalMemory(P + 1, tra_iic_opcode);


            // FIXED 2026-07-11: the old value 0xFF only set IIE bits 0-7. Per the ND-100 reference
            // manual the IIE bit for POWFAIL is bit 10 (IIE bit n enables the source with IIC = n;
            // 1=MC .. 10=POW), and the TRR IIE microcode (TRIIE/RIIE1, ND-110-RASK.LISTING.TXT
            // CS 003705 + 000756-000770) maps software IIE bit n to PIC pin n+3, so POW = pin 13.
            // With A=0xFF POW was never enabled and TRA IIC correctly returned 0. Enable everything
            // so the POWFAIL request below is actually detectable.
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);


            Utils.LogEOL();
            Utils.Log("Memory setup done");
            Utils.LogEOL();


            // Run the MacroCode "TRR IIE" to enable all internal interrupts
            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.LogEOL();


            Assert.That(newP, Is.EqualTo(P + 1), "P after TRR IIE");



            // Trigger Interrupt POWFAIL
            cpu.hw.pic.SetInterruptPIN(InterruptPin.POWFAIL);

            Utils.LogEOL();
            Utils.Log("Triggered POWFAIL (pin 13, IIC 10)");
            Utils.LogEOL();

            // Run the MacroCode "TRA IIC" to read out the IIC vector
            P = (ushort)newP;
            newP = cpu.UnitTest_FetchAndExecute(P);

            Assert.That(newP, Is.EqualTo(P + 1), "P after TRA IIC");


            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            // POWFAIL = PIC pin 13 = IIC code 10 (pin = IIC + 3; ND-100 ref manual: IIC 10 = POW).
            Assert.That(A, Is.EqualTo(10), "IIC should be 10");

            Utils.LogEOL();



            Assert.Pass();
        }

        [Test]
        public void Macro_LevelShift_TRA_IIC()
        {
            PIC_MasterClear();
            PIC_ClearMaskRegister();


            ushort expectedB_0 = 0xDEAD;
            ushort expected_P_12 = 123;

            // Setup PIL LEVEL 12

            ushort expected_X_12 = 0xFAAD;
            cpu.hw.RF.Write(12, (ushort)Enums.RegisterEnum.X, expected_X_12);
            cpu.hw.RF.Write(12, (ushort)Enums.RegisterEnum.P, expected_P_12);

            ushort tra_iic_opcode = (ushort)Utils.Oct2Int("150005"); //TRA IIC (150005)
            cpu.memory.WritePhysicalMemory(expected_P_12, tra_iic_opcode);


            // SETUP PIL 0
            // Run the MacroCode "TRA IIC" to read out the current interrupt level
            ushort P = (ushort)Utils.Oct2Int("100");
            cpu.memory.WritePhysicalMemory(P, 0); // use STZ *0 in Level 0 as opcode            


            // Setup registers - PIL 0
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0x00);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.B, expectedB_0);
            cpu.WriteALURegister(Enums.RegisterEnum.R1, 0);

            Utils.LogEOL();
            Utils.Log("Memory setup done");
            Utils.LogEOL();

            // Trigger Interrupt 12
            cpu.hw.pic.SetInterruptPIN(InterruptPin.INT12);
            // EXPECTATION FIXED 2026-07-11: the microcode at CS 000017 ("PIC,RVECT B,R1 ... IDBS,PICVC")
            // stores the FULL vector word read from the PIC pair into R1, and since commit f29ba3b
            // (Am2914 /GS fix) that word is: bits 0-2 = vector within the winning chip, bit 3 = high
            // chip /GS, bit 4 = low chip /GS. BINT12 is pin 2 on the LOW chip, so R1 = 2 | (1<<4) = 18
            // (octal 22), not the bare pin number 2. The MACRI dispatch (CS 000054, T,JMPAOPR into the
            // 16-entry ITSRV table at CS 003740) uses the low 4 bits, entry 2 -> level 12.
            ushort expected_val = (ushort)((int)InterruptPin.INT12 | (1 << 4));

            Utils.LogEOL();
            Utils.Log("Triggered Interrupt 12");
            Utils.LogEOL();


            // Fake interrupt handling (jump to 00017, macro interrupt - to read vector to R5)            
            cpu.hw.mic.LatchMap((ushort)Utils.Oct2Int("00017"));
            cpu.UnitTest_RunUntilFetch();


            Utils.LogEOL();
            Utils.Log("Level shift done");
            Utils.LogEOL();



            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(12), "Expecting PIL = 12");
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.X), Is.EqualTo(expected_X_12), "X after level shift");

            P = cpu.ReadALURegister(Enums.RegisterEnum.P);
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(expected_P_12), "P after level shift");


            ushort regval = cpu.ReadALURegister(Enums.RegisterEnum.R1);
            Assert.That(regval, Is.EqualTo(expected_val), "R1 must hold the full PIC vector word: pin 2 + low chip /GS (bit 4)");

            // Execute
            int newP = cpu.UnitTest_FetchAndExecute(P);


            Utils.LogEOL();
            Utils.Log("TRA IIC Execute done");
            Utils.LogEOL();

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after 'TRA IIC' in Level 12");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from 'TRA IIC'");


            // EXPECTATION FIXED 2026-07-11: TRA IIC returns the Internal Interrupt Code (1=MC..10=POW,
            // ND-100 reference manual), NOT the current level. An external BINT12 interrupt sets no
            // internal source, and the TRA IIC microcode (AIIC1, CS 000676-000715) only scans PIC
            // pins 4-13 - BINT12 is pin 2, outside that window - so the correct result is 0.
            Assert.That(A, Is.EqualTo(0), "IIC must be 0 - external BINT12 is not an internal interrupt source");

            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");


            Assert.Pass();
        }




        [Test]
        public void Macro_TRA_STS()
        {

            // Setup memory
            // Run the MacroCode "TRA IIC" to read out the current interrupt level
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();

            // Execute            
            int newP = cpu.UnitTest_FetchAndExecute(P);

            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Utils.LogEOL();
            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");

            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set
            byte loSTS = (byte)(A & 0x0F);

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.That(loSTS, Is.EqualTo(0x00), "STS low 8 bits should be all 0");

            //Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");


            Assert.Pass();
        }



        [Test]
        public void Macro_TRA_STS_Check_ND100_Flag()
        {

            // Setup memory
            // Run the MacroCode "TRA STS" to read current STS
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();

            // Execute            
            int newP = cpu.UnitTest_FetchAndExecute(P);


            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);
            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            // STS bit 12 (N100) is HARDWIRED to 1 on the ND-100/110: the microcode power-up self-test
            // ST7 (ND-110-RASK.LISTING.TXT CS 002062-002065) writes 0 into STS and then requires the
            // read-back to be exactly 10000 octal (bit 12), which can only hold if the bit is wired
            // high in the STS read path. Fixed in BUFALU.STS (2026-07-11).
            Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");

            Assert.Pass();
        }

        [Test]
        public void Macro_TRR_STS()
        {

            #region SETUP
            // Set start memory address
            ushort P = (ushort)Utils.Oct2Int("100");

            // Run the MacroCode "TRR STS" to set lower 8 bits of STS
            ushort opcode = (ushort)Utils.Oct2Int("150101"); //TRR STS (150101)
            cpu.memory.WritePhysicalMemory(P, opcode);


            // Read the STS register
            opcode = (ushort)Utils.Oct2Int("150001"); //TRA STS (150001)
            cpu.memory.WritePhysicalMemory(P + 1, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();
            #endregion SETUP

            // Execute "TRR STS"
            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.LogEOL();

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");


            // move to next instruction
            P = (ushort)newP;

            // Execute "TRA STS"
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);
            Utils.LogEOL();


            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");

            bool isND100 = (A & 1 << 12) != 0; // check if ND100 flag is set
            byte loSTS = (byte)(A & 0x0F);

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            //Assert.That(isND100, Is.EqualTo(true), "STS ND100 indicator");
            Assert.That(loSTS, Is.EqualTo(0xF), "STS low 8 bits should be all 1");

            Assert.Pass();
        }

        [Test]
        public void Macro_TRR_IIE_TRA_IIE()
        {

            #region SETUP
            // Set start memory address
            ushort P = (ushort)Utils.Oct2Int("100");

            // Run the MacroCode "TRR IIE" to set "Internal Interrupt Enable"
            ushort opcode_trr_iie = (ushort)Utils.Oct2Int("150105"); //TRR IIE (150105)
            ushort opcode_tra_iie = (ushort)Utils.Oct2Int("150005"); //TRA IIE (150001)

            cpu.memory.WritePhysicalMemory(P, opcode_trr_iie);
            cpu.memory.WritePhysicalMemory(P + 1, opcode_tra_iie);

            // Read the STS register



            // Setup registers
            //cpu.WriteALURegister(Enums.RegisterEnum.A, 0b011111111110); //Enable all, and remember 0 in position 0
            cpu.WriteALURegister(Enums.RegisterEnum.A, 1);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();
            #endregion SETUP


            // Execute "TRR IIE"
            Utils.Log("Executing TRR IIE", true);

            int newP = cpu.UnitTest_FetchAndExecute(P);
            Utils.LogEOL();

            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            ushort scratch = cpu.hw.RF.Read(15, 10); // Read scratch word holding IIE representation

            Console.WriteLine($"scratch value = {Utils.Int2Oct(scratch)}  - {ToBinary(scratch)}");

            //// move to next instruction
            P = (ushort)newP;

            //// Execute "TRA IIE"
            newP = cpu.UnitTest_FetchAndExecute(P);
            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);
            Utils.LogEOL();


            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");

            // Validate

            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            Assert.Pass();
        }

        [Test]
        public void Macro_IDENT_LVL12()
        {
            // Run the MacroCode "IDENT PL12" to identify the interrupt source at level 12
            ushort P = (ushort)Utils.Oct2Int("100");
            ushort opcode = (ushort)Utils.Oct2Int("143622"); //IDENT PL12
            cpu.memory.WritePhysicalMemory(P, opcode);

            // Setup registers
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xFFFF);
            cpu.WriteALURegister(Enums.RegisterEnum.P, P);

            Utils.LogEOL();

            // Execute            
            int newP = cpu.UnitTest_FetchAndExecute(P);


            ushort A = cpu.ReadALURegister(Enums.RegisterEnum.A);

            Utils.LogEOL();
            Console.WriteLine($"A value = {Utils.Int2Oct(A)}");


            // Validate
            Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");
            Assert.Pass();
        }



        [Test]
        public void UcTestInterruptChangeToLvl12()
        {
            PIC_MasterClear();
            PIC_ClearMaskRegister();
            // FIXED 2026-07-11: the old code did PIC_SetIrqPin(12), i.e. RAW PIC pin 12 - but per the
            // ND-100 CPU card schematic pin 12 is MOR (memory out of range), an INTERNAL interrupt
            // source. The interrupt vector table ITSRV (ND-110-RASK.LISTING.TXT CS 003740-003757)
            // dispatches high-chip pins 8-14 to EXT14, so the CPU correctly entered LEVEL 14, not 12,
            // and the test failed with "PIL Expected 12, was 14". Program level 12 is BINT12 = PIC
            // pin 2 (ITSRV entry 2 -> level 12 / XINT). Use the InterruptPin enum, which encodes the
            // schematic's pin numbering.
            PIC_SetIrqPin((int)InterruptPin.INT12);


            // Run the MacroCode "PIOF" 
            // Transfer the priority interrupt enable register (PIE) contents into the A register 
            ushort P = 0x100;
            ushort opcode = (ushort)Utils.Oct2Int("150007"); // TRA PIE

            cpu.WriteALURegister(Enums.RegisterEnum.P, P);
            cpu.WriteALURegister(Enums.RegisterEnum.A, 0xF0F0);
            Utils.LogEOL();


            cpu.hw.mic.LatchMap((ushort)Utils.Oct2Int("17"));
            cpu.UnitTest_RunUntilFetch(); // MACRO INTERRUPT



            //Assert.That(cpu.ReadALURegister(Enums.RegisterEnum.P), Is.EqualTo(P + 1), "P after ExecuteMacroInstruction");
            //Assert.That(newP, Is.EqualTo(P + 1), "Return value from ExecuteMacroInstruction");

            int PIL = cpu.hw.alu.PIL;
            Assert.That(PIL, Is.EqualTo(12), "PIL should be 12");

            Assert.Pass();

        }


        [Test]
        public void Test_LVSWP()
        {
            // REWRITTEN 2026-07-11: the old test jumped straight to LVSWP with the level NUMBER in Q
            // and nothing else set up, then asserted two leftovers that could never pass (a 'cnt'
            // variable that was never incremented, compared against 23, and mic.map == -1).
            //
            // Ground truth from the real microcode (ND-110-RASK.LISTING.TXT):
            //   LVSWP ("CHANGE LEVEL ROUTINE, Q = NEW LEVEL") is at CS 001116 (= 0x24E).
            //   Its caller PLINT (CS 001103-001115) establishes the actual input parameters before
            //   entering LVSWP:
            //     - R6 = new level number swapped into the HIGH byte (CS 001104: "B,R6 ALUF,PASSD
            //       IDBS,SWAP") - LVSWP later builds the new STS-high/PIL word as Z | R6 and loads it
            //       with COMM,LDPIL (CS 001123), where Z preserves STS bits 12-15 (CS 001122,
            //       ANDDQ 170000 octal).
            //     - Q = bit mask of the new level, BMG(level) (CS 001113: "A,LC ALUD,Q IDBS,BMG"),
            //       used at LVSWP entry to update the ACTLV scratch word (CS 001116, ALUF,ORDQ).
            // Without R6 the routine loads PIL = 0 and the old test failed with "new PIL: expected 1,
            // was 0". Provide both inputs exactly as PLINT does, then verify the level switch.

            int newPIL = 1; // Change to level 1

            cpu.hw.alu.Q = 1 << newPIL;                                    // Q = BMG(new level)
            cpu.WriteALURegister(Enums.RegisterEnum.R6, (ushort)(newPIL << 8)); // R6 = SWAP(new level)

            ushort address = 0x24E; // LVSWP = CS 001116 octal

            // Run micro code; cancel after max 100 microinstructions
            cpu.hw.mic.LatchMap(address);
            cpu.UnitTest_RunUntilFetch(100);


            Assert.That(cpu.hw.alu.PIL, Is.EqualTo(newPIL), "new PIL");

            Assert.Pass();
        }



        #endregion Interrupt instructions


        string ToBinary(byte b)
        {

            return Convert.ToString(b, 2).PadLeft(8, '0');
        }
        string ToBinary(ushort u)
        {

            return Convert.ToString(u, 2).PadLeft(16, '0');
        }

    }
}
