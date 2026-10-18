//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using NDMicroCpu.Core.AMD;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.AMD.Am2914;
using static NDMicroCpu.Core.Enums;

// The implementation of this Interrupt Subsystem of the ND machine is made after the schematics of the ND-100 cpu card.
namespace NDMicroCpu.Core
{
    /// <summary>
    /// The ND-100 interrupt subsystem: two cascaded Am2914 priority interrupt chips that hold the sixteen program levels, built from the schematics of the ND-100 CPU card.
    /// </summary>
    public class InterruptSubSystem
    {
        /// <summary>
        /// PIC Am214 Chip 23AB (Low 8 interrupts)
        /// </summary>
        Am2914 pic_low;

        /// <summary>
        /// PIC Am214 Chip 21AB (High 8 interrupts)
        /// </summary>
        Am2914 pic_high;

        /// <summary>
        /// Creates the two chips in their power-up state.
        /// </summary>
        public InterruptSubSystem()
        {
            pic_high = new Am2914();
            pic_low = new Am2914();

            // Tie pin 4 to GND.
            //pic_low.SetBitsTiedToGround(1 << 4); 

            // Pin 4 (/GAR) of 23AB connected to GND
            pic_low.Input_GroupAdvanceReceive = false;
        }

        /// <summary>
        /// Sets the interrupt detect bits on both Am2914 chips
        /// Low 8 bits go to Chip 23AB
        /// High 8 bits go to CHip 21AB
        /// </summary>
        /// <param name="detectBits"></param>
        public void SetInterruptDetectbits(ushort detectBits)
        {
            // Bit 4 (pin 1) on the 23A/B seem to be held LOW.
            //detectBits = (ushort)(detectBits & 0xFFF7);// Force bit 4 to 0 
            //detectBits = (ushort)(detectBits | 0x0010); // Force bit 4 to 1


            string s1 = $"Detect HI: [{ToBinary((byte)(detectBits >> 8))}]";
            Utils.Log(s1);


            pic_high.SetInterruptDetectBits((byte)(detectBits >> 8));

            string s2 = $"Detect LO: [{ToBinary((byte)(detectBits & 0xFF))}]";
            Utils.Log(s2);

            pic_low.SetInterruptDetectBits((byte)(detectBits & 0xFF));

            Debug.WriteLine($"SetInterruptDetectBits [{s1}] [{s2}]");
        }

        /// <summary>
        /// Clears bits in the interrupt detect registers of both chips.
        /// </summary>
        /// <param name="clearBits">
        /// A 1 bit clears the level with that number.
        /// </param>
        public void ClearInterruptDetectbits(ushort clearBits)
        {
            // BUG FIX (2026-07-11): pic_high was being passed the LOW byte (clearBits & 0xFF)
            // instead of the HIGH byte (clearBits >> 8), so high-half clears never reached
            // pic_high - matches the split used by the (correct) SetInterruptDetectbits above.
            string s1 = $"Clear HI: [{ToBinary((byte)(clearBits >> 8))}]";
            Utils.Log(s1);
            pic_high.ClearInterruptDetectBits((byte)(clearBits >> 8));

            string s2 = $"Clear LO: [{ToBinary((byte)(clearBits & 0xFF))}]";
            Utils.Log(s2);
            pic_low.ClearInterruptDetectBits((byte)(clearBits & 0xFF));
        }



        /// <summary>
        /// The combined interrupt request output of the two chips.
        /// </summary>
        public bool ReadInterruptRequestPin { get => ReadIRQ(); }

        /// <summary>
        /// Whether interrupt requests are enabled; read from the high chip, the two are kept in step.
        /// </summary>
        public bool InterruptRequestEnabled { get => pic_high.InterruptRequestEnabled_FlipFlop; } // Note: Reading only one chip, as they should be in sync

        /// <summary>
        /// Interrupt!
        /// 
        /// Return FALSE if there is any interrupt set in any of the Am2914's
        /// 
        /// Active LOW
        /// </summary>
        /// <returns></returns>
        internal bool ReadIRQ()
        {
            bool retval = true;

            if (pic_high.Output_InterruptRequest == false) retval = false;

            // GROUP GATING (2026-07-11): The low chip (pins 0-7) may only raise an interrupt
            // request when its group is the "lowest group enabled" (its /GE input - status
            // bit 4 loaded by LOSTS in ExecuteLoadStatus - is active/LOW). When the status
            // fence lies in the HIGH group (e.g. the TRA IIC scan in ND-110-RASK.LISTING.TXT
            // CS 000702-000706 loading fences 25..16 octal = 1GGGB SSS with bit4=1), every
            // low-chip pin is BELOW the fence and must be silent. Without this gate a pending
            // low-chip interrupt (e.g. PAGEF, pin 6) answered already at fence 25 octal and
            // TRA IIC decoded it as IIC=10 (POW) instead of IIC=3 (PF).
            // Output_GroupSignal is the LGE flip-flop output: false (active LOW) = this group
            // is the lowest enabled group.
            if ((pic_low.Output_GroupSignal == false) && (pic_low.Output_InterruptRequest == false)) retval = false;

            //Utils.Log($"Read /IRQ={retval}");
            //Debug.WriteLine($"Read /IRQ={retval}");
            return retval;
        }

