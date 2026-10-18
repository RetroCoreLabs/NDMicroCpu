//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using NDMicroCpu.Core.Common;


// Reference documentation:


// ND-120 CX HW
// http://www.norsk-data.com/hardware/nd-100/nd-350104.html

// SCN2661A UART
// http://www.norsk-data.com/library/libother/extern/SCN2661.pdf


// Enhanced Programmable Communication Interface EPCI 
// https://datasheetspdf.com/pdf-file/1412058/SMSC/COM2661-3/1


// MAME ?
// https://github.com/mamedev/mame/blob/62c0904ba46c497bc45605709a02963b582e14ea/src/devices/machine/scn_pci.cpp

namespace NDMicroCpu.Core
{

    #region UART Enums
    /// <summary>
    /// Mode and Baud Rate Factor
    /// </summary>
    public enum ModeAndBaudRateFactor
    {
        /// <summary>
        /// Synchronous, 1 times the baud rate clock.
        /// </summary>
        Synchronous_1X_rate = 0,
        /// <summary>
        /// Asynchronous, 1 times the baud rate clock.
        /// </summary>
        Asynchronous_1X_rate = 1,
        /// <summary>
        /// Asynchronous, 16 times the baud rate clock.
        /// </summary>
        Asynchronous_16X_rate = 2,
        /// <summary>
        /// Asynchronous, 64 times the baud rate clock.
        /// </summary>
        Asynchronous_64X_rate = 3,
    }


    /// <summary>
    /// Parity Type
    /// </summary>
    public enum ParityType
    {
        /// <summary>
        /// Odd parity.
        /// </summary>
        Odd = 0,
        /// <summary>
        /// Even parity.
        /// </summary>
        Even = 1,
    }

    /// <summary>
    /// Number of stop bits, mode register 1 bits 7-6 of the 2661.
    /// </summary>
    public enum StopBits
    {
        /// <summary>
        /// Not a valid setting.
        /// </summary>
        Invalid = 0,
        /// <summary>
        /// One stop bit.
        /// </summary>
        OneStopBit = 1,
        /// <summary>
        /// One and a half stop bits.
        /// </summary>
        OneAndAHalfStopBits = 2,
        /// <summary>
        /// Two stop bits.
        /// </summary>
        TwoStopBits = 3
    }



    /// <summary>
    /// Operating Mode bits.
    /// 00 = Normal operation, 01= Async (Automatic Echo mode), Synch: SYN AND/OR DLE STRIPPING MODE, 01 = LOCAL LOOPBACK, 11=REMOTE LOOPBACK
    /// </summary>
    public enum OperationMode
    {

        /// <summary>
        /// Normal full duplex operation.
        /// </summary>
        NormalOperation = 0,
        /// <summary>
        /// Asynchronous: automatic echo of received data. Synchronous: SYN and DLE stripping.
        /// </summary>
        Async_AutomaticEchoMode = 1,  // Synch: SYN AND/OR DLE STRIPPING MODE
        /// <summary>
        /// Local loopback: transmitted data is looped back to the receiver inside the chip.
        /// </summary>
        LocalLoopbackMode = 2,
        /// <summary>
        /// Remote loopback: received data is looped back out on the transmitter.
        /// </summary>
        RemoteLoopbackMode = 3
    }


    /// <summary>
    /// MR16 in SYNC mode
    /// </summary>
    public enum TransparencyControl
    {
        /// <summary>
        /// Normal: control characters are acted on.
        /// </summary>
        Normal = 0,
        /// <summary>
        /// Transparent: control characters pass as data.
        /// </summary>
        Transparent = 1
    }



    /// <summary>
    /// MR17 in SYNC mode
    /// </summary>
    public enum NumberOfSYNchar
    {
        /// <summary>
        /// Two SYN characters start a synchronous frame.
        /// </summary>
        DoubleSYN = 0,
        /// <summary>
        /// One SYN character starts a synchronous frame.
        /// </summary>
        SingleSYN = 1
    }




    #endregion UART Enums

    #region UART Config

    /// <summary>
    /// UART Configuration Registers
    /// </summary>
    internal class UartConfigRegs
    {
        internal bool TxEN { get; set; }
        internal bool ForceDTRLow { get; set; }
        internal bool RxEN { get; set; }
        internal bool CR3 { get; set; }
        internal bool ResetError { get; set; }
        internal bool ForceRTSLow { get; set; }
        internal OperationMode operationMode { get; set; }


