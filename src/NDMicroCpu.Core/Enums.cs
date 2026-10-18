//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using static System.Reflection.Metadata.BlobBuilder;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// The enumerations shared by the whole emulator: CPU type, the micro word fields, the registers, the addressing modes and the interrupt pins.
    /// </summary>
    public class Enums
    {
        #region CPU Enums

        /// <summary>
        /// Which CPU the emulator runs as. The two differ in micro code (RASK or DELILAH), UART and panel.
        /// </summary>
        public enum CPUType
        {
            /// <summary>
            /// ND-110, the default. Runs the RASK micro code with the ND-110 UART.
            /// </summary>
            ND110 = 0, // default
            /// <summary>
            /// ND-120. Runs the DELILAH micro code with the 2661 UART.
            /// </summary>
            ND120
        }

        /// <summary>
        /// The command code field of the micro word (bits 32-36): the side operation a micro instruction performs besides its ALU work.
        /// </summary>
        public enum Commands
        {
            /// <summary>
            /// No command executed.
            /// </summary>
            NONE,

            /// <summary>
            /// Latch the upper byte of the STS register from the upper byte of IDB. 
            /// The new PIL will not be available as the A—operand until the next microinstruction has been completed.
            /// </summary>
            LDPIL,

            /// <summary>
            /// Load IDB into GPR. If a shift is specified in the same microinstruction, GPR will not be shifted, only loaded.
            /// </summary>
            LDGPR,

            /// <summary>
            /// Write IDB into the register file specified by the A and B operands
            /// </summary>
            EWRF,

            /// <summary>
            /// Force interrupt request to the cleared condition. This is used by the interrupt handler.
            /// </summary>
            CLIRQ,

            /// <summary>
            /// In ND-110, reset the data available signal from the UART.
            /// 
            /// Named CEUART in ND-120, with 4 subcommands in MIS
            /// </summary>
            RSDA,

            /// <summary>
            /// In ND-110, Transmit Buffert Strobe command to the UART on the CPU card
            /// Transfers the lower (DB byte to the UART output part)
            /// 
            /// 06,0 = TBSTR
            /// 06,0 = in ND-12, spare
            /// 06,1 = spare
            /// 06,2 = LDPANC
            /// 06,3 = LDPCR
            /// </summary>
            TBSTR,

            /// <summary>
            /// SIOC 
            /// Set I/O control. 
            ///
            /// Transfers IDB to control several functions. 
            /// </summary>
            SIOC,


            /// <summary>
            /// Command a "slow" microcycle
            /// </summary>
            SLOW,

            /// <summary>
            /// Enable Priority interrupt controller.
            /// The A-Operand specifices commadn to the PIC
            /// 
            /// Indicates that A operand should be a command to the PIC. The specifications of the PIC are included in Appendix C of ND-06.010.01 NORD-100 Microprogramming Description
            /// If the PIC command is a read command, other than read status, the PIC will enable the register onto IDB regardless of the microprogram bits 37 - 41 .
            /// </summary>
            EPIC,

            /// <summary>
            /// Set bits in the micro-PID
            /// 
            /// sets bits in the micro PlD register in the PlC. Bits can only be set to 1 by this command. (The PIC command MCLPID can reset bits to 0.)
            /// </summary>
            SMPID,

            /// <summary>
            /// Reset the Stop flip-flop
            /// 
            /// The CPU will start executing a main memory program.
            /// </summary>
            START,

            /// <summary>
            /// Set the Stop flip-flop
            /// 
            /// ND-100: Whenever a FETCH has been performed, the microprogram is forced to microaddress 3760 or 3770 by the interrupt hardware. The stop routine is executed.
            /// ND-110: When the next FETCH is performed, the microprogram is forced to microaddress 16 (panel interrupt).
            /// 
            /// COMM,SSTOP sets it → after the next COMM,CONTINUE completes one instruction, the pipeline check at FTCH1 sees COND, STP = true and enters the stop handler
            /// Used to single step the CPU, by setting this bit and then executing a single instruction with COMM,CONTINUE. After the instruction is executed, the CPU will be in STOP again.
            /// </summary>
            SSTOP,

            /// <summary>            
            /// Clear real—time interrupt. Every 20ms the real time clock will generate a panel interrupt, thereby entering the clock routine. This routine sends out the CLRTC command.
            /// </summary>
            CLRTC,


            /// <summary>
            /// Clear all flip-flops having special functions regarding floating point rounding indicator
            /// 
            /// ND-100: clears flip-flops having special functions regarding the floating point rounding indicator (T6). The DZD (double zero detect used in FDV) and the COD(one out detect used in FAD, FSB and FMU) flip-flops are cleared by CLFF.
            /// ND-110: Clear all flip—flops having special functions regarding the floating point rounding indicator (T6). The DZD (double zero detect used in FDV) and the OOD (one out detect used in FAD, FSB and FMU) flip-flops are cleared by CLFF.
            /// </summary>
            CLFF,

            /// <summary>
            /// Load the loop counter with the 6 lower bits of the IDB
            /// 
            /// loads the loop counter with the 6 lower bits of 108. The modified loop counter will not be available as A operand before in the microinstruction following the next microinstruction.
            /// </summary>
            LDLC,

            /// <summary>
            /// Load segment register / Load instruction register
            /// 
            /// 20,0: LDSEG
            /// This selects a physical 64K memory address area (address bits 16—23). The segment register is located in the MAC.
            /// 
            /// 
            /// 20,1: Spare
            /// 20,2: Spare
            /// 
            /// 20,3:  LDIRV 
            /// Load instruction register (MIC).
            /// </summary>
            Command20,

            /// <summary>
            /// Different commands depending on MIS register
            /// </summary>
            Command21,

            /// <summary>
            /// Different commands depending on MIS register
            /// 
            /// MIS=2 COMM,MAP
            /// </summary>
            Command22,

            /// <summary>
            /// Conditional jump
            /// </summary>
            CJMP,

            /// <summary>
            /// Conditional next
            /// </summary>
            CNEXT_24,

            /// <summary>
            /// Conditional next
            /// </summary>
            CNEXT_25,


            /// <summary>
            /// Macro jump
            /// </summary>
            JMP_26,

            /// <summary>
            /// Jump
            /// </summary>
            JMP_27,

            /// <summary>
            /// Read
            /// </summary>
            AREAD_30,

            /// <summary>
            /// Read
            /// </summary>
            AREAD_31,

            /// <summary>
            /// Write
            /// </summary>
            AWRITE_32,

            /// <summary>
            /// Write
            /// </summary>
            AWRITE_33,

            /// <summary>
            /// Read
            /// </summary>
            READ_34,

            /// <summary>
            /// Write
            /// </summary>
            WRITE_35,

            /// <summary>
            /// ADCS, RWCS,LCS, XSLOW
            /// </summary>
            Command_36,

            /// <summary>
            /// Ident, IOX(T)
            /// </summary>
            Command_37,

        }

        /// <summary>
        /// It specifies stack control(hold, pop, load, push)
        ///
        ///  TRUE instruction
        ///  * TS3-4, bit 28-29
        /// 
        ///  FALSE instruction (Same encoding but uses other bits )
        ///  * FS3-4, bit 0-1
        /// </summary>
        public enum StackControl
        {
            /// <summary>
            /// STACK OPERATION IS HOLD. USED WITH A COND SETTING
            /// </summary>
            Hold = 0,

            /// <summary>
            /// STACK OPERATION IS POP. USED WITH A COND SETTING
            /// </summary>
            Pop = 1,

            /// <summary>
            /// STACK OPERATION IS LOAD. USED WITH A COND SETTING
            /// </summary>
            Load = 2,

            /// <summary>
            /// STACK OPERATION IS PUSH. USED WITH A COND SETTING
            /// </summary>
            Push = 3,
        }

        /// <summary>
        /// The branch delay field of the micro word (bits 26-27): extra settling time the sequencer waits before taking a branch.
        /// </summary>
        public enum BranchDelay
        {
            /// <summary>
            /// No extra delay.
            /// </summary>
            Normal,
            /// <summary>
            /// One extra 25 ns step.
            /// </summary>
            ns25,
            /// <summary>
            /// Two extra 25 ns steps, first encoding.
            /// </summary>
            ns50a,
            /// <summary>
            /// Two extra 25 ns steps, second encoding.
            /// </summary>
            ns50b
        }

        /// <summary>
        /// It specifies the sequencer instruction(jump, return, next, repeat) 
        /// 
        ///  TRUE instruction
        /// * TS5-6, bit 30-21       
        /// 
        ///  FALSE instruction (Same encoding but uses other bits )
        ///  * FS5-6, bit 2-3
        /// </summary>
        public enum NextAddressSelect
        {
            /// <summary>
            /// Take the address in the micro word (or the vectored address when VECTOR is set).
            /// </summary>
            Jump,
            /// <summary>
            /// Pop the return address from the micro program stack.
            /// </summary>
            Return,
            /// <summary>
            /// Continue with the next micro address.
            /// </summary>
            Next,
            /// <summary>
            /// Execute the same micro instruction again.
            /// </summary>
            Repeat
        }



        /// <summary>
        /// The test object field: which signal a conditional micro instruction tests (SCOND and ECOND).
        /// </summary>
        public enum TestObjects
        {
            /// <summary>
            /// 0 - DZD
            /// Double Zero Detect
            /// 
            /// The double zero detect flip-flop is used to set TG correctly in FDV instructions.
            /// 
            /// </summary>
            DZD,

            /// <summary>
            /// 1 - LCZ
            /// Loop counter is 0. Used to terminate looping on an instruction
            /// </summary>
            LCZ,

            /// <summary>
            /// 2 - IRQ
            /// Used to test the interrupt request flip-flop, to see if the interrupts are pending
            /// </summary>
            IRQ,


            /// <summary>
            /// 3 - RESTR
            /// Used to test if the current program is running in restricted mode. 
            /// Instructions that are privileged should not execute when this condition is true.
            /// </summary>
            RESTR,

            /// <summary>
            /// 4 - FETCH
            /// Indicates wheter the last memory access was a fetch of a new macroinstruction. Used to generate correct P in case of page fault.
            /// </summary>
            FETCH,

            /// <summary>
            /// 5 - OOD
            /// The one out detect flip-flop is used to set TG (floating point rounding indicator) correctly in FAD, FSB and FMU instructions
            /// </summary>
            OOD,

            /// <summary>
            /// 6 - Spare
            /// Spare
            /// </summary>
            Spare,

            /// <summary>
            /// 7 - COND
            /// This condition latches the true/false result of the last microinstruction, so that it can be tested later if that is more convenient
            /// than testing it now. It will postpone the test as long as desired.
            /// </summary>
            COND,

            /// <summary>
            /// 8 - reserved
            /// Always false
            /// 
            /// In ND-100, it was used to detect if a single or multiple stored instructions has added *+1, and if true, the prefetched instruction must be discarded
            /// </summary>
            Reserved1,

            /// <summary>
            /// 9 - reserved
            /// 
            /// </summary>
            Reserved2,

            /// <summary>
            /// 10 - OVF
            /// Overflow from ALU. Indicates overflow during arithmetic operations
            /// 
            /// (IN ND-110 this is never used in the microcode ??)
            /// </summary>
            OVF,

            /// <summary>
            /// 11 - CRY
            /// Carry out from ALU
            /// </summary>
            CRY,


            /// <summary>
            /// 12 - F11
            /// Bit 11 from ALU
            /// </summary>
            F11,

            /// <summary>
            /// 13 - F15
            /// Bit 15 (sign bit) from ALU
            /// </summary>
            F15,

            /// <summary>
            /// 14 - ZF
            /// result = 0 from ALU
            /// </summary>
            ZF,

            /// <summary>
            /// 15 - STP
            /// Indicates the Stop flip-flop is set (i.e. no program running)
            /// </summary>
            STP
        }


        /// <summary>
        /// Bits 37-41
        /// This controls which source is selected onto the IDB
        /// 
        /// </summary>
        public enum IDBSource
        {
            /// <summary>
            /// 0
            /// ALU result or register pointed to by the A—operand (dependent on bits 60—63).
            /// </summary>
            ALU = 0,

            /// <summary>
            /// 1
            /// Bit—mask generator.
            /// 
            /// A 1 bit among zeros is enabled onto lDB. The position of the 1 bit is controlled by the A operand.
            /// </summary>
            BMG = 1,

            /// <summary>
            /// 2
            /// General purpose register.
            /// 
            /// The general purpose register is enabled onto IDB.
            /// </summary>
            GPR = 2,

            /// <summary>
            /// 3
            /// DB/cache read register.
            /// 
            /// The system bus data read register is enabled onto IDB. This must be used with the WAIT for data ready timing specification.
            /// </summary>
            DBR = 3,

            /// <summary>
            /// Microcode argument register.
            /// 
            /// The 16 lower bits of the microinstruction is enabled onto IDB. This makes it easy to generate 16 bit arguments within the microprogram.
            /// </summary>
            ARG = 4,

            /// <summary>
            /// Register  file, selected by  A and B operands
            /// 
            /// The register file address selected by the A and B operands is enabled onto lDB.
            /// </summary>
            REG = 5,

            /// <summary>
            /// 6
            /// Macro status register
            /// 
            /// The 16 bit STS register is enabled onto lDB.
            /// </summary>
            STS = 6,

            // 7 not used

            /// <summary>
            /// 10 (oct)
            /// B—operand as argument on bits 0—3. Values 0-17 B
            /// 
            /// The B operand is regarded as an argument and enabled onto IDB. In this way arguments between 0-17 may be generated.
            /// </summary>
            BARG = 8,

            /// <summary>
            /// 11 (oct)
            /// 
            /// Byte swap register (previous IDB contents are byteeswapped).
            /// 
            /// A byte swap circuit is enabled onto IDB. The two bytes present on IDB during the last cycle have exchanged positions.
            /// </summary>
            SWAP = 9,

            /// <summary>
            /// 12 oct
            /// 
            /// Parity Error address
            /// 
            /// The inverse of the PEA register is enabled onto IDB (ND100.. maybe inverse on 110 also?
            /// </summary>
            PEA,

            /// <summary>
            /// 13 oct
            /// 
            /// Parity Error status and Address
            /// 
            /// The inverse of the PES register is enabled onto IDB
            /// </summary>
            PES,

            /// <summary>
            /// 14 oct
            /// A—operand as argument on bits 3—6 (with bits 0-2=0) giving a range of values 0-170
            /// 
            /// The A operand is regarded as an argument shifted 3 bits left, and enabled onto IDB. in this way arguments between 0 -170 with 0 in the 3 lower bits may be generated
            /// </summary>
            AARG,

            /// <summary>
            /// 15 oct
            /// Internal status of the PIC.
            /// A read status command must be issued to the PIC.
            /// Simultaneously (command decode 11-EPIC)
            /// 
            /// The internal status of the PIC is enabled onto IDB. The RSTS command must be issued to the PIC simultaneously.
            /// </summary>
            PICS,


            /// <summary>
            /// 16 oct
            /// I/O register from UART etc
            /// 
            /// The register containing UART data and status is enabled onto lDB.
            /// </summary>
            IOR,

            /// <summary>
            /// 17 oct
            /// Nothing enabled onto the IDB by IDBS field
            /// 
            /// IDB is disabled. Read commands to the PIC may cause PIC to enable internal PIC registers onto IDB.
            /// </summary>
            none,

            /// <summary>
            /// 20 oct
            /// Panel status reguster (EPANS)
            /// </summary>
            MIPANS,

            /// <summary>
            /// 21 oct
            /// Panel status register (EPANS), and also panel status read bit (12) is reset
            /// </summary>
            MAPANS,

            /// <summary>
            /// 22 oct
            /// General purpose register, sign extended
            /// </summary>
            GPR_SEXT,

            /// <summary>
            /// 23 oct
            /// Paging status extended
            /// </summary>
            PGS,

            /// <summary>
            /// Oct 24
            /// Cache status register
            /// </summary>
            CSR,

            /// <summary>
            /// 25 oct
            /// 
            /// Paging control register
            /// </summary>
            PCR,

            /// <summary>
            /// 26 oct
            /// 
            /// Automatic load description and print-status
            /// </summary>
            ALD,

            /// <summary>
            /// 27 oct
            /// 
            /// Panel interrupt vector
            /// </summary>
            PANEL,

            /// <summary>
            /// 30 oct
            /// 
            /// Read control store (se also command decode 36,1 - RWCS)
            /// </summary>
            RCS,

            /// <summary>
            /// 31 oct
            /// PIC interrupt vector (see also command decode 11 - EPIC)
            /// </summary>
            PICVC,

            /// <summary>
            ///  32 oct
            /// </summary>
            spare1,

            /// <summary>
            /// 33 oct
            /// </summary>
            spare2,

            /// <summary>
            /// 34 oct 
            /// 
            /// In ND-110, LA (10-15), CA (0-9) update P in ALU jump
            /// In ND-120, space
            /// </summary>
            JMPA,

            /// <summary>
            /// 35 oct
            /// 
            /// Read installation number
            /// </summary>
            RINR,

            /// <summary>
            /// 36 oct
            /// 
            /// In ND110, spare.
            /// In ND120, read PIC mask register
            /// </summary>
            PICMASK,

            /// <summary>
            /// 37 oct
            /// 
            /// In ND-110, spare.
            /// In ND-120, read UART (see also command decode 5,x - UART)
            /// </summary>
            UART,
        }

        /// <summary>
        /// CIN - Carry In
        /// Bits 46-47
        /// </summary>
        public enum CIN
        {
            /// <summary>
            /// Carry is 0
            /// </summary>
            CarryIs0,
            /// <summary>
            /// Carry is 1
            /// </summary>
            CarryIs1,
            /// <summary>
            /// Carry is the C flip-flop of the STS register.
            /// </summary>
            CarryIsCFlipFlop,

            /// <summary>
            /// Carry is the GPR bit 0 (used in division).
            /// </summary>
            CarryIsGPRBit0
        }

        /// <summary>
        /// STSOperation
        /// Bits 53-54
        /// </summary>
        public enum STSOperation
        {
            /// <summary>
            /// Leave the status register as it is.
            /// </summary>
            HOLD_STS,
            /// <summary>
            /// Allow the carry (C), overflow (O) and dynamic overflow (Q) flags to be set from the ALU result.
            /// </summary>
            ENABLE_SET_COQ,
            /// <summary>
            /// Allow the multi-shift (M) flag to be set from the shifter.
            /// </summary>
            ENABLE_SET_M,
            /// <summary>
            /// Load the whole status register from the data bus.
            /// </summary>
            LOAD_ST
        }

        /// <summary>
        /// Bits 44-45: These modify bits 55-65, before they reach the ALU. See bits 55-65.
        /// </summary>
        public enum ND_ALUMode
        {
            /// <summary>
            /// Micro controlled mode.
            /// The ALU behaviour is controlled entirely from the microprogram
            /// </summary>
            uSHIFT,

            /// <summary>
            /// Multiply Mode (FMU)
            /// 
            /// The ALU behaviour is controlled from the microprogram, except that GPR bit 0 controls the 
            /// addition of the A—operand register or O.
            /// 
            /// This is only used in multiplication instructions.
            /// </summary>
            FMU,

            /// <summary>
            /// Divide Mode (FDV)
            /// 
            /// The ALU behaviour is controlled from the microprogram, except that GPR bit 0 controls 
            /// whether or not the A—operand register should be added or subtracted.  
            /// 
            /// This is only used in divide instructions.
            /// </summary>
            FDV,

            /// <summary>
            /// Shift Instruction (IR SHIFT)
            /// 
            /// The shift direction and shift mode is controlled from the loop counter together with bits 9—10 of the macroinstruction. 
            /// The extra microinstruction executed after the loop counter reaches 0 does not shift the ALU result
            /// </summary>
            IR_SHIFT
        }


        /// <summary>
        /// Depending on ALU Mode (bit 44-45)
        /// 
        /// ND-110 Extra logic that impacts how shifts are done in the ALU
        /// Must be handled outside the ALU by other HW in the real ND-110
        /// </summary>
        public enum ShiftMode
        {
            /// <summary>
            /// Arithmetic shift: the sign bit is kept and copied in.
            /// </summary>
            ARITHM = 0,
            /// <summary>
            /// Rotate: the bit shifted out comes back in at the other end.
            /// </summary>
            ROT = 1,
            /// <summary>
            /// Zero end input: zeros are shifted in.
            /// </summary>
            ZIN = 2,
            /// <summary>
            /// Link end input: the M flag is shifted in.
            /// </summary>
            LIN = 3,
            /// <summary>
            /// No shift.
            /// </summary>
            NONE = 4
        }
        #region AM2910 Enums



        /// <summary>
        /// ALUSource
        /// Bits 55-57
        /// Am2901: bits I2,I1 and I0. ALU Source Operands
        /// </summary>
        public enum ALUSource
        {
            /// <summary>
            /// Am2901 AQ
            /// </summary>
            A_Q,

            /// <summary>
            /// Am2901 AB
            /// </summary>
            A_B,

            /// <summary>
            /// Am2901 ZQ
            /// </summary>
            O_Q,

            /// <summary>
            /// Am2901 ZB
            /// </summary>
            O_B,

            /// <summary>
            /// Am2901 ZA
            /// </summary>
            O_A,

            /// <summary>
            /// Am2901 DA
            /// </summary>
            D_A,

            /// <summary>
            /// Am2901 DQ
            /// </summary>
            D_Q,

            /// <summary>
            /// Am2901 DZ
            /// </summary>
            D_O
        }


        /// <summary>
        /// ALUFunc
        /// Bits 58-60 in the 64 bit ucode
        /// 
        /// Am2901: Bits I5,I4 and I3. ALU Function
        /// </summary>
        public enum ALUFunc
        {
            /// <summary>
            /// ADD (R plus S)
            /// </summary>
            ADD,
            /// <summary>
            /// SUBR (S Minus R)
            /// </summary>
            SUBR,

            /// <summary>
            /// SUBS (R Minis S)
            /// </summary>
            SUBS,

            /// <summary>
            /// OR (R or S)
            /// </summary>
            OR,

            /// <summary>
            /// AND (R and S)
            /// </summary>
            AND,

            /// <summary>
            /// NOTRS (/R AND /S)
            /// </summary>
            NOTRS,

            /// <summary>
            /// EXOR (R EX-OR S)
            /// </summary>
            EXOR,

            /// <summary>
            /// EXNOR (R EX-NOR S)
            /// </summary>
            EXNOR
        }


        /// <summary>
        /// ALUDestination
        /// Bits 61-63 ALU Destintation
        /// Am2901: bits I8,I7 and I6. ALU Destination Control
        /// </summary>
        public enum ALUDestination
        {
            /// <summary>
            /// Am2901.QREG 
            /// Q:=F
            /// Y:=F
            /// </summary>
            ALUD_Q,

            /// <summary>
            /// Am2901.NOP 
            /// 
            /// Y:=F
            /// </summary>
            ALUD_NONE,

            /// <summary>
            /// Am2901.RAMA
            /// 
            /// Y:A
            /// </summary>
            ALUD_B_YA,

            /// <summary>
            /// Am2901.RAMF
            /// 
            /// Y:F
            /// </summary>
            ALUD_B,

            /// <summary>
            /// Am2901.RAMQD
            /// 
            /// Y:F
            /// F,Q/2 -> B,Q
            /// </summary>
            ALUD_SRD,

            /// <summary>
            /// Am2901.RAMD
            /// 
            /// Y:F
            /// F/2 -> B
            /// </summary>
            ALUD_SRB,

            /// <summary>
            /// Am2901.RAMQU
            /// 
            /// Y:F
            /// 2F,Q->B,Q
            /// </summary>
            ALUD_SLD,

            /// <summary>
            /// Am2901.RAMU
            /// 
            /// Y:F
            /// 2F->B 
            /// </summary>
            ALUD_SLB
        }
        #endregion AM2910 Enums

        /// <summary>
        /// ND-110 naming of internal registers in the internal ALU Registerblock (16* 16 bits registers)
        /// </summary>
        public enum RegisterEnum
        {
            /// <summary>
            ///  ZERO REG. , STATUS OR SCRATCH
            /// </summary>
            Zero = 0,

            /// <summary>
            /// D Register
            /// </summary>
            D,
            /// <summary>
            /// Program Counter
            /// </summary>
            P,
            /// <summary>
            /// B Register
            /// </summary>
            B,
            /// <summary>
            /// L Register
            /// </summary>
            L,
            /// <summary>
            /// A register
            /// </summary>
            A,
            /// <summary>
            /// T register
            /// </summary>
            T,
            /// <summary>
            /// X register
            /// </summary>
            X,

            /// <summary>
            /// STATUS OR SCRATCH
            /// (STS low bits)
            /// </summary>
            STS,

            // Internal registers

            /// <summary>
            /// R1 - ADDR. OR SCRATCH
            /// </summary>
            R1,

            /// <summary>
            /// R2 - SCRATCH
            /// </summary>
            R2,

            /// <summary>
            /// R3 - SCRATCH
            /// </summary>
            R3,

            /// <summary>
            /// R4 - SCRATCH
            /// </summary>
            R4,

            /// <summary>
            /// R5 - SCRATCH
            /// </summary>
            R5,

            /// <summary>
            /// R6 - SCRATCH
            /// </summary>
            R6,

            /// <summary>
            /// R7 - SCRATCH
            /// </summary>
            R7
        }
        #endregion CPU Enums

        #region Memory Addressing modes
        /// <summary>
        /// The addressing modes of a memory reference macro instruction (the X, I and B bits of the instruction word).
        /// </summary>
        public enum AdressingMode
        {
            /// <summary>
            /// ,X=0 I=0 ,B=0
            /// </summary>
            RelativeAdressing = 0,


            /// <summary>
            /// ,X=0  I=0 B=1
            /// </summary>
            BrelativeAddressing,

            /// <summary>
            /// ,X=0  I=1 ,B =0
            /// </summary>
            IndirectPrelativeAddressing,

            /// <summary>
            /// ,X=0  I=1 ,B=1
            /// </summary>
            IndirectBrelativeAddressing,


            /// <summary>
            /// ,X=1  I=0 ,B=0
            /// </summary>
            XrelativeAddressing,

            /// <summary>
            /// ,X=1  I=0 ,B=1
            /// </summary>
            BrelativeIndexedAddressing,

            /// <summary>
            /// ,X=1  I=1 ,B=0
            /// </summary>
            IndirectPrelativeIndexedAddressing,

            /// <summary>
            /// ,X=1  I=1 ,B=1
            /// </summary>
            IndirectBrelativeIndexedAddressing,

            /// <summary>
            /// No adressing mode
            /// </summary>
            Undefined
        }

        /// <summary>
        /// Which of the two page tables selected by the paging control register an access uses.
        /// </summary>
        public enum PageTable
        {
            /// <summary>
            /// The normal page table (PT bits of the paging control register).
            /// </summary>
            StandardPageTable,
            /// <summary>
            /// The alternative page table (APT bits), used by instructions that address the other memory space.
            /// </summary>
            AlternativePageTable
        }
        internal enum PageTableMode
        {
            Four,
            Sixteen

        }
        /// <summary>
        /// What a memory access does, checked against the page protection bits.
        /// </summary>
        public enum AccessMode
        {
            /// <summary>
            /// A data read.
            /// </summary>
            READ = 1,
            /// <summary>
            /// A data write.
            /// </summary>
            WRITE = 2,
            /// <summary>
            /// An instruction fetch.
            /// </summary>
            FETCH = 3,
            /// <summary>
            /// A read that is also an instruction fetch.
            /// </summary>
            READ_FETCH = READ + FETCH
        }


        /// <summary>
        /// When reading memory, what page tables to use
        /// </summary>
        public enum IndirectRead
        {
            /// <summary>
            /// Through the normal page table.
            /// </summary>
            PT,
            /// <summary>
            /// Through the alternative page table.
            /// </summary>
            APT
        }
        #endregion

        #region Events and interrupts

        //
        /// <summary>
        /// Enum keeping names for ND-100 pin names based on schematics.
        /// Maps to HW interrupt bits, which is different than expected based on PID bits
        /// </summary>
        public enum InterruptPin
        {
            /// <summary>
            /// Output Device
            /// </summary>
            INT10,

            /// <summary>
            /// Mass Storage
            /// </summary>
            INT11,

            /// <summary>
            /// Input Device
            /// </summary>
            INT12,

            /// <summary>
            /// Real Time Clock
            /// </summary>
            INT13,

            /// <summary>
            /// Pin 26 on Am2914 23AB in ND-100 schematics. Looks like its connected to something saying "II I0"..
            /// Some text mention this as "MC – Monitor Call" ?
            /// TODO: Research this more!
            /// </summary>
            UnknownPin26,

            /// <summary>
            /// MPV- Memory Protect Violation
            /// </summary>
            PROTV,

            /// <summary>
            /// PF -Page Fault
            /// </summary>
            PAGEF,

            /// <summary>
            /// ROR  or  II - Illegal Instruction 
            /// Two signals combined into pin 20 on the Am2914 23AB
            /// </summary>
            ROR_II,

            /// <summary>
            /// Z – Error Flag
            /// </summary>
            Z,

            /// <summary>
            /// PI -Privileged Instruction
            /// </summary>
            II6,

            /// <summary>
            /// IOX-  IOX Error
            /// </summary>
            IOXERR,

            /// <summary>
            /// PTY-Memory Parity Error
            /// </summary>
            PARERR,

            /// <summary>
            /// MOR-Memory out of range 
            /// </summary>            
            MOR,

            /// <summary>
            /// POW-Power fail
            /// </summary>
            POWFAIL,

            /// <summary>
            /// INT14  - Internal Interrupt
            /// </summary>
            INT14,

            /// <summary>
            /// INT15 - Option Interrupt
            /// </summary>
            INT15
        }
        /// <summary>
        /// Raised by the ALU when an internal interrupt pin changes state.
        /// </summary>
        /// <param name="interruptPin">
        /// The pin that changed.
        /// </param>
        public delegate void InternalInterruptEvent(InterruptPin interruptPin);

        #endregion

        #region PANEL Integration

        /// <summary>
        /// Vector read by IDBS,PANEL.
        /// PANEL device sets the Vector to be read
        /// </summary>
        public enum PanelVector
        {
            /// <summary>
            /// FROM INTERRUPT VECTOR IF THE CPU IS IN STOP (3760 OR 3770)
            /// </summary>
            STOP,

            /// <summary>
            /// ENTERED FROM INTERRUPT VECTOR EVERY 20 MS
            /// </summary>
            MS20,

            /// <summary>
            /// PANEL REQUEST, ENTERED FROM INTERRUPT VECTOR (3762)
            /// </summary>
            PRQ,

            /// <summary>
            ///  ENTERED FROM INTERRUPT VECTOR,SINGLE BUTTON PRESSED. ONLY LEGAL IN STOP
            /// </summary>
            SING2,

            /// <summary>
            /// LOAD BUTTON IS PRESSED,ENTERED FROM INTERRUPT VECTOR (3764 OR 3774)
            /// </summary>
            LOAD,

            /// <summary>
            /// NORMAL CONTINUE EXECUTION
            /// </summary>
            CONT,

            /// <summary>
            /// RESTART,ENTERED FROM INTERRUPT VECTOR (3766). ONLY IF STAND-BY POWER IS "OK", OR RESTART BUTTON PRESSED
            /// </summary>
            RSTRT,

            /// <summary>
            /// MASTER CLEAR ROUTINE
            /// </summary>
            MACL
        }

        #endregion PANEL Integration

    }
}
