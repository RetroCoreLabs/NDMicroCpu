//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;
using NDMicroCpu.Core.AMD;
using Newtonsoft.Json.Linq;


// Test code for the Am2914 PIC CHIP.
// Setup "ALONE, ie NO CASCDE WITH ANOTHER CHIP
namespace NDMicroCpu.Core.Tests.PIC
{
    [TestFixture]
    public class TestNoneCascadedAm2914Pic
    {
        private Am2914 chip = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            chip = new Am2914();
        }

        [Test]
        public void TestLoadStatus()
        {
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);
            Assert.That(chip.StatusRegister, Is.EqualTo(0));
        }




        [Test]
        public void TestMasterClear()
        {
            // Arrange
            chip.LatchStatusBus(0b10000000);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA); // Set bit 7 of status register to 1
                                                                     // 
            chip.MaskRegister = 0b11111110; // Set bit 0 of mask register to 0


            chip.ExecuteInstruction(Am2914.Am2914Instruction.ENIN);


            // Act
            chip.ExecuteInstruction(Am2914.Am2914Instruction.MCLR);

            // Assert
            Assert.That(chip.StatusRegister, Is.EqualTo(0b00000000)); // Status register should be cleared
            // BUG FIX (2026-07-11): Master Clear "clears" the mask register - same semantics
            // as the CLRM instruction (all bits = 0 = all priorities enabled), NOT all-ones.
            // Per the Am2914 datasheet and this class's own ClearMaskRegister()/CLRM comment
            // ("Clear Mask Register (Enables all priorities)"). This test previously asserted
            // the pre-fix (backwards) behavior.
            Assert.That(chip.MaskRegister, Is.EqualTo(0b00000000)); // Mask register cleared - all interrupts enabled
            Assert.IsTrue(chip.InterruptRequestEnabled_FlipFlop); // Interrupts should be enabled


            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(0));

            //Assert.That(chip.Inpout GroupEnable, Is.EqualTo(true));
            //Assert.That(chip.GroupPriority, Is.EqualTo(0));
        }

        [Test]
        public void TestEnableInterruptRequest()
        {
            chip.ExecuteInstruction(Am2914.Am2914Instruction.ENIN);
            Assert.That(chip.InterruptRequestEnabled_FlipFlop, Is.True); // Interrupts should be enabled

        }

        [Test]
        public void TestDisableInterruptRequest()
        {
            chip.ExecuteInstruction(Am2914.Am2914Instruction.DISIN);
            Assert.That(chip.InterruptRequestEnabled_FlipFlop, Is.False); // Interrupts should be disabled
        }


        [Test]
        public void TestReadMaskRegister()
        {
            chip.MaskRegister = 0b00011000;
            chip.ExecuteInstruction(Am2914.Am2914Instruction.RDM);

            Assert.That(chip.ReadMaskBus, Is.EqualTo(0b00011000));
        }


        // According to the documentation, SETM (Set Mask Register) sets the bits specified by the M-bus to 1,
        // while LDM (Load Mask Register) loads the entire mask register with the value specified by the M-bus.
        // So, SETM only sets specific bits to 1, while LDM loads the entire mask register with a specific value.


        [Test]
        public void TestLoadMaskRegister()
        {
            // Load M-bios to Mask register, shouldnt update status  
            chip.LatchMaskBus(0b00001111);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0b00001111));
            Assert.That(chip.StatusRegister, Is.EqualTo(0b00000000));
        }
        [Test]
        public void TestSetMaskRegister()
        {

            chip.LatchMaskBus(0b00001111);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0b00001111));

            // SETM sets all bits to 1
            chip.LatchMaskBus(0b11001001);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.SETM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0xFF));
        }


        [Test]
        public void TestBitSetMaskRegister()
        {

            chip.LatchMaskBus(0b00001111);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0b00001111));

            // Set the bits by the latched bits
            chip.LatchMaskBus(0b11001001);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.BSETM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0b11001111));
        }

        [Test]
        public void TestBitClearMaskRegister()
        {

            chip.LatchMaskBus(0b1100_1111);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0b1100_1111));

            // Clear the bits by the latched bits
            chip.LatchMaskBus(0b1110_1001);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.BCLRM);

            Assert.That(chip.MaskRegister, Is.EqualTo(0b0000_0110));
        }


        [Test]
        public void TestIRQDetection_one_chip()
        {
            // Lowest chip HAS this set
            chip.Input_GroupAdvanceReceive = false; // GND

            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0xFF));
            Assert.That(chip.Output_InterruptRequest, Is.EqualTo(true));
            Assert.That(chip.Output_RippleDisable, Is.EqualTo(true)); // ripple disable should not be active


            // Enable all interrupts
            chip.LatchMaskBus(0);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);


            chip.SetInterrupPIN(6); // Set pin 6 as interrupt

            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0xBF));

            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);


            Assert.That(chip.Output_InterruptRequest, Is.EqualTo(false));

            //Assert.That(chip.Output_RippleDisable, Is.EqualTo(false)); // When interrupt is detected, ripple disable show be false



            chip.LatchStatusBus(0);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);
            Assert.That(chip.StatusRegister, Is.EqualTo(0));
            Assert.That(chip.Output_InterruptRequest, Is.EqualTo(false)); // status > int req

            chip.LatchStatusBus(7);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);
            Assert.That(chip.StatusRegister, Is.EqualTo(7));
            Assert.That(chip.Output_InterruptRequest, Is.EqualTo(true)); // status > int req


            chip.LatchStatusBus(5);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);
            Assert.That(chip.StatusRegister, Is.EqualTo(5));
            Assert.That(chip.Output_InterruptRequest, Is.EqualTo(false)); // status == int req
        }

        public void TestInterruptDetectionWithStatusSet()
        {
            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0xFF));
            for (int i = 7; i >= 0; i--)
            {
                chip.SetInterrupPIN(i);
            }

            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0x00));

            // Clear the bits by the latched bits
            chip.LatchMaskBus(0b1110_1001);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.CLRMB);

            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0b1110_1001));

            // Clear all interrupts
            chip.ExecuteInstruction(Am2914.Am2914Instruction.CLRMB);
        }


        [Test]
        public void TestCLRMR()
        {
            /*
             This test sets the interrupt enable flip-flop to 1, loads the mask register with 0xFF (enabling all interrupts), 
             and sets the interrupt detected register to indicate an interrupt on pin 3. 

             It then loads the mask register with 0xF7 (disabling interrupt on pin 3) and executes the CLRMR instruction to clear the interrupt detected register for pin 3. 
            
            Finally, it verifies that the interrupt detected register has been cleared for pin 3.
            */

            // Set interrupt enable flip-flop to 1
            chip.ExecuteInstruction(Am2914.Am2914Instruction.ENIN);

            // Set mask register to 0xFF
            chip.LatchMaskBus(0xFF);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);


            // Set interrupt detected register to indicate interrupt on pin 3
            chip.SetInterrupPIN(3);

            // Set mask register to disable interrupt on pin 3
            chip.LatchMaskBus(0xF7);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);
            Assert.That(chip.MaskRegister, Is.EqualTo(0xF7));


            // Execute CLRMR instruction to TRY to clear interrupt detected register for pin 3 (should fail)
            chip.ExecuteInstruction(Am2914.Am2914Instruction.CLRMR);

            // since bit 3 was 0, it should not affect the result
            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0xF7));


            // Now latch mask register with all 1
            chip.LatchMaskBus(0xFF);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);


            // Execute CLRMR instruction to clear interrupt detected register for pin 3
            chip.ExecuteInstruction(Am2914.Am2914Instruction.CLRMR);

            // Verify that interrupt detected register has been cleared for pin 3
            Assert.That(chip.InterruptDetectRegister, Is.EqualTo(0xFF));
        }

        [Test]
        public void TestCLRVC()
        {

            Assert.That(chip.Output_InterruptRequest, Is.True); // Interrupts should NOT be detected

            // Set interrupt detected register to indicate interrupt on pin 3
            chip.SetInterrupPIN(3);

            chip.LatchMaskBus(0x00); // enable all interrupts
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);
            Assert.That(chip.Output_InterruptRequest, Is.False); // Interrupts should be detected

            // Execute the RDVC instruction to read the interrupt vector of the highest priority interrupt
            chip.ExecuteInstruction(Am2914.Am2914Instruction.RDVC);

            // Assert that the vector hold register is set to the correct value
            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(3));

            // Assert that the status register fence is updated
            Assert.That(chip.StatusRegister, Is.EqualTo(3 + 1));


            // Execute CLRVC instruction
            chip.ExecuteInstruction(Am2914.Am2914Instruction.CLRVC);

            // Assert that the vector hold register still is set to the correct value
            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(3));

            // Assert that the status register fence is NOT updated (shouldnt update down automatic)
            Assert.That(chip.StatusRegister, Is.EqualTo(3 + 1));


            // Check that interrupt detected flag is cleared
            Assert.That(chip.Output_InterruptRequest, Is.True); // Interrupts should NOT be detected

            // Execute the RDVC instruction to read the interrupt vector of the highest priority interrupt
            chip.ExecuteInstruction(Am2914.Am2914Instruction.RDVC);

            // Vector holding should still be 3
            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(3));

            // Assert that the status register fence is NOT updated (shouldnt update down automatic)
            Assert.That(chip.StatusRegister, Is.EqualTo(3 + 1));

            // Load new status to clear the overflow, status fence and make vector
            chip.LatchStatusBus(0);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);

            // Vector holding should still be 3
            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(3));

            // Assert that the status register fence is updated
            Assert.That(chip.StatusRegister, Is.EqualTo(0));

            // Execute the RDVC instruction to read the interrupt vector of the highest priority interrupt
            chip.ExecuteInstruction(Am2914.Am2914Instruction.RDVC);

            // Vector holding should still be 3
            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(0));

            // Assert that the status register fence is updated
            Assert.That(chip.StatusRegister, Is.EqualTo(0));
        }


        [Test]
        public void TestReadInterruptVector()
        {
            Am2914 chip = new Am2914();

            // Load the mask register, enable all interrupts
            chip.LatchMaskBus(0b0000_0000);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDM);


            chip.ExecuteInstruction(Am2914.Am2914Instruction.DISIN);

            Assert.That(chip.Output_InterruptRequest, Is.True); // Interrupts should NOT be detected

            // Set interrupt vector for group 0
            chip.SetInterrupPIN(0);


            // Set interrupt vector for group 1
            chip.SetInterrupPIN(1);

            // Set interrupt vector for group 2
            chip.SetInterrupPIN(2);

            // Set interrupt vector for group 3
            chip.SetInterrupPIN(3);

            // Set interrupt vector for group 4
            chip.SetInterrupPIN(4);

            // Set interrupt vector for group 5
            chip.SetInterrupPIN(5);

            // Set interrupt vector for group 6
            chip.SetInterrupPIN(6);

            // Set interrupt vector for group 7
            chip.SetInterrupPIN(7);


            // Enable all interrupts
            Assert.That(chip.Output_InterruptRequest, Is.True); // Interrupts should NOT be detected
            chip.ExecuteInstruction(Am2914.Am2914Instruction.ENIN);
            Assert.That(chip.Output_InterruptRequest, Is.False); // Interrupts should be detected

            // Execute the RDVC instruction to read the interrupt vector of the highest priority interrupt
            chip.ExecuteInstruction(Am2914.Am2914Instruction.RDVC);
            Assert.That(chip.InterruptRequestEnabled_FlipFlop, Is.True); // Interrupts should be enabled

            // Assert that the vector hold register is set to the correct value
            Assert.That(chip.VectorHoldingRegister, Is.EqualTo(7));

            // Assert that the status fence is set to the correct value
            Assert.That(chip.StatusRegister, Is.EqualTo(8));

            // Clear the last vector
            chip.ExecuteInstruction(Am2914.Am2914Instruction.CLRVC);

            // Assert that the status fence is still 8
            Assert.That(chip.StatusRegister, Is.EqualTo(8));
            Assert.That(chip.Output_StatusOverflow, Is.EqualTo(false)); // should still indicate overflow

            // Load new status to clear the overflow
            chip.LatchStatusBus(0);
            chip.ExecuteInstruction(Am2914.Am2914Instruction.LDSTA);
            Assert.That(chip.Output_StatusOverflow, Is.EqualTo(true)); // overflow should now be cleared

            Assert.That(chip.Output_InterruptRequest, Is.False); // Interrupts should be detected

            // Assert that the status fence is set to the correct value (Read vector has not yet been run, so fence has not been updated)
            Assert.That(chip.StatusRegister, Is.EqualTo(0));

            // Execute the RDVC instruction to read the interrupt vector of the highest priority interrupt
            chip.ExecuteInstruction(Am2914.Am2914Instruction.RDVC);

            // Assert that the status fence is set to the correct value (Read vector has not yet been run, so fence has not been updated)
            Assert.That(chip.StatusRegister, Is.EqualTo(7));

        }
        [Test]
        public void Test_GroupEnable_And_GroupSignal()
        {
            var chip = new Am2914();
            chip.Input_GroupAdvanceReceive = false; // GND

            // Test that Group Signal is initially LOW when Group Enable is LOW
            chip.Input_GroupEnable = false;
            Assert.That(chip.Output_GroupSignal, Is.EqualTo(false));

            // Test that Group Signal is initially HIGH when Group Enable is HIGH
            chip.Input_GroupEnable = true;
            Assert.That(chip.Output_GroupSignal, Is.EqualTo(true));

            // Test that Group Signal remains LOW when Group Enable is changed to LOW
            chip.Input_GroupEnable = false;
            Assert.That(chip.Output_GroupSignal, Is.EqualTo(false));

            // Test that Group Signal remains HIGH when Group Enable is changed to HIGH
            chip.Input_GroupEnable = true;
            Assert.That(chip.Output_GroupSignal, Is.EqualTo(true));
        }

