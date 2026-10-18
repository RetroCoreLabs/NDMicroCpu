//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;

namespace NDMicroCpu.Core.NDBus
{
    /// <summary>
    /// Norsk Data floppy-disk DMA controller (the 3112 dual-density DMA card), ported
    /// self-contained onto the transaction-level bus (<see cref="INDBusDevice"/>) from the
    /// RetroCore NDBusFloppyDMA. Register-accurate to ND-11.021.1 so the ND-100/110/120
    /// LOAD microcode and SINTRAN's BFDIS driver drive it unchanged.
    ///
    /// Device numbers (thumbwheel): 0 -> 1560-1567 (ident 21), 1 -> 1570-1577 (ident 22);
    /// interrupt level 11 (mass storage). IOX registers (offset from base):
    ///   +0 ReadData   +2 ReadStatus1   +3 LoadControlWord   +4 ReadStatus2
    ///   +5 LoadPointerHi   +7 LoadPointerLo
    /// A transfer is command-block driven: the control word's pointer (hi/lo) points at a
    /// 12-word command block in ND-100 memory (command, disk addr, mem addr hi/lo, word/
    /// sector count); the controller DMAs the block out, moves the data, and DMAs a status
    /// block back at offset +6..+11, then raises READY-FOR-TRANSFER (and level 11 if the
    /// interrupt is enabled). Boot uses the autoload control bit, serviced by the host via
    /// <see cref="OnAutoloadRequest"/> (the controller cannot parse BPUN itself).
    ///
    /// Completion is deferred a few Clock() ticks after the IOX GO, like the real card, so
    /// the driver sees BUSY then the interrupt - not an instantaneous result.
    /// </summary>
    public sealed class NdFloppyDma : INDBusDevice
    {
        // ---- Register offsets (from base) ----
        private const int RegReadData = 0;
        private const int RegReadStatus1 = 2;
        private const int RegLoadControlWord = 3;
        private const int RegReadStatus2 = 4;
        private const int RegLoadPointerHigh = 5;
        private const int RegLoadPointerLow = 7;

        // ---- RSR1 status bits (IOX +2) ----
        private const int S1_InterruptEnabled = 1 << 1;
        private const int S1_DeviceActive = 1 << 2;
        private const int S1_ReadyForTransfer = 1 << 3;
        private const int S1_InclusiveOrReg2 = 1 << 4;   // OR-of-errors
        private const int S1_DualDensity = 1 << 15;      // always 1 on the DMA card

        // ---- Control word bits (IOX +3) ----
        private const int C_EnableInterrupt = 1 << 1;
        private const int C_ActivateAutoload = 1 << 2;
        private const int C_TestMode = 1 << 3;
        private const int C_DeviceClear = 1 << 4;
        private const int C_EnableStreamer = 1 << 5;
        private const int C_ExecuteCommand = 1 << 8;

        // ---- Floppy functions (command-block command word, low 6 bits) ----
        private const int Fn_ReadData = 0x00;
        private const int Fn_WriteData = 0x01;

        // ---- Error codes (RSR bits 9-14 in status word 1) ----
        private const int Err_Ok = 0;
        private const int Err_CrcError = 5;
        private const int Err_DriveNotReady = 0x10;              // oct 20
        private const int Err_NoBootstrap = 0x28;               // oct 50

        // Completion delay (Clock ticks) between the GO write and READY-FOR-TRANSFER.
        private const int IoDelayFloppy = 300;

        private readonly int _base;
        private readonly ushort _identCode;
        private readonly int _level;
        private readonly ISystemBus _dma;
        private readonly IFloppyMedia _media;

        private int _selectedDrive = -1;
        private int _rsr1;
        private int _errorCode = Err_Ok;
        private uint _pointerHi;
        private uint _pointerLo;

        private int _interruptBits;

        // deferred completion engine
        private int _delay = -1;
        private Action? _onComplete;

