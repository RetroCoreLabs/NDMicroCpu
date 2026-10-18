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
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// One decoded 64-bit ND-110/ND-120 micro word: every field as a property, filled by <see cref="DecodeMicroCode"/>.
    /// </summary>
    /// <remarks>
    /// Bit numbers in the member descriptions are micro word bits as the ND-110 and ND-120 Microprogrammer's Guide (ND-06.031.1 EN) numbers them, 63 down to 0.
    /// </remarks>
    public class MicroCode
    {

        #region Public properties
        /// <summary>
        /// Stack control for the false sequence of a conditional instruction (bits 0-1 of the argument field when SCOND is set).
        /// </summary>
        public StackControl SCOND_False_StackControl { get; internal set; }
        /// <summary>
        /// Stack control for the true sequence: hold, pop, load or push (bits 28-29).
        /// </summary>
        public StackControl TRUE_StackControl { get; internal set; }

        /// <summary>
        /// Extra settling time before a branch is taken (bits 26-27).
        /// </summary>
        public BranchDelay BRANCH_DELAY { get; internal set; }

        /// <summary>
        /// Next address select for the false sequence of a conditional instruction (bits 2-3 of the argument field when SCOND is set).
        /// </summary>
        public NextAddressSelect SCOND_False_AddressSelect { get; internal set; }
        /// <summary>
        /// Next address select for the true sequence: jump, return, next or repeat (bits 30-31).
        /// </summary>
        public NextAddressSelect TRUE_AddressSelect { get; internal set; }
        /// <summary>
        /// The signal a start-conditional (SCOND) instruction tests.
        /// </summary>
        public TestObjects SCOND_test { get; internal set; }

        // Bits decoded from MicroInstruction

        /// <summary>
        /// Bits 0-3
        /// This selects the false sequence to be latched by SCOND (bit 24). It specifices the sequencer instruction (jump, return, next, repeat)
        /// and stack control (hold, pop,load, push), in case a later test ECOND (bit 23) gives a false result.
        /// </summary>
        public int bits0_3 { get; internal set; }
        /// <summary>
        /// Bits 0-11: the argument field, the jump address or an immediate value depending on the instruction.
        /// </summary>
        public int bits0_11 { get; internal set; }
        /// <summary>
        /// Bits 12-15: the A operand, a register number or a bit number.
        /// </summary>
        public int bits12_15 { get; internal set; }
        /// <summary>
        /// Bits 16-19: the B operand.
        /// </summary>
        public int bits16_19 { get; internal set; }
        /// <summary>
        /// Bit 20: the top bit of the 13-bit jump address.
        /// </summary>
        public int bit20 { get; internal set; }

        /// <summary>
        /// Pre-calculated jmp-address bit 0-11 + bit_20 as bit 12
        /// </summary>
        public ushort jmpAddress { get; internal set; }

        /// <summary>
        /// Bit 21: timing; 0 is a normal cycle, 1 adds 25 ns.
        /// </summary>
        public int bit21 { get; internal set; }
        /// <summary>
        /// Bits 42-43: the MIS field, micro instruction select for the jump address low bits.
        /// </summary>
        public byte MIS { get; internal set; }
        /// <summary>
        /// Bits 48-49: register file B select.
        /// </summary>
        public int RBSEL { get; internal set; }

        /// <summary>
        /// Bit 50, microcode token "XRF" meaning access RegisterFile3 (RF3) instead of Registerfile (RF)
        /// </summary>
        public bool XREF3 { get; internal set; }

        /// <summary>
        /// Bits 50-52: register file A select.
        /// </summary>
        public int RASEL { get; internal set; }

        //Chooses which RegisterFile to be accessed. Depends on the encoding in RASEL.
        /// <summary>
        /// Which register file the RASEL encoding addresses.
        /// </summary>
        public int RASEL_RegisterFile { get; internal set; }

        /// <summary>
        /// Bit 24: the loop counter is in use for this instruction.
        /// </summary>
        public bool LOOP { get; internal set; }
        /// <summary>
        /// Bit 23: end conditional, the instruction ends a conditional sequence.
        /// </summary>
        public bool ECOND { get; internal set; }
        /// <summary>
        /// Bit 22: start conditional, the instruction tests <see cref="SCOND_test"/> and may take the false sequence.
        /// </summary>
        public bool SCOND { get; internal set; }
        /// <summary>
        /// Bit 25: the jump address is vectored, its low bits come from the instruction register or RASEL as MIS selects.
        /// </summary>
        public bool Vectored { get; internal set; }

        // Bits mapped to enuims
        /// <summary>
        /// Bits 32-36: the command code.
        /// </summary>
        public Commands command { get; internal set; }
        /// <summary>
        /// Bits 37-41: the internal data bus source.
        /// </summary>
        public IDBSource IDBsource { get; internal set; }
        /// <summary>
        /// Bits 46-47: the ALU carry input.
        /// </summary>
        public CIN cin { get; internal set; }
        /// <summary>
        /// Bits 53-54: what happens to the status register.
        /// </summary>
        public STSOperation stsOperation { get; internal set; }


        /// <summary>
        ///Bits 44-45 ALU Mode
        ///These modify bits 55-65, before they reach the ALU. See bits 55-65.
        /// </summary>
        public ND_ALUMode aluMode { get; internal set; }


        /// <summary>
        /// Bits 55-57 ALU Source
        /// </summary>
        public ALUSource aluSource { get; internal set; }


        /// <summary>
        /// Bits 58-60 ALU Function
        /// </summary>
        public ALUFunc aluFunction { get; internal set; }

        /// <summary>
        /// Bits 61-63 ALU Destination
        /// </summary>
        public ALUDestination aluDestination { get; internal set; }

        /// <summary>
        /// Calculated FETCH flag (based on VERILOG implementation in CGA_DCD Module)
        /// </summary>
        public bool CFETCH { get; internal set; }

        #endregion Public Properties

        /// <summary>
        /// Splits a 64-bit micro word into the fields of this instance.
        /// </summary>
        /// <param name="uc">
        /// The micro word.
        /// </param>
        public void DecodeMicroCode(ulong uc)
        {
            // Bits 0-3
            // This selects the false sequence to be latched by SCOND (bit 24). It specifices the sequencer instruction (jump, return, next, repeat)
            // and stack control (hold, pop,load, push), in case a later test ECOND (bit 23) gives a false result.
            // In ND-100 this result is latched into chip 19F, a 74LS273 together with SCOND_TEST (below)
            bits0_3 = (int)(uc & 0x0F);
            SCOND_False_StackControl = (StackControl)(bits0_3 & 0x03);
            SCOND_False_AddressSelect = (NextAddressSelect)((bits0_3 >> 2) & 0x03);

            // Bits 4-7
            // This selects the test object to be latched by SCOND (bit 24). If a test object changes state, one microinstruction will be executed before the change is detected.
            // This is caused by the pipelining of the ALU, and sequencing calculations.
            int bits4_7 = (int)(uc >> 4 & 0x0F);
            SCOND_test = (TestObjects)bits4_7;

            // Bits 0-11
            // This field may contain the branch address in jump microinstructions, i which case which bit 20 is available as the 13th bit of the branch address (see Bit 20).
            // The jump addresses may also be generated by the MIC, and partly by the vectorized jump mechanism.
            // When the vectorized jump mechanism is in use the microinstruction bits 0-3 will be discarded.
            bits0_11 = (int)(uc & 0xFFF);

            // Bits 12-15
            // These select the A-operand if RASEL (bits 50-52) are 0. The A-operand controls the BMG, as well being a register file and ALU operator.
            bits12_15 = (int)(uc >> 12) & 0x0F;
            //int RA = bits12_15;

            // Bits 16-19:
            // These select the B-operand if RBSEL (bits 48-49) are 0            
            bits16_19 = (int)(uc >> 16) & 0x0F;


            // For microinstuction branches (see bits 0-11), this is issued as the 13th address but along with bits0_11. 
            // This covers the 8K address range of the ND-110 and ND-120 control store.
            bit20 = (int)(uc >> 20) & 0x01;


            // Bit 21
            // In ND-110, this is the delay function DELAY, which extends the length of the _previous_ microcycle of the function of the current microcycle
            // In ND-120, this is the delay function DELAY, which extends the length of the _current_  microcycle of the function of the current microcycle
            bit21 = (int)(uc >> 21) & 0x01; // 0=Normal, 1= +25nS

            // Bits 22-24:
            // These are the condition control bits:
            // * In ND-110, these are true when = 0 (low)
            // * In ND-120, these are true when = 1 (high)

            // Bit 22 (LOOP) increments or decrements the loop counter (always towards zero). If the selected condition is false, RETURN/HOLD is executed by the MIC.
            // If its true, TS3-6 (bits 28-31) are executed.
            // The new value of the loop counter will not be available as an A-operand or test object until after the next microinstruction.

            // Bit 23 (ECOND) enables testing (against the previously set test condition) of the result of the previous microinstruction.
            // If the result is true, the MIC sequencer executes TS3—6 (bits 28—31) to find the next microinstruction.
            // If the  result is false, the MIC sequencer executes the false sequence instruction which was previously output to the hold register by SCOND (bit 24).

            // Bit 24(SCOND) sets a condition to be tested on a later occasion.
            // A 4—bit code indicating the test condition  and a 4—bit code indicating the sequence instructions in case of a false test,
            // are output to the Hold register.



            LOOP = (byte)((uc >> 22) & 0x01) == 1;
            //if (LOOP)
            //    Utils.Log("LOOP");

            ECOND = (byte)((uc >> 23) & 0x01) == 1;
            //if (ECOND)
            //    Utils.Log("ECOND");

            SCOND = (byte)((uc >> 24) & 0x01) == 1;
            //if (SCOND)
            //    Utils.Log("SCOND");

            // Bit 25
            // This specifies a vectored jump if the true sequence is "jump":
            //  In ND-IIO, it is true when = 0[low]
            //  In ND—120, it is true when = 1(high).
            //
            //  If true, the vector address is the microinstruction branch address (Bits 0—11 plus Bit 20).
            //  If false, the lower 4 bits of the vector address are determined by MISO (bit 42) to be either IR(O-3) or the A—operand.
            Vectored = ((uc >> 25) & 0x01) == 0;

            // Pre-calculate jmpAddress, FETCH logic needs to know if its zero
            jmpAddress = ((ushort)(bits0_11 | bit20 << 12));


            // Bits 26-27 :
            // In ND-11O, these are the delay function DLY[1,0]
            //      They extend the length of the _current_ microcycle because of the function of the current microcycle. They are true when = 0 (low)

            BRANCH_DELAY = (BranchDelay)((uc >> 27) & 0x03);

            // In ND-12O, these are the delay function DLY[1,0]
            //      They the length of the _previous_ microcycle because of the function of the current microcycle. They are true when = 1 (high).
            // Note: Not used in emulator
            bool bit26 = (byte)((uc >> 26) & 0x01) == 1;
            bool bit27 = (byte)((uc >> 27) & 0x01) == 1;

            // Bits 28—31: (TS3—6)
            // These control the microprogram sequence "next address source" and "push—pop stack operation" if the condition result is true.
            // They will only be in control as long as LOOP (bit 22) and ECOND(bit 23) are true.
            int bits28_31 = (int)(uc >> 28) & 0x0F;
            TRUE_StackControl = (StackControl)(bits28_31 & 0x03);
            TRUE_AddressSelect = (NextAddressSelect)((bits28_31 >> 2) & 0x03);



            // Bits 32-36: This is the command field
            int bits32_36 = (int)(uc >> 32) & 0x1F;
            command = (Commands)bits32_36;


            // Bits 37-41: This controls which source is selected into the IDB
            int bits37_41 = (int)(uc >> 37) & 0x1F;
            IDBsource = (IDBSource)bits37_41;

            // Bits 42-43:
            // Miscellaneous bits (MIS 0-1). The bits are used:
            // * To control the shift linkage for the ALU
            // * as an extension to the command field (see bits 32-36)
            MIS = (byte)((uc >> 42) & 0x03);


            // Bits 44-45:
            // These modify bits 55-65, before they reach the ALU. See bits 55-65.
            int bits44_45 = (byte)((uc >> 44) & 0x03);
            aluMode = (ND_ALUMode)bits44_45;

            // Bits 46-47:
            // These contol the carry-in line to the ALU
            int bits46_47 = (byte)((uc >> 46) & 0x03);
            cin = (CIN)bits46_47;


            // Bits 48-49: RBSEL 
            // These contol the source of the B-operand in to the CPU          
            RBSEL = (int)(uc >> 48) & 0x03;

            // Bits 50-52
            // These control the source of the A-Operand to the ALU
            RASEL = (int)(uc >> 50) & 0x07;



            // Bits 53-54
            // These control the behaviour of the 8 lower bits of the STS register.
            int bits53_54 = (int)(uc >> 53) & 0x03;
            stsOperation = (STSOperation)bits53_54;

            // Bits 55-63
            // Together with bits 44-45, these control the behaviour of the ALU.
            // Bits 44-45 modify bits 55-63 before they reach the ALU.
            int bit55_63 = (int)(uc >> 55) & 0x1FF;


            // Bits 55-57 ALU Source
            int bit55_57 = (int)(uc >> 55) & 0b111;
            aluSource = (ALUSource)bit55_57;

            // Bits 58-60 ALU Function
            int bit58_60 = (int)(uc >> 58) & 0b111;
            aluFunction = (ALUFunc)bit58_60;

            // Bits 61-63 ALU Destintation
            int bit61_63 = (int)(uc >> 61) & 0b111;
            aluDestination = (ALUDestination)bit61_63;

            // Calculate fetch flag
            CFETCH = Calc_CFETCH(uc);
        }


        /// <summary>
        ///  FETCH is triggered when the microcode contains specific CSCOMM command patterns (bits 36-32) that indicate an instruction fetch operation should be performed.
        ///  The CFETCH signal is generated in Verilog\DELILAH-CPU\CGA_DCD\circuit\CGA_DCD.v:273. in the nd-120 repository
        /// </summary>
        /// <param name="microcode"></param>
        /// <returns></returns>
        bool Calc_CFETCH(ulong microcode)
        {
            byte cscomm = (byte)((microcode >> 32) & 0x1F);  // Bits 36-32
            byte csmis = (byte)((microcode >> 42) & 0x3);    // Bits 43-42

            // Check the three trigger conditions
            bool ifetchn1 = (cscomm & 0x1A) == 0x12 && csmis == 0x3;  // 1x011 + MIS=11
            bool ifetchn2 = (cscomm & 0x1B) == 0x13;                  // 1x011
            bool ifetchn3 = (cscomm & 0x1C) == 0x14;                  // 1x1xx

            return ifetchn1 || ifetchn2 || ifetchn3;
        }
    }
}
