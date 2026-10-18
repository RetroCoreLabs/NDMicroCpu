//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.Support;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// Console terminal interface
    /// The console terminal interface is on the CPU board.It occupies the I/0 address address range 300 (octal) - 307 (octal). Addresses 301 (octal) and 304 (octal) are not used.
    /// </summary>


    /* BITS ON ND100, enabled on EIOR~
     * 
     * 15 - UART - A High level on "TRANSMIT BUFFER REGISTER EMPTY" indicates the transmit buffer register has transferred its data to the transmitter register and is ready for new data
     * 14 - UART - A high level on "DATA RECEIVED" indicates a character has been received and transferred to the receiver buffer register
     * 13 - EAUTO~ 
     * 12 - LOCK~
     * 11 - CONSOLE ~ (output from IOControl IDB-Bit 6)
     * 10 - OVERRUN ERROR
     * 09 - PARITY ERROR
     * 08 - FRAMING ERROR
     * 00-07 - UART DATA

     */




    public class Uart
    {
        private const ushort STATUS_TRANSMIT_BUFFER_EMPTY = 15;

        /// <summary>
        /// Mirrors the real IM6402's DR (Data Received, pin 19) / DRR (Data Received Reset,
        /// pin 18) pair - see http://www.bitsavers.org/pdf/dec/pdp8/cmos8/_dataSheets/IM6402.pdf.
        /// DR is a genuine hardware latch: set autonomously by the receiver clock (pin 17,
        /// RRC) whenever a character finishes arriving, and cleared ONLY by a DRR pulse -
        /// never by reading status. In this emulator that maps to:
        ///   - SET only from Tick() -> TryReadNext() (time-driven, matches the receiver clock)
        ///   - CLEARED only from ResetDataAvailable() (COMM,RSDA - the DRR pulse equivalent)
        /// ReadIORegister() (IDBS,IOR - a plain status read) must NEVER touch this bit. Doing
        /// so previously coupled receive-buffer timing to how often status happened to be
        /// polled instead of to elapsed time, silently dropping typed OPCOM input - see
        /// TestND110/Boot/TestInstructionVerify.cs.
        /// </summary>
        private const ushort STATUS_DATA_RECEIVED = 14;
        private const ushort STATUS_EAUTO_n = 13;
        private const ushort STATUS_LOCK_n = 12;
        private const ushort STATUS_CONSOLE_n = 11;
        private const ushort STATUS_OVERRUN_ERROR = 10;
        private const ushort STATUS_PARITY_ERROR = 9;
        private const ushort STATUS_FRAMING_ERROR = 9;



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
        /// Raised whenever a character is newly received (DR set) - see the DR/DRR
        /// documentation on STATUS_DATA_RECEIVED. CpuInternals subscribes and, if IOC1 is
        /// enabled (Cpu.cs's Commands.SIOC handler - IOC1 HIGH and bit 6 (~SCONS) HIGH),
        /// raises InterruptPin.INT12 ("UART data available").
        /// </summary>
        public event Action? OnDataReceived;

        /// <summary>
        /// Raised whenever a pending transmission completes (TRANSMIT_BUFFER_EMPTY becomes
        /// true - see Tick()). CpuInternals subscribes and, if IOC2 is enabled (Cpu.cs's
        /// Commands.SIOC handler - IOC2 HIGH enables ~TBMT onto BINT~10), raises
        /// InterruptPin.INT10 ("UART transmit buffer empty").
        /// </summary>
        public event Action? OnTransmitComplete;

        /// <summary>
        /// Characters waiting to be received, pushed by the console and taken by the receiver.
        /// </summary>
        public Queue<byte> KeyboardQueue { get; set; }

        ushort io_register = 9;

        /// <summary>
        /// Set from bit 6 from IOControl. Read back to Bit 11 on the UART/IO register
        /// Active LOW
        /// </summary>
        public bool CONSOLE_n { get; set; }

        /// <summary>
        /// Controls bit 13 (EAUTO~)
        /// TRUE  - During boot goes to STOP2 (Continue to MOPC stop) (uc-address 2134)
        /// FALSE - During boot, try to boot via ALD
        /// </summary>
        public bool EAUTO_n { get; set; }

        /// <summary>
        /// Controls bit 12 (LOCK~)
        /// 
        /// TRUE = Not locked ?
        /// FALSE = Lock?
        /// </summary>
        public bool LOCK_n { get; set; }



        /// <summary>
        /// Set to true after data has been transmitted to trigger "SEND" interrupt
        /// </summary>
        internal bool TRANSMIT_BUFFER_EMPTY = true;


        internal readonly ushort IODELAY_TERMINAL = 25; // 512
        internal ushort DELAY_TRANSMIT = 0;
        internal ushort DELAY_READ = 0;


        /// <summary>
        /// Configured word length in bits (5-8). Default 7 for ND-110 terminal (7E1).
        /// Controlled by IOControl bit 12 via COMM,SIOC (set by microcode TRM3 handler for IOX 303).
        /// Note: IOControl bits are INVERTED relative to IOX 303 spec because TRM3 inverts its input.
        ///   IOControl bit 12 = 0 → 7-bit word length (default terminal mode)
        ///   IOControl bit 12 = 1 → 8-bit word length (binary loader mode via DVACT value 4005)
        /// </summary>
        internal int ConfiguredWordLength = 7;

        /// <summary>
        /// Data mask applied to received bytes based on ConfiguredWordLength.
        /// </summary>
        internal byte DataMask = 0x7F;

        /// <summary>
        /// Whether parity checking is enabled. Default true for ND-110 terminal (7E1).
        /// Controlled by IOControl bit 14 via COMM,SIOC (inverted from IOX 303 spec).
        ///   IOControl bit 14 = 0 → parity enabled (default)
        ///   IOControl bit 14 = 1 → parity disabled (binary loader mode)
        /// </summary>
        internal bool ParityEnabled = true;


        /// <summary>
        /// Creates the UART in its reset state with an empty receive queue.
        /// </summary>
        public Uart()
        {
            KeyboardQueue = new Queue<byte>();

            Reset();

            EAUTO_n = true; // active low (False will try to boot via ALD)
            LOCK_n = true; // active low (Locked?)
            TRANSMIT_BUFFER_EMPTY = true;
        }


        /// <summary>
        /// Configure UART word length and parity from COMM,SIOC IOControl register.
        /// Called by Cpu.cs when COMM,SIOC is executed.
        ///
        /// The IOControl bits 11-14 contain UART configuration as processed by the TRM3 microcode
        /// handler (IOX 303 write). TRM3 inverts the input value before masking and storing,
        /// so the encoding is inverted relative to the IOX 303 software spec.
        ///
        /// The TRM31 mask (74002 octal = 0x7802) only preserves bit 12 for word length
        /// (bit 11 is always masked to 0), giving a single-bit word length control:
        ///   bit 12 = 0 → 7-bit mode (normal terminal)
        ///   bit 12 = 1 → 8-bit mode (binary loader)
        /// </summary>
        internal void ConfigureFromSIOC(ushort ioControl)
        {
            // Bit 12: word length (inverted from IOX 303 spec)
            //   0 = 7-bit (from IOX 303 value with bit 12=1 after inversion)
            //   1 = 8-bit (from IOX 303 value with bit 12=0 after inversion, e.g. DVACT's 4005)
            bool wordLength8Bit = (ioControl & (1 << 12)) != 0;
            ConfiguredWordLength = wordLength8Bit ? 8 : 7;
            DataMask = wordLength8Bit ? (byte)0xFF : (byte)0x7F;

            // Bit 14: parity enable (inverted from IOX 303 spec)
            //   0 = parity enabled (default, from IOX 303 bit 14=1 after inversion)
            //   1 = parity disabled (from IOX 303 bit 14=0 after inversion, e.g. DVACT's 4005)
            ParityEnabled = (ioControl & (1 << 14)) == 0;
        }

        /// <summary>
        /// Tick UART clock.
        /// Used to trigger IRQ when character has been sent
        /// </summary>
        public void Tick()
        {
            if (DELAY_TRANSMIT > 0)
            {
                DELAY_TRANSMIT--;
                if (DELAY_TRANSMIT == 0)
                {
                    TRANSMIT_BUFFER_EMPTY = true;
                    OnTransmitComplete?.Invoke();
                }
            }

            // Receive-side timing must be driven by elapsed time (once per micro-cycle),
            // not by how often the CPU happens to poll status via IDBS,IOR - see
            // ReadIORegister() below, which used to call this and made status reads
            // destructive/timing-sensitive against real hardware, where a STATUS read is a
            // non-destructive peek and only reading the actual data register consumes it.
            TryReadNext();
        }

        /// <summary>
        /// Receive a character to the UART from the Console terminal
        /// </summary>
        /// <param name="ch"></param>
        internal void AddConsoleChar(char ch)
        {
            KeyboardQueue.Enqueue((byte)ch);
            TryReadNext();
        }

        internal ushort Read_Data()
        {
            // ND-120 func
            Debug.Assert(false);

            return 0;
        }


        void TryReadNext()
        {
            if (!(io_register.BIT(STATUS_DATA_RECEIVED))) // only read new char if the previous has been read
            {
                if (DELAY_READ > 0)
                    DELAY_READ--;


                if ((KeyboardQueue.Count > 0) && (DELAY_READ == 0))
                {
                    byte rawByte = KeyboardQueue.Dequeue();

                    // Apply word length mask to received data
                    byte maskedByte = (byte)(rawByte & DataMask);

                    // Compute parity error: check even parity on the data bits within configured word length
                    io_register = (ushort)((io_register & 0xFF00) | maskedByte);
                    io_register = io_register.SetBIT(STATUS_DATA_RECEIVED);

                    if (ParityEnabled)
                    {
                        // Count set bits in the masked data for even parity check
                        // If the raw byte has bit 7 as parity (7-bit mode), check it
                        // In 7-bit mode: bits 0-6 are data, bit 7 is parity bit on the wire
                        // Even parity: total number of 1-bits (data + parity) should be even
                        if (ConfiguredWordLength < 8)
                        {
                            int dataBits = maskedByte;
                            int parityBit = (rawByte >> ConfiguredWordLength) & 1;
                            int bitCount = 0;
                            int tmp = dataBits;
                            for (int i = 0; i < ConfiguredWordLength; i++)
                            {
                                bitCount += tmp & 1;
                                tmp >>= 1;
                            }
                            bitCount += parityBit;
                            // Even parity: bitCount should be even
                            if ((bitCount & 1) != 0)
                            {
                                io_register = io_register.SetBIT(STATUS_PARITY_ERROR);
                            }
                            else
                            {
                                io_register = io_register.ClearBIT(STATUS_PARITY_ERROR);
                            }
                        }
                        else
                        {
                            // 8-bit mode with parity: no parity bit available in the byte
                            io_register = io_register.ClearBIT(STATUS_PARITY_ERROR);
                        }
                    }
                    else
                    {
                        io_register = io_register.ClearBIT(STATUS_PARITY_ERROR);
                    }

                    DELAY_READ = (ushort)(IODELAY_TERMINAL * 10); // give cpu some breathing time..

                    OnDataReceived?.Invoke();
                }
                else
                {
                    io_register = io_register.ClearBIT(STATUS_DATA_RECEIVED);
                    io_register = (ushort)(io_register & 0xFF00); // Clear data bits
                }

            }
        }

        internal ushort ReadIORegister()
        {
            ushort reg = io_register;

            // Remove TBRE bit (Transmit buffer is empty)
            //io_register = (ushort)(io_register & ~(1 << 15));


            // NOTE: The bits are inverted so that microcode works correctly

            if (!CONSOLE_n)
            {
                //reg = (ushort)(reg & ~(1 << 11));
                reg = reg.ClearBIT(STATUS_CONSOLE_n);
            }
            else
            {
                //reg |= 1 << 11;
                reg = reg.SetBIT(STATUS_CONSOLE_n);
            }

            if (LOCK_n)
            {
                //reg = (ushort)(reg & ~(1 << 12));
                reg = reg.ClearBIT(STATUS_LOCK_n);
            }
            else
            {
                //reg |= 1 << 12;
                reg = reg.SetBIT(STATUS_LOCK_n);
            }

            if (EAUTO_n)
            {
                //reg = (ushort)(reg & ~(1 << 13));
                reg = reg.ClearBIT(STATUS_EAUTO_n);
            }
            else
            {
                //reg |= 1 << 13;
                reg = reg.SetBIT(STATUS_EAUTO_n);
            }

            if (TRANSMIT_BUFFER_EMPTY)
            {
                //reg |= 1 << 15;

                reg = reg.SetBIT(STATUS_TRANSMIT_BUFFER_EMPTY);
            }
            else
            {
                //reg = (ushort)(reg & ~(1 << 15));
                reg = reg.ClearBIT(STATUS_TRANSMIT_BUFFER_EMPTY);
            }


            // 
            // * 10 - OVERRUN ERROR
            // * 09 - PARITY ERROR
            // * 08 - FRAMING ERROR
            //reg |= 1 << 10; // OVERRUN ERROR
            //reg |= 1 << 9; // PARITY ERROR

            //reg |= 1 << 8; // FRAMING ERROR 


            io_register = reg;

            Logger.Log($"IOREAD:  [{Numeric.Num2Str(reg)}]", Logger.LogLevel.CPU);
            // NB: status reads are non-destructive on real hardware - do NOT call
            // TryReadNext() here. Receive timing is driven by Tick() instead.




            //Console.WriteLine($"Read IOREG: {Utils.Int2Oct(reg)}");
            return reg;
        }

        internal void ResetDataAvailable()
        {
            // In ND-11O, reset the data available signal from the UART.
            //KeyboardQueue.Clear();

            // This is the DRR pulse - the moment the program CONSUMES a received character.
            // Logged to expose phantom receives (characters the program echoes that were
            // never typed).
            Logger.Log($"UART RSDA consumed=0x{io_register & 0xFF:X2}", Logger.LogLevel.Debug);

            io_register = io_register.ClearBIT(STATUS_DATA_RECEIVED);
            TryReadNext();
        }

        internal void TransmitDataStrobe(byte data)
        {
            TRANSMIT_BUFFER_EMPTY = false;

            Debug.Write((char)data);
            if (OnTextOutput != null)
            {
                OnTextOutput((char)data);

            }
            else
            {
                //Debug.Write(data);
                Console.Write(data);
            }


            Logger.Log($"IOWRITE:  [{Numeric.Num2Str(data)}]", Logger.LogLevel.CPU);
            // Will trigger IRQ if TX SEND IRQ is enabled
            DELAY_TRANSMIT = IODELAY_TERMINAL;
        }

        internal void Reset()
        {
            KeyboardQueue.Clear();
        }

        /// <summary>
        /// Int12 (data-available) enabled state, mirrors IOC1 - set by Cpu.cs's
        /// Commands.SIOC handler whenever the macro program writes IOControl. Gates
        /// OnDataReceived so it is only ever invoked when the CPU has actually asked for it.
        /// </summary>
        internal bool Int12Enabled { get; private set; }

        /// <summary>
        /// Int10 (transmit-buffer-empty) enabled state, mirrors IOC2 - set by Cpu.cs's
        /// Commands.SIOC handler whenever the macro program writes IOControl. Gates
        /// OnTransmitComplete so it is only ever invoked when the CPU has actually asked for it.
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
    }
}

