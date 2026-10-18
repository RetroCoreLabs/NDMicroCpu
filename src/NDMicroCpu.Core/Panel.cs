//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{



    // https://www.ndwiki.org/wiki/ND-100_front_panel
    // http://sintran.com/sintran/hardware/nd-other/nd-322691.html


    ///  PANS - Panel Status Register (READ)
    /// +-----------+--------+----------+-------+----+--------+------------+
    /// |	15      |  14    |  13		|   12  | 11 | 10-9-8 | 7    -   0 |
    /// +-----------+--------+----------+-------+----+--------+------------+
    /// |DISP.PRESS |INP PDY | RPAN VAL |PAN INT|  0 | PFUNC  |   RPAN     |
    /// +-----------+--------+----------+-------+----+--------+------------+

    ///  PANC - Panel Control Register (WRITE)
    /// +---+---+--------------+----+----+--------+------------+
    /// |15 | 14|  13		   | 12 | 11 | 10-9-8 | 7    -   0 |
    /// +---+---+--------------+----+----+--------+------------+
    /// | 0 | 0 | Read Request |N.A.|  0 | PFUNC  |   WPAN     |
    /// +---+---+--------------+----+----+--------+------------+


    /* From ND-120 Schematics sheet 40/50 IO/PANCAL. Enabled on /EPANS to IDB (PANS)
     * 
     *  Bit 0-7  <=  PA0-PA7 from PANEL CPU (MC68705U3)
     *  Bit 8-11 <=  STAT0-STAT3 from PANEL CPU
     *  Bit 12   <= VAL (Panel interrupt) - // Also known as "COM RDY", The command in PCOM has been processed
     *  Bit 13   <= READ (Read request from panel processor)
     *  Bit 14   <= /FUL (Panel FIFO is full)
     *  Bit 15   <= PRES (Panel is present)
     */

    public class Panel
    {
        /// <summary>
        /// Raised when the panel requests an interrupt.
        /// </summary>
        public delegate void PanelInterruptEvent();
        /// <summary>
        /// Raised when the panel requests an interrupt, for the CPU to set the panel interrupt pin.
        /// </summary>
        public event PanelInterruptEvent? OnPanelInterruptEvent;


        RealTimeClock rtc;

        /// <summary>
        /// The panel status word, as the micro code reads it with the panel status command.
        /// </summary>
        public ushort EPANS { get; internal set; }

        string textBuffer = "";

        /// <summary>
        /// The last panel interrupt vector the panel raised.
        /// </summary>
        public PanelVector InterruptVector { get; internal set; }

        /// <summary>
        /// The last word the panel returned to a read; the same value as PAN_READ_RESPONSE.
        /// </summary>
        public int PAN_READ_RESPONSE1 => PAN_READ_RESPONSE;

        int panc_command = 0;
        int fifoCnt = 0;

        /// <summary>
        /// Set to true if the display is to rotate text, 4 characters at a time on the display
        /// </summary>
        bool rotate = false;

        /// <summary>
        /// True while the panel has been told to rotate its message, 4 characters at a time
        /// (the MessageControl commands RotateMessage and ClearAndRotate; StopRotatingMessage clears it).
        /// </summary>
        public bool IsRotatingMessage => rotate;

        ControlPanelRegs _cpRegs = new ControlPanelRegs();
        private bool _isStopped;

        enum PFUNC
        {
            Illegal,
            FutureExtension,
            MessageAppend,
            MessageControl,
            UpdateLowSeconds,
            UpdateHighSeconds,
            UpdateLowDays,
            UpdateHighDays,

            MemoryExamine = 8, // After 2 bytes from AB,OCTA2
            DataToExamine = 9, // After 2 bytes with register P (DYTP1:)
            ActiveLevels = 10, // After 2 bytes with register Q  (DYTP2:)
            OutExamineMode = 13, // (EXM02) after 1 byte Q ?
            SendLabel = 14, // After, 2 words: AB,TXT1 then AB,TXT2 (LABEL:)
            F_typed = 15, //  
        }

        enum MessageControl
        {
            /// <summary>
            /// Stop rotating the message
            /// </summary>
            StopRotatingMessage = 0b000,

            /// <summary>
            /// Return display to normal funciont
            /// </summary>
            ReturnDisplayToNormal = 0b001,

            /// <summary>
            /// Clear text buffer and Function display. Prepare for text to be appended
            /// </summary>
            ClearTextBuffer = 0b010,

            /// <summary>
            /// Rotate the message in the text buffer, displaying four characters at a a time
            /// </summary>
            RotateMessage = 0b100,


            /// <summary>
            /// CLear the text and start rotating. (Command 010 plus command 100)
            /// </summary>
            ClearAndRotate = 0b110,


        }

        #region PANEL bits
        /// <summary>
        /// Panel present. Bit 15 also triggers a call to MOPC every 20ms
        /// </summary>
        static readonly ushort PAN_PRES = 1 << 15;

        /// <summary>
        /// When 0, the PAC fifo is full. Is status stays at 0 for more than 2 ms then PAP is not working
        /// </summary>
        static readonly ushort PAN_FULL = 1 << 14;

        /// <summary>
        /// The command requests data from the panel processor.
        /// If 1, then last command PAP processed was a READ, data is in bit 0-7
        /// </summary>
        static readonly ushort PAN_READ = 1 << 13;

        /// <summary>
        /// COM RDY, The command in PCOM has been processed
        /// </summary>
        static readonly ushort PAN_RDY = 1 << 12;

        static readonly ushort PAN_READ_RESPONSE = (ushort)(PAN_PRES | PAN_FULL | PAN_READ | PAN_RDY);
        static readonly ushort PAN_WRITE_RESPONSE = (ushort)(PAN_PRES | PAN_FULL | PAN_RDY);

        #endregion PANEL bits

        /// <summary>
        /// Creates the panel with its real-time clock, in the power-up state.
        /// </summary>
        public Panel()
        {
            EPANS = 0;
            rtc = new RealTimeClock();

            rtc.OnRtcInterruptEvent += Rtc_OnRtcInterruptEvent;
        }

        private void Rtc_OnRtcInterruptEvent()
        {
            TriggerRTC();
        }
        private void TriggerRTC()
        {
            InterruptVector = PanelVector.MS20;

            // If bit 15 is set in the EPANS then MS20 routine will not jump to OPCOM.
            // Should be set when the machine is in RUN mode
            //
            // MOPC-WHILE-RUNNING (2026-07-12): the MS20 microcode (ND-110-RASK CS 002263,
            // "IDBS,MIPANS ... COND,F15 -> MOPC") must ALSO enter the MOPC console service
            // when the console is assigned to the operator (IOControl bit 6 = 0) even
            // though the CPU is running - that is how the OPCOM instruction (150400)
            // works: its INPUT routine polls once, de-assigns the console via WSIOC and
            // CONTINUEs the program (DISPL CS 002421 -> CONT); every following typed
            // character is then serviced by MS20 -> MOPC at 20ms cadence. Previously only
            // _isStopped was considered, so the running-with-console-at-OPCOM state never
            // saw its input (INSTRUCTION-VERIFY's OPCOM command appeared hung).
            if (_isStopped || _consoleAtOpcom)
                EPANS = 0; // clear bit 15 - MS20 routine will call OPCOM
            else
                EPANS = PAN_PRES; // set bit 15 - MS20 routine will NOT call OPCOM routine

            if (OnPanelInterruptEvent != null)
                OnPanelInterruptEvent();
        }

        bool _consoleAtOpcom = true; // boot state: console belongs to the operator

        /// <summary>
        /// Console assignment, from IOControl bit 6 (SIOC): true when the console is
        /// assigned to OPCOM/MOPC (bit 6 = 0), false when the program owns it.
        /// Feeds the MS20 "activate MOPC" decision above.
        /// </summary>
        public void SetConsoleAssignedToOpcom(bool atOpcom)
        {
            _consoleAtOpcom = atOpcom;
        }


        /// <summary>
        /// Force RTC interrupt.
        /// Used by debugger/sync
        /// </summary>
        public bool ForceRTC()
        {
            return rtc.ForceRTC();
        }

        // Called by microcode COMM,CLRRTC
        // Also called by writing to 
        /// <summary>
        /// Clears the real-time clock interrupt, as the micro code command COMM,CLRRTC does.
        /// </summary>
        public void ClearRTC()
        {
            rtc.ClearRTC();
        }


        /// <summary>
        /// The buttons on the operator panel that the emulator can press.
        /// </summary>
        public enum PanelButton
        {
            /// <summary>
            /// The STOP button.
            /// </summary>
            STOP,
            /// <summary>
            /// The LOAD button.
            /// </summary>
            LOAD
        }

        /// <summary>
        /// Presses a panel button, which raises the matching panel interrupt.
        /// </summary>
        /// <param name="button">
        /// The button.
        /// </param>
        public void PushPanelButton(PanelButton button)
        {
            switch (button)
            {
                case PanelButton.STOP:
                    InterruptVector = PanelVector.STOP;
                    break;
                case PanelButton.LOAD:
                    InterruptVector = PanelVector.LOAD;
                    break;
                default:
                    break;
            }

            if (OnPanelInterruptEvent != null)
                OnPanelInterruptEvent();

        }


        internal void LoadFifo(byte command)
        {
            if (fifoCnt == 0)
            {
                fifoCnt++;

                panc_command = command;
            }
            else
            {
                panc_command |= command << 8;
                ProcessTerminalPanc(panc_command);
                fifoCnt = 0;
            }
        }

        internal void ProcessTerminalPanc(int PANC)
        {

            PFUNC cmd = (PFUNC)((PANC >> 8) & 0b111);

            bool isReadRequest = (PANC & 1 << 13) != 0;

            ushort WPAN = (ushort)(PANC & 0xFF);
            ushort RPAN = 0;

            Utils.Log($"Processing terminal PANC. PANC= {cmd} IsRead={isReadRequest} WPAN={WPAN}", true);

            ushort okResp = 0;

            byte bcmd = (byte)cmd;
            bcmd = (byte)(bcmd & 0b111); // make sure its just 3 bits

            // Calculate response
            if (isReadRequest)
            {
                okResp = (ushort)(PAN_READ_RESPONSE | (bcmd << 8));
            }
            else
            {
                okResp = (ushort)(PAN_WRITE_RESPONSE | (bcmd << 8));
            }


            switch (cmd)
            {
                case PFUNC.Illegal:
                    EPANS = okResp;
                    break;
                case PFUNC.FutureExtension:
                    EPANS = okResp;
                    break;
                case PFUNC.MessageAppend:
                    if (isReadRequest)
                    {
                        //what now ?
                    }
                    else
                    {
                        if (WPAN != 0)
                        {
                            char c = (char)WPAN;
                            textBuffer += c;

                            if (WPAN == 0x0E)
                            {
                                // "Shift Out" => switch to an alternate character set or font. ??
                            }
                            else
                            {
                                Utils.LogAlways($"Appending '{c}' to buffer [{textBuffer}]", true);
                            }
                        }
                    }
                    EPANS = okResp;
                    break;

                case PFUNC.MessageControl:
                    MessageControl mc = (MessageControl)(WPAN & 0b111);

                    Utils.Log($"Processing Message Control Command: {mc}  WPAN={WPAN}", true);
                    switch (mc)
                    {
                        case MessageControl.StopRotatingMessage:
                            rotate = false;
                            break;
                        case MessageControl.ReturnDisplayToNormal:
                            break;
                        case MessageControl.ClearTextBuffer:
                            textBuffer = "";
                            break;
                        case MessageControl.RotateMessage:
                            rotate = true;
                            break;
                        case MessageControl.ClearAndRotate:
                            textBuffer = "";
                            rotate = true;
                            break;
                        default:
                            //Debug.Assert(false);
                            break;
                    }

                    EPANS = okResp;
                    break;

                case PFUNC.UpdateLowSeconds:

                    if (isReadRequest)
                    {
                        RPAN = (ushort)(_cpRegs.Seconds & 0xFF);
                        EPANS = (ushort)(okResp | RPAN);
                    }
                    else
                    {
                        _cpRegs.Seconds = (ushort)((_cpRegs.Seconds & 0xff00) | WPAN);
                        EPANS = okResp;
                    }

                    break;
                case PFUNC.UpdateHighSeconds:
                    if (isReadRequest)
                    {
                        RPAN = (ushort)((_cpRegs.Seconds >> 8) & 0xFF);
                        EPANS = (ushort)(okResp | RPAN);
                    }
                    else
                    {
                        _cpRegs.Seconds = (ushort)((_cpRegs.Seconds & 0x00ff) | (WPAN << 8));
                        EPANS = okResp;
                    }
                    break;
                case PFUNC.UpdateLowDays:
                    if (isReadRequest)
                    {
                        RPAN = (ushort)(_cpRegs.Days & 0x00ff);
                        EPANS = (ushort)(okResp | RPAN);
                    }
                    else
                    {
                        _cpRegs.Days &= 0xFF00;
                        _cpRegs.Days |= WPAN;
                        EPANS = okResp;
                    }

                    break;
                case PFUNC.UpdateHighDays:
                    if (isReadRequest)
                    {
                        RPAN = (ushort)(_cpRegs.Days >> 8 & 0x00ff);
                        EPANS = (ushort)(okResp | RPAN);
                    }
                    else
                    {
                        _cpRegs.Days &= 0x00ff;
                        _cpRegs.Days |= (ushort)(WPAN << 8);
                        EPANS = okResp;
                    }
                    break;
                default:

                    break;
            }

            Utils.Log($"Processing terminal PANC. Command = {cmd} Result={Utils.Int2Oct(EPANS)}", true);
        }


        /// <summary>
        /// Reset read bit (#12
        /// </summary>
        internal void ResetPanelStatusReadBit()
        {
            EPANS = (ushort)(EPANS & ~(1 << 12));
        }

        /// <summary>
        /// Tick routines in PANEL including RTC
        /// </summary>
        internal void Tick(bool isStopped)
        {
            _isStopped = isStopped;
            rtc.Tick();
        }


    }

    class ControlPanelRegs
    {
        public ControlPanelRegs()
        {
            Seconds = 0x7c9c; // (ushort)DateTime.Now.Second;
            Days = 0x1a00; // (ushort)DateTime.Now.DayOfYear;
        }
        public ushort Seconds { get; set; }
        public ushort Days { get; set; }
    }
}
