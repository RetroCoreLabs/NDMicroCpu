//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

//#define USE_HW_SHIFT
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using NDMicroCpu.Core.Common;
using static System.Formats.Asn1.AsnWriter;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static NDMicroCpu.Core.Enums;


// Reference implementation
// https://github.com/livingcomputermuseum/Darkstar/blob/0511a45b8e026fa138fdd2c6e48f10781779db60/D/CP/AM2901.cs
// https://github.com/MaisiKoleni/Am2900ME/blob/master/src/main/java/net/maisikoleni/am2900me/logic/Am2901.java
// https://github.com/Nakazoto/CenturionComputer/blob/main/!Other%20Gits/TemptorSent%20-%20centemu-master/centemu/hwemu/am2901.c


// ALU calculations "borrowed" from
// https://github.com/livingcomputermuseum/Darkstar/blob/0511a45b8e026fa138fdd2c6e48f10781779db60/D/CP/AM2901.cs
// https://github.com/htmlgod/AM2901A/blob/main/AM2901A/sources/am2901a.cpp


// Am 2900 Books and trainings
// http://www.whitepubs.com/Am2900_references.html
// http://bitsavers.trailing-edge.com/components/amd/bitslice/
// http://www.bitsavers.org/pdf/amd/ED2900A_vol1_Jan85.pdf
// http://www.bitsavers.org/pdf/amd/ED2900A_vol2_Jan85.pdf

// Am2901 Articles
// https://www.righto.com/2020/04/inside-am2901-amds-1970s-bit-slice.html
// https://hackaday.io/project/180805-bit-slice-am2900-design
// http://www.donnamaie.com/BITSLICE/BIT_CHAP_1/bitslcIc.html

namespace NDMicroCpu.Core
{
    /// <summary>
    /// BUFALU
    /// 
    /// The Arithmetical and Logical Unit gate array (ALU, also known as the BUFALU). 
    /// Replaced four Am2901 bit-slice processors
    /// 
    /// * Register File, or "16x16 bit 2-port RAM"
    /// * Q-Register (16 bit)
    /// * ALU (16 bit)
    /// 
    /// Added registers like the 
    /// * Data bus register  (DBR)
    /// * General purpose register (GPR)
    /// * The internal register block.
    /// 
    /// This performs arithmetic and logic functions. Two 4—bit pointers, the A and B operands, select sources for
    /// operations to be performed. Nine ALU function bits (ALUI) select operations within the ALU.
    /// 
    /// Data input and output ports are directly connected to the CPU's internal bus system
    /// </summary>
    public class BUFALU
    {

        #region eventing        
        /// <summary>
        /// Raised when the ALU raises an internal interrupt pin.
        /// </summary>
        public event InternalInterruptEvent? OnInternalInterrupt;
        #endregion eventing


        #region Public ALU settings
        // All of these properties need to be set before you call ExecutOperation()

        /// <summary>
        /// The ALU mode select from the micro word (ALUM), set before an operation is executed.
        /// </summary>
        public ND_ALUMode ALUM { get; internal set; }

        /// <summary>
        /// The ALU source select from the micro word, set before an operation is executed.
        /// </summary>
        public ALUSource aluSource { get; internal set; }

        /// <summary>
        /// The ALU destination select from the micro word, set before an operation is executed.
        /// </summary>
        public ALUDestination aluDestination { get; internal set; }

        /// <summary>
        /// The ALU function from the micro word, set before an operation is executed.
        /// </summary>
        public ALUFunc aluFunc { get; internal set; }
        #endregion ALU settings

        #region Current register block

        /// <summary>
        /// WRF - Work Register File
        /// Registers
        /// </summary>        
        /// 
        public RegisterFile ALURAM;


        #endregion




        #region STS - Register Block, register 000010 (0x08)

        // STS register, low 8 bit - follows level        
        internal const int STS_M_bit = 7;
        internal const int STS_C_bit = 6;
        internal const int STS_O_bit = 5;
        internal const int STS_Q_bit = 4;
        internal const int STS_Z_bit = 3;
        internal const int STS_K_bit = 2;
        internal const int STS_TG_bit = 1;
        internal const int STS_PM_bit = 0;

        private ushort STS_value { get; set; }



        /// <summary>
        /// Writes the status register directly, for the debugger and the tests.
        /// </summary>
        /// <param name="value">
        /// The new status word.
        /// </param>
        public void SetSTSfromDebugger(ushort value)
        {
            STS_value = value;
        }


        /// <summary>
        /// Replace all 16 bits in the STS with this value
        /// </summary>
        /// <param name="sts"></param>
        public void LoadSTS(ushort sts)
        {
            STS_value = sts;
            CheckForInterruptSTS_Z();
        }