        /// <summary>
        /// Host services boot (control-word autoload): read the diskette, parse the BPUN
        /// bootstrap, DMA it into ND-100 memory, and return the boot/start address (or -1
        /// on no-bootstrap). The controller cannot do this itself because the BPUN loader
        /// lives above it; wiring it as a delegate keeps the device dependency-free.
        /// </summary>
        public Func<byte, int>? OnAutoloadRequest;

        /// <summary>
        /// Creates the floppy controller.
        /// </summary>
        /// <param name="thumbwheel">
        /// The thumbwheel setting that selects the device number and interrupt level.
        /// </param>
        /// <param name="dma">
        /// The bus the controller reads and writes memory through.
        /// </param>
        /// <param name="media">
        /// The diskette images in the drives.
        /// </param>
        public NdFloppyDma(byte thumbwheel, ISystemBus dma, IFloppyMedia media)
        {
            _dma = dma;
            _media = media;
            switch (thumbwheel)
            {
                case 0: _base = Convert.ToInt32("1560", 8); _identCode = (ushort)Convert.ToInt32("21", 8); _level = 11; break;
                case 1: _base = Convert.ToInt32("1570", 8); _identCode = (ushort)Convert.ToInt32("22", 8); _level = 11; break;
                default: throw new ArgumentOutOfRangeException(nameof(thumbwheel), "floppy thumbwheel must be 0 or 1");
            }
        }

        /// <summary>
        /// The IOX device number the thumbwheel selects.
        /// </summary>
        public int DeviceBaseAddress => _base;
        /// <summary>
        /// The interrupt level the controller requests on.
        /// </summary>
        public int InterruptLevel => _level;

        // ---- INDBusDevice ----

        /// <summary>
        /// Whether an IOX address belongs to this device: its eight registers from the base address.
        /// </summary>
        /// <param name="ioxAddress">
        /// The IOX address.
        /// </param>
        /// <returns>
        /// True for the eight addresses of the device.
        /// </returns>
        public bool Claims(int ioxAddress) => ioxAddress >= _base && ioxAddress < _base + 8;