        /// <summary>
        /// Clear pending IRQ in both Am2914
        /// </summary>
        public void ClearPendingIRQ()
        {
            pic_high.ClearPendingIRQ();
            pic_low.ClearPendingIRQ();
        }




        /// <summary>
        /// Debug hook fired on every PIC micro-op (both chips), with before/after
        /// snapshots of each chip. Used by the PIC command-trace instrumentation to
        /// give a gate-level Verilog port ground truth for the Am2914 status-fence /
        /// vector-read behavior. Args: (instruction, highBefore, highAfter,
        /// lowBefore, lowAfter). Fires synchronously inside the microinstruction, so a
        /// subscriber can read the live CSAR/PIL at the exact moment. Null in normal runs.
        /// </summary>
        public event System.Action<Am2914Instruction, Am2914.Snapshot, Am2914.Snapshot,
            Am2914.Snapshot, Am2914.Snapshot>? OnPicExec;

        /// <summary>
        /// Execute the instruction on both Am2914 chips
        /// </summary>
        /// <param name="instruction"></param>
        public void Exec(Am2914Instruction instruction)
        {
            // Capture before-state only when someone is listening (zero cost otherwise).
            if (OnPicExec != null)
            {
                Am2914.Snapshot hb = pic_high.Capture();
                Am2914.Snapshot lb = pic_low.Capture();
                pic_high.ExecuteInstruction(instruction);
                pic_low.ExecuteInstruction(instruction);
                OnPicExec(instruction, hb, pic_high.Capture(), lb, pic_low.Capture());
            }
            else
            {
                pic_high.ExecuteInstruction(instruction);
                pic_low.ExecuteInstruction(instruction);
            }
        }

        /// <summary>
        /// Latches the 8-bit MaskBus on both Am2914 from splitting one 16 bits word
        /// </summary>
        /// <param name="maskBits"></param>
        public void LatchMaskBus(ushort maskBits)
        {
            Utils.Log($"Mask HI: [{ToBinary((byte)(maskBits >> 8))}]");
            pic_high.LatchMaskBus((byte)((maskBits >> 8) & 0xFF));

            Utils.Log($"Mask LO: [{ToBinary((byte)(maskBits & 0xFF))}]");
            pic_low.LatchMaskBus((byte)(maskBits & 0xFF));
        }


        string ToBinary(byte b)
        {

            return Convert.ToString(b, 2).PadLeft(8, '0');
        }
        string ToBinary(ushort b)
        {

            return Convert.ToString(b, 2).PadLeft(16, '0');
        }


        /// <summary>
        /// Reads the Status register (without executing RDSTA) from both Am2914 and returns their 8 bits results in one 16 bit word
        /// </summary>
        /// <returns>
        /// Low chip in the low byte, high chip in the high byte.
        /// </returns>
        public ushort ReadStatus()
        {
            return (ushort)(pic_high.StatusRegister << 8 | pic_low.StatusRegister);
        }

        /// <summary>
        /// Reads the Status bus from both Am2914 and returns their 8 bits results in one 16 bit word
        /// </summary>
        /// <returns>
        /// Low chip in the low byte, high chip in the high byte.
        /// </returns>
        public ushort ExecuteReadStatus()
        {
            Exec(Am2914Instruction.RDSTA);

            // TODO: Check if pic_low should NOT report anything if the pic_high has an interrupt. (ie low=disabled)

            ushort statusBus = (ushort)(pic_high.ReadStatusBus() << 8 | pic_low.ReadStatusBus());
            return statusBus;
        }


        /// <summary>
        /// Execute the PIC command RDM (Read Mask) for both PIC's
        /// Reads the Mask bus from both Am2914 and returns their 8 bits results in one 16 bit word
        /// </summary>        
        public ushort ExecuteReadMask()
        {
            Exec(Am2914Instruction.RDM);
            ushort mask = (ushort)(pic_high.ReadMaskBus() << 8 | pic_low.ReadMaskBus());

            return mask;
        }