#if _not_yet_
        [Test]
        public void TestClearAllInterrupts()
        {
            // Arrange
            chip.LoadStatusRegister(0b10000000); // Set bit 7 of status register to 1
            chip.MaskRegister = 0b11111110; // Set bit 0 of mask register to 0

            // Act
            chip.ClearAllInterrupts();

            // Assert
            Assert.AreEqual(0b00000000, chip.StatusRegister); // Status register should be cleared
            Assert.AreEqual(0b11111111, chip.MaskRegister); // Mask register should be set to all ones
        }

        [Test]
        public void TestClearInterruptsDetectedRegister()
        {
            // Arrange
            chip.LoadStatusRegister(0b10000000); // Set bit 7 of status register to 1
            chip.MaskRegister = 0b11111110; // Set bit 0 of mask register to 0
            chip.InterruptsDetectedRegister = 0b10101010; // Set some bits in interrupts detected register

            // Act
            chip.ClearInterruptsDetectedRegister(0b01010101); // Clear only even bits

            // Assert
            Assert.AreEqual(0b10101010 & 0b01010101, chip.InterruptsDetectedRegister); // Only even bits should be cleared
        }

        [Test]
        public void TestClearMaskRegister()
        {
            // Arrange
            chip.MaskRegister = 0b10101010; // Set some bits in mask register

            // Act
            chip.ClearMaskRegister(0b01010101); // Clear only even bits

            // Assert
            Assert.AreEqual(0b10101010 & 0b10101010, chip.MaskRegister); // Only odd bits should be unchanged
        }

        [Test]
        public void TestReadInterruptVector()
        {
            // Arrange
            chip.InterruptsDetectedRegister = 0b10000000; // Set bit 7 of interrupts detected register to 1

            // Act
            var vector = chip.ReadInterruptVector();

            // Assert
            Assert.AreEqual(7, vector); // Vector should be 7
            Assert.AreEqual(0b00000000, chip.StatusRegister); // Status register should be cleared
            Assert.IsFalse(chip.VectorHoldRegister); // Vector hold register should be cleared
        }

        [Test]
        public void TestReadStatusRegister()
        {
            // Arrange
            chip.LoadStatusRegister(0b10000000); // Set bit 7 of status register to 1

            // Act
            //var status
        }
#endif
    }
}