        /// <summary>
        /// Set the low 8 bits of STS.
        /// </summary>
        /// <param name="lo_sts"></param>
        internal void LoadSTS_Low(byte lo_sts)
        {
            STS_value &= 0xFF00;
            STS_value |= lo_sts;

            CheckForInterruptSTS_Z();
        }

        /// <summary>
        /// Sets the high 8 bits of sts
        /// Paramater hi_sts will be shifted left 8 bits and stored into STS high 8 bits.
        /// </summary>
        /// <param name="hi_sts"></param>
        internal void LoadSTS_Hi(byte hi_sts)
        {
            STS_value &= 0x00FF;
            STS_value = (ushort)(STS_value | ((ushort)hi_sts << 8));
        }


        //
        void CheckForInterruptSTS_Z()
        {
            if (GetSTSBit(STS_Z_bit))
            {
                // trigger IRQ line in PIC for Z
                if (OnInternalInterrupt != null)
                {
                    OnInternalInterrupt(InterruptPin.Z);
                }
            }
        }

        /// <summary>
        /// 16 bit STS register.
        /// </summary>
        /// <remarks>
        /// Bit 12 is N100 ("this is an ND-100 family CPU"); SINTRAN uses it (via TRA STS) to
        /// distinguish ND-100 family CPUs from the NORD-10/S, so after power-up it must read 1.
        /// Ground truth from the real microcode (ND-110-RASK.LISTING.TXT): the master-clear routine
        /// explicitly sets it before handing control to macro code - CS 002070-002071 execute
        /// Q = BMG(14 octal = bit 12) and then "A,R7 ALUF,ORAQ ... COMM,LDPIL STS,LO", i.e.
        /// STS = saved STS | 10000 octal. (The self-test comment at CS 002065, "STERR IF STS NOT
        /// 10000", likewise shows bit 12 is expected to be present at that point on real hardware.)
        /// The unit-test harness starts executing microcode without going through the full MACL
        /// power-up path, so the reset value is seeded in <see cref="Clear"/> instead - see there.
        /// NOTE: an earlier experiment hardwired the bit in this getter (the commented-out line
        /// below existed historically), but that makes the emulator's boot self-test ST7 read back
        /// 10000 after writing 0 to STS and trip STERR 5000, so it is seeded at reset instead.
        /// </remarks>
        //public ushort STS => (ushort)(STS_value | 1 << 12); // hardwire experiment - breaks emulated ST7, see remarks
        public ushort STS => (ushort)(STS_value);



        /// <summary>
        /// Extended indicator to show that MMS is in 24 bits extended addressing mode instead of the usual 19 bits addressing mode.
        /// SEXI bit 13
        /// Returns TRUE if its enabled
        /// </summary>
        public bool STS_SEXI => (STS & 1 << 13) != 0;

        /// <summary>
        /// Memory Management system
        /// STS bit 14
        /// Returns TRUE if its enabled
        /// </summary>
        public bool STS_PONI => (STS & 1 << 14) != 0;

        /// <summary>
        /// Interrupt system
        /// STS bit 15
        /// Returns TRUE if its enabled
        /// </summary>
        public bool STS_IONI => (STS & 1 << 15) != 0;

        /// <summary>
        /// STS register - high 8 bits
        /// </summary>
        public byte STS_system => (byte)(STS >> 8 & 0xFF);

        /// <summary>
        ///  PIL - Current program level Indicator, bit 11-8 in the Status register assignment (Valid values: 0-15)
        /// </summary>
        public byte PIL
        {
            get => (byte)(STS_system & 0x0F);
            set => STS_value |= (ushort)((value & 0x0F) << 8);
        }


        /// <summary>
        /// P
        /// paging table mode
        /// (enables alternate page table mode)
        /// </summary>
        public bool STS_P
        {
            get => GetSTSBit(STS_PM_bit);
            set => SetSTSBit(STS_PM_bit, value);
        }


        /// <summary>
        /// G
        /// rounding flag
        /// (for floating point operations)
        /// </summary>
        public bool STS_G
        {
            get => GetSTSBit(STS_TG_bit);
            set => SetSTSBit(STS_TG_bit, value);
        }


        /// <summary>
        /// K
        /// 1-bit accumulator
        /// (used for bit operations)
        /// </summary>
        public bool STS_K
        {
            get => GetSTSBit(STS_K_bit);
            set => SetSTSBit(STS_K_bit, value);
        }


        /// <summary>
        /// Z
        /// Error flag
        /// </summary>
        public bool STS_Z
        {
            get => GetSTSBit(STS_Z_bit);
            set => SetSTSBit(STS_Z_bit, value);
        }


        /// <summary>
        /// Q
        /// Dynamic Overflow flag
        /// </summary>
        public bool STS_Q
        {
            get => GetSTSBit(STS_Q_bit);
            set => SetSTSBit(STS_Q_bit, value);
        }