        /// <summary>
        /// Table 1 in the SCN2661A datasheet
        /// </summary>
        ushort[] baudRate6881A = new ushort[]
        {
              50,
              75,
              110,
              134, // Actual: 134.5 
              150,
              200,
              300,
              600,
              1050,
              1200,
              1800,
              2000,
              2400,
              4800,
              9600,
              19200
        };


        #region Mode register 1

        //Common
        internal ModeAndBaudRateFactor modeAndBaudRateFactor;
        internal byte charLen;
        internal bool parityEnabled;
        internal ParityType parityType;


        // ASYNC
        internal StopBits stopBits;

        // SYNC
        TransparencyControl transparencyControl;
        NumberOfSYNchar numberOfSYNchar;

        #endregion Mode register 1


        #region Mode Register 2
        ushort baudRate;

        #endregion Mode register 2

        internal byte[] modeRegister = new byte[2];
        internal byte commandRegister = 0;

        internal void Clear()
        {
            modeRegister[0] = 0;
            modeRegister[1] = 0;
            commandRegister = 0;
        }

        internal void SetCommandRegister(byte data)
        {
            commandRegister = data;

            TxEN = ((commandRegister >> 0) & 0x01) == 1; // Transmit Control bit. 0 = Disable transmitter, 1 = Enable transmitter
            ForceDTRLow = ((commandRegister >> 1) & 0x01) == 1; // 0 = Force /DTR output high, 1= Force /DTR output low
            RxEN = ((commandRegister >> 2) & 0x01) == 1; // Receive Control bit. 0 = Disable receiver, 1 = Enable receiver

            CR3 = ((commandRegister >> 3) & 0x01) == 1;

            ResetError = ((commandRegister >> 4) & 0x01) == 1; // 1=Reset error flag in status (FE;OD; PE/DLE detect), 0=normal (no effect?)

            ForceRTSLow = ((commandRegister >> 5) & 0x01) == 1; // 0 = Force /RTS output high, 1= Force /RTS output low
            operationMode = (OperationMode)((commandRegister >> 6) & 0x03); // Operating Mode bits. 00 = Normal operation, 01= Async (Automatic Echo mode), Synch: SYN AND/OR DLE STRIPPING MODE, 01 = LOCAL LOOPBACK, 11=REMOTE LOOPBACK

        }

        internal void SetModeRegisters(byte writeMode, byte data)
        {
            if (writeMode == 1)
            {

                // MR11 and MR 10 bits are used to select the operating mode and baud rate factor.
                modeAndBaudRateFactor = (ModeAndBaudRateFactor)(data & 0x03);

                // MR13 and MR12 bits are used to select the character length.
                charLen = (byte)((data >> 2) & 0x03); // Character length bits. 00 = 5 bits, 01 = 6 bits, 10 = 7 bits, 11 = 8 bits
                charLen += 5;

                // MR14 bit is used to select the parity.
                parityEnabled = ((data >> 4) & 0x01) == 1; // 0 = Disable parity, 1 = Enable parity

                // MR15 bit is used to select the parity type.
                parityType = (ParityType)((data >> 5) & 0x01); // 0 = Odd parity, 1 = Even parity   



                if (modeAndBaudRateFactor == ModeAndBaudRateFactor.Synchronous_1X_rate)
                {
                    // Synchronous mode
                    // MR16 bit 
                    transparencyControl = (TransparencyControl)((data >> 6) & 0x01);

                    // MR17 bit is used to select the number of SYN characters.
                    numberOfSYNchar = (NumberOfSYNchar)((data >> 7) & 0x01);

                }
                else
                {
                    // Asynchronous mode
                    // MR16 and MR17 bits are used to select the number of stop bits.
                    stopBits = (StopBits)((data >> 6) & 0x03);
                }



                // Write MODE register 1
                modeRegister[writeMode - 1] = data;
            }
            else
            {
                //MR20-23 
                baudRate = baudRate6881A[data & 0x0F];

                // Write MODE register 2
                modeRegister[writeMode - 1] = data;
            }
        }

        internal void SetSynDleRegisters(byte data)
        {
            throw new NotImplementedException();
        }
    }

    #endregion UART Config


