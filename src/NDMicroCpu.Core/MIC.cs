//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using static System.Formats.Asn1.AsnWriter;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{

    /*
    
    The Micro Instruction Controller, the MIC - also known as RMIC, for "Rask MIC" ("Speedy MIC"). 
    It replaced three 74S482 sequencers and about 30 other ICs.

    The MIC computes control store addresses, to ensure that the  CPU executes  microinstructions in the correct sequence.

    Fetch operations in the microcode obtain the next macroinstruction, from the memory address specified by the by MAC.   
    
    The MIC then generates the "map" (microprogram  address pointer) entry points for the fetched macroinstruction
    by adding 6000 to the op code (ie. the most significant 10  bits)  of  the  macroinstruction  itself.  
    
    This defines the starting point in microprogram for every macroinstruction fetched into the CPU.

    If a cache hit occurred for the instruction fetch, the first microinstruction for this macroinstruction is obtained
    directly from  microcache  (see "Control store"), and so the MIC's operation to compute the map address is bypassed,
    and the result of its computation is ignored.

    One or more microinstructions are required to execute a macroinstruction. The MIC controls the sequence required.

    This may specify a jump condition, or the current address + 1 (next), or a return condition 
    (from an internal 4—deep push/pop stack which can nest up to four microprogram subroutine  levels).  
    
    The microinstruction control may include the conditional branching in the microcode sequence (involving selection from a variety of  
    test objects), and vectored jump sequencing, 
    
    
    The MIC incorporates the loop counter function. This is 6-bit register, with the most significant bit as a sign  bit.
    It has an auto—increment/decrement facility, which enables it to be counted towards zero from either direction. On
    reaching zero, further counting is inhibited. The loop counter is used to repeat one (or several) microinstructions
    a predetermined number of times, or to count (for example) the number of shifts needed to normaIise a floating point

    In the ND-100 the MIC was three 74S482 chips. 
    The 482 is a "4-BIT-SLICE EXPANDABLE CONTROL ELEMENTS"
    */


    /// <summary>
    /// Microprogram instruction controller
    /// Microprogram Sequencer RMIC
    /// </summary>
    public class MIC
    {


        // The microprogram sequencer contains a microprogram address register (ADR) with multiplexed input, a push/pop stack and an incrementer.
        // Control lines provide the information needed to select the source of the next microinstruction address

        #region LoopCounter, LC
        // Constants
        const int MASK_bits = 0b011111;
        const int MASK_sign = 0b100000;


        /// <summary>
        /// 6 bit register, signed
        /// </summary>
        public int loopCounter { get; set; } // must be settable because of debugger

        /// <summary>
        /// The loop counter as the micro code reads it.
        /// </summary>
        public ushort LC { get => (ushort)loopCounter; }

        /// <summary>
        /// Sign of the loop counter (true while LC is negative = counting UP = right shift
        /// in ALUM,IR-SHIFT mode), sampled once per microinstruction in ClockLZ0 - i.e.
        /// with the SAME one-cycle detection delay as LCZ ("The new value of the loop
        /// counter will not be available ... until after the next microinstruction").
        ///
        /// This must be RE-SAMPLED as the counter counts, not latched once at LDLC (bug
        /// fixed 2026-07-11): the shared shift microinstruction (ND-110-RASK.LISTING.TXT
        /// CS 000237, "B,A ALUF,PASSB ALUM,IR ALUD,SRD" with Q holding D) derives ALU-I7
        /// from /UP each cycle. A right shift loads LC = -n and counts up; by the time the
        /// documented non-shifting EXTRA pass runs (one cycle after LC reached 0, same
        /// pipelining as LCZ) /UP has flipped, so the extra pass's destination decays to
        /// 010 = ALUD_B_YA (A := F, harmless) exactly like the left-shift case. With UP0
        /// latched at load time the RIGHT-shift extra pass decayed to 000 = ALUD_Q
        /// instead, executing Q := F and destroying D (Q holds D during SAD/SHD) -
        /// INSTRUCTION-VERIFY's SAD4OP ("SAD SHR 1" with A=100000 D=0 must give A=140000
        /// D=0) caught this: D read back 140000, a copy of A. (A LIVE unsampled UP0 is
        /// wrong the other way: the shift pass itself already sees LC=0 and would shift
        /// LEFT - the delay is what makes both the shift pass and the extra pass correct.)
        /// </summary>
        internal bool UP0 { get; private set; }

        /// <summary>
        /// Loop counter zero
        /// </summary>
        internal bool LCZ0 { get; private set; }

        /// <summary>
        /// The loop counter value visible on the A/B OPERAND path (register-file
        /// addressing via the "A,LC" / "B,LC" microcode tokens).
        ///
        /// Ground truth (ND-06.031.1EN Microprogrammer's Guide, command 17 LDLC): "The
        /// modified loop counter will not be available as an A-operand until the next
        /// microinstruction has been completed." - i.e. the operand path sees the counter
        /// through a pipeline latch: instruction N uses the value the counter had at the
        /// START of instruction N-1, so BOTH an LDLC load and an LCOUNT count become
        /// operand-visible one full instruction later.
        ///
        /// This latch is what makes the register-file ring buffers in the microcode line
        /// up (verified against ND-110-RASK.LISTING.TXT):
        ///  * LVSWP register save/restore (CS 001121/001126): LDLC 7, one non-counting
        ///    instruction, then the loop - operands run 7,7,6,5,4,3,2,1,0 over nine
        ///    passes, covering all eight registers.
        ///  * MOVB/MOVBF fast word loop (FWL, CS 001705-001725): the read burst
        ///    (CS 001710, B,LC) files eight source words into registers 7..0 and the
        ///    write burst (prime "A,7" at CS 001725 + CS 001714 A,LC) reads them back
        ///    from 7,6..0. With a LIVE operand the reads landed in 6..0 while the write
        ///    prime fetched register 7 = the untouched user X register - which is exactly
        ///    what INSTRUCTION-VERIFY's BYTE-STRING area caught ("MOVB" FAILED with the
        ///    destination address value in the copied data).
        /// </summary>
        internal int LC_operand { get; private set; }

        /// <summary>
        /// Clocks the operand-path pipeline latch. Called once per microinstruction with
        /// the loop counter value captured at the START of that instruction (before its
        /// LDLC and/or LCOUNT modified the counter) - so the NEXT instruction's A/B
        /// operand sees the pre-modification value, per the LDLC documentation quoted on
        /// LC_operand.
        /// </summary>
        internal void LatchLCOperand(int lcAtInstructionStart)
        {
            LC_operand = lcAtInstructionStart;
        }

        internal void LatchLC(int tmplc)
        {
            loopCounter = tmplc;
            UP0 = (loopCounter < 0); // freshly loaded sign is visible immediately, like LCZ0 below
            // LCZ semantics - ground truth from the ND-120 CGA Verilog (CGA_MIC.v +
            // CGA_MIC_CONDREG.v, verified 2026-07-11):
            //   * The real "LCZ" test object is NOT "loop counter == 0". CGA_MIC_CONDREG
            //     latches microcode bits 11:8 as LCC ("Loop Counter Compare") when a
            //     condition is armed (SCOND), and a live combinational CMP4 comparator
            //     (LC_CMP in CGA_MIC.v) asserts LCZ when LC[3:0] == LCC[3:0] while
            //     LC[5:4] == 0 (GATES_3/GATES_17). It is a PROGRAMMABLE TERMINAL COUNT,
            //     re-evaluated live every cycle - there is no suspension flip-flop.
            //   * This emulator currently approximates LCZ as (loopCounter == 0), i.e.
            //     LCC = 0. That is correct for every microcode use EXCEPT loops that arm
            //     LCZ with a non-zero compare nibble - known case: FAD's mantissa
            //     alignment (CS 003156/003160 loads LC = exp-diff +/- 1 and the NORM1 loop
            //     at CS 003204-003205 must run |diff| net shifts; FAD_32_1plus3 returns
            //     5.0 instead of 4.0). The proper fix is to decode the compare nibble from
            //     the arming microword and compare against it here.
            //   * A "suspend LCZ0 sampling between LDLC and the first LCOUNT" model was
            //     tried as a FAD workaround (2026-07-11) and REVERTED: with it, the CPU
            //     could no longer see a freshly loaded zero counter in LDLC-then-test
            //     sequences that never set the LOOP bit, which stalled INSTRUCTION-VERIFY's
            //     banner printing mid-line inside the IOX console-output path
            //     (CS 000472-000510) and cost the ">" prompt. Live sampling is what the
            //     real comparator does.
            LCZ0 = (loopCounter == 0);
        }



        /// <summary>
        /// microprogram address pointer
        /// </summary>
        public ushort map { get; private set; }


        /// <summary>
        /// Latch new MAP address
        /// </summary>
        /// <param name="newmap"></param>
        public void LatchMap(ushort newmap)
        {
            map = newmap;
        }



        /// <summary>
        /// Latch new MAP address
        /// </summary>
        /// <param name="address">
        /// The micro address to latch.
        /// </param>
        public void LatchJumpAddress(ushort address)
        {
            jmp_map = address;
        }

        private ushort jmp_map;

        /// <summary>
        /// Increase MAP address with 1
        /// 
        /// Move to next uc instruction
        /// </summary>
        private void IncMap()
        {
            map++;
        }


        ///// <summary>
        ///// Count towards Zero. Stop when reaching zero
        ///// Replaced!!
        ///// </summary>
        //public void IncrementLoopCounterBitwise()
        //{
        //    // Count towards zero. 
        //    int cnt = loopCounter & MASK_bits;
        //    bool neg = (loopCounter & MASK_sign) != 0;

        //    if (cnt > 0)
        //    {
        //        cnt--;
        //    }

        //    //On reaching zero, further counting is inhibited
        //    if (cnt > 0)
        //    {
        //        loopCounter = cnt;
        //        if (neg)
        //            loopCounter |= MASK_sign;
        //    }

        //    Utils.Log($"LC={loopCounter}");
        //}

        /// <summary>
        /// Count towards Zero. Stop when reaching zero
        /// </summary>
        public bool IncrementLoopCounter()
        {
            if (loopCounter != 0)
            {
                // Count towards zero. 
                if (loopCounter > 0)
                {
                    loopCounter--;
                    Utils.Log($"LC-={loopCounter}");
                }
                else
                {
                    loopCounter++;
                    Utils.Log($"LC+={loopCounter}");
                }
            }
            return LCZ0;
        }

        /// <summary>
        /// Re-sample LCZ0 from the loop counter. Called once per microinstruction (before a
        /// potential decrement, giving the documented one-instruction detection delay).
        /// The real hardware comparator (CMP4 LC_CMP in ND-120 CGA_MIC.v) is combinational
        /// and live - see the LCC ground-truth comment in LatchLC.
        /// </summary>
        /// <param name="clffActive">True when the current microinstruction executes
        /// COMM,CLFF. Per ND-120 CGA_MIC.v GATES_3 (LCZ_n = NAND(NOR(LC4, LC5, CLFF),
        /// LC[3:0]==LCC)), CLFF forces LCZ FALSE for that cycle - FAD's alignment loop
        /// arms COND,LC=0 together with COMM,CLFF (CS 003204) and relies on exactly this
        /// to hide a freshly loaded zero counter for one pass.</param>
        public void ClockLZ0(bool clffActive)
        {
            LCZ0 = (loopCounter == 0) && !clffActive;
            UP0 = (loopCounter < 0); // direction bit pipelines the same way - see UP0 remarks
        }
        #endregion LoopCounter, LC


        #region Calculate Macro-Instruction to microAddress

        /// <summary>
        /// Calculate entrypoint into MicroCode from an Macro Instruction
        /// </summary>
        /// <param name="instruction"></param>
        /// <returns></returns>
        public ushort CalculateMacEntryPoint(ushort instruction)
        {

            map = ((ushort)((instruction >> 6) + 0xC00)); // 0ct 6000

            return map;
        }

        #endregion  Calculate Macro-Instruction to microAddress


        #region Public API for doing MIC calculations
        /// <summary>
        /// The stack control the current micro instruction asked for.
        /// </summary>
        public StackControl StackControl { get; set; }
        /// <summary>
        /// The next address select the current micro instruction asked for.
        /// </summary>
        public NextAddressSelect AddressSelect { get; set; }



        internal void ExecuteMAPUpdate()
        {

            // Keep a copy of the top stack value in case of RETURN combined with some stack control that updates top-stack.
            ushort topStack = RMIC_stack[0];

            switch (StackControl)
            {
                case StackControl.Hold:
                    // Do nothing with stack, use register output
                    //Utils.Log($"StackControl: HOLD");

                    break;
                case StackControl.Pop:
                    PopStack();

                    //Utils.Log($"StackControl: POP");
                    break;
                case StackControl.Load:
                    LoadStack();

                    //Utils.Log($"StackControl: LOAD");
                    break;
                case StackControl.Push:
                    PushStack();

                    //Utils.Log($"StackControl: PUSH");
                    break;
            }

            // Sequencer instruction
            switch (AddressSelect)
            {
                case NextAddressSelect.Jump:
                    // jmp_map must be latched before this point
                    //Utils.Log($"AddressSelect: JUMP");
                    map = jmp_map;
                    break;

                case NextAddressSelect.Return:
                    //Utils.Log($"AddressSelect: RETURN"); // fetch top address from STACK

                    //if (StackControl != StackControl.Pop) // If it has been "popped" dont read again.
                    map = topStack;

                    IncMap(); // move to next uc address after the address JUMP came from

                    break;
                case NextAddressSelect.Next: // Move to next address
                    IncMap();
                    //Utils.Log($"AddressSelect: NEXT");
                    break;

                case NextAddressSelect.Repeat:  // dont modify mic.map addrees => repeat the same ucode again
                    Utils.Log($"AddressSelect: REPEAT");
                    break;

            }


        }
        #endregion

        #region RMIC Stack

        ushort[] RMIC_stack = new ushort[4];

        /// <summary>
        /// Reads an entry of the micro program stack, for the debugger.
        /// </summary>
        /// <param name="offset">
        /// The entry, 0 to 3.
        /// </param>
        /// <returns>
        /// The micro address stored there.
        /// </returns>
        public ushort GetStackValue(byte offset)
        {
            if ((offset < 0) || (offset >= RMIC_stack.Length)) return 0;
            return RMIC_stack[offset];
        }

        /// <summary>
        /// Writes an entry of the micro program stack, for the debugger.
        /// </summary>
        /// <param name="offset">
        /// The entry, 0 to 3.
        /// </param>
        /// <param name="val">
        /// The micro address to store.
        /// </param>
        public void SetStackValue(byte offset, ushort val)
        {
            if ((offset < 0) || (offset >= RMIC_stack.Length)) return;
            RMIC_stack[offset] = val;
        }

        /// <summary>
        /// a jump to a subroutine (PUSH)
        /// 
        /// From the 74S482 doc:
        /// In the PUSH mode, the new word is again entered in the first (top) location; 
        /// however, previous data residing in the top three words are pushed down one word location and retained at their new locations.
        /// The bottom word is written over and lost.
        /// </summary>
        public void PushStack()
        {
            for (int i = 2; i >= 0; i--)
            {
                RMIC_stack[i + 1] = RMIC_stack[i];
            }

            RMIC_stack[0] = map;
            //Utils.Log($"StackPush {Utils.Int2Oct(map)}");
        }

        /// <summary>
        /// Return from a subroutine (POP)
        /// 
        /// From the 74S482 doc:
        /// In the POP mode, words from the pus-pop stack move up one location on each clock transition. 
        /// A unique function is provided by the bottom (fourth) register as its contents is retained during the pop mode, and after 3 clock transitions,
        /// all words in the stack are filled with the operand/address that occupied the bottom register.
        /// 
        /// The operand/address will remain available indefinitely if stack functions are limited to pop or hold modes.
        /// 
        /// </summary>
        public void PopStack()
        {
            //map = RMIC_stack[0];

            for (int i = 0; i < 3; i++)
            {
                RMIC_stack[i] = RMIC_stack[i + 1];
            }

            Utils.Log($"StackPop {Utils.Int2Oct(map, true)}");
        }

        /// <summary>
        /// Load the top word (LOAD) with the current microprogram address 1 without affecting the rest of the stack
        /// 
        /// From the 74S482 doc:
        /// In the LOAD mode, the first (top) word is filled with new data from the output of the full adder, and no push occurs. 
        /// Meaning that previous data at that location is lost. However, all other word locations in the push-pop stack remain unchanged.
        /// </summary>
        public void LoadStack()
        {
            RMIC_stack[0] = map;

            Utils.Log($"LoadStack {Utils.Int2Oct(map, true)}");
        }


        #endregion RMIC Stack
    }
}