        /// <summary>
        /// O
        /// Static Overflow flag
        /// </summary>
        public bool STS_O
        {
            get => GetSTSBit(STS_O_bit);
            set => SetSTSBit(STS_O_bit, value);
        }

        /// <summary>
        /// C
        /// Carry Flag
        /// </summary>
        public bool STS_C
        {
            get => GetSTSBit(STS_C_bit);
            set => SetSTSBit(STS_C_bit, value);
        }

        /// <summary>
        /// M
        /// multishift link flag
        /// (1-bit extension for the A, D or T register )
        /// </summary>
        public bool STS_M
        {
            get => GetSTSBit(STS_M_bit);
            set => SetSTSBit(STS_M_bit, value);
        }



        /// <summary>
        /// Set or clear bit value in STS register (low 8 bit, unique pr level)
        /// </summary>
        /// <param name="sts_bit">Which bit</param>
        /// <param name="value">0=Clear, !0 = Set bit</param>
        internal void SetSTS(ushort sts_bit, bool value)
        {
            if (sts_bit > 7) return; // shouldnt happen

            SetSTSBit(sts_bit, value);

        }

        private void SetSTSBit(ushort bit, bool val)
        {
            if (val)
            {
                STS_value = (ushort)(STS_value | (1 << bit));
            }
            else
            {
                STS_value = (ushort)(STS_value & (~(1 << bit)));
            }
        }

        internal bool GetSTSBit(short bit)
        {
            return (STS_value & (1 << bit)) != 0;
        }
        #endregion STS

        #region Bit Mask Generator

        /// <summary>
        /// BMG - Bit Mask Generator
        /// Generates a 1 bit among zeros. The position of the 1 bit is controlled by the A operand.
        /// </summary>
        /// <returns></returns>
        public ushort BMG()
        {
            return (ushort)(1 << A_Operand);
        }
        #endregion Bit Mask Generator

        #region ALU IN/OUT pins

        /// <summary>
        /// Carry in to ALU
        /// </summary>
        public bool CARRY_IN { get; set; }

        /// <summary>
        /// CRY - Carry out from ALU
        /// </summary>
        public bool CARRY_OUT { get; internal set; }

        /// <summary>
        /// OVF - Overflow
        /// Static overflow indicator
        /// This indicator remains set after an overflow condition until it is reset by program.
        /// </summary>
        public bool OVERFLOW_OUT { get; set; }

        /// <summary>
        /// Q - Dynamic overflow indicator.
        /// </summary>
        public bool DYNAMIC_OVERFLOW_OUT { get; internal set; }

        /// <summary>
        /// ZF - Result = 0 from ALU
        /// </summary>
        public bool ZF { get; internal set; }

        /// <summary>
        /// Negative, Result bit15=1 from ALU
        /// </summary>
        public bool F15 { get; internal set; }

        /// <summary>
        /// Bit 11 from ALU
        /// </summary>
        public bool F11 { get; internal set; }


        /// <summary>
        /// SIGNED GREATER OR EQUAL
        /// </summary>
        public bool SGR { get; internal set; } // True = Greater or Equal, False = Less

        #endregion ALU OUT pins

        #region Shift Link

        // The BUFALU contains register sets and the ALU,
        // but also contains a "shift link" for ALU shift operations with the right-in/left-out and right-out/left-in lines

        // Input and output fields for shift-registers

        /// <summary>
        /// Disappering RAM bit, whatever direction you rotate
        /// </summary>
        public int RAM_DISAPPEARING_BIT { get; set; }

        /// <summary>
        /// BIT 15 of RAM, can be in and out
        /// (RAM_15)
        /// </summary>
        public int RLI
        {
            get => (F >> 15) & 0x01;
            set => IN_RAM15 = value;
        }
        int IN_RAM15;

        /// <summary>
        /// BIT 15 of Q - OUTPUT only (read by the shift-linkage logic in Cpu.cs, e.g.
        /// "RAM0 := Q15" on double right shifts).
        ///
        /// There is no writable Q15 input pin on real hardware: on a double shift RIGHT
        /// (RAMQD) the Q register's bit 15 is HARDWIRED to F[0] inside the Q mux
        /// (CGA_ALU_QREG.v MUXQ15 input D3, QSEL = 11) - see ExecuteAluDestination.
        /// </summary>
        public int QLI => (Q >> 15) & 0x01;


        /// <summary>
        /// BIT 0 of RAM, can be in and out
        /// (RAM_0)
        /// </summary>
        public int RRI
        {
            get => F & 0x01;
            set => IN_RAM0 = value;
        }
        int IN_RAM0;

