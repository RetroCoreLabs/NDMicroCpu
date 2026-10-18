//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Core.NDBus
{
    /// <summary>
    /// Transaction/register-level contract for a device on the Norsk Data I/O bus.
    ///
    /// This deliberately models the bus at the PROGRAMMING level (IOX register access,
    /// DMA, IDENT, interrupt levels) rather than at the wire level (the
    /// BAPR/BIOXE/BINPUT/BINACK/BDRY handshake and DMA REQ/ACK/GRANT). The wire-level
    /// timing lives in the ND-120 DELILAH Verilog module; keeping the C# side at the
    /// transaction level makes it a simple, faithful reference for cross-validating
    /// external bus-device implementations (e.g. the Pi Pico bus-device firmware)
    /// on register behavior, DMA patterns, IDENT codes and interrupt-level timing.
    ///
    /// The shape mirrors the RetroCore NDBusDeviceBase and the ND-120 sim DeviceManager
    /// (both share this device lineage): Read / Write / Ident / Clock / interrupt bits.
    /// A device reaches main memory for DMA through the <see cref="ISystemBus"/> handed
    /// to it by the machine - it never touches the RAM array directly.
    /// </summary>
    public interface INDBusDevice
    {
        /// <summary>True if this device owns the given IOX register address (octal device
        /// number range, e.g. a floppy controller claims 1560-1567). The bus asks every
        /// attached device; the first to claim handles the transaction.</summary>
        bool Claims(int ioxAddress);

        /// <summary>IOX read of one of this device's registers (even IOX address). Returns
        /// the 16-bit register value placed on the bus for the CPU to read (via DBR).</summary>
        ushort Read(int ioxAddress);

        /// <summary>IOX write to one of this device's registers (odd IOX address).</summary>
        void Write(int ioxAddress, ushort data);

        /// <summary>IDENT on the given PIC level (10-13): return this device's identification
        /// code (octal) and clear its interrupt on that level, or -1 if this device is not
        /// the one interrupting at that level.</summary>
        int Ident(int level);

        /// <summary>Called once per CPU micro-cycle (alongside uart.Tick()). Drives the
        /// device's internal delay/DMA/completion engine; a command started on an IOX write
        /// typically completes a fixed number of Clock() ticks later and raises its
        /// interrupt then.</summary>
        void Clock();

        /// <summary>Bitmask of the interrupt LEVELS this device is currently asserting.
        /// Bit N set = level N pending (valid ND levels 10-13). The bus folds this into the
        /// PIC each cycle. A device clears its bit when its interrupt is serviced (IDENT).</summary>
        int InterruptBits { get; }
    }
}
