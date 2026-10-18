//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.AMD
{

    /// <summary>
    /// Priority Interrupt Controller
    /// An emulation of the Am2914 Chip
    /// The ND-110 has two of these, each handles 8 interrupt inputs.
    /// </summary>
    /// Ref: 
    ///     PIC: http://sintran.com/sintran/library/libother/extern/AM2914.pdf
    ///     CPU-Card: http://sintran.com/sintran/hardware/nd-100/nd-324141.html
    ///     AMD: http://www.donnamaie.com/BITSLICE/BIT_CHAP_4/bitCh4D1.html
    ///     More: http://datasheets.chipdb.org/AMD/290x/

    /*
     * Distinctive Characteristics
     *
     
        Accepts 8 interrupt inputs  - Interrupts may be pulses or levels and are stored internally
        Built-in mask register      - Six different operations can be performed on mask register
        Built-in status register    - Status register holds code for lowest allowed interrupt (3 bit in the Am2914, 4 in the BufALU)
        Vectored output             - Output is binary code for highest prionty unmasked interrupt
        Expandabie                  - Any number of Am2914's may be stacked for large interrupt systems
        Microprogrammable           - Executes 16 different microinstructions. Instruction enable pin aids in vertical microprogramming

     *
     *  General Description 
     * 
        The Am2914 1s a high-speed, eight-bit priority interrupt unit
        that is cascadable to handle any number of priority interrupt
        request levels. The high-speed of the Am2914 makes it
        ideal for use in Am2900 family microcomputer designs, but
        it can also be used with the Am9080A MOS microprocessor.

        The Am2914 receives interrupt requests on 8 interrupt input
        lines (P0-P7). A LOW level is a request. An internal latch
        may be used to catch pulses on these lines, or the latch
        may be bypassed so the request iines dnve the level-
        triggered interrupt reqister directly. An 8-bit mask register is
        used to mask individual interrupts. Considerable flexibility is
        provided for controlling the mask register. Requests in the
        interrupt register are ANDed with the corresponding bits in
        the mask register and the results are sent to an 8-input
        priority encoder, which produces a three-bit encoded vector
        representing the highest numbered input which is not
        masked.

        An internal status register is used to point to the lowest
        priority at which an interrupt will be accepted. The contents
        of the status register are compared with the output of the
        priority encoder, and an interrupt request output will occur if
        the vector is greater than or equal to status. Whenever a
        vector is read from the Am2914 the status register is
        automatically updated to point to one level higher than the
        vector read. (The status register can be loaded externally or
        read out at any time using the S pins.) Signals are provided
        for moving the status upward across devices (Group
        Advance Send and Group Advance Receive) and for
        inhibiting lower priorities from higher order devices (Ripple
        Disable, Parallel Disable, and Interrupt Disable). A status
        overflow output indicates that an interrupt has been read at
        the highest priority.

        The Am2914 is controlled by a 4-bit instruction field I0-I3.
        The command on the instruction lines is executed if IE is
        LOW and is ignored it IE is HIGH, allowing the 4 I bits to be
        shared with other devices. (IE=Instruction Enable)
     
    *
    *  FUNCTIONAL DESCRIPTION
    *
        The Microinstruction Decode circuitry decodes the Interrupt
        Microinstructions and generates required control signals for
        the chip.

        The Interrupt Register holds the Interrupt Inputs and is an
        eight-bit, edge-triggered register which is set on the rising
        edge of the Clock (CP) signal.

        The Interrupt latches are set/reset-type latches. When the
        Latch Bypass (LB) signal is LOW, the latches are enabled and
        act as negative pulse catchers on the inputs to the Interrupt
        Register. When the Latch Bypass (LB) signal is HIGH, the
        Interrupt latches are transparent.

        The Mask Register holds the eight mask bits associated with
        the eight interrupt levels. The register may be loaded from or
        read to the M Bus. Also, the entire register or individual mask
        bits may be set or cleared.

        The Interrupt Detect circuitry detects the presence of any
        unmasked Interrupt (Pp_7) Input. The eight-input Priority
        Encoder determines the highest priority, non-masked Interrupt
        Input and forms a binary coded Interrupt Vector. Following a
        Vector Read, the three-bit Vector Hold Register holds the
        binary coded interrupt vector. This stored vector is used for
        Clearing interrupts.

        The three-bit Status Register holds the status bits and may be
        loaded from or read to the S Bus. During a Vector Read, the
        Incrementer increments the Interrupt Vector by one, and the
        result is clocked into the Status Register. Thus the Status 
        Register always points to the lowest level at which an interrupt
        will be accepted.

        The three-bit Comparator compares the Interrupt Vector with
        the contents of the Status Register and indicates if the
        Interrupt Vector is greater than or equal to the contents of the
        Status Register.

        The Lowest Group Enabled Flip-Flop is used when a number
        of Am2914's are cascaded. In a cascaded system, only one
        Lowest Group Enabled Flip-Flop is LOW at a time. It indicates
        the eight interrupt group, which contains the lowest priority
        interrupt level which will be accepted and is used to form the
        higher order status bits.

        The Interrupt Request and Group Enable logic contain various
        gating to generate the Interrupt Request (IR), Parallel Disable
        (PO), Ripple Disable (RD), and Group Advance Send (GAS)
        signals.

        The Status Overflow (SO) signal is used to disable all
        interrupts. It indicates the highest priority Interrupt Vector has
        been read and the Status Register has overflowed.

        The Clear Control logic generates the eight individual clear
        signals for the bits in the Interrupt Latches and Register. The
        Vector Clear Enable Flip-Flop indicates if the last vector read
        was from this group. When it is set, it enables the Clear
        Control Logic.

        The clock (CP) signal is used to clock the Interrupt Register,
        Mask Register, Status Register, Vector Hold Register, and the
        Lowest Group Enabled, Vector Clear Enable and Status
        Overflow Flip-Flops, all on the clock LOW-to-HIGH transition.


        NOTE: The AM2914 documentation references LGE flip-flop, which is the "Lowest Group Enabled Flip-Flop"
              This is not in use in the BUFALU interface, and hence ignored.
     

        Input Pins	            Description
        ----------------------  -------------------------------------------------------------------------------------------------------
        Interrupt Requests	    The eight interrupt request inputs, IRQ0 to IRQ7. Active LOW
        Interrupt Enable	    Active low input, indicates that the interrupt enable flip-flop is set
                
        
        
        Output Pins	            Description
        ----------------------  -------------------------------------------------------------------------------------------------------
        Interrupt Request	    Active high outputs, indicate that an interrupt has been requested
        
        Group Signal (/GS)	    This signal is the output of the Lowest Group Enabled flip-flop and is used during a Read Status microinstruction to generate the high-order bits of the Status word.
        Status Overflow	        Active low output, indicates that the status register has overflowed
        Ripple Disable	        Active low output, disables the interrupt request output of the lower priority group
        Parallel Disable	    Active low output, disables the interrupt request output of all lower priority groups
        Vector Hold	Active high output, holds the vector register output until the next vector read instruction

     */
    public class Am2914
    {
        #region ENUMs
        /// <summary>
        /// The sixteen micro instructions of the Am2914, selected by the I3-I0 instruction inputs (table 1 of the datasheet).
        /// </summary>
        public enum Am2914Instruction
        {
            /// <summary>
            /// MCLR - Master Clear
            /// Clear Regs and enable interrupts
            /// </summary>
            MCLR,

            /// <summary>
            /// CLRIN - "Clear all Interrupts" clears the Interrupts latches and registers
            /// </summary>
            CLRIN,

            /// <summary>
            /// CLRMB - "Clear Interrupts from M-Bus data" clears those Interrupt Latches and Register bits which have corresponding M-Bus bits set equal to one
            /// Selective clear
            /// </summary>
            CLRMB,

            /// <summary>
            /// CLRMR - "Clear Interrupts from mask register" clears those Interrupt Latches and Register bits which have the corresponding Mask Register Bits set to one.
            /// Selective clear (M bus floating)
            /// </summary>
            CLRMR,

            /// <summary>
            /// CLRVC - Clear interrupt for last vect read
            /// Specific clear or clear on service
            /// </summary>
            CLRVC,

            /// <summary>
            /// RDVC - Read interrupt vector
            /// 
            /// Read Vector of highest priority
            /// Sets status fence
            /// Sets up clear last instruction
            /// </summary>
            RDVC,

            /// <summary>
            /// RDSTA - Read Status Register
            /// Read status fence
            /// </summary>
            RDSTA,

            /// <summary>
            /// RDM - Read MASK register
            /// (ND Microcode: PIC.RMSK)
            /// 
            /// Read enable pattern
            /// </summary>
            RDM,

            /// <summary>
            /// SETM - Set mask reg: inhibit/disable all interrupts
            /// 
            /// Bits where M-BUS = 1 is affected
            /// </summary>
            SETM,

            /// <summary>
            /// LDSTA - Load Status
            /// </summary>
            LDSTA,

            /// <summary>
            /// BCLRM - Bit clear mask register
            /// 
            /// Clear where M-BUS = 1
            /// </summary>
            BCLRM,

            /// <summary>
            /// BSETM - Bit set mask register 
            /// </summary>
            BSETM,

            /// <summary>
            /// Clear Mask Register - Enable all interrupts
            /// </summary>
            CLRM,

            /// <summary>
            /// DISIN - Disable Interrupt Request
            /// 
            /// Disable Interrupts and reset interrupt-request enable F/F
            /// </summary>
            DISIN,

            /// <summary>
            /// LDM - Load mask register 
            /// 
            /// Load entire register
            /// </summary>
            LDM,

            /// <summary>
            /// ENIN - Enable interrupt request
            /// 
            /// Set enable Flip-Flop
            /// </summary>
            ENIN
        }


        #endregion ENUMs

        /// <summary>
        /// Creates a chip in its power-up state: nothing pending, everything masked, status fence at zero.
        /// </summary>
        public Am2914()
        {
            MasterClear();
        }

        /// <summary>
        /// Immutable snapshot of every internal register/flip-flop of one Am2914 chip,
        /// for the PIC command-trace instrumentation (InterruptSubSystem.OnPicExec).
        /// All the fields a gate-level Verilog port needs to verify its wiring against:
        /// status fence, mask, interrupt-detect, priority/vector, vector-hold, and the
        /// four flip-flops (vector-clear, status-overflow, IRQ-enable, group-enable),
        /// plus the two derived outputs (IRQ, vector-claim). Active-LOW signals are
        /// captured raw (as the chip stores them); the trace formatter documents the sense.
        /// </summary>
        public readonly struct Snapshot
        {
            /// <summary>
            /// The status register, the 3-bit fence below which interrupts are not taken.
            /// </summary>
            public readonly byte Status;        // StatusRegister (3-bit fence)
            /// <summary>
            /// The mask register; active low, a 1 masks the level.
            /// </summary>
            public readonly byte Mask;          // MaskRegister (active LOW: 1=masked)
            /// <summary>
            /// The interrupt detect register; active low, a 0 means a request is pending.
            /// </summary>
            public readonly byte Detect;        // InterruptDetectRegister (active LOW: 0=pending)
            /// <summary>
            /// The priority encoder output: the highest unmasked pending level.
            /// </summary>
            public readonly byte Priority;      // PriorityEncoder output (highest unmasked level)
            /// <summary>
            /// The latched 3-bit vector output.
            /// </summary>
            public readonly byte Vector;        // VectorOutput (latched 3-bit vector)
            /// <summary>
            /// The vector holding register.
            /// </summary>
            public readonly byte VectorHold;    // VectorHoldingRegister
            /// <summary>
            /// The vector clear flip-flop: true when this chip supplied the last vector.
            /// </summary>
            public readonly bool VectorClearFF; // VectorClear_FlipFlop (this chip supplied last vector)
            /// <summary>
            /// The status overflow output; active low, false means overflowed or disabled.
            /// </summary>
            public readonly bool StatusOvfFF;   // Output_StatusOverflow (active LOW: false=overflowed/disabled)
            /// <summary>
            /// The interrupt request enable flip-flop.
            /// </summary>
            public readonly bool IrqEnableFF;   // interrupt-request-enable flip-flop
            /// <summary>
            /// The group enable output (LGE); active low.
            /// </summary>
            public readonly bool GroupEnableFF; // Output_GroupSignal / LGE (active LOW)
            /// <summary>
            /// The interrupt request output; active low, false means asserted.
            /// </summary>
            public readonly bool Irq;           // Output_InterruptRequest (active LOW: false=asserted)
            /// <summary>
            /// The vector claim output; active low, false means this chip wins the vector.
            /// </summary>
            public readonly bool VectorClaim;   // Output_VectorClaim (active LOW: false=this chip wins)

            /// <summary>
            /// Captures one set of register and output values.
            /// </summary>
            /// <param name="status">
            /// The status register.
            /// </param>
            /// <param name="mask">
            /// The mask register.
            /// </param>
            /// <param name="detect">
            /// The interrupt detect register.
            /// </param>
            /// <param name="priority">
            /// The priority encoder output.
            /// </param>
            /// <param name="vector">
            /// The latched vector.
            /// </param>
            /// <param name="vectorHold">
            /// The vector holding register.
            /// </param>
            /// <param name="vectorClearFF">
            /// The vector clear flip-flop.
            /// </param>
            /// <param name="statusOvfFF">
            /// The status overflow output.
            /// </param>
            /// <param name="irqEnableFF">
            /// The interrupt request enable flip-flop.
            /// </param>
            /// <param name="groupEnableFF">
            /// The group enable output.
            /// </param>
            /// <param name="irq">
            /// The interrupt request output.
            /// </param>
            /// <param name="vectorClaim">
            /// The vector claim output.
            /// </param>
            public Snapshot(byte status, byte mask, byte detect, byte priority, byte vector,
                byte vectorHold, bool vectorClearFF, bool statusOvfFF, bool irqEnableFF,
                bool groupEnableFF, bool irq, bool vectorClaim)
            {
                Status = status; Mask = mask; Detect = detect; Priority = priority; Vector = vector;
                VectorHold = vectorHold; VectorClearFF = vectorClearFF; StatusOvfFF = statusOvfFF;
                IrqEnableFF = irqEnableFF; GroupEnableFF = groupEnableFF; Irq = irq; VectorClaim = vectorClaim;
            }
        }

        /// <summary>Captures this chip's full internal state (see Snapshot).</summary>
        public Snapshot Capture()
        {
            return new Snapshot(
                StatusRegister, MaskRegister, InterruptDetectRegister, PriorityEncoder, VectorOutput,
                VectorHoldingRegister, VectorClear_FlipFlop, Output_StatusOverflow,
                InterruptRequestEnabled_FlipFlop, Output_GroupSignal, Output_InterruptRequest, Output_VectorClaim);
        }

        /// <summary>
        /// Trigger interrupt from external device
        /// </summary>
        /// <param name="interrupt">Interrupt level</param>
        public void SetInterrupPINE(InterruptPin interrupt)
        {
            InterruptDetectRegister &= (byte)~(1 << (int)interrupt);
        }

        /// <summary>
        /// Asserts one of the eight interrupt input pins, which sets the matching bit in the detect register on the next clock.
        /// </summary>
        /// <param name="interrupt">
        /// The pin, 0 to 7.
        /// </param>
        public void SetInterrupPIN(int interrupt)
        {
            InterruptDetectRegister &= (byte)~(1 << interrupt);
        }


        /// <summary>
        /// Clear interrupt from external device
        /// </summary>
        /// <param name="interrupt">Interrupt level</param>
        public void ClearInterruptPINE(InterruptPin interrupt)
        {
            InterruptDetectRegister |= (byte)(1 << (int)interrupt);
        }

        /// <summary>
        /// Releases one of the eight interrupt input pins.
        /// </summary>
        /// <param name="interrupt">
        /// The pin, 0 to 7.
        /// </param>
        public void ClearInterruptPIN(int interrupt)
        {
            InterruptDetectRegister |= (byte)(1 << interrupt);
        }


        /// <summary>
        /// Output from 8-input priority encoder
        /// Input to 3-bit comparator and incrementer (and the 3 bit vector output pins)
        /// </summary>
        public void RecalcPriorityEncoder()
        {
            // Recalculate Interrupt Detect

            byte detectedBits = (byte)(~_interruptDetectRegister);
            detectedBits |= _pinsTiedToGround;


            byte activeBits = (byte)(detectedBits & ~_maskRegister); // active low, invert first            
            if (activeBits == 0)
            {
                _interruptDetected = false;
                _priorityEncoder = 0;
                return;
            }

            // The Interrupt Detect Circuitry detects any unmasked Interrupt Input. 
            _interruptDetected = true;


            // Recalc 8-input priority encoder (3 bit)            
            for (int i = 7; i >= 0; i--)
            {
                if ((activeBits & 1 << i) != 0)
                {
                    _priorityEncoder = (byte)i;
                    break;
                }
            }
        }


        /// <summary>
        /// Set by RDVC (Read Vector)
        /// 
        /// The Vector Clear Enable Flip-Flop indicates if the last vector read was from this chip. 
        /// When it is set, it enables the Clear Control Logic.
        /// </summary>
        internal bool VectorClear_FlipFlop { get; set; }

        /// <summary>
        /// Interrupt pending flip_flop
        /// 
        /// Active HIGH
        /// </summary>
        public bool _interruptDetected { get; internal set; }

        /// <summary>
        /// Extern API to clear pending IRQ
        /// </summary>
        public void ClearPendingIRQ()
        {
            _interruptDetected = false;
        }

        #region Internal variables

        /// <summary>
        /// Priority Encoder (automatic updated when Mask or Interrupt register is updated)
        /// </summary>
        public byte PriorityEncoder { get { return _priorityEncoder; } }
        byte _priorityEncoder;

        /// <summary>
        /// Return the latched Vector value
        /// Vector output 4 bits (8 interrupts)
        /// </summary>
        public byte VectorOutput { get { return (byte)(_vectorOutput & 0x07); } }
        byte _vectorOutput;


        /// <summary>
        /// Interrupt Detected Register. 8 bits
        /// Active LOW
        /// </summary>
        public byte InterruptDetectRegister
        {
            get { return _interruptDetectRegister; }
            set { _interruptDetectRegister = ((byte)(value & 0xFF)); RecalcPriorityEncoder(); }
        }
        byte _interruptDetectRegister;


        /// <summary>
        /// Call this after the Chip has been created if you need to tie some Interrupt pins to GND.
        /// bits with value 1 will be tied.
        /// </summary>
        /// <param name="bits"></param>
        public void SetBitsTiedToGround(byte bits)
        {
            _pinsTiedToGround = bits;
        }
        byte _pinsTiedToGround = 0;
        /// <summary>
        /// Current value on SBUS (Status Bus)
        /// </summary>
        internal byte _statusBus;
        /// <summary>
        /// The value on the status bus (S bus), as latched by the last LatchStatusBus.
        /// </summary>
        public byte SBUS { get => _statusBus; }

        /// <summary>
        /// Status register
        /// 
        /// During an Vector Read, the Incrementer increments the Interrupt Vector by one, and the result is clocked into the Status Register.
        /// Thus the Status Register always points to the lowest level at which an interrupt will be accepted.
        /// </summary>
        internal byte _statusRegister;

        /// <summary>
        /// The three-bit Status Register holds the status bits and may be loaded from or read to the S Bus. 
        /// During a Vector Read, the Incrementer increments the Interrupt Vector by one, and the result is clocked into the Status Register. 
        /// Thus the Status  Register always points to the lowest level at which an interrupt will be accepted.
        /// </summary>
        public byte StatusRegister
        {
            get { return _statusRegister; }
        }

        /// <summary>
        /// Vector hold register
        /// </summary>
        public byte VectorHoldingRegister
        {
            get { return _vectorHoldingRegister; }
        }

        internal byte _vectorHoldingRegister;

        /// <summary>
        /// Interrupt request enabled
        /// </summary>
        internal bool _interruptRequestEnableFlipFlop;
        /// <summary>
        /// The interrupt request enable flip-flop; true when the chip may raise its interrupt request output.
        /// </summary>
        public bool InterruptRequestEnabled_FlipFlop
        {
            get => _interruptRequestEnableFlipFlop;
        }

        #endregion Internal variables

        #region CHIP interface

        /// <summary>
        /// Mask Register. 8 bits. 1=Masked, 0=Enabled
        /// Active LOW
        /// Public access for Debugger to read
        /// </summary>
        public byte MaskRegister
        {
            get { return _maskRegister; }
            set { _maskRegister = value; RecalcPriorityEncoder(); }
        }
        byte _maskRegister;


        /// <summary>
        /// Latch the 3 bits of the status bus
        /// </summary>
        /// <param name="bits"></param>
        public void LatchStatusBus(byte bits)
        {
            _statusBus = (byte)(bits & 0x07);
        }

        /// <summary>
        /// Reads the status bus, the 3-bit status register driven out during a read status instruction.
        /// </summary>
        /// <returns>
        /// The 3-bit status.
        /// </returns>
        public byte ReadStatusBus()
        {
            if (_interruptDisable == false)  // Active low
            {
                return 0; // No interrupts detected when disabled
            }

            return _statusBus;
        }


        /// <summary> 
        /// Current value on MBUS (Mask Bus)
        /// </summary>
        internal byte _maskBus;


        /// <summary>
        /// The value on the mask bus (M bus), as latched by the last LatchMaskBus.
        /// </summary>
        public byte MBUS { get => _maskBus; }

        /// <summary>
        /// Puts a value on the mask bus, for the next instruction that loads or modifies the mask register.
        /// </summary>
        /// <param name="bits">
        /// The 8-bit mask value.
        /// </param>
        public void LatchMaskBus(byte bits)
        {
            _maskBus = bits;
        }

        /// <summary>
        /// Reads the mask bus, the mask register driven out during a read mask instruction.
        /// </summary>
        /// <returns>
        /// The 8-bit mask.
        /// </returns>
        public ushort ReadMaskBus()
        {
            return _maskBus;
        }

        /// <summary>
        /// /IR  => /InterruptRequest
        /// 
        /// Output pin.
        /// Active LOW
        /// 
        /// Pin 9
        /// </summary>        
        public bool Output_InterruptRequest
        {
            get
            {
                //Utils.Log($"[Read-IRQ: P:{_interruptDetected} E:{_interruptRequestEnableFlipFlop}]");

                if (_interruptDisable == false) return true; // If chip is disabled, return "NO INTERRUPT DETECTED";
                                                             //if (_groupEnable_FlipFlop == true) return true; // If GROUP_ENABLE is HIGH (active low), means we are disabled. So no Interrupt signals

                if (_statusRegister > _priorityEncoder) return true; // No interrupt if the STATUS register is higher than the caculated interrupt vector

                if (_interruptDetected & _interruptRequestEnableFlipFlop) return false;
                return true;
            }
        }


        /// <summary>
        /// Called from HW when there is an interrupt to clear specific interrupt bits
        /// </summary>
        /// <param name="bits"></param>
        public void ClearInterruptDetectBits(byte bits)
        {
            InterruptDetectRegister |= bits;
        }

        /// <summary>
        /// A none existent instruction in the Chip, but is implemented in HW around it on the ND-100 to set bits in the interrupt detect)
        /// Sets the Interrupt detected (ie set bit =0) for the incoming bits where the bits are 1
        /// </summary>
        /// <param name="bits"></param>
        public void SetInterruptDetectBits(byte bits)
        {
            InterruptDetectRegister = (byte)(InterruptDetectRegister & ~bits);
        }

        /// <summary>
        /// Executes one micro instruction, as the chip does on the rising clock edge with the instruction on I3-I0.
        /// </summary>
        /// <param name="instruction">
        /// The instruction to execute.
        /// </param>
        public void ExecuteInstruction(Am2914Instruction instruction)
        {
            switch (instruction)
            {
                case Am2914Instruction.MCLR:
                    MasterClear();
                    break;
                case Am2914Instruction.CLRIN:
                    ClearAllInterrupts();
                    break;
                case Am2914Instruction.CLRMB:
                    ClearInterruptsDetectedRegister();
                    break;
                case Am2914Instruction.CLRMR:
                    ClearInterruptsFromMaskRegister();
                    break;
                case Am2914Instruction.CLRVC:
                    ClearInterruptForLastVectorRead();
                    break;
                case Am2914Instruction.RDVC:
                    ReadVector();
                    break;
                case Am2914Instruction.RDSTA:
                    ReadStatusRegister();
                    break;
                case Am2914Instruction.RDM:
                    ReadMaskRegister();
                    break;
                case Am2914Instruction.SETM:
                    SetMaskRegister();
                    break;
                case Am2914Instruction.LDSTA:
                    LoadStatus();
                    break;
                case Am2914Instruction.BCLRM:
                    BitClearMaskRegister();
                    break;
                case Am2914Instruction.BSETM:
                    BitSetMaskRegister();
                    break;
                case Am2914Instruction.CLRM:
                    ClearMaskRegister();
                    break;
                case Am2914Instruction.DISIN:
                    DisableInterruptRequest();
                    break;
                case Am2914Instruction.LDM:
                    LoadMaskRegister();
                    break;
                case Am2914Instruction.ENIN:
                    EnableInterruptRequest();
                    break;
                default:
                    break;
            }

        }


        #endregion CHIP interface

        // ==============================================================================
        // Internal functions ordered identical to "Table 1", page 6 in the AM2914 PDF
        // ==============================================================================

        #region Mask Register Functions

        /// <summary>
        /// LDM
        /// MicroInstruction: 1110 / 14
        /// 
        /// Load Mask Register from M-bus
        /// </summary>
        private void LoadMaskRegister()
        {
            MaskRegister = _maskBus;
        }



        /// <summary>
        /// RDM
        /// MicroInstruction: 0111 / 7
        /// 
        /// Read Mask Register to M bus
        /// </summary>
        /// <returns></returns>
        private void ReadMaskRegister()
        {
            _maskBus = MaskRegister;
        }

        /// <summary>
        /// CLRM
        /// MicroInstruction: 1100 / 12
        /// 
        /// Clear Mask Register (Enables all priorities)
        /// </summary>
        private void ClearMaskRegister()
        {
            MaskRegister = 0x00;
        }


        /// <summary>
        /// SETM
        /// MicroInstruction: 1000 / 9
        /// 
        /// The "Set mask register" microinstruction sets all the bits in the Mask Register to one. 
        /// This results in all interrupts being inhibited.
        /// </summary>
        private void SetMaskRegister()
        {
            MaskRegister = 0xFF;
        }



        /// <summary>
        /// BCLRM
        /// MicroInstruction: 1010 / 10
        /// 
        /// Bit clear mask register from Mbus
        /// (Clear those bits which have corresponding M-bus bits equal to one)
        /// </summary>
        private void BitClearMaskRegister()
        {

            MaskRegister &= (byte)~_maskBus;
        }

        /// <summary>
        /// BSETM
        /// MicroInstruction: 1011 / 11
        /// 
        /// Bit set mask register from MBus
        /// (Sets those bits which have corresponding M-bus bits equal to one)
        /// </summary>
        private void BitSetMaskRegister()
        {
            MaskRegister |= _maskBus;
        }
        #endregion Mask Register Functions


        #region Status Register Functions

        /// <summary>
        /// LDSTA
        /// MicroInstruction: 1001 / 9
        /// 
        /// Load status register from S bus 
        /// </summary>
        private void LoadStatus()
        {
            _statusOverflow = true; // active low - so true = disabled
            _statusRegister = _statusBus;
            RecalcPriorityEncoder();

        }

        /// <summary>
        /// RDSTA
        /// MicroInstruction: 0110 / 6
        /// 
        /// Read Status Register to S bus
        /// </summary>
        /// <returns></returns>
        private void ReadStatusRegister()
        {
            RecalcGroupSignal();
            _statusBus = _statusRegister;
        }

        #endregion Status Register Functions

        #region Interrupt Request Control

        /// <summary>
        /// ENIN
        /// MicroInstruction: 1111 / 15
        /// 
        /// Enable Interrupt Request
        /// </summary>
        private void EnableInterruptRequest()
        {
            _interruptRequestEnableFlipFlop = true;
            RecalcPriorityEncoder();
        }


        /// <summary>
        /// DISIN
        /// MicroInstruction: 1101 / 13
        /// 
        /// Disable Interrupt Request
        /// </summary>
        private void DisableInterruptRequest()
        {
            _interruptRequestEnableFlipFlop = false;
        }


        #endregion Interrupt Request Control




        #region Vectored Output
        /// <summary>
        /// RDVC
        /// MicroInstruction: 0101 / 5
        /// 
        /// Read vector output to V outputs, load V+1 into status register, load V into vector hold register and set "vector clear enable" flip-flop
        /// </summary>
        /// <returns></returns>
        private void ReadVector()
        {

            _gas = CalculateGAS(_interruptRequestEnableFlipFlop, _interruptDetected, _interruptDisable);


            RecalcPriorityEncoder();

            // VECTOR CLAIM during Read Vector: identifies whether THIS chip holds the
            // winning (unmasked, above-fence, enabled) pending request being vectored - the
            // ND-110 microcode reads both chips' claim flags as vector bits 3/4 and
            // dispatches on them (hi claim => internal-interrupt path EXT14/level 14, lo
            // claim => device levels 10-13). Before 2026-07-11 this was derived from
            // _groupSignal, which only mirrored the Group Enable input latched by the last
            // Load Status: after the IIC fence-scan ended with LOSTS 000000 (both GE
            // asserted) a lone RTC request on the LOW chip was reported with the HIGH claim
            // asserted + vector 0 -> the microcode took EXT14, entered level 14 with no
            // internal fault pending, and TRA IIC hung forever (INSTRUCTION-VERIFY "0!"
            // hang, traced 2026-07-11).
            // Output_InterruptRequest is active LOW and already combines _interruptDetected
            // (detect AND NOT mask), the status fence comparison, the interrupt-request
            // enable flip-flop, and Input_InterruptDisable (wired by PropagateSignals from
            // the higher-priority chip's Ripple Disable) - exactly the "this chip wins"
            // condition. Latch it BEFORE the status auto-increment below so the decision
            // uses the pre-read fence.
            // NOTE: must NOT be latched into _groupSignal - that is the Lowest Group
            // Enabled flip-flop, whose GE-latch semantics InterruptSubSystem.ReadIRQ()'s
            // cascade group gating depends on. A first version that overwrote _groupSignal
            // here silenced the low chip's /IRQ contribution after unrelated vector reads,
            // stalling INSTRUCTION-VERIFY's interrupt-driven banner printing mid-line.
            _vectorClaim = Output_InterruptRequest; // both active LOW: asserted = this chip supplies the vector

            if (_interruptDisable == false)
            {
                _vectorOutput = 0;
                return; // We are disabled
            }

            _vectorOutput = PriorityEncoder;
            // if the calculated vector is higher in priority than the current vector held in the vector hold register,
            // the chip updates the vector hold register with the new vector.
            //if (_vectorOutput > _vectorHoldingRegister)
            if (_vectorOutput >= _statusRegister)
            {
                _vectorHoldingRegister = _vectorOutput;
            }


            if (_vectorOutput == 7)
            {
                // Interrupt 7 is detected, set the status overflow
                _statusOverflow = false; // active low
            }


            if (_interruptDetected)
            {
                _statusRegister = (byte)(_vectorHoldingRegister + 1);
                VectorClear_FlipFlop = true;
            }
        }
        #endregion Vectored Output

        #region Priority Interrupt Register Clear





        /// <summary>
        /// CLRVC
        /// MicroInstruction: 0100 / 4
        /// 
        /// Clear the individual interrupt associated with the last vector read (=set its bit to 1)
        /// </summary>
        private void ClearInterruptForLastVectorRead()
        {
            if (VectorClear_FlipFlop == false) return;

            InterruptDetectRegister |= (byte)(1 << _vectorHoldingRegister);

            VectorClear_FlipFlop = false;
        }

        /// <summary>
        /// CLRMR
        /// MicroInstruction: 0011 / 3
        /// 
        /// The "Clear interrupts from Mask register" microinstruction clears those Interrupt Latches and Register bits 
        /// which have a corresponding Mask Register bits set equal to one
        /// 
        /// The Mbus is used during execution
        /// </summary>
        private void ClearInterruptsFromMaskRegister()
        {
            _maskBus = MaskRegister;
            ClearInterruptsDetectedRegister();
        }

        /// <summary>
        /// MCLRMPID
        /// MicroInstruction: 0010 / 2
        /// 
        /// Clear interrupts from M-bus
        /// 
        /// Clear the bits which have corresponding M-bus bits equal to one
        /// (which is setting its bit to 1)
        /// </summary>
        private void ClearInterruptsDetectedRegister()
        {
            InterruptDetectRegister |= _maskBus;
        }


        /// <summary>
        /// CLRIN
        /// MicroInstruction: 0001 / 1
        /// 
        /// Clear all Interrupts
        /// </summary>
        private void ClearAllInterrupts()
        {
            //The PIC command CLRIN clear the interrupt latches and registers
            InterruptDetectRegister = 0xFF; // Interrupt detection is Active low, so set all high means all is cleared
        }




        #endregion Priority Interrupt Register Clear

        #region Master Clear
        /// <summary>
        /// MCLR - MasterClear
        /// MicroInstruction: 0000 / 0
        /// 
        /// Clear all interrupts, clear mask register, clear status register, enable interrupt request.
        /// </summary>
        private void MasterClear()
        {
            // Clear "Vector output pins"
            _vectorOutput = 0;

            // Clear Detect and Mask registers
            _interruptDetectRegister = 0xFF; // Active low, clear means set to HIGH (no interrupts pending)
            // BUG FIX (2026-07-11): Mask register uses the OPPOSITE polarity - 1=masked/inhibited,
            // 0=enabled (see SetMaskRegister/ClearMaskRegister below, and the AMD datasheet: "Clear
            // Mask Register" instruction (CLRM) sets all bits to 0 and "results in all priorities
            // [being] enabled"; Master Clear uses the same "clear" semantics for the mask register).
            // This previously set 0xFF here, which - using this file's own mask polarity - means
            // "all interrupts inhibited immediately after reset", the opposite of real hardware.
            _maskRegister = 0x00;

            // Clear status and holding register
            _statusRegister = 0x0;
            _vectorHoldingRegister = 0;
            _statusOverflow = true; // Status overflow disabled

            // Set Interrupt Request Enable to active
            _interruptRequestEnableFlipFlop = true;

            // Clear pending IRQ
            _interruptDetected = false;

            // Pin 6, default set chip to "not disabled".
            _interruptDisable = true; // low = disabled, high=not disabled


            #region For cascading

            // Recalc Group Advanced Send
            //RecalcGas();
            _gas = true;
            // 
            // The Group Advance Receive and Group Enable inputs should be connected to ground so that the Lowest Group Enabled flip-flop is forced
            // LOW during a Master Clear or Load Status microinstruction.
            RecalcGroupSignal();

            // Status Overflow should be connected to the Interrupt Disable to disable interrupts when vector seven is read.
            // The Group Advance Send, Ripple Disable, Group Signal, and Parallel Disable pins should be left open.
            #endregion For cascading



        }

        #endregion Master Clear

        #region Group 

        /// <summary>
        /// /GE  => /Group Enable
        /// (Active LOW)
        /// 
        /// Input to the lowest Group Enable flip-flop
        /// </summary>
        public bool Input_GroupEnable
        {
            set
            {
                _groupEnable_FlipFlop = value;
                RecalcGroupSignal();
            }
        }

        /// <summary>
        /// Group Enable - Active LOW
        /// </summary>
        private bool _groupEnable_FlipFlop;


        /// <summary>
        /// According to a truth table generated by ChatGPT after reading the spec, this is the way
        /// </summary>
        void RecalcGroupSignal()
        {
            // TODO: Validate this!!!


            if ((_groupEnable_FlipFlop == false))// && (_gar == false))
            {
                _groupSignal = false; // Active LOW
            }
            else
            {
                _groupSignal = true;
            }
        }


        /// <summary>
        /// Recalculate Group Advance Send
        /// 
        /// When the Group Advance Send (GAS) signal goes LOW during a Read Vector microinstruction, 
        /// it will remain LOW until the next Vector Read microinstruction is executed,
        /// or until the next Master Clear microinstruction is executed.
        /// 
        /// The GAS signal is used to transfer status information between cascaded groups in a multi-chip system, 
        /// and it goes LOW when the highest priority vector(vector seven) of the group is being read.
        /// 
        /// The GAS signal must be connected to the Group Advance Receive (GAR) input of the next higher group to transfer the status information. 
        /// When the next Vector Read microinstruction is executed in the current group, the GAS signal will go HIGH again, indicating that the current group has finished transferring status information to the next higher group.
        /// 
        /// Alternatively, if the next Master Clear microinstruction is executed in the current group, the GAS signal will also go HIGH, indicating that the current group has been reset and is ready to start a new interrupt cycle.
        /// </summary>
        void RecalcGas()
        {
            if (InterruptRequestEnabled_FlipFlop == false) // Interrupt request disabled
            {
                _gas = true;
            }
            else if (_interruptDisable == false) // all interrupts disabled, chip disabled..
            {
                _gas = false;
            }
        }

        /// <summary>
        /// Function to calculate GAS (Group Advance Send). Called upon during Read Vector
        /// </summary>
        /// <param name="IRE">Interrupt Request Enable </param>
        /// <param name="IR">Interrupt Request</param>
        /// <param name="ID">Interrupt Disable</param>
        /// <returns></returns>
        public bool CalculateGAS(bool IRE, bool IR, bool ID)
        {
            if (!IRE) // Interrupts are disabled in the current group
            {
                return true; // GAS is HIGH
            }
            else if (IR && !ID) // There is an unmasked interrupt request in the current group and interrupts are not disabled
            {
                return false; // GAS is LOW
            }
            else // Interrupts are enabled but there are no unmasked interrupt requests or interrupts are disabled
            {
                return true; // GAS is HIGH
            }
        }

        /// <summary>
        /// /GAR => /GroupAdvanceReceive
        /// Active LOW
        /// 
        /// Pin 4
        /// </summary>
        public bool Input_GroupAdvanceReceive
        {
            set
            {
                _gar = value;
                RecalcGroupSignal();
            }
        }
        private bool _gar;


        /// <summary>
        /// GroupAdvanceSend (internal)
        /// Active LOW
        /// The "GROUP ADVANCE SEND" is an output signal that indicates when the AM2914 has acknowledged an interrupt and 
        /// is sending a group advance signal to the next lower priority interrupt controller in the daisy chain.
        /// </summary>
        public bool Output_GroupAdvanceSend
        {
            get => _gas;
        }
        bool _gas;


        /// <summary>
        /// /SO => /StatusOverflow
        /// Pin 14
        /// 
        /// When set, it stays LOW until a Master Clear or Load Status microinstruction is executed.
        /// </summary>
        /// <returns></returns>
        public bool Output_StatusOverflow
        {
            get => _statusOverflow;
        }

        /// <summary>
        /// Status overflow (internal)
        /// Active LOW
        /// 
        /// 
        /// </summary>
        bool _statusOverflow;

        /// <summary>
        /// /RD => /RippleDisable
        /// Pin 7
        /// </summary>
        /// The "RIPPLE DISABLE" (RD) pin is an output signal on the AM2914 Priority Interrupt Controller (PIC). 
		/// This signal is used in a daisy-chained configuration of multiple AM2914 PICs, where each PIC is assigned a different priority level.
		/// 
		/// The purpose of the "RIPPLE DISABLE" signal is to inform the higher priority PIC in the chain that the current PIC has acknowledged an interrupt 
		/// request and is processing it.When the RD signal is activated, it prevents the higher priority PIC from acknowledging any new interrupt requests, 
		/// ensuring that the current PIC can handle its interrupt without being interrupted by higher priority requests.
		/// 
		/// The "RIPPLE DISABLE" signal is calculated based on the status of the interrupt requests and the internal state of the PIC.
		/// 
		/// The RD signal will be activated (low) under the following conditions:
		/// *  At least one of the interrupt request inputs (IRQ0-IRQ7) is active.
		/// *  The "Group Enable" (GE) input is active low(0).
		/// *  The "Group Advance Receive" (GAR) input is inactive high(1).
		/// *  The "Interrupt Disable" (DI) input is inactive high(1).
		/// 
		/// When these conditions are met, the RD output signal will be low(0), indicating that the current PIC has acknowledged an interrupt request and is processing it.
		/// If any of the conditions are not met, the RD signal will be inactive high (1), meaning that the current PIC is not processing an interrupt request, 
		/// and the higher priority PIC is free to acknowledge new requests.
		/// 
		/// In summary, the "RIPPLE DISABLE" pin is an output signal used to communicate the interrupt processing status between PICs in a daisy-chained configuration.
		/// It is calculated based on the state of the input pins and the internal status of the PIC.The RD signal is activated (low) 
		/// when the current PIC is processing an interrupt request and remains inactive (high) when no interrupt request is being processed.
		/// 
		/// The "RIPPLE DISABLE" (RD) output signal is connected to the "Interrupt Disable" (DI) input pin of the next higher priority PIC in the daisy chain. 
		/// When the RD signal is activated (low), it informs the higher priority PIC that the current PIC is processing an interrupt request, 
		/// which causes the DI input of the higher priority PIC to become active low (0), preventing the higher priority PIC from acknowledging 
		/// new interrupt requests until the lower priority PIC has completed processing its current interrupt.
		/// 
		/// <returns></returns>
		public bool Output_RippleDisable
        {
            get => CalculateRD(_interruptDisable, _groupEnable_FlipFlop, Output_InterruptRequest);
        }
        private bool _rippleDisable;
        /// <summary>
        /// Computes the ripple disable output from its inputs, as the datasheet logic diagram wires them.
        /// </summary>
        /// <param name="interruptDisable">
        /// The interrupt disable input.
        /// </param>
        /// <param name="lowestGroupEnabled">
        /// The group enable flip-flop of the lowest group.
        /// </param>
        /// <param name="interruptRequest">
        /// The interrupt request output.
        /// </param>
        /// <returns>
        /// The ripple disable output.
        /// </returns>
        public bool CalculateRD(bool interruptDisable, bool lowestGroupEnabled, bool interruptRequest)
        {
            _rippleDisable = true;

            if (
                (_interruptDetected) &&
                (_groupEnable_FlipFlop == false) && // "Group Enable" (GE) input is active low(0).
                (_gar == true) && // "Group Advance Receive" (GAR) input is inactive high(1).
                (_interruptDisable == true)  // "Interrupt Disable" (DI) input is inactive high(1).
                )
                _rippleDisable = false;

            //if (!interruptDisable || !lowestGroupEnabled || !interruptRequest)
            //{
            //	_rippleDisable= false; // RD is active (LOW)
            //}
            //else
            //{
            //	_rippleDisable= true; // RD is inactive (HIGH)
            //}

            return _rippleDisable; // inactive..
        }


        /// <summary>
        /// /GS => /Group Signal
        /// 
        /// (Active LOW)
        /// 
        /// Pin 3
        /// 
        /// The output of the Lowest Group Enabled flipflop
        /// </summary>
        public bool Output_GroupSignal
        {
            get => _groupSignal;
        }
        bool _groupSignal;

        /// <summary>
        /// Vector-claim flag latched by Read Vector (RDVC): active LOW - asserted (false)
        /// when THIS chip held the winning pending unmasked request at the moment of the
        /// vector read and therefore supplies the vector. Kept separate from
        /// Output_GroupSignal (the Lowest Group Enabled flip-flop), whose GE-latch
        /// semantics the /IRQ cascade group gating depends on - see the comment inside
        /// ReadVector().
        /// </summary>
        public bool Output_VectorClaim
        {
            get => _vectorClaim;
        }
        bool _vectorClaim = true; // inactive (no claim) until the first RDVC

        /// <summary>
        /// /ID -> /InterruptDisable
        /// Pin 6
        /// Active LOW
        /// </summary>
		public bool Input_InterruptDisable
        {
            get => _interruptDisable;
            set
            {
                _interruptDisable = value;
                RecalcIntrRequestAndGroupEnableLogic();
            }
        }


        /// <summary>
        /// Active low
        /// </summary>
        bool _interruptDisable;


        void RecalcIntrRequestAndGroupEnableLogic()
        {
            if (_interruptDisable == false)
                _rippleDisable = false;
        }
        #endregion Group 

    }
}