        /// <summary>
        /// BIT 0 of Q - OUTPUT only (read by the shift-linkage logic in Cpu.cs, e.g.
        /// "RAM15 := Q0" on 32-bit rotates, and by the SM1/M-flag selector).
        /// The INPUT side of Q bit 0 is QLeftSerialInput below.
        /// </summary>
        public int QRI => Q & 0x01;

        /// <summary>
        /// The Q register's serial input for a double shift LEFT (RAMQU): the value
        /// clocked into Q bit 0. This models the real QLI control signal computed by
        /// CGA_CPU_ALU_CONTR.v and fed to CGA_ALU_QREG.v's MUXQ0 input D2 (QSEL = 10).
        /// It is the ONLY external data input the Q register has besides F:
        ///   QSEL 00 hold, 01 load F, 10 shift left (bit0 := this), 11 shift right
        ///   (bit15 := F[0], hardwired).
        /// Set per-cycle by the shift-linkage block in Cpu.cs (zero for plain/ZIN
        /// shifts, RAM15 for 32-bit rotate, STS M for LIN).
        /// </summary>
        public int QLeftSerialInput { get; set; }

        #endregion

        #region Data input and output ports

        /// <summary>
        /// A - Integer Operand A
        /// </summary>
        public ushort A_Operand { get; set; }

        /// <summary>
        /// B - Integer Operand B
        /// </summary>
        public ushort B_Operand { get; set; }

        /// <summary>
        /// Q - ALU Holding register 
        /// (output from ALU calculations)
        /// </summary>
        public int Q { get; set; }


        /// <summary>
        /// F - ALU Result
        /// Can be directed to: Q, B-Source, Y
        /// Output calculation from ALU
        /// </summary>
        public int F { get; set; }

        /// <summary>
        /// Y - ALU Output (IDB Bus)                
        /// </summary>
        public int Y { get; internal set; }

        /// <summary>
        /// ALU R register
        /// </summary>
        public int R { get; set; }

        /// <summary>
        /// ALU S register
        /// </summary>
        public int S { get; set; }

        /// <summary>
        /// D - Internal Data Bus
        /// (Direct data in)
        /// </summary>
        public int DataBus { get; internal set; }

        /// <summary>
        /// GPR is a General Purpose register used by microprogram for various purposes. 
        /// It will always be shiftet the same way as an ALU register if shift is specified in the ALU control.
        /// 
        /// GPR is especially valuable during multiply and divide operations. 
        /// In multiplay operations it holds one of the operands and in divide operands it gets the result shiftet in from the right.
        /// 
        /// It will
        /// </summary>
        public ushort GPR { get; set; }

        // int GPR0 = (hw.alu.GPR & 0x01); // Bit 0 of GPR
        /// <summary>
        /// Return bit 0 of GPR
        /// </summary>
        public ushort GPR0 { get { return (ushort)(GPR & 0x01); } }



        /// <summary>
        /// GPR Sign Extended
        /// GPR BITS 0-7 (WITH BITS 8-15 EQUAL TO BIT 7) -> IDB
        /// </summary>
        /// <returns></returns>
        public ushort GPR_SEXT()
        {
            bool sign = (GPR & 1 << 7) != 0;
            ushort retval = (ushort)(GPR & 0x00FF);
            if (sign)
                retval |= 0xFF00;

            return retval;
        }



        /// <summary>
        /// Shift operations
        /// </summary>
        public ShiftMode ShiftMode { get; internal set; }



        #endregion  Data input and output ports

        #region Constructor
        /// <summary>
        /// Creates the ALU with its register file and the power-up status.
        /// </summary>
        public BUFALU()
        {
            ALURAM = new RegisterFile("ALURAM", 1);
            // Seed the N100 flag (STS bit 12) at power-up. On the real machine the master-clear
            // microcode sets it (ND-110-RASK.LISTING.TXT CS 002070-002071: Q = BMG(14 octal);
            // "A,R7 ALUF,ORAQ ... COMM,LDPIL STS,LO" -> STS |= 10000 octal) before any macro code
            // runs, so software (e.g. SINTRAN's TRA STS CPU-type check) always observes it as 1.
            // The unit-test harness does not run the full MACL power-up path, hence the seed here.
            STS_value = 1 << 12;
        }

        /// <summary>
        /// Resets the ALU state to the power-up values.
        /// </summary>
        public void Clear()
        {
            ALURAM.Clear();
            GPR = 0;
            A_Operand = B_Operand = 0;
            Q = F = Y = R = S = DataBus = 0;
            RLI = RRI = QLeftSerialInput = 0;
            CARRY_OUT = CARRY_IN = false;
            // Reset STS to its power-up value: N100 flag set (see constructor comment).
            STS_value = 1 << 12;
        }
        #endregion Constructor 

