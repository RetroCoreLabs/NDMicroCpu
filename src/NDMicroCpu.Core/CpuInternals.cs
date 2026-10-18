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
using NDMicroCpu.Core.AMD;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// Class to keep all references to internal HW objects
    /// </summary>
    public class CpuInternals
    {

        CPUType cpuType = CPUType.ND110;

        /// <summary>
        /// The control store the micro code runs from.
        /// </summary>
        public ControlStore cs;

        /// <summary>
        /// The registers that are not in the register file: instruction register, latched condition fields and the last addresses.
        /// </summary>
        public Registers cpu_regs;


        /// <summary>
        /// Macroinstruction address controller
        /// </summary>
        public MAC mac;

        /// <summary>
        /// Microprogram Sequencer RMIC
        /// </summary>
        public MIC mic;



        /// <summary>
        /// BUFALU
        /// </summary>
        public BUFALU alu;

        /// <summary>
        /// Internal Data Bus
        /// </summary>
        public IDB idb;

        /// <summary>
        /// ND-100/ND-110 HW Design with 2* Am2914 PIC
        /// </summary>
        public InterruptSubSystem pic;

        /// <summary>
        /// Serial port on CPU board
        /// </summary>
        public Uart uart = null!;        // created for ND110 only; the other CPU type leaves it null, callers go by cpuType

        /// <summary>
        /// Serial port on CPU board for ND-120. 
        /// This is an RS-232 UART.
        /// Doc: 
        ///   https://datasheetspdf.com/pdf-file/1412058/SMSC/COM2661-3/1
        ///   https://bitsavers.org/components/microchipTechnology/_dataBooks/1990_Microchip_Data_Book.pdf
        /// </summary>
        public Uart2661 uart2661 = null!; // created for ND120 only; the other CPU type leaves it null, callers go by cpuType


        /// <summary>
        /// CPU Panel
        /// </summary>
        public Panel panel;

        #region Extended Register File (XRF)
        // There are threee sets of 256 registers which are refered to collectively as the extended register file (XRF)


        /// <summary>
        /// Register File
        /// 
        /// The Register File (where all the different levels store their values when changing levels). Note: only registers 0-7 are saved - the rest in this block is CPU "internal registers"
        /// </summary>
        public RegisterFile RF;



        /// <summary>
        /// Extended register file 1
        /// 
        /// The Register File (where all the different levels store their values when changing levels). Note: only registers 0-7 are saved - the rest in this block is CPU "internal registers"
        /// </summary>
        public RegisterFile RF1;

        /// <summary>
        /// Extended register file 2
        /// </summary>
        public RegisterFile RF2;

        /// <summary>
        /// Extended register file 3 // For PCR/PGC 
        /// </summary>
        public RegisterFile RF3;


        /// <summary>
        /// Bit mapped vector that is set when a TRAP occurs, and makes the microcode jump to the trap vector
        /// </summary>
        public ushort TrapVector;

        /// <summary>
        /// TrapController manages trap vector calculation and priority handling
        /// </summary>
        public TrapController trapController;

        #endregion Extended Register File (XRF)





        /// <summary>
        /// Builds every hardware block of the CPU and wires their events together.
        /// </summary>
        /// <param name="cpu_type">
        /// ND-110 or ND-120; decides the UART and the panel variant.
        /// </param>
        public CpuInternals(CPUType cpu_type)
        {
            cpuType = cpu_type;

            mac = new MAC();
            mic = new MIC();
            cs = new ControlStore();
            cpu_regs = new Registers();

            RF = new RegisterFile("rf");
            RF.InitializeMetadata();

            RF1 = new RegisterFile("rf1");
            RF2 = new RegisterFile("rf2");
            RF3 = new RegisterFile("rf3");


            alu = new BUFALU();
            alu.OnInternalInterrupt += Alu_OnInternalInterrupt;
            idb = new IDB();
            pic = new InterruptSubSystem();

            if (cpuType == CPUType.ND110)
            {
                uart = new Uart();
                uart.OnDataReceived += Uart_OnDataReceived;
                uart.OnTransmitComplete += Uart_OnTransmitComplete;
            }
            else
            {
                uart2661 = new Uart2661();
                uart2661.OnDataReceived += Uart2661_OnDataReceived;
                uart2661.OnTransmitComplete += Uart2661_OnTransmitComplete;
            }


            panel = new Panel();
            panel.OnPanelInterruptEvent += Panel_OnPanelInterruptEvent;

            cpu_regs.PGS = 0x00; // Set ring = 4;

            cpu_regs.STOP = true; // default to stopped, ends up in opcom ?

            // Initialize TrapController
            trapController = new TrapController(this);

            _rtc_interrupt_enabled = false;
        }


        /// <summary>
        /// SIOC microcode will write to IOC register and update these flags
        /// Int 12 enabled together with DataArrived (DA_n) will trigger an interrupt
        /// </summary>
        /// <param name="int12enabled"></param>
        internal void SetUartInt12Enabled(bool int12enabled)
        {
            if (cpuType == CPUType.ND110)
            {
                uart.SetUartInt12Enabled(int12enabled);
            }
            else
            {
                uart2661.SetUartInt12Enabled(int12enabled);
            }
        }

        /// <summary>
        /// SIOC microcode will write to IOC register and update these flags
        /// Int 10 enabled together with TransmitBuffer Empty (TBMT_n) will trigger an interrupt
        /// </summary>
        /// <param name="int10enabled"></param>
        internal void SetUartInt10Enabled(bool int10enabled)
        {
            if (cpuType == CPUType.ND110)
            {
                uart.SetUartInt10Enabled(int10enabled);
            }
            else
            {
                uart2661.SetUartInt10Enabled(int10enabled);
            }
        }

        void SetCpuClockInterrupt(bool int13signal)
        {
            // Set CPU clock interrupt INT13
        }



        /// <summary>
        /// Add Console Character to UART receive queue
        /// 
        /// Function to "emulate" that we received a character from the console serial port. 
        /// Character is forwarded to the UART receive queue. 
        /// Depending on the CPU type, the UART is either a ND-110 UART or a ND-120 UART
        /// </summary>
        /// <param name="ch"></param>
        public void AddConsoleChar(char ch)
        {
            if (cpuType == CPUType.ND110)
            {
                uart?.AddConsoleChar(ch);
            }
            else
            {
                uart2661?.AddConsoleChar(ch);
            }
        }



        #region Interrupt and TRAP vector handling

        private bool _rtc_interrupt_enabled;

        internal bool IsRTCInterruptEnabled()
        {
            return _rtc_interrupt_enabled;
        }

        /// <summary>
        /// Enables the real-time clock interrupt, as the IOX clock control word does.
        /// </summary>
        public void EnableRTCInterrupt()
        {
            _rtc_interrupt_enabled = true;
        }
        /// <summary>
        /// Disables the real-time clock interrupt.
        /// </summary>
        public void DisableRTCInterrupt()
        {
            _rtc_interrupt_enabled = false;
        }

        private bool _panelInterruptDetected;
        private void Panel_OnPanelInterruptEvent()
        {
            if (panel.InterruptVector == Enums.PanelVector.MS20)
            {
                if (IsRTCInterruptEnabled())
                {
                    SetInternalInterrupt(Enums.InterruptPin.INT13);
                }
            }

            SetTrap(0x0E); // Set PANEL interrupt bit (octal 016)


            if (panel.InterruptVector == Enums.PanelVector.MS20)
            {
                //if (_rtc_interrupt_enabled)
                _panelInterruptDetected = true;
            }
            else
                _panelInterruptDetected = true;
        }

        /// <summary>
        /// Did we have a panel interrupt?
        /// </summary>
        /// <returns></returns>
        internal bool PanelInterruptDetected()
        {
            return _panelInterruptDetected;
        }

        /// <summary>
        /// CLear the panel interrupt flag
        /// </summary>
        internal void ClearPanelInterrupt()
        {
            _panelInterruptDetected = false;
        }



        private void Alu_OnInternalInterrupt(Enums.InterruptPin interruptPin)
        {
            SetInternalInterrupt(interruptPin);
        }

        /// <summary>
        /// Fires whenever the ND-110 UART newly receives a character. Raises INT12 ("UART
        /// data available") only when the macro program has actually enabled it via
        /// COMM,SIOC - Uart.Int12Enabled mirrors IOC1 (set by Cpu.cs's Commands.SIOC handler
        /// through SetUartInt12Enabled()), and Uart.CONSOLE_n mirrors bit 6 (~SCONS), per
        /// the documented condition: IOC1 HIGH AND bit 6 HIGH enables DA onto BINT~12.
        /// </summary>
        private void Uart_OnDataReceived()
        {
            if (uart.Int12Enabled && uart.CONSOLE_n)
            {
                SetInternalInterrupt(Enums.InterruptPin.INT12);
            }
        }

        /// <summary>
        /// Fires whenever the ND-110 UART finishes transmitting a character. Raises INT10
        /// ("UART transmit buffer empty") only when the macro program has actually enabled
        /// it via COMM,SIOC - Uart.Int10Enabled mirrors IOC2 (set by Cpu.cs's Commands.SIOC
        /// handler through SetUartInt10Enabled()).
        /// </summary>
        private void Uart_OnTransmitComplete()
        {
            if (uart.Int10Enabled)
            {
                SetInternalInterrupt(Enums.InterruptPin.INT10);
            }
        }

        /// <summary>
        /// ND-120 twin of Uart_OnDataReceived: raises INT12 ("UART data available")
        /// when the macro program has enabled IOC1 via COMM,SIOC AND the console is
        /// in Normal mode. Uart2661.CONSOLE_n now has the SAME polarity as
        /// Uart.CONSOLE_n (TRUE = console in Normal mode, IOR bit 11 = 1) - the
        /// earlier inverted polarity broke the DELILAH TRM2 IOX-302 status handler,
        /// see the SIOC handler comment in Cpu.cs.
        /// </summary>
        private void Uart2661_OnDataReceived()
        {
            if (uart2661.Int12Enabled && uart2661.CONSOLE_n)
            {
                SetInternalInterrupt(Enums.InterruptPin.INT12);
            }
        }

        /// <summary>
        /// ND-120 twin of Uart_OnTransmitComplete: raises INT10 ("UART transmit
        /// buffer empty") when the macro program has enabled IOC2 via COMM,SIOC.
        /// </summary>
        private void Uart2661_OnTransmitComplete()
        {
            if (uart2661.Int10Enabled)
            {
                SetInternalInterrupt(Enums.InterruptPin.INT10);
            }
        }

        internal void SetInternalInterrupt(Enums.InterruptPin interruptPin)
        {
            pic.SetInterruptPIN((ushort)interruptPin);
        }

        /// <summary>
        /// Raises an internal interrupt (trap) on the interrupt controller.
        /// </summary>
        /// <param name="vector">
        /// The trap vector, one of the TrapController.TrapVectors constants.
        /// </param>
        public void SetTrap(int vector)
        {
            // Maintain backward compatibility with existing TrapVector bitmap
            TrapVector = (ushort)(TrapVector | (1 << vector));

            // Use new TrapController for enhanced functionality
            trapController.SetTrapVector(vector);
        }

        /// <summary>
        /// Clear bit in TrapVector bitmap - because this Trap is about to be handled
        /// </summary>
        /// <param name="vector"></param>
        public void ClearTrapVector(int vector)
        {
            if ((vector >= 0) && (vector <= 0xF))
            {
                // Maintain backward compatibility with existing TrapVector bitmap
                TrapVector = (ushort)(TrapVector & ~(1 << vector));

                // Use new TrapController for enhanced functionality
                trapController.ClearTrapVector(vector);
            }
        }

        /// <summary>
        /// Get the Vector from the bitmask. Lower bits/vectors has highest priority
        /// Enhanced with TrapController for automatic signal-based trap detection
        /// </summary>
        /// <returns>Vector, or -1 if no vector</returns>
        public int GetTrapVector()
        {
            // Use TrapController for enhanced trap vector calculation
            // This includes automatic detection of hardware signals and interrupts
            int enhancedVector = trapController.GetActiveTrapVector();

            // The controller's bitmap is the single source of truth: every writer
            // (SetTrap/ClearTrapVector) mirrors into it, and it additionally tracks the
            // LIVE /IRQ pin for vector 0x0F. Sync legacy TrapVector unconditionally.
            // Previously the sync only happened when enhancedVector >= 0, which let a
            // stale bit survive in the legacy bitmap: a few-cycle /IRQ pulse inside the
            // TRA IIC epilogue (mask/fence restored at CS 000713-000715 while the IRQ
            // enable flip-flop is still ON, before CS 000675's PIC,IOF) latched 0x0F into
            // BOTH bitmaps mid-instruction (FETCH false, so not dispatched); after the
            // pulse the controller cleared its live-tracked 0x0F bit and returned -1, and
            // the legacy fallback below then resurrected the stale 0x0F - dispatching a
            // macro interrupt with the interrupt system OFF and nothing pending. RDVC
            // then read vector 0, which the ITSRV table (CS 003740) maps to level 10,
            // derailing INSTRUCTION-VERIFY into an inactive level (traced 2026-07-11).
            TrapVector = trapController.TrapVectorBitmap;
            return enhancedVector;
        }

        /// <summary>
        /// Convenience method to set memory protection violation trap
        /// </summary>
        public void SetMemoryProtectionTrap()
        {
            SetTrap(TrapController.TrapVectors.MEMORY_PROTECT);
        }

        /// <summary>
        /// Convenience method to set page fault trap
        /// </summary>
        public void SetPageFaultTrap()
        {
            SetTrap(TrapController.TrapVectors.PAGE_FAULT);
        }

        /// <summary>
        /// Convenience method to set arithmetic overflow trap
        /// </summary>
        public void SetArithmeticOverflowTrap()
        {
            SetTrap(TrapController.TrapVectors.ARITHMETIC_OVERFLOW);
        }

        /// <summary>
        /// Check if any immediate traps are pending (vectors 0x00-0x0D)
        /// </summary>
        public bool HasImmediateTraps()
        {
            return trapController.HasActiveTrapVectors &&
                   trapController.GetActiveTrapVector() <= 0x0D;
        }

        /// <summary>
        /// Check if any interrupt-type traps are pending (vectors 0x0E-0x0F)
        /// </summary>
        public bool HasInterruptTraps()
        {
            int vector = trapController.GetActiveTrapVector();
            return vector == 0x0E || vector == 0x0F;
        }

        internal int ReadIORegister()
        {
            if (cpuType == CPUType.ND110)
            {
                return uart.ReadIORegister();
            }
            else
            {
                return uart2661.ReadIORegister();
            }
        }


        /// <summary>
        /// Load PROM to Control Store ram. Reset devices
        /// </summary>
        internal void MasterClear()
        {
            // TODO: Do master Clear....
            // Reload Control Store from disk ?
        }



        #endregion Interrupt and TRAP vector handling
    }
}