        /// <summary>
        /// Reads the Mask registers from both Am2914 and returns their 8 bits results in one 16 bit word WITHOUT EXECUTING RDM (for debugging read)
        /// </summary>
        /// <returns>
        /// Low chip in the low byte, high chip in the high byte.
        /// </returns>
        public ushort ReadMask()
        {
            ushort mask = (ushort)(pic_high.MaskRegister << 8 | pic_low.MaskRegister);

            return mask;
        }

        /// <summary>
        /// Write the Mask registers from both Am2914 and returns their 8 bits results in one 16 bit word WITHOUT EXECUTING LDM (for debugging write)
        /// </summary>
        /// <param name="mask">
        /// Low chip in the low byte, high chip in the high byte.
        /// </param>
        public void WriteMask(ushort mask)
        {
            pic_high.MaskRegister = (byte)(mask >> 8);
            pic_low.MaskRegister = (byte)(mask & 0xFF);
        }



        /// <summary>
        /// Reads the mask bus of both chips as one 16-bit word, low chip in the low byte.
        /// </summary>
        /// <returns>
        /// The 16-bit mask.
        /// </returns>
        public ushort ReadMaskBus()
        {
            ushort maskBus = (ushort)(pic_high.MBUS << 8 | pic_low.MBUS);
            return maskBus;
        }

        /// <summary>
        /// Reads the status bus of both chips as one 16-bit word, low chip in the low byte.
        /// </summary>
        /// <returns>
        /// The combined status.
        /// </returns>
        public ushort ReadSBus()
        {
            ushort sBus = (ushort)(pic_high.SBUS << 8 | pic_low.SBUS);
            return sBus;
        }


        /// <summary>
        /// Read the Interrupt detect bits from the internals of both Am2914 and returns the two 8 bits values in one 16 bit value
        /// </summary>
        /// <returns></returns>
        public ushort ReadInterruptDetect()
        {
            PropagateSignals();

            ushort interruptDetect = (ushort)(pic_high.InterruptDetectRegister << 8 | pic_low.InterruptDetectRegister);
            return interruptDetect;
        }

        /// <summary>
        /// Executes PIC.ReadVector
        /// Reads the Vector from the active Am2914. 
        /// Sets bit 3 = Pin 21A.3 -> /GS (high 8 interrupts)
        /// Sets bit 4 = Pin 23A.3 -> /GS (low 8 interrupts)
        /// </summary>
        /// <returns>
        /// The vector word.
        /// </returns>
        public ushort ExecuteReadVector()
        {
            PropagateSignals();

            byte vector = 0;
            Exec(Am2914Instruction.RDVC);
            //hw.pic.LatchMaskBus(hw.pic.ReadVectorOutputPins());

            // Exactly ONE group may claim the vector. The HIGH chip (pins 8-15: internal
            // faults + BINT14/15) is the higher-priority group per the ND-100 schematic -
            // its Ripple Disable output feeds the LOW chip's Interrupt Disable input - so
            // ties resolve to the high chip. The microcode dispatches on these two bits
            // (bit 3 = hi claim => internal-interrupt path, bit 4 = lo claim => device
            // levels 10-13); reporting both asserted at once sent a lone RTC (pin 3)
            // request down the EXT14/level-14 path and hung TRA IIC.
            // Uses Output_VectorClaim (latched from each chip's live winning-request
            // condition during RDVC), NOT Output_GroupSignal - the latter is the Lowest
            // Group Enabled flip-flop whose GE-latch state ReadIRQ()'s cascade group
            // gating depends on; see the matching comments in Am2914.ReadVector().
            bool hiWins = pic_high.Output_VectorClaim == false; // active low
            bool loWins = !hiWins && pic_low.Output_VectorClaim == false;

            if (hiWins)
            {
                vector = pic_high.VectorOutput;
            }
            else if (loWins)
            {
                vector = pic_low.VectorOutput;
            }

            vector |= (byte)((hiWins ? 1 : 0) << 3);
            vector |= (byte)((loWins ? 1 : 0) << 4);


            Logger.Log(
                $"PIC RDVC vector={Convert.ToString(vector, 2).PadLeft(5, '0')}b " +
                $"(hiGS={(hiWins ? "asserted" : "-")} loGS={(loWins ? "asserted" : "-")}) " +
                $"detect={Utils.Int2Oct(pic_high.InterruptDetectRegister << 8 | pic_low.InterruptDetectRegister)} " +
                $"mask={Utils.Int2Oct(pic_high.MaskRegister << 8 | pic_low.MaskRegister)}",
                Logger.LogLevel.Debug);
            VectorOutput = vector;
            return vector;
        }