        #region Public API

        ///// <summary>
        ///// Write a value into the working register file specified by the A and B operands.
        ///// </summary>
        ///// <param name="value"></param>
        //public void WriteRegisterFile(ushort value)
        //{
        //    Utils.Log($"WRF: A,B => Value: {Utils.Int2Oct(value)}");
        //    WRF.Write(A_Operand, B_Operand, value);
        //}

        /// <summary>
        /// Write to internal ALU Register
        /// </summary>
        public void WriteRegister(ushort reg, ushort value)
        {
            RegisterEnum re = (RegisterEnum)reg;
            Logger.Log($"ALU.W: R:{re} [{Utils.Int2Oct(reg)}] V:{Utils.Int2Oct(value)}", Logger.LogLevel.Opcodes);

            ALURAM.UIWrite(0, reg, value);
            //Registers[reg & 0xF] = value;
        }

        /// <summary>
        /// Writes an ALU register by name.
        /// </summary>
        /// <param name="reg">
        /// The register.
        /// </param>
        /// <param name="value">
        /// The new value.
        /// </param>
        public void WriteRegisterE(RegisterEnum reg, ushort value)
        {
            WriteRegister((ushort)reg, value);
        }

        internal void WriteRegisterLo(ushort reg, ushort value)
        {
            RegisterEnum re = (RegisterEnum)reg;

            value &= 0xFF;
            Utils.Log($"ALU.W.LO: R:{re} [{Utils.Int2Oct(reg)}] V:{Utils.Int2Oct(value)}");

            ushort regval = ALURAM.UIRead(0, reg);
            regval = (ushort)((regval & 0xFF00) | value);
            ALURAM.UIWrite(0, reg, value);

            //Registers[reg & 0xF] = (ushort)((Registers[reg & 0xF] & 0xFF00) | value);

        }

        internal void WriteRegisterHi(ushort reg, ushort value)
        {
            RegisterEnum re = (RegisterEnum)reg;
            value &= 0xFF;
            Utils.Log($"ALU.W.HI: R:{re} [{Utils.Int2Oct(reg)}] V:{Utils.Int2Oct(value)}");

            ushort regval = ALURAM.UIRead(0, reg);
            regval = (ushort)((regval & 0x00FF) | value << 8);
            ALURAM.UIWrite(0, reg, value);

            //Registers[reg & 0xF] = (ushort)((Registers[reg & 0xF] & 0x00FF) | (value << 8));

        }



        /// <summary>
        /// Reads an ALU register by name.
        /// </summary>
        /// <param name="reg">
        /// The register.
        /// </param>
        /// <param name="doLog">
        /// False suppresses the debug log entry.
        /// </param>
        /// <returns>
        /// The register value.
        /// </returns>
        public ushort ReadRegisterE(RegisterEnum reg, bool doLog = true)
        {
            return ReadRegister((ushort)reg, doLog);
        }

        /// <summary>
        /// Read from internal ALU register
        /// </summary>
        public ushort ReadRegister(ushort reg, bool doLog = true)
        {
            RegisterEnum re = (RegisterEnum)reg;

            //ushort value = Registers[reg & 0xF];
            ushort value = ALURAM.UIRead(0, reg);
            if (doLog)
                Logger.Log($"ALU.R: R:{re} [{Utils.Int2Oct(reg)}]  V:{Utils.Int2Oct(value)}", Logger.LogLevel.Opcodes);
            return value;
        }

