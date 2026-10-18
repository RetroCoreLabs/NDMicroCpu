//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Web.Objects
{
    /// <summary>
    /// Bit mask for breakpoint type
    /// </summary>
    public enum BreakpointType
    {
        /// <summary>
        /// Break when an instruction at the address is executed.
        /// </summary>
        Execution = 0x01,
        /// <summary>
        /// Break when physical memory at the address is written.
        /// </summary>
        MemoryWrite = 0x02,
        /// <summary>
        /// Break when physical memory at the address is read.
        /// </summary>
        MemoryRead = 0x04,
        /// <summary>
        /// Break when virtual memory at the address is read.
        /// </summary>
        VirtualMemoryRead = 0x08,
        /// <summary>
        /// Break when virtual memory at the address is written.
        /// </summary>
        VirtualMemoryWrite = 0x10,
        /// <summary>
        /// Break when the program level changes.
        /// </summary>
        LevelShift = 0x20,
        /// <summary>
        /// Every kind of break.
        /// </summary>
        ALL = 0xFF
    }

    /// <summary>
    /// Class defining a breakpoint
    /// </summary>
    public class BreakPoint
    {
        /// <summary>
        /// The address the breakpoint watches.
        /// </summary>
        public ushort address;
        /// <summary>
        /// What the breakpoint watches for.
        /// </summary>
        public BreakpointType type = BreakpointType.Execution;
        /// <summary>
        /// A note the user gave the breakpoint.
        /// </summary>
        public string description;

        /// <summary>
        /// Used for visualizing in Editor
        /// </summary>
        public string[]? decorationIds;
        /// <summary>
        /// Creates an execution breakpoint with no address.
        /// </summary>
        public BreakPoint()
        {
            description = "";
        }
    }

}