/*

Console terminal interface
The console terminal interface is on the CPU board. It occupies the I/0 address address range 300 (octal) - 307 (octal). Addresses 301 (octal) and 304 (octal) are not used.

read-data
address 300 (octal)
The read-data register contains the most recently received character.

read-status
address 302 (octal)
The read-status register contains the current status of the input channel. The bits are assigned as follows:

Bit 0: Set (= 1) if interrupt on data available.
Bit 1: always zero.
Bit 2: always zero.
Bit 3: Set (= 1) if data is available. Note: bit 3 is never set when the CPU is in OPCOM mode.
Bit 4: Set (= 1) if data is in error (one or more of bits 5-7 set).
Bit 5: Set (= 1) if there was a framing error.
Bit 6: Set (= 1) if there was a parity error.
Bit 7: Set (= 1) if there was an overrun.
Bits 8-15 are always zero.

read-control
address 303 (octal)
The read-control register is used to set the input channel parameters. Bits 0, 11, 12 and 13 and 14 are used. Unused bits should be set to zero. Note: The word length and parity settings also apply for the output channel.

Bit 0: Set to 1 to enable interrupt when data is available.
Bit 11 & 12: Bits 11 and 12 determine the word length. Parity, if used, adds 1 extra bit to the word 1ength.
bit 11	bit 12	word length
1	1	5 bits
0	1	6 bits
1	0	7 bits
0	0	8 bits
Bit 13: Set (=> 1) for one stop bit. Reset (=> 0) for two stop bits (1.5 for 5-bit word length).
Bit 14: Set (=> 1) to make the interface check parity. The word length will be increased by one when parity is being used.
write-data
address 305 (octal)
Data written to the write-data register wi11 be sent to the output channel.

write-status
address 306 (octal)
The write-status register contains the current status of the output channel. The bits are assigned as follows:

Bit 0: Set (= 1) indicates that the interface will generate an interrupt when it is ready for transfer,
Bit 3: Set (= 1) indicates that the transmitter is ready for transfer (data may be written).
Bits 1-2 and 4-5 are not used (always zero).

write-control
address 307 (octal)
The write-control register is used to set the output channel parameters. Only bit 0 is used. Set the other bits to zero.

Bit 0: Set (= 1) to generate interrupts when the device is ready for transfer (a new data word may be written to the write-data register).
The word length and parity (if used) are the same as set in the read-control register. 


*/
