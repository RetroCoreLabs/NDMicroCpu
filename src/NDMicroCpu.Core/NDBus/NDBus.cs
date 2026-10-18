//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Collections.Generic;

namespace NDMicroCpu.Core.NDBus
{
    /// <summary>
    /// The Norsk Data I/O bus - the device layer that was missing from the emulator
    /// (see the "TODO: Implement IO BUS and Connect to devices" at the COMM,IOX handler
    /// in Cpu.cs). It owns the attached bus devices and provides the three things the
    /// microcode needs from the bus:
    ///
    ///   * IOX transactions  - <see cref="Iox"/>, called from the COMM,IOX microcommand:
    ///       route an IOX register read/write to whichever device claims the address.
    ///   * per-cycle clocking - <see cref="Clock"/>, called each CPU micro-cycle to run
    ///       every device's delay/DMA/completion engine.
    ///   * IDENT              - <see cref="Ident"/>, called from the IDENT microcode to
    ///       find the identification code of the device interrupting at a given level.
    ///
    /// Devices reach main memory for DMA through the <see cref="ISystemBus"/> the bus was
    /// constructed with. With no devices attached the bus is inert: Iox always returns
    /// false (caller raises the normal IOX-error), Clock is a no-op, and no interrupts are
    /// asserted - so an emulator with nothing plugged in behaves exactly as before.
    /// </summary>
    public sealed class NDBus
    {
        private readonly List<INDBusDevice> _devices = new List<INDBusDevice>();

        /// <summary>Physical-memory access handed to DMA-capable devices.</summary>
        public ISystemBus Dma { get; }

        /// <summary>
        /// Creates the bus with the memory access DMA devices use.
        /// </summary>
        /// <param name="dma">
        /// Physical memory access for DMA.
        /// </param>
        public NDBus(ISystemBus dma)
        {
            Dma = dma;
        }

        /// <summary>Plug a device onto the bus.</summary>
        public void Attach(INDBusDevice device)
        {
            _devices.Add(device);
        }

        /// <summary>Number of attached devices (0 = inert bus).</summary>
        public int DeviceCount => _devices.Count;

        /// <summary>
        /// Execute an IOX transaction. <paramref name="isWrite"/> comes from the low bit of
        /// the IOX address (odd = write, even = read) per the ND convention. Returns true if
        /// a device claimed the address (then <paramref name="readData"/> holds the read
        /// result for a read); false if no device owns it, in which case the caller must
        /// raise the IOX-error interrupt exactly as the pre-bus stub did.
        /// </summary>
        public bool Iox(int ioxAddress, bool isWrite, ushort writeData, out ushort readData)
        {
            readData = 0;
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i].Claims(ioxAddress))
                {
                    if (isWrite)
                        _devices[i].Write(ioxAddress, writeData);
                    else
                        readData = _devices[i].Read(ioxAddress);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Clock every device one micro-cycle (delay timers, in-flight DMA, completion).</summary>
        public void Clock()
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                _devices[i].Clock();
            }
        }

        /// <summary>
        /// OR of every device's currently asserted interrupt levels (bit N = level N
        /// pending). The CPU folds this into the PIC each cycle so a device that completes
        /// a transfer raises its interrupt (e.g. a floppy raising level 11).
        /// </summary>
        public int PendingInterruptBits()
        {
            int bits = 0;
            for (int i = 0; i < _devices.Count; i++)
            {
                bits |= _devices[i].InterruptBits;
            }
            return bits;
        }

        /// <summary>
        /// IDENT on a PIC level (10-13): returns the identification code of the device
        /// interrupting at that level (which clears its interrupt), or -1 if no attached
        /// device is interrupting there - matching real hardware, where an unanswered IDENT
        /// does NOT manufacture a new error.
        /// </summary>
        public int Ident(int level)
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                int code = _devices[i].Ident(level);
                if (code >= 0)
                    return code;
            }
            return -1;
        }
    }
}