    #region UART SCN2661A implementation & ND-120/CX PCB 3202D IORegister
    /// <summary>
    /// UART 2661 emulation
    /// This is the UART that is used on the ND-120 CPU board 3202 supporting RS-232, not current loop as all other ND-CPU console UARTs until now.|
    /// The ND-120/CX CPU Board 3202D has an SCN2661A UART.
    /// 
    /// Some pins from the UART are not connected:
    /// * /TXEMT (Transmit Empty) - Not connected
    /// * /DTR (Data Terminal Ready) - Not connected
    /// * /RTS (Request To Send) - Not connected
    /// 
    /// Some pins are connected to GND:
    /// * /DCD (Data Carrier Detect) - Connected to GND
    /// * /DSR (Data Set Ready) - Connected to GND
    /// * /CTS (Clear To Send) - Connected to GND
    /// </summary>
    /// http://www.norsk-data.com/library/libother/extern/SCN2661.pdf
    /// https://datasheetspdf.com/pdf/1412058/SMSC/COM2661-3/1
    public class Uart2661
    {
        /// <summary>
        /// Receives each character the UART transmits.
        /// </summary>
        /// <param name="out_char">
        /// The character sent.
        /// </param>
        public delegate void TextOutput(char out_char);
        /// <summary>
        /// Raised for every character the UART transmits, for the console view.
        /// </summary>
        public event TextOutput? OnTextOutput;

        /// <summary>
        /// Raised when a received character becomes newly visible (one event per
        /// character - re-armed when the receive holding register is read). ND-120
        /// twin of Uart.OnDataReceived: CpuInternals subscribes and, when the macro
        /// program has enabled IOC1 via COMM,SIOC and the console is NOT assigned to
        /// OPCOM, raises InterruptPin.INT12 ("UART data available"). OPCOM itself
        /// never uses interrupts - it polls via IDBS,IOR - which is why boot/OPCOM
        /// worked without any of this but BPUN programs (interrupt-driven console
        /// I/O, e.g. INSTRUCTION-B) hung at the '>' prompt.
        /// </summary>
        public event Action? OnDataReceived;

        /// <summary>
        /// Raised when a transmitted character completes (functional timing: one
        /// Tick after the transmit-holding-register write - deliberately NOT
        /// cycle-aligned to real 2661 baud timing). ND-120 twin of
        /// Uart.OnTransmitComplete: CpuInternals raises InterruptPin.INT10 ("UART
        /// transmit buffer empty") when IOC2 is enabled.
        /// </summary>
        public event Action? OnTransmitComplete;

        /// <summary>
        /// True while the currently visible received character has already fired
        /// OnDataReceived - cleared when the receive holding register is read so the
        /// next queued character fires its own event (one interrupt per character,
        /// matching the ND-110 Uart's DR/DRR behavior functionally).
        /// </summary>
        bool _rxEventFired = false;

        /// <summary>
        /// Set by WriteTransmitHoldingRegister; the next Tick() fires
        /// OnTransmitComplete. The one-tick deferral keeps the interrupt out of the
        /// middle of the microinstruction that performed the write.
        /// </summary>
        bool _txCompletePending = false;

        UartConfigRegs regs;

        /// <summary>
        /// Characters waiting to be received, pushed by the console and taken by the receiver.
        /// </summary>
        public Queue<byte> KeyboardQueue { get; set; }

        /// <summary>
        /// Creates the UART in its reset state with an empty receive queue.
        /// </summary>
        public Uart2661()
        {
            regs = new UartConfigRegs();
            regs.Clear();
            KeyboardQueue = new Queue<byte>();
        }



        /// <summary>
        /// Tick UART clock (called once per micro-cycle from Cpu.TickCpu). Drives the
        /// interrupt events: fires OnDataReceived once per newly visible received
        /// character, and OnTransmitComplete one tick after a transmit write. The
        /// events themselves are unconditional - the IOC1/IOC2 enable gating lives in
        /// CpuInternals' handlers, mirroring the ND-110 Uart arrangement.
        /// </summary>
        public void Tick()
        {
            if (_txCompletePending)
            {
                _txCompletePending = false;
                OnTransmitComplete?.Invoke();
            }

            if (KeyboardQueue.Count > 0 && !_rxEventFired)
            {
                _rxEventFired = true;
                OnDataReceived?.Invoke();
            }
        }

        /// <summary>
        /// Add character received from  console-terminal keyboard to queue (To be 'received' by UART later)
        /// </summary>
        /// <param name="ch"></param>
        public void AddConsoleChar(char ch)
        {
            KeyboardQueue.Enqueue((byte)ch);
            //TryReadNext();
        }

