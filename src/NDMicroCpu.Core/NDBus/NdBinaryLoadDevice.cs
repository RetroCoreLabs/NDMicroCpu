//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Core.NDBus
{
    /// <summary>
    /// The PIO byte-streaming "binary load" personality of an ND mass-storage device
    /// (floppy 1560 / paper-tape reader / etc.) as driven by the panel-LOAD BINARY LOADER
    /// microcode branch (RASK ETLO1 002210, ALD selects device via the ALDVC 003560 jump
    /// table - ALD=1 -> device 1560). This is DELIBERATELY NOT the DMA command-block
    /// controller (see <see cref="NdFloppyDma"/>); the binary loader reads the boot image
    /// one BYTE at a time and does the memory storage + checksum itself in microcode.
    ///
    /// Register protocol (traced from ND-110-RASK.LISTING.TXT and confirmed live on the
    /// bus by TestBinaryLoaderProbe - the loader emitted exactly W N+3=004005, R N+2..., R N):
    ///   * IOX N+3  WRITE 004005 (octal)  -> DVACT: activate / start reading (002243).
    ///   * IOX N+2  READ  status           -> INCH (002236): bit 010 octal (0x08) = byte ready.
    ///                                        The loader polls this until the bit is set.
    ///   * IOX N+0  READ  data             -> INCH (002241): the next boot-image byte (low 8 bits).
    ///
    /// The image handed in is the RAW bytes of a .BPUN file (or any ND binary-load stream):
    /// Preamble -> '!' -> Address -> Count -> Data -> Checksum -> Action, big-endian. We do
    /// NOT parse it - the microcode binary loader consumes these bytes and lays the program
    /// into memory, exactly matching NDMicroCpu.Core.Support.FileLoader.BPUN's decode of the same format.
    /// So streaming a real BPUN file here makes a real panel LOAD boot that program.
    /// </summary>
    public sealed class NdBinaryLoadDevice : INDBusDevice
    {
        // Octal 1560 = 0x370: the standard floppy device number (ALDVC entry 1). Thumbwheel/
        // strap could move it, but the binary-load boot the panel selects is device 1560.
        /// <summary>
        /// Device number 1560 octal, the floppy in binary-load mode; the device the panel LOAD boots from.
        /// </summary>
        public const int DefaultBaseAddress = 0x370;

        // Octal 400 = 0x100: the classic ND paper-tape reader device number. The paper-tape
        // reader IS a PIO byte-stream device with the very same protocol as the floppy's binary-
        // load mode (N+3 activate, N+2 status bit3, N data), so this same class models it - just
        // at device 400. Boot it from the console with "400$" (ALDVC entry 4 selects it too).
        /// <summary>
        /// Device number 400 octal, the paper tape reader; the same byte-stream protocol at another address.
        /// </summary>
        public const int PaperTapeReaderBase = 0x100;

        /// <summary>
        /// Construct a paper-tape reader loaded with <paramref name="tapeImage"/> (a raw ND
        /// binary-load / .BPUN byte stream) at the classic device number 400. Boot it by typing
        /// "400$" at the OPCOM console. Functionally identical to a floppy binary load at 1560 -
        /// the ND binary loader does not care which serial device the bytes come from.
        /// </summary>
        public static NdBinaryLoadDevice PaperTapeReader(byte[] tapeImage)
            => new NdBinaryLoadDevice(tapeImage, PaperTapeReaderBase);

        // Status bit the INCH microcode masks with (AND 010 octal at 002237) to see "a byte
        // is available to read". 010 octal = 0x08 = bit 3.
        private const ushort STATUS_BYTE_READY = 0x08;

        // Control word the DVACT microcode writes to N+3 (002243/002244, the "4005" constant)
        // to activate the reader and start streaming.
        private const ushort CONTROL_ACTIVATE = 0x805; // 004005 octal

        private readonly int _base;
        private readonly byte[] _image;
        private int _pos;       // next byte offset to hand out
        private bool _active;    // set once the loader writes the activate control word

        /// <summary>
        /// Creates the device holding the image the loader will stream in.
        /// </summary>
        /// <param name="image">
        /// The BPUN image, byte by byte as the device hands it out.
        /// </param>
        /// <param name="baseAddress">
        /// The IOX device number.
        /// </param>
        public NdBinaryLoadDevice(byte[] image, int baseAddress = DefaultBaseAddress)
        {
            _image = image ?? new byte[0];
            _base = baseAddress;
            _pos = 0;
            _active = false;
        }

        /// <summary>The IOX device base (octal number as int, e.g. 0x370 = octal 1560).</summary>
        public int DeviceBaseAddress => _base;

        /// <summary>How many image bytes have been streamed so far (for tests/telemetry).</summary>
        public int BytesRead => _pos;

        /// <summary>True once the loader has written the activate control word.</summary>
        public bool IsActive => _active;

        // This device owns its 8-register IOX block N..N+7 (even=read, odd=write).
        /// <summary>
        /// Whether an IOX address belongs to this device: its eight registers from the base address.
        /// </summary>
        /// <param name="ioxAddress">
        /// The IOX address.
        /// </param>
        /// <returns>
        /// True for the eight addresses of the device.
        /// </returns>
        public bool Claims(int ioxAddress) => ioxAddress >= _base && ioxAddress <= _base + 7;

        /// <summary>
        /// Reads a device register: the next data byte at the base address, the status word at base plus 2.
        /// </summary>
        /// <param name="ioxAddress">
        /// The IOX address.
        /// </param>
        /// <returns>
        /// The register value.
        /// </returns>
        public ushort Read(int ioxAddress)
        {
            int reg = ioxAddress - _base;
            switch (reg)
            {
                case 0: // DATA register: hand out the next image byte, then advance.
                    if (!_active || _pos >= _image.Length) return 0;
                    return (ushort)(_image[_pos++] & 0xFF);

                case 2: // STATUS register: byte-ready bit set while activated and bytes remain.
                    return (_active && _pos < _image.Length) ? STATUS_BYTE_READY : (ushort)0;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Writes a device register: the control word at base plus 3 activates the stream.
        /// </summary>
        /// <param name="ioxAddress">
        /// The IOX address.
        /// </param>
        /// <param name="data">
        /// The word written.
        /// </param>
        public void Write(int ioxAddress, ushort data)
        {
            int reg = ioxAddress - _base;
            // N+3 is the control register; the activate/read command (004005) starts streaming.
            // NB: the BINARY LOADER re-issues DVACT (this same N+3=004005 write) once PER BYTE
            // (RASK 002243, after every INCH data read) as a "fetch next" pulse - it is NOT a
            // stream rewind. So activation must NOT reset _pos, or the loader re-reads byte 0
            // forever. The stream starts at 0 from the constructor; a real device-clear (a
            // different control value) would be the thing to reset, not this activate pulse.
            if (reg == 3 && data == CONTROL_ACTIVATE)
            {
                _active = true;
            }
        }

        // The binary loader polls (it does not use interrupts), so no IDENT / interrupt / clock.
        /// <summary>
        /// The device never interrupts, so it never answers an IDENT.
        /// </summary>
        /// <param name="level">
        /// The program level being identified.
        /// </param>
        /// <returns>
        /// Always -1.
        /// </returns>
        public int Ident(int level) => -1;
        /// <summary>
        /// The device has no timing; nothing happens on a clock.
        /// </summary>
        public void Clock() { }
        /// <summary>
        /// The device never requests an interrupt.
        /// </summary>
        public int InterruptBits => 0;
    }
}