        /// <summary>
        /// Set R and S value based on property aluSource
        /// Source decoding and source select
        /// 
        /// (specified by ALU source field (bits 55-57) of the microinstruction)
        /// </summary>
        private void SetRS()
        {

            //Logger.Log($"ALU.SETRS: {aluSource}", Logger.LogLevel.Opcodes);

            switch (aluSource)
            {
                case ALUSource.A_Q:
                    R = ReadRegister(A_Operand);
                    S = Q;
                    break;
                case ALUSource.A_B:
                    R = ReadRegister(A_Operand);
                    S = ReadRegister(B_Operand);
                    break;
                case ALUSource.O_Q:
                    R = 0;
                    S = Q;
                    break;
                case ALUSource.O_B:
                    R = 0;
                    S = ReadRegister(B_Operand);
                    break;
                case ALUSource.O_A:
                    R = 0;
                    S = ReadRegister(A_Operand);
                    break;
                case ALUSource.D_A: // D = Internal Databus
                    R = DataBus;
                    S = ReadRegister(A_Operand);
                    break;
                case ALUSource.D_Q:
                    R = DataBus;
                    S = Q;
                    break;
                case ALUSource.D_O:
                    R = DataBus;
                    S = 0;
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Latch a given result directly into the ALU registers Q,F and Y
        /// </summary>
        /// <exception cref="NotImplementedException"></exception>
        internal void Latch(ushort value)
        {
            Q = F = Y = value;
        }


        /// <summary>
        /// Perform ALU operation.
        /// </summary>
        internal void ExecuteALUOperation()
        {

            SetRS();

            // TODO: Validate all calculation

            int carry = CARRY_IN ? 1 : 0;



            //Logger.Log($"ALU.EXECUTE: {aluFunc}", Logger.LogLevel.Opcodes);



            switch (aluFunc)
            {
                case ALUFunc.ADD:
                    F = R + S + carry;

                    //Logger.Log($"ALU.ADD: F = R + S + carry R:{Utils.Int2Oct(R)} S:{Utils.Int2Oct(S)} Carry:{carry}", Logger.LogLevel.Opcodes);
                    //Logger.Log($"ALU.ADD: F:{Utils.Int2Oct(F)}", Logger.LogLevel.Opcodes);
                    CARRY_OUT = ((uint)F > 0xffff);
                    OVERFLOW_OUT = CalcAluOVF(R, S, F);
                    DYNAMIC_OVERFLOW_OUT = OVERFLOW_OUT;
                    break;

                case ALUFunc.SUBR:
                    int complementR = ~R & 0xFFFF;
                    F = S + complementR + carry; // add with one's complement
                    //F = S - R + carry;

                    //Logger.Log($"ALU.SUBR: F = S - ~R + carry R:{Utils.Int2Oct(S)} S:{Utils.Int2Oct(complementR)} Carry:{carry}", Logger.LogLevel.Opcodes);
                    //Logger.Log($"ALU.SUBR: F:{Utils.Int2Oct(F)}", Logger.LogLevel.Opcodes);

                    CARRY_OUT = (((uint)F > 0xffff) || (F < 0));
                    OVERFLOW_OUT = CalcAluOVF(complementR, S, F);
                    DYNAMIC_OVERFLOW_OUT = OVERFLOW_OUT;
                    break;

                case ALUFunc.SUBS:
                    int complementS = ~S & 0xFFFF;
                    F = R + complementS + carry;

                    //Logger.Log($"ALU.SUBS: F = R - ~S + carry R:{Utils.Int2Oct(F)} S:{Utils.Int2Oct(complementS)} Carry:{carry}", Logger.LogLevel.Opcodes);
                    //Logger.Log($"ALU.SUBS: F:{Utils.Int2Oct(F)}", Logger.LogLevel.Opcodes);

                    CARRY_OUT = (((uint)F > 0xffff) || (F < 0));
                    OVERFLOW_OUT = CalcAluOVF(R, complementS, F);
                    DYNAMIC_OVERFLOW_OUT = OVERFLOW_OUT;
                    break;

                case ALUFunc.OR:
                    F = R | S;

                    CARRY_OUT = false;
                    OVERFLOW_OUT = false;
                    break;

                case ALUFunc.AND:
                    F = R & S;
                    CARRY_OUT = false;
                    OVERFLOW_OUT = false;
                    break;

                case ALUFunc.NOTRS:
                    F = (~R) & S;
                    CARRY_OUT = false;
                    OVERFLOW_OUT = false;
                    break;

                case ALUFunc.EXOR:

                    F = R ^ S;
                    CARRY_OUT = false;
                    OVERFLOW_OUT = false;
                    break;

                case ALUFunc.EXNOR:
                    F = (~R) ^ S;
                    CARRY_OUT = false;
                    OVERFLOW_OUT = false;

                    break;
            }

            // Make sure its 16 bits, no more.
            F = (ushort)(F & 0xFFFF);



            // Set output bits
            /*
                        RL1 = (F >> 15) & 0x01;
                        RR1 = F & 0x01;

                        QL1 = (Q >> 15) & 0x01;
                        QR1 = Q & 0x01;
            */


            // Calc Zero and Negative flags

            ZF = (F == 0);
            F15 = ((F & 0x8000) != 0);
            F11 = ((F >> 11) & 0x01) != 0;


            // SGR is true when F15 == OVERFLOW_OUT
            // (from LLM analyzing ND-120 schematics and signal)
            SGR = (F15 == OVERFLOW_OUT);

        }




        /// <summary>
        /// Write outputs after an ALU calculation
        /// Also include bit-shifts
        /// </summary>
        internal void ExecuteAluDestination()
        {

            // Rotate
            //if (this.ShiftMode != ShiftMode.NONE)
            //{
            //    Utils.Log($"@SM={ShiftMode}");
            //}




            int carry = CARRY_IN ? 1 : 0;

            // Q REGISTER (hardware-accurate QSEL model, CGA_ALU_QREG.v + the QSEL
            // gates GATES_26/27 in CGA_CPU_ALU_CONTR.v):
            //   QSEL_1 = !I6 & I8, QSEL_0 = !I6 & !I7  (I8..I6 = Am2901 dest code)
            //   00 = HOLD (NOP/RAMA/RAMF/RAMD/RAMU - any I6=1, plus RAMA)
            //   01 = LOAD F (QREG)
            //   10 = SHIFT LEFT, bit 0 := QLeftSerialInput (RAMQU; the CONTR's QLI signal)
            //   11 = SHIFT RIGHT, bit 15 := F[0] HARDWIRED (RAMQD; the double-shift
            //        internal link - MUXQ15 input D3 taps the combinational ALU output
            //        F directly, so the bit leaving the RAM shifter's bottom streams
            //        into Q's top. There is no writable Q15 input on real hardware.)
            // Q is computed here, BEFORE the RAM branch below - like the hardware, where
            // the Q mux samples F and Q while ALUCLK loads both destinations at once.
            {
                int destCode = (int)aluDestination;
                int i6q = destCode & 0x01;
                int i7q = (destCode >> 1) & 0x01;
                int i8q = (destCode >> 2) & 0x01;
                int qsel = ((i6q == 0 && i8q == 1) ? 2 : 0) | ((i6q == 0 && i7q == 0) ? 1 : 0);
                switch (qsel)
                {
                    case 0: /* HOLD */ break;
                    case 1: Q = F & 0xFFFF; break;
                    case 2: Q = ((Q << 1) & 0xFFFE) | (QLeftSerialInput & 0x01); break;
                    case 3: Q = ((Q >> 1) & 0x7FFF) | ((F & 0x01) << 15); break;
                }
            }

            // TODO: Review!!
            switch (aluDestination)
            {
                case ALUDestination.ALUD_Q: //QREG //Q := F  (Q handled by the QSEL model above)
                    Y = (ushort)F;
                    break;


                case ALUDestination.ALUD_NONE: //NOP // NO WRITE TO RAM (Registers)
                    Y = (ushort)F;
                    break;

                case ALUDestination.ALUD_B_YA: // RAMA // F=>B, Y=>A
                    // Y output := A
                    Y = ReadRegister(A_Operand); // //TODO: Validate if this is more correct: "Y = A_Operand;"
                    //Y = A_Operand;

                    // RAM FUNCTION, LOAD: F->B
                    WriteRegister(B_Operand, (ushort)F);
                    break;


                case ALUDestination.ALUD_B: //RAMF  // F=>B
                    Y = (ushort)F;

                    // RAM FUNCTION, LOAD: F->B
                    WriteRegister(B_Operand, (ushort)F);
                    break;


                case ALUDestination.ALUD_SRD: //RAMQD  // Q/2=>Q, F/2 => B (multiply function))
                                              // (Q handled by the QSEL model above: Q>>1, Q15 := F[0] hardwired)
                    Y = (ushort)F;

                    Logger.Log($"ALU shift RQ: M:{ShiftMode} D:{aluDestination}", Logger.LogLevel.Opcodes);

                    // RAM FUNCTION, LOAD: F/2->B
                    ushort tmpF = ExecuteShift(F, AluShiftDir.RIGHT, AluShiftRegister.F);
                    WriteRegister(B_Operand, (ushort)tmpF);


                    break;


                case ALUDestination.ALUD_SRB: // RAMD // F/2 =>B 
                    Y = (ushort)F;

                    Logger.Log($"ALU shift R: M:{ShiftMode} D:{aluDestination}", Logger.LogLevel.Opcodes);


                    // RAM FUNCTION, LOAD: F/2->B
                    ushort tmpF1 = ExecuteShift(F, AluShiftDir.RIGHT, AluShiftRegister.F);
                    WriteRegister(B_Operand, (ushort)tmpF1);
                    break;

                case ALUDestination.ALUD_SLD: //RAMQU // 2Q=>Q, 2F=>B
                                              // (Q handled by the QSEL model above: Q<<1, Q0 := QLeftSerialInput)
                    Y = (ushort)F;

                    Utils.Log($"ALU shift LQ: M:{ShiftMode} D:{aluDestination} Q:{Utils.Int2Oct(Q)}");

                    // RAM FUNCTION, LOAD: 2F->B
                    ushort tmpF2 = ExecuteShift(F, AluShiftDir.LEFT, AluShiftRegister.F);
                    WriteRegister(B_Operand, (ushort)tmpF2);
                    break;


                case ALUDestination.ALUD_SLB: // RAMU // 2F => B
                    Y = (ushort)F;

                    Utils.Log($"ALU shift L: M:{ShiftMode} D:{aluDestination}");

                    // RAM FUNCTION, LOAD: 2F->B
                    ushort tmpF3 = ExecuteShift(F, AluShiftDir.LEFT, AluShiftRegister.F);
                    WriteRegister(B_Operand, (ushort)tmpF3);
                    break;
            }


            // Calc Zero and Negative flags
            ZF = (F == 0);
            F15 = ((F & 0x8000) != 0);
            F11 = ((F >> 11) & 0x01) != 0;
        }

        private enum AluShiftDir
        {
            RIGHT,
            LEFT
        }
        private enum AluShiftRegister
        {
            /// <summary>
            /// RAM
            /// </summary>
            F,

            /// <summary>
            /// Q
            /// </summary>
            Q
        }

        ushort ExecuteShift(int register, AluShiftDir shiftDir, AluShiftRegister sr)
        {
            int orgRegister = register;
            // Calculate bit shifted out and do the shift
            // NOTE: only the RAM (F) path shifts here nowadays - the Q register is a
            // hardware-accurate QSEL mux in ExecuteAluDestination (CGA_ALU_QREG.v) and
            // no longer goes through this helper.
            if (shiftDir == AluShiftDir.RIGHT)
            {
                register = (register >> 1) & 0x7fff; // Execute shift and make sure bit 15 is cleared
                switch (sr)
                {
                    case AluShiftRegister.F:
                        register |= (ushort)(IN_RAM15 << 15);
                        break;
                    default:
                        break;
                }
            }
            else
            {
                // shift left
                register = (register << 1) & 0xfffe; //Execute shift and make sure bit 0 is cleared

                switch (sr)
                {
                    case AluShiftRegister.F:
                        register |= IN_RAM0;
                        break;
                    default:
                        break;
                }
            }

            // Extra debugging for RDIV operation
            //Utils.Log($"ExecuteShift for Register:{sr} Direction:{shiftDir}  Input:{Utils.Int2Oct(orgRegister)}  Output:{Utils.Int2Oct(register)}");

            return (ushort)register;
        }


        #endregion Public API                           

        #region Overflow calculation

        /// <summary>
        /// Calculate ALU OVF Signal
        ///
        /// Signed overflow occurs when:
        ///     Two positive numbers yield a negative result (unexpected wraparound)
        ///     Two negative numbers yield a positive result
        /// 
        /// Based on A and B signals
        /// A is RN or R - depending on ALU function
        /// B is Sn or S - depending on ALU function
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="f"></param>
        /// <returns>True on overflow</returns>        
        private bool CalcAluOVF(int a, int b, int f)
        {
            // Q = ~(~(A15 & B15 & F15_n) & ~(A15_n & B15_n & F15)); // ND120 CPU- DGA Page 46

            // Overflow happens when:
            //  A and B are positive, but result is negative, or
            //  A and B are negative, but result is positive

            /*
             * Original code before optimizing
             *
             
            bool A15 = ((a >> 15) & 1) != 0;
            bool B15 = ((b >> 15) & 1) != 0;
            bool F15 = ((f >> 15) & 1) != 0;

            bool A15_n = !A15;
            bool B15_n = !B15;
            bool F15_n = !F15;

            bool term1 = !(A15 & B15 & F15_n);
            bool term2 = !(A15_n & B15_n & F15);

            bool result = !(term1 & term2);

            return result;
            */


            // Optimized version based on
            // "A and B have the same sign, but F has a different sign."       
            bool a15 = (a & 0x8000) != 0;
            bool b15 = (b & 0x8000) != 0;
            bool f15 = (f & 0x8000) != 0;

            return ((a15 == b15) && (f15 != a15));

        }

        /// <summary>
        /// Sign Generated Result (SGR)
        /// 
        /// Based on A and B signals
        /// A is RN or R - depending on ALU function
        /// B is Sn or S - depending on ALU function
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="f"></param>
        /// <returns></returns>
        private bool CalcSGR(int a, int b, int f)
        {
            // SGR = ~(~(A15_n & F15_n) & ~(A15_n & B15_n) & ~(B15_n & F15_n)) // ND120 CPU- DGA Page 46

            /*
              Original code
            bool a15_n = ((a >> 15) & 1) == 0;
            bool b15_n = ((b >> 15) & 1) == 0;
            bool f15_n = ((f >> 15) & 1) == 0;

            bool term1 = !(a15_n && f15_n);
            bool term2 = !(a15_n && b15_n);
            bool term3 = !(b15_n && f15_n);

            return !(term1 && term2 && term3);
            */

            // Optimized code based on truth-table
            // "SGR is true if at most one of the sign bits (A15, B15, F15) is 1"

            int a15 = (a >> 15) & 1;
            int b15 = (b >> 15) & 1;
            int f15 = (f >> 15) & 1;

            return (a15 + b15 + f15) <= 1;

        }

        #endregion Overflow calculation
    }
}