        internal void Reset()
        {
            regs.Clear();
        }


        #region READ 
        internal ushort Read(byte address)
        {
            Logger.Log($"Uart2661.Read({address})", Logger.LogLevel.CPU);
            switch (address)
            {
                case 0x00: // Read receive holding register 

                    return ReadReceiveHoldingRegister();

                case 0x01: // Read status register
                    return ReadStatusRegister();

                case 0x02: // Read mode register 1 and 2
                    return ReadModeRegister1And2();

                case 0x03: // Read command register
                    return ReadCommandRegister();

                default:
                    break;
            }

            return 0x00;
        }



        /// <summary>
        /// Address 0x00
        /// </summary>
        /// <returns></returns>
        private byte ReadReceiveHoldingRegister()
        {
            byte data = 0;
            if (KeyboardQueue.Count > 0)
            {

                data = (byte)KeyboardQueue.Dequeue();
            }

            // The visible character has been consumed - re-arm the receive event so
            // the next queued character fires its own OnDataReceived (see Tick()).
            _rxEventFired = false;

            return data;
        }


        /// <summary>
        /// Address 0x01   
        /// </summary>
        /// <returns></returns>
        private byte ReadStatusRegister()
        {
            byte retval = 0;


            retval |= 1 << 0; // TxRDY (Transmit holding reg empty)

            if (KeyboardQueue.Count > 0)
            {
                retval |= 1 << 1; // RxRDY (Receive holding reg has data)
            }

            retval |= 1 << 6; // DCD (Data Carrier Detect) - Detected low
            retval |= 1 << 7; // DSR (Data Set Ready) - Detected low

            return retval;
        }


        byte readMode = 1;
        /// <summary>
        /// Address 0x02
        /// </summary>
        /// <returns></returns>
        private byte ReadModeRegister1And2()
        {
            byte retval = 0;
            if (readMode == 1)
            {
                // Return MODE register 1. Switch to MODE register 2
                retval = regs.modeRegister[readMode - 1];
                readMode = 2;
            }
            else
            {

                // Return MODE register 2. Switch back to MODE register 1
                retval = regs.modeRegister[readMode - 1];
                readMode = 1;
            }



            return retval;
        }

        /// <summary>
        /// Address 0x03
        /// </summary>
        /// <returns></returns>
        private ushort ReadCommandRegister()
        {
            return regs.commandRegister;
        }

        #endregion READ 



        #region WRITE
        internal void Write(byte address, byte data)
        {
            Logger.Log($"Uart2661.Write({address}, 0x{data:X2})", Logger.LogLevel.CPU);

            switch (address)
            {
                case 0x00: // Write transmit holding register
                    WriteTransmitHoldingRegister(data);
                    break;

                case 0x01: // Write SYN1/SYN2/DLE registers
                    WriteSynDleRegisters(data);
                    break;

                case 0x02: // Write mode registers 1 and 2
                    WriteModeRegisters(data);
                    break;
                case 0x03: // Write command register
                    WriteCommandRegister(data);
                    break;
                default:
                    break;
            }
        }




        /// <summary>
        /// Address 0x00 - Transmit data to console-terminal
        /// </summary>
        /// <param name="data"></param>
        private void WriteTransmitHoldingRegister(byte data)
        {
            data = ((byte)(data & 0xFF));

            // A transmit completes (functionally instantaneous - see Tick()); the
            // INT10 "transmit buffer empty" event must fire even for NUL padding
            // bytes, which the level-1 test framework sends after every character.
            _txCompletePending = true;

            if (data == 00) return;

            Debug.Write((char)data);
            if (OnTextOutput != null)
            {
                OnTextOutput((char)data);

            }
            else
            {
                //Debug.Write(data);
                Console.Write((char)data);
            }
        }


        /// <summary>
        /// Address 0x01
        /// </summary>
        /// <param name="data"></param>
        private void WriteSynDleRegisters(byte data)
        {
            regs.SetSynDleRegisters(data);
        }



        byte writeMode = 1;
        /// <summary>
        /// Address 0x02
        /// </summary>
        /// <param name="data"></param>
        private void WriteModeRegisters(byte data)
        {
            regs.SetModeRegisters(writeMode, data);

            if (writeMode == 1)
            {
                writeMode = 2;
            }
            else
            {
                writeMode = 1;
            }
        }

