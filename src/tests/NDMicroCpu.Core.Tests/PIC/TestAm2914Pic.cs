//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using NDMicroCpu.Core;
using NDMicroCpu.Core.AMD;
using static NDMicroCpu.Core.AMD.Am2914;

namespace NDMicroCpu.Core.Tests.PIC
{

    /// <summary>
    /// Tests here test the Am2914 in a single chip set up
    /// (Not testing cascade)
    /// </summary>
    internal class TestAm2914Pic
    {
        Am2914 pic = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            pic = new Am2914();

            // GAR => GND
            pic.Input_GroupAdvanceReceive = false;

            // GE => GND
            pic.Input_GroupEnable = false;
        }

        void PIC_MasterClear()
        {
            // Initialize PIC
            pic.ExecuteInstruction(Am2914Instruction.MCLR); // Master Clear
        }

        [Test]
        public void TestMCLR()
        {

            pic.ExecuteInstruction(Am2914Instruction.MCLR); // Master Clear

            Assert.That(pic.VectorOutput, Is.EqualTo(0), "Vector output");

            Assert.Pass();
        }


        [Test]
        public void TestStatusBus_InterruptDisabled()
        {

            pic.Input_InterruptDisable = false;

            byte set_value = 0x05;
            byte expected = 0x05;
            pic.LatchStatusBus(set_value);
            pic.ExecuteInstruction(Am2914Instruction.LDSTA);


            /*
             * You can still load the Status Register with the LDSTA microinstruction even if the Interrupt Disable (ID) signal is active (i.e., ID = 0). 
             * 
             * The ID signal is used to inhibit the Interrupt Request output from the Am2914 chip, but it does not affect the ability to load or read the Status Register.
             */
            Assert.That(pic.StatusRegister, Is.EqualTo(expected), "Status register");

            Assert.Pass();
        }


        [Test]
        public void TestLoadStatus()
        {

            byte expected = 0x05;

            pic.LatchStatusBus(expected);
            pic.ExecuteInstruction(Am2914Instruction.LDSTA);

            Assert.That(pic.StatusRegister, Is.EqualTo(expected), "Status register");

            Assert.Pass();
        }


    }
}