        /// <summary>
        /// The vector the last read vector instruction produced.
        /// </summary>
        public ushort VectorOutput { get; set; }

        /// <summary>
        /// Loads the status bus for both Am2914 chips
        /// </summary>
        /// <param name="bits"></param>
        public void ExecuteLoadStatus(ushort bits)
        {
            PropagateSignals();

            Logger.Log($"PIC LOSTS bits={Utils.Int2Oct(bits)} (status={bits & 0x7} hiGE={(bits >> 3) & 1} loGE={(bits >> 4) & 1})", Logger.LogLevel.Debug);

            // GROUP GATING (2026-07-11): status word layout on the ND-100 is
            //   bit4..3 = group select (01 = low group / pins 0-7, 10 = high group / pins 8-15)
            //   bit2..0 = level within the selected group  (fence value = pin + 8)
            // When the fence lies in the LOW group (bit 3 set -> high chip /GE inactive) the
            // whole HIGH group is ABOVE the fence, so the high chip must accept ANY unmasked
            // pending interrupt - its 3-bit status must not be (mis)used as a fence. Latch 0
            // for that case. Example: TRA IIC scan fence 16 octal (=PF): a pending POW (pin 13,
            // high chip level 5) must still assert /IRQ even though 5 < (16 octal & 7)=6.
            bool highIsFenceGroup = (bits & 1 << 3) == 0; // /GE active LOW
            pic_high.LatchStatusBus(highIsFenceGroup ? (byte)(bits & 0x7) : (byte)0);
            pic_high.Input_GroupEnable = (bits & 1 << 3) != 0; // Active LOW

            pic_low.LatchStatusBus((byte)(bits & 0x7));
            pic_low.Input_GroupEnable = (bits & 1 << 4) != 0; // Active LOW (low chip is gated off in ReadIRQ() when inactive)

            Exec(Am2914Instruction.LDSTA);

            string bitss = ToBinary(bits);
            int h_irq = pic_high.Output_InterruptRequest ? 1 : 0;
            int h_gs = pic_high.Output_GroupSignal ? 1 : 0;

            int l_irq = pic_low.Output_InterruptRequest ? 1 : 0;
            int l_gs = pic_low.Output_GroupSignal ? 1 : 0;

            //Debug.WriteLine($"{bitss} HIGH:[I:{h_irq} GS:{h_gs}] LO:[I:{l_irq} GS:{l_gs}]");
            Logger.Log($"{bitss} HIGH:[I:{h_irq} GS:{h_gs}] LO:[I:{l_irq} GS:{l_gs}]", Logger.LogLevel.Opcodes);

        }


        /// <summary>
        /// Asserts one of the sixteen interrupt input pins.
        /// </summary>
        /// <param name="pin">
        /// The program level, 0 to 15.
        /// </param>
        public void SetInterruptPIN(int pin)
        {
            if (pin < 8)
            {
                pic_low.SetInterrupPIN(pin);
            }
            else
            {
                pic_high.SetInterrupPIN(pin - 8);
            }
        }

        /// <summary>
        /// Set interrupt pin according to ENUM values.
        /// To make it easier to test using ND pin-names
        /// </summary>
        /// <param name="epin"></param>
        public void SetInterruptPIN(InterruptPin epin)
        {
            ushort pin = (ushort)epin;
            SetInterruptPIN(pin);
        }


        /// <summary>
        /// Read pin 14 of 21A - Status Overflow (active low)
        /// </summary>
        /// <returns></returns>
        public bool SIGNAL_SOWF()
        {
            return pic_high.Output_StatusOverflow;
        }

        /// <summary>
        /// Write pin 6 of 21A - Interrupt Disable
        /// Active LOW
        /// </summary>
        /// <returns></returns>
        public void SIGNAL_EMINT(bool value)
        {
            pic_high.Input_InterruptDisable = value;

        }

        /// <summary>
        /// Two Am2914 connected in cascade. Propagate the signals.
        /// 
        ///     MAYBE: Create events for the signal OUT change and hook the event handles out to signals in??
        /// 
        /// </summary>
        internal void PropagateSignals()
        {
            // Only two set of signals connecting the chips in the ND-100 CPU Schematics

            // GAS(Group Advance Send) output from the "low" chip to the GAR(Group Advance Receive) input of the "high" chip
            // Ripple Disable output from the "high" chip to the Interrupt Disable input of the "low" chip


            // Output from 21A (High) pin 7 (Ripple disable) is connected to 23A pin 6 (Interrupt disable)
            pic_low.Input_InterruptDisable = pic_high.Output_RippleDisable;

            //Group Advanced Send / Receive
            //23A.15-> 21A.4  => / GAS-> / GAR
            // 21A.4 / GAR connected to GND.
            // * „Signal out from(low) 23A.15 / GAS „Group Advance Send“ to(high)21A.4 „Group Advance Receive“
            pic_high.Input_GroupAdvanceReceive = pic_low.Output_GroupAdvanceSend;

        }