        /// <summary>
        /// Address 0x03
        /// </summary>
        /// <param name="data"></param>
        private void WriteCommandRegister(byte data)
        {
            regs.SetCommandRegister(data);
        }



        #region ND-120/CX PCB 3202D IORegister

        /// <summary>
        /// Transmit ready (negated)
        /// </summary>        
        bool TxDRDY_n = false;

        bool EAUTO_n = true;
        bool LOCK_n = true;

        /// <summary>
        /// Console-assignment flag, exposed on IOR bit 11: TRUE (bit = 1) when the
        /// console is in NORMAL mode (owned by the running program), FALSE when
        /// assigned to OPCOM - the SAME polarity as Uart.CONSOLE_n on the ND-110
        /// board. Set by Cpu.cs's Commands.SIOC handler from IOC bit 6. The DELILAH
        /// IOX-302 handler (TRM2, CS 000531-000536) tests this bit to decide whether
        /// the data-available bit belongs to the program or to OPCOM. Default false =
        /// console at OPCOM at power-up, before any SIOC has run.
        /// </summary>
        public bool CONSOLE_n = false;

        /// <summary>
        /// TH2 CPU Board Switch
        /// </summary>
        ushort UART_BAUD_RATE = 0b1111; //<= 19200, 1110 = 9600

        internal ushort ReadIORegister()
        {
            ushort reg = 0;


            if (TxDRDY_n)
                reg |= 1 << 15; // TxRDY (Transmit holding reg empty)

            //if (RxRDY_n)
            if (KeyboardQueue.Count == 0)
                reg |= 1 << 14; // RxRDY (Receive holding reg has data), negated

            if (EAUTO_n)
                reg |= 1 << 13; // EAUTO_n

            if (LOCK_n)
                reg |= 1 << 12; // LOCK_n

            if (CONSOLE_n)
                reg |= 1 << 11; // CONSOLE_n

            reg |= 1 << 4; // IDB bit 4 is always set (pulled high in sheet 42 of the 3202D schematics)

            // Baud rate
            reg = (ushort)(reg | (UART_BAUD_RATE & 0x0F)); // bits 3 to 0



            //Console.WriteLine($"Read IOREG: {Utils.Int2Oct(reg)}");
            return reg;
        }

        /// <summary>
        /// Int12 (data-available) enabled state, mirrors IOC1 - set by Cpu.cs's
        /// Commands.SIOC handler whenever the macro program writes IOControl. Gates
        /// the INT12 raise in CpuInternals.Uart2661_OnDataReceived. Twin of
        /// Uart.Int12Enabled on the ND-110 board.
        /// </summary>
        internal bool Int12Enabled { get; private set; }

        /// <summary>
        /// Int10 (transmit-buffer-empty) enabled state, mirrors IOC2 - set by Cpu.cs's
        /// Commands.SIOC handler whenever the macro program writes IOControl. Gates
        /// the INT10 raise in CpuInternals.Uart2661_OnTransmitComplete. Twin of
        /// Uart.Int10Enabled on the ND-110 board.
        /// </summary>
        internal bool Int10Enabled { get; private set; }

        internal void SetUartInt12Enabled(bool int12enabled)
        {
            Int12Enabled = int12enabled;
        }

        internal void SetUartInt10Enabled(bool int10enabled)
        {
            Int10Enabled = int10enabled;
        }

        #endregion ND-120/CX PCB 3202D IORegister


        #endregion WRITE

        #endregion UART SCN2661A implementation
    }
}


/*
 
68661A (BRCLK = 4.9152MHz)
MR23–20 BAUD RATE ACTUAL FREQUENCY
16X CLOCK
PERCENT
ERROR DIVISOR
0000 50 0.8kHz — 6144
0001 75 1.2 — 4096
0010 110 1.7598 –0.01 2793
0011 134.5 2.152 — 2284
0100 150 2.4 — 2048
0101 200 3.2 — 1536
0110 300 4.8 — 1024
0111 600 9.6 — 512
1000 1050 16.8329 0.196 292
1001 1200 19.2 — 256
1010 1800 28.7438 –0.19 171
1011 2000 31.9168 –0.26 154
1100 2400 38.4 — 128
1101 4800 76.8 — 64
1110 9600 153.6 — 32
1111 19200 307.2 — 16


*/
