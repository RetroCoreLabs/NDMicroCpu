//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// TrapController manages trap vector calculation and priority handling for the ND-110/120 CPU.
    /// This class centralizes the logic for determining which trap vector should be activated based on
    /// various interrupt sources, hardware signals, and error conditions.
    ///
    /// Based on ND-110/120 hardware specifications where:
    /// - Vectors 0x00-0x0D: Immediate traps (highest priority)
    /// - Vector 0x0E: Panel interrupts (wait for FETCH)
    /// - Vector 0x0F: Macro interrupts (wait for FETCH)
    /// </summary>
    public class TrapController
    {
        private ushort trapVectorBitmap;
        private readonly CpuInternals hwInternals;

        /// <summary>
        /// Creates the trap controller for one CPU.
        /// </summary>
        /// <param name="hwInternals">
        /// The hardware the controller reads the trap sources from.
        /// </param>
        public TrapController(CpuInternals hwInternals)
        {
            this.hwInternals = hwInternals;
            trapVectorBitmap = 0;
        }

        #region Public Interface

        /// <summary>
        /// Calculate and return the highest priority active trap vector
        /// </summary>
        /// <returns>Trap vector number (0-15), or -1 if no traps active</returns>
        public int GetActiveTrapVector()
        {
            UpdateTrapVectorsFromSignals();
            return GetHighestPriorityVector();
        }

        /// <summary>
        /// Set a specific trap vector (used for memory protection, page faults, etc.)
        /// </summary>
        /// <param name="vector">Vector number 0-15</param>
        public void SetTrapVector(int vector)
        {
            if (vector >= 0 && vector <= 15)
            {
                // Skip (including the log-string allocation) when already set - this runs
                // every micro-cycle via UpdateTrapVectorsFromSignals for vector 0x0F.
                if ((trapVectorBitmap & (1 << vector)) != 0) return;
                trapVectorBitmap = (ushort)(trapVectorBitmap | (1 << vector));
                Logger.Log($"TRAP Vector {vector:X1} set", Logger.LogLevel.Machine);
            }
        }

        /// <summary>
        /// Clear a trap vector after it has been processed
        /// </summary>
        /// <param name="vector">Vector number 0-15</param>
        public void ClearTrapVector(int vector)
        {
            if (vector >= 0 && vector <= 15)
            {
                // Skip (including the log-string allocation) when already clear - this runs
                // every micro-cycle via UpdateTrapVectorsFromSignals for vector 0x0F.
                if ((trapVectorBitmap & (1 << vector)) == 0) return;
                trapVectorBitmap = (ushort)(trapVectorBitmap & ~(1 << vector));
                Logger.Log($"TRAP Vector {vector:X1} cleared", Logger.LogLevel.Machine);
            }
        }

        /// <summary>
        /// Check if any trap vectors are active
        /// </summary>
        public bool HasActiveTrapVectors => trapVectorBitmap != 0;

        /// <summary>
        /// Get current trap vector bitmap for debugging
        /// </summary>
        public ushort TrapVectorBitmap => trapVectorBitmap;

        #endregion

        #region Private Implementation

        /// <summary>
        /// Update trap vectors based on current hardware signals and interrupt states
        /// </summary>
        private void UpdateTrapVectorsFromSignals()
        {
            // Check Panel Interrupts (Vector 0x0E)
            if (hwInternals.PanelInterruptDetected())
            {
                SetTrapVector(0x0E);
            }

            // Check PIC Interrupts (Vector 0x0F)
            // NB: INT10-INT13 (device/timer interrupt pins) are NOT mapped to their own
            // vectors here. Real ND-100/110/120 hardware combines all asynchronous device
            // interrupts into a single PIC-driven request line (this same ReadInterruptRequestPin
            // check) that is only recognized at a FETCH boundary - see Cpu.cs's switch(vector),
            // which only gates 0x0E/0x0F on hw.mac.FETCH. Individual INT-pin identities are used
            // for PIL/priority resolution inside the PIC, not as independent synchronous trap
            // vectors. Previously this class mapped INT10-13 straight to vectors 0x0A-0x0D, which
            // fall through Cpu.cs's switch default case and force a microcode jump on every
            // micro-cycle the pin is asserted (not just at instruction boundaries) - causing the
            // CPU to abandon mid-execution instructions whenever e.g. the RTC (every 20ms) fired.
            // Vector 0x0F must track the LIVE /IRQ pin, not latch transients. This method
            // runs every micro-cycle (Cpu.TickCpu -> GetTrapVector), so a sticky
            // SetTrapVector here latched the few-cycle /IRQ pulse that occurs INSIDE the
            // TRA IIC microcode epilogue (CS 000713-000715 restore the caller's mask and
            // status fence while the IRQ-enable flip-flop is still ON from the fence scan's
            // PIC,ION; CS 000675 then executes PIC,IOF and the pulse ends). The latched bit
            // survived to the next FETCH and dispatched a macro interrupt with the
            // interrupt system OFF (IONI=0, PIC disabled) and NOTHING pending - RDVC then
            // returned vector 0, which the ITSRV jump table (CS 003740) maps to level 10,
            // sending INSTRUCTION-VERIFY to a level it never activated and stopping the CPU
            // at its next WAIT (traced 2026-07-11). Real hardware (ND-120 CGA_INTR.v)
            // re-samples the OR of panel/IRQ on every MCLK - a request that disappears
            // before FETCH never dispatches.
            if (hwInternals.pic.ReadInterruptRequestPin == false) // active low
            {
                SetTrapVector(0x0F);
            }
            else
            {
                ClearTrapVector(0x0F);
            }

            // Check Memory Protection and other system errors
            CheckSystemErrors();
        }

        /// <summary>
        /// Check for system errors that generate immediate traps
        /// </summary>
        private void CheckSystemErrors()
        {
            // These would be set by other components when errors occur:

            // Vector 0x00: Power failure
            // Vector 0x01: Page fault
            // Vector 0x02: Memory protect violation (already handled in Memory.cs)
            // Vector 0x03: Illegal instruction
            // Vector 0x04: Privileged instruction violation
            // Vector 0x05: Arithmetic overflow
            // Vector 0x06: Division by zero
            // Vector 0x07: Floating point errors
            // Vector 0x08: I/O errors
            // Vector 0x09: System call
        }

        /// <summary>
        /// Get the highest priority (lowest numbered) active trap vector
        /// Priority: 0 (highest) to 15 (lowest)
        /// </summary>
        /// <returns>Vector number, or -1 if none active</returns>
        private int GetHighestPriorityVector()
        {
            if (trapVectorBitmap == 0) return -1;

            // Find lowest set bit (highest priority)
            for (int i = 0; i <= 15; i++)
            {
                if ((trapVectorBitmap & (1 << i)) != 0)
                {
                    return i;
                }
            }
            return -1;
        }

        #endregion

        #region Trap Vector Definitions and Documentation

        /// <summary>
        /// Trap vector definitions based on ND-110/120 hardware specifications
        /// </summary>
        public static class TrapVectors
        {
            /// <summary>
            /// Vector 0: power failure detected.
            /// </summary>
            public const int POWER_FAILURE = 0x00;        // Power failure detected
            /// <summary>
            /// Vector 1: virtual memory page fault.
            /// </summary>
            public const int PAGE_FAULT = 0x01;           // Virtual memory page fault
            /// <summary>
            /// Vector 2: memory protection violation.
            /// </summary>
            public const int MEMORY_PROTECT = 0x02;       // Memory protection violation
            /// <summary>
            /// Vector 3: illegal instruction opcode.
            /// </summary>
            public const int ILLEGAL_INSTRUCTION = 0x03;  // Illegal instruction opcode
            /// <summary>
            /// Vector 4: privileged instruction executed in user mode.
            /// </summary>
            public const int PRIVILEGED_VIOLATION = 0x04; // Privileged instruction in user mode
            /// <summary>
            /// Vector 5: arithmetic overflow.
            /// </summary>
            public const int ARITHMETIC_OVERFLOW = 0x05;  // Arithmetic overflow
            /// <summary>
            /// Vector 6: division by zero.
            /// </summary>
            public const int DIVISION_BY_ZERO = 0x06;     // Division by zero
            /// <summary>
            /// Vector 7: floating point error.
            /// </summary>
            public const int FLOATING_POINT = 0x07;       // Floating point error
            /// <summary>
            /// Vector 8: I/O system error.
            /// </summary>
            public const int IO_ERROR = 0x08;             // I/O system error
            /// <summary>
            /// Vector 9: system call trap.
            /// </summary>
            public const int SYSTEM_CALL = 0x09;          // System call trap
            /// <summary>
            /// Vector 10, INT10: output device, the terminal transmitter.
            /// </summary>
            public const int OUTPUT_DEVICE = 0x0A;        // INT10 - Output device (terminal TX)
            /// <summary>
            /// Vector 11, INT11: mass storage device.
            /// </summary>
            public const int MASS_STORAGE = 0x0B;         // INT11 - Mass storage device
            /// <summary>
            /// Vector 12, INT12: input device, the terminal receiver.
            /// </summary>
            public const int INPUT_DEVICE = 0x0C;         // INT12 - Input device (terminal RX)
            /// <summary>
            /// Vector 13, INT13: real time clock.
            /// </summary>
            public const int REAL_TIME_CLOCK = 0x0D;      // INT13 - Real time clock
            /// <summary>
            /// Vector 14: panel interrupt, taken at the next instruction fetch.
            /// </summary>
            public const int PANEL_INTERRUPT = 0x0E;      // Panel interrupt (wait for FETCH)
            /// <summary>
            /// Vector 15: macro interrupt, taken at the next instruction fetch.
            /// </summary>
            public const int MACRO_INTERRUPT = 0x0F;      // Macro interrupt (wait for FETCH)
        }

        /// <summary>
        /// Get human-readable description of trap vector
        /// </summary>
        /// <param name="vector">Vector number 0-15</param>
        /// <returns>Description string</returns>
        public static string GetTrapVectorDescription(int vector)
        {
            return vector switch
            {
                0x00 => "Power Failure",
                0x01 => "Page Fault",
                0x02 => "Memory Protect Violation",
                0x03 => "Illegal Instruction",
                0x04 => "Privileged Instruction Violation",
                0x05 => "Arithmetic Overflow",
                0x06 => "Division by Zero",
                0x07 => "Floating Point Error",
                0x08 => "I/O Error",
                0x09 => "System Call",
                0x0A => "Output Device (INT10)",
                0x0B => "Mass Storage (INT11)",
                0x0C => "Input Device (INT12)",
                0x0D => "Real Time Clock (INT13)",
                0x0E => "Panel Interrupt",
                0x0F => "Macro Interrupt",
                _ => $"Unknown Vector {vector:X1}"
            };
        }

        #endregion

        #region Trap Behavior Classification

        /// <summary>
        /// Determine if a trap vector requires waiting for FETCH before execution
        /// </summary>
        /// <param name="vector">Vector number 0-15</param>
        /// <returns>True if must wait for FETCH, false for immediate execution</returns>
        public static bool RequiresFetchWait(int vector)
        {
            return vector switch
            {
                0x0E => true,  // Panel interrupt waits for FETCH
                0x0F => true,  // Macro interrupt waits for FETCH
                _ => false     // All other traps are immediate
            };
        }

        /// <summary>
        /// Get trap vector priority (lower number = higher priority)
        /// </summary>
        /// <param name="vector">Vector number 0-15</param>
        /// <returns>Priority level</returns>
        public static int GetTrapPriority(int vector)
        {
            return vector; // Vector number directly corresponds to priority
        }

        #endregion
    }
}