        /// <summary>
        /// Reads a device register.
        /// </summary>
        /// <param name="ioxAddress">
        /// The IOX address.
        /// </param>
        /// <returns>
        /// The register value.
        /// </returns>
        public ushort Read(int ioxAddress)
        {
            int r = ioxAddress - _base;
            switch (r)
            {
                case RegReadData:
                    // Placeholder programmed-read value (real transfers are DMA); 0x1 lets
                    // SINTRAN's "enter directory" probe proceed. (RetroCore parity.)
                    return 0x1;
                case RegReadStatus1:
                case RegReadStatus2:
                    // Both status reads return the same HARDWARE status word: RSR1 flags,
                    // bit 15 dual-density, bit 4 OR-of-errors. NO numeric error code here.
                    return HardwareStatusWord();
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Writes a device register; the control word starts a command.
        /// </summary>
        /// <param name="ioxAddress">
        /// The IOX address.
        /// </param>
        /// <param name="value">
        /// The word written.
        /// </param>
        public void Write(int ioxAddress, ushort value)
        {
            int r = ioxAddress - _base;
            switch (r)
            {
                case RegLoadControlWord:
                    ExecuteControlWord(value);
                    break;
                case RegLoadPointerHigh:
                    _pointerHi = value;
                    break;
                case RegLoadPointerLow:
                    _pointerLo = value;
                    break;
            }
        }

        /// <summary>
        /// The interrupt levels the controller is requesting, one bit per level.
        /// </summary>
        public int InterruptBits => _interruptBits;

        /// <summary>
        /// Answers an IDENT on a level the controller is interrupting on.
        /// </summary>
        /// <param name="level">
        /// The program level being identified.
        /// </param>
        /// <returns>
        /// The ident code, or -1 when the controller is not interrupting on that level.
        /// </returns>
        public int Ident(int level)
        {
            if (level == _level && _interruptBits != 0)
            {
                _interruptBits &= ~(1 << _level); // acknowledged
                return _identCode;
            }
            return -1;
        }

        /// <summary>
        /// Advances the command in progress by one clock; a command completes after its delay.
        /// </summary>
        public void Clock()
        {
            if (_delay < 0) return;
            if (_delay == 0)
            {
                _delay = -1;
                Action? done = _onComplete;
                _onComplete = null;
                done?.Invoke();
            }
            else
            {
                _delay--;
            }
        }

        // ---- control word ----

        private void ExecuteControlWord(ushort value)
        {
            int control = value;

            // interrupt-enable mirrors control bit 1 into RSR1
            if ((control & C_EnableInterrupt) != 0) _rsr1 |= S1_InterruptEnabled;
            else _rsr1 &= ~S1_InterruptEnabled;

            // device clear deselects the drive and (re)asserts READY-FOR-TRANSFER
            if ((control & C_DeviceClear) != 0)
            {
                _selectedDrive = -1;
                _rsr1 |= S1_ReadyForTransfer;
            }

            RefreshInterrupt();

            if ((control & C_ActivateAutoload) != 0)
            {
                ExecuteAutoload();
            }
            else if ((control & C_ExecuteCommand) != 0)
            {
                // Test mode and streamer are not modelled yet (see NDBusFloppyDMA); a plain
                // command-block GO runs the floppy transfer.
                if ((control & C_TestMode) != 0) { /* test mode: not modelled */ }
                else if ((control & C_EnableStreamer) != 0) { /* streamer: not modelled */ }
                else ExecuteFloppyGo();
            }
        }

        // ---- autoload (boot) ----

        private void ExecuteAutoload()
        {
            byte unit = (byte)(_selectedDrive >= 0 ? _selectedDrive : 0);

            // busy for the duration of the load
            _rsr1 &= ~S1_ReadyForTransfer;
            _rsr1 |= S1_DeviceActive;

            int bootAddress = OnAutoloadRequest != null ? OnAutoloadRequest(unit) : -1;
            if (bootAddress < 0)
            {
                _errorCode = Err_NoBootstrap; // oct 50
                _selectedDrive = -1;
            }
            else
            {
                _errorCode = Err_Ok;
                _selectedDrive = unit;
            }

            ScheduleCompletion();
        }

        // ---- command-block transfer ----

        private void ExecuteFloppyGo()
        {
            uint blockAddr = _pointerLo | (_pointerHi << 16);

            // 12-word command block (ND-11.021.1 6.3): 0 command, 1 disk addr, 2 mem hi,
            // 3 mem lo, 4 opt+wcHi (bit15 = word-count vs sector-count), 5 wc/sc lo.
            int commandWord = DmaRead(blockAddr + 0);
            uint diskAddress = (uint)DmaRead(blockAddr + 1);
            uint memHi = (uint)(DmaRead(blockAddr + 2) & 0xFF);
            uint memAddress = (uint)DmaRead(blockAddr + 3) | (memHi << 16);
            int wcHi = DmaRead(blockAddr + 4);
            bool isWordCount = (wcHi & (1 << 15)) != 0;
            uint wordCount = (uint)DmaRead(blockAddr + 5) | (((uint)wcHi & 0xFF) << 16);

            int command = commandWord & 0x3F;
            _selectedDrive = (commandWord >> 6) & 0x3;
            uint floppyFormat = (uint)((commandWord >> 8) & 0x3);
            byte unit = (byte)_selectedDrive;

            uint bytesPerSector = floppyFormat switch { 0 => 512u, 1 => 256u, 2 => 128u, 3 => 1024u, _ => 512u };

            if (!_media.IsAttached(unit))
            {
                FailCommand(blockAddr, unit, Err_DriveNotReady);
                return;
            }

            uint position = diskAddress * bytesPerSector;
            uint wordsToTransfer = isWordCount ? wordCount : wordCount * (bytesPerSector >> 1);

            _rsr1 &= ~S1_ReadyForTransfer;
            RefreshInterrupt();
            _errorCode = Err_Ok;

            if (command == Fn_ReadData)
            {
                int byteCount = (int)(wordsToTransfer * 2);
                byte[]? block = _media.ReadBlock(unit, position, byteCount);
                if (block == null) { FailCommand(blockAddr, unit, Err_CrcError); return; }

                uint addr = memAddress;
                int off = 0;
                for (uint i = 0; i < wordsToTransfer; i++)
                {
                    ushort word = (ushort)((block[off] << 8) | block[off + 1]); // big-endian on disk
                    DmaWrite(addr, word);
                    addr++; off += 2;
                }
                WriteStatusBlock(blockAddr, unit, addr);
                ScheduleCompletion();
            }
            else if (command == Fn_WriteData)
            {
                int byteCount = (int)(wordsToTransfer * 2);
                byte[] block = new byte[byteCount];
                uint addr = memAddress;
                int off = 0;
                for (uint i = 0; i < wordsToTransfer; i++)
                {
                    int data = DmaRead(addr);
                    block[off] = (byte)((data >> 8) & 0xFF);
                    block[off + 1] = (byte)(data & 0xFF);
                    addr++; off += 2;
                }
                _media.WriteBlock(unit, position, block);
                WriteStatusBlock(blockAddr, unit, addr);
                ScheduleCompletion();
            }
            else
            {
                // Other functions (format/copy/find-EOF/…) not modelled yet: complete OK
                // with no data movement so the driver isn't wedged. Extend as needed.
                WriteStatusBlock(blockAddr, unit, memAddress);
                ScheduleCompletion();
            }
        }

        private void FailCommand(uint blockAddr, byte unit, int error)
        {
            _errorCode = error;
            _rsr1 &= ~S1_DeviceActive;
            _rsr1 |= S1_ReadyForTransfer;
            DmaWrite(blockAddr + 6, StatusWord1());
            DmaWrite(blockAddr + 7, (ushort)(unit << 8));
            RefreshInterrupt();
        }

        private void WriteStatusBlock(uint blockAddr, byte unit, uint lastAddr)
        {
            // +6 Status1, +7 Status2 (selected unit in bits 8-9), +8/9 last mem addr.
            DmaWrite(blockAddr + 6, StatusWord1());
            DmaWrite(blockAddr + 7, (ushort)(unit << 8));
            DmaWrite(blockAddr + 8, (ushort)((lastAddr >> 16) & 0xFF));
            DmaWrite(blockAddr + 9, (ushort)(lastAddr & 0xFFFF));
        }

        // Deferred READY-FOR-TRANSFER + interrupt, IoDelayFloppy ticks after GO.
        private void ScheduleCompletion()
        {
            _delay = IoDelayFloppy;
            _onComplete = () =>
            {
                _rsr1 &= ~S1_DeviceActive;
                _rsr1 |= S1_ReadyForTransfer;
                RefreshInterrupt();
            };
        }

        private void RefreshInterrupt()
        {
            bool assert = (_rsr1 & S1_InterruptEnabled) != 0 && (_rsr1 & S1_ReadyForTransfer) != 0;
            if (assert) _interruptBits |= (1 << _level);
            else _interruptBits &= ~(1 << _level);
        }

        // ---- status words ----

        private ushort HardwareStatusWord()
        {
            int s = _rsr1 | S1_DualDensity;
            if (_errorCode != Err_Ok) s |= S1_InclusiveOrReg2;
            return (ushort)s;
        }

        private ushort StatusWord1()
        {
            int s = _rsr1; // low flags only, no dual-density here
            if (_errorCode != Err_Ok) s |= S1_InclusiveOrReg2;
            return (ushort)(s | (_errorCode << 9));
        }

        // ---- DMA helpers (word address -> byte address = word<<1, matching the card) ----

        private int DmaRead(uint wordAddress) => _dma.ReadMemory16(wordAddress << 1);
        private void DmaWrite(uint wordAddress, ushort data) => _dma.WriteMemory16(wordAddress << 1, data);
    }
}