        /// <summary>
        /// Executes the Am2914 instruction the current micro word selects on both chips.
        /// </summary>
        /// <param name="hw">
        /// The hardware the instruction and the data bus come from.
        /// </param>
        public void ExecuteInstruction(CpuInternals hw)
        {
            ushort idb_word = hw.idb.ReadWord();
            Am2914Instruction instruction = (Am2914Instruction)hw.alu.A_Operand;
            //string strX = $"### PIC.EXEC({instruction}) MaskBus:{_maskBus:X4} MaskRegister:{MaskRegister:X4} StatusBus:{_statusBus:X4} IRQE:{_InterruptRequestEnabled} IRQP:{_pendingIRQ} ###";            
            string strX = $"PIC.EXEC({instruction} IDB:{Utils.Int2Oct(idb_word)}  Y:{Utils.Int2Oct(hw.alu.Y)} F:{Utils.Int2Oct(hw.alu.F)})  Q:{Utils.Int2Oct(hw.alu.Q)})";
            Logger.Log(strX, Logger.LogLevel.Opcodes);
            //Debug.WriteLine(strX);

            //Console.WriteLine(strX);


            PropagateSignals();



            switch (instruction)
            {

                case Am2914Instruction.MCLR:
                case Am2914Instruction.CLRIN:
                case Am2914Instruction.CLRMR:
                case Am2914Instruction.CLRVC:
                case Am2914Instruction.RDSTA:
                case Am2914Instruction.SETM:
                case Am2914Instruction.CLRM:
                case Am2914Instruction.DISIN:
                case Am2914Instruction.ENIN:
                    // ENIN = ND microcode "PIC,ION", DISIN = "PIC,IOF" - these gate the
                    // pair's /IRQ output and therefore whether the CPU dispatches macro
                    // interrupts at all. Logged because a stray ENIN (e.g. from a panel /
                    // MOPC service path) silently re-enables interrupts behind a macro
                    // program's back after it executed IOF.
                    if (instruction == Am2914Instruction.ENIN || instruction == Am2914Instruction.DISIN)
                    {
                        Logger.Log($"PIC {(instruction == Am2914Instruction.ENIN ? "ION(ENIN)" : "IOF(DISIN)")} CSAR={Utils.Int2Oct(hw.mic.map)}", Logger.LogLevel.Debug);
                    }
                    Exec(instruction);
                    break;

                case Am2914Instruction.CLRMB:

                    //#hw.pic.LatchMaskBus((ushort)hw.alu.Y);

                    // TODO: Is this hw.aly.Y instead ??
                    LatchMaskBus(hw.idb.ReadWord());

                    Exec(instruction);
                    break;


                case Am2914Instruction.RDVC:
                    hw.idb.Latch(ExecuteReadVector());
                    break;

                case Am2914Instruction.RDM:
                    hw.idb.Latch(ExecuteReadMask());
                    break;

                case Am2914Instruction.LDSTA:
                    ExecuteLoadStatus(hw.idb.ReadWord());
                    break;

                case Am2914Instruction.BCLRM:

                    LatchMaskBus(hw.idb.ReadWord());
                    //TODO: Evaluate!!
                    //LatchMaskBus((ushort)hw.alu.Y);

                    Exec(instruction);
                    break;

                case Am2914Instruction.BSETM:
                    LatchMaskBus(hw.idb.ReadWord());
                    //TODO: Evaluate!!
                    //LatchMaskBus((ushort)hw.alu.Y);

                    Exec(instruction);
                    break;


                case Am2914Instruction.LDM:
                    LatchMaskBus(hw.idb.ReadWord());
                    //LatchMaskBus((ushort)hw.alu.Y);
                    Exec(instruction);
                    break;


                default:
                    break;
            }


            //strX = $"### PIC.EXEC({instruction}) MaskBus:{_maskBus:X4} MaskRegister:{MaskRegister:X4} StatusBus:{_statusBus:X4} IRQE:{_InterruptRequestEnabled} IRQP:{_pendingIRQ} ###";
            //Utils.Log(strX);
        }


    }
}

