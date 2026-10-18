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
using static NDMicroCpu.Core.CPU;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{


    /// <summary>
    /// The registers of the CPU that are not in the register file: the instruction register, the condition fields the micro code latched, the I/O word and the addresses of the last memory access.
    /// </summary>
    public class Registers
    {
        /// <summary>
        /// Creates the registers in their power-up state.
        /// </summary>
        public Registers()
        {
            Clear();
        }
        /// <summary>
        /// Current CPU operand (macroinstruction)
        /// </summary>
        public int Operand { get; set; }

        /// <summary>
        /// Microcode Argument register
        /// 
        /// The 16 lower bits of the microinstruction is enabled onto IDB. This makes it easy to generate 16 bit arguments within the microprogram.
        /// </summary>
        public ushort ARG { get; set; }

        /// <summary>
        /// IR - instruction register
        /// Bit 0-10 of the instruction will be loaded into the instruction register
        /// </summary>
        public int IR { get; set; }


        /// <summary>
        /// Stack control for the false sequence, latched from bits 0-1 of the argument field by a conditional micro instruction.
        /// </summary>
        public StackControl COND_StackControl { get; set; } // Bits 0-1
        /// <summary>
        /// Next address select for the false sequence, latched from bits 2-3 of the argument field.
        /// </summary>
        public NextAddressSelect COND_AddressSelect { get; set; } // Bits 2-3
        /// <summary>
        /// The test object of the condition, latched from bits 4-7 of the argument field.
        /// </summary>
        public TestObjects COND_TestObjects { get; set; } // Bits 4-7

        /// <summary>
        /// The last word transferred by an IOX instruction.
        /// </summary>
        public int IO { get; set; }


        /// <summary>
        /// Test result from last microcode instruction COND
        /// </summary>
        public bool COND { get; set; }

        /// <summary>
        /// Holds the current UC test condition
        /// </summary>
        public bool CurrentCond { get; set; }

        /// <summary>
        /// TRUE when the condition value currently held in <see cref="COND"/> was produced
        /// by a BARE SCOND-arm in the immediately-preceding microinstruction - i.e. that
        /// instruction set SCOND (armed a new test object) but did NOT also carry ECOND
        /// (CONDENABL) or LOOP, so the freshly-armed condition was never actually put
        /// through the condition TEST pipeline.
        ///
        /// Ground truth (ND-120 CGA Verilog): the condition-select register TSEL/FS/LCC
        /// (CGA_MIC_CONDREG.v, SCAN_FF_EN CSBIT4..CSBIT7) is (re)loaded from the microword
        /// only on the MCLK edge where CSSCOND=1, and CGA_MIC.v GATES_5/6/8 only fold the
        /// selected condition (s_cond_n) into the sequencer's TRUE/FALSE decision
        /// (s_etrue/s_efalse -> SC5/SC6) when CSECOND or CSLOOP is asserted. On a bare
        /// SCOND word (CSECOND=0, CSLOOP=0) s_etrue collapses to 1 - the just-armed
        /// condition has NOT yet been evaluated as a settled/tested result. This flag lets
        /// the LCOUNT gate honour that: a bare-armed condition must not suppress the count.
        /// See Cpu.Execute_4_ConditionControl (CS 001070 vs CS 012465).
        /// </summary>
        public bool COND_FromBareArm { get; set; }


        /// <summary>
        /// DataBus/cache read register
        /// </summary>
        public ushort DBR { get; set; }


        /// <summary>
        /// PEA - Parity Error Address
        /// </summary>
        public ushort PEA { get; set; }

        /// <summary>
        /// PES - Parity Error Source 
        /// </summary>
        public ushort PES { get; set; }


        /// <summary>
        /// PGS - Paging Status Extended (from Memory Management i assume). A copy will be stored in RegisterFile(10,13) 
        /// </summary>
        public ushort PGS { get; set; }

        /// <summary>
        /// Cache Status
        /// Bit
        /// 3 = Finished Cache Clear (FIN)
        /// 2 = Cache Disabled Manually (MAN DIS)
        /// 1 = Cache On  (CON)
        /// 0 = Cache Updated on current memory request
        /// </summary>
        public ushort CSR { get; set; } = 0x0C; // MAN DIS + FIN

        /// <summary>
        /// PCR - Paging Control Register (Delilah scehmatics shows that only bits 0-2,7-15 is stored here. Ie the rest is not latched)
        /// </summary>
        public ushort PCR { get; set; }

        /// <summary>
        /// Automatic load description (thumbwheel on cpu crate)
        /// </summary>
        public ushort ALD { get; set; }


        /// <summary>
        /// STOP flip/flop 
        /// When set, Indicating that the cpu should stop execuing an instrction after its done. Used by OPCOM for single stepping.
        /// </summary>
        public bool STOP { get; internal set; }

        #region IO Control register


        /// <summary>
        /// Set by Command.SIOC 
        /// </summary>
        public ushort IOControl { get; internal set; }


        /* IO Control register

            bit 7 : Reset real time clock
            bit 6 : Set terminal #1 in OPCOM (Console) as opposed to Normal
            bit 5 : Enable Master Clear, red LED ON (*1)
            bit 4 : Initialization completed, green LED ON
            bit 3 : Clock interrupt generated from RTC trap handler
            bit 2 : Enable output interrup on level 10, from terminal #1 - UART transmit buffer empty
            bit 1: Enable output interrup on level 12, from terminal #1 - UART data available           
            bit 0 : Enable clock interrupt on level 13

            NOTE (*1): When CLEAR is held, both red and green LEDs are illuminated. When CLEAR is released, red LED only stays illuminated while
            control store is loaded from EPRDM, hardware is initialised, and self—test runs.

            After successful completion, the red LED is extinguished and the green LED is illuminated.
        */

        #endregion IO Control Register

        #region CPU operation helper regs

        /// <summary>
        /// Last effective address
        /// </summary>
        public ushort lastEA { get; set; } = 0;
        /// <summary>
        /// The page table the last memory access went through.
        /// </summary>
        public PageTable lastPT { get; set; } = 0;

        /// <summary>
        /// Last PHYSICAL Memory access (24 bits)
        /// </summary>
        public uint lastPhysicalAddress { get; set; } = 0;

        /// <summary>
        /// True when the P register was written by the current macro instruction, so the fetch does not step it again.
        /// </summary>
        public bool P_was_updated { get; internal set; }
        /// <summary>
        /// The value of <see cref="P_was_updated"/> one micro cycle ago.
        /// </summary>
        public bool P_was_updated_last_cycle { get; internal set; }

        /// <summary>
        /// Control Store address, set by command ADCS
        /// </summary>
        public ushort ControlStoreAddress { get; internal set; }

        internal void Clear()
        {
            Operand = ARG = 0;

            IR = IO = DBR = PEA = PES = PGS = PCR = ALD = 0;

            IOControl = 0;
            ControlStoreAddress = 0;
            STOP = true;
            COND = false;
            CurrentCond = false;
            COND_FromBareArm = false;

            lastEA = 0;
            lastPT = PageTable.StandardPageTable;

            P_was_updated = P_was_updated_last_cycle = false;
        }

        #endregion
    }


}
