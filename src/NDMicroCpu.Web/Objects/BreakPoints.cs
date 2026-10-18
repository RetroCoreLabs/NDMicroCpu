//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using MudBlazor;
using NDMicroCpu.Core;

namespace NDMicroCpu.Web.Objects
{
    /// <summary>
    /// The breakpoints the debugger pages set, keyed by address.
    /// </summary>
    public class BreakPoints
    {

        Dictionary<int, BreakPoint> breakpoints = new Dictionary<int, BreakPoint>();

        /// <summary>
        /// Number of breakpoints.
        /// </summary>
        public int Count { get => breakpoints.Count; }

        /// <summary>
        /// Helper logic to evaluate if a given address is a breakpoint
        /// </summary>
        /// <param name="addr"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public bool IsBreakpoint(int addr, BreakpointType type)
        {
            BreakPoint? bp;

            if (breakpoints.Count == 0) return false;

            if (breakpoints.TryGetValue(addr, out bp))
            {
                if ((bp.type & type) != 0)
                    return true;
            }

            return false;
        }
        /// <summary>
        /// The editor decoration ids of the breakpoint at an address.
        /// </summary>
        /// <param name="addr">
        /// The address.
        /// </param>
        /// <returns>
        /// The ids, or null when no breakpoint is set there.
        /// </returns>
        public string[]? GetDecorationID(int addr)
        {
            BreakPoint? bp;

            if (breakpoints.Count == 0) return null;

            if (breakpoints.TryGetValue(addr, out bp))
            {
                return bp.decorationIds;
            }

            return null;
        }



        /// <summary>
        /// Add a breakpont
        /// </summary>
        /// <param name="addr">Address of breakpoint</param>
        /// <param name="type">Type of breakpoint: EXEC/READ/WRITE</param>
        /// <param name="description">Optional description of breakpoint</param>
        /// <param name="decorationIds">
        /// The editor decoration ids that mark the breakpoint, or null.
        /// </param>
        public void AddBreakpoint(ushort addr, BreakpointType type, string description = "", string[]? decorationIds = null)
        {
            BreakPoint? bp;

            // Check if breakpoint exists
            if (breakpoints.TryGetValue(addr, out bp))
            {
                // exists, update it
                bp.type = bp.type | type; // Add new type ?
                if (!String.IsNullOrEmpty(description))
                {
                    bp.description = description;
                }
            }
            else
            {
                bp = new BreakPoint();
                bp.address = addr;
                bp.type = type;
                bp.description = description;
                bp.decorationIds = decorationIds;

                breakpoints.Add(bp.address, bp);
            }
        }

        /// <summary>
        /// Remove one specific breakpoint.
        /// Returns true if bitmask is removed or the breakpoint itself is removed (ie no other Breakpoint types on the same address)
        /// </summary>
        /// <param name="addr">Address to set breakpoint</param>
        /// <param name="type">Type of breakpoint EXEC/READ/WRITE</param>
        public bool RemoveBreakpoint(ushort addr, BreakpointType type)
        {
            BreakPoint? bp;

            // Check if breakpoint exists
            if (breakpoints.TryGetValue(addr, out bp))
            {

                // Check if given type exists in bitmask
                if ((bp.type & type) == 0)
                    return false;

                // exists, remove the given type from bitmask
                bp.type = bp.type & ~type;

                // remove the breakpoint totally ?
                if (bp.type == 0)
                {
                    breakpoints.Remove(addr);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove all breakpoints, return count of breakpoints
        /// </summary>
        public int ClearBreakpoints()
        {
            int reval = breakpoints.Count;
            breakpoints.Clear();
            return reval;
        }


        /// <summary>
        /// Return a list of breakpoints
        /// </summary>
        /// <returns></returns>
        public List<string> ListBreakpoints()
        {
            int totBps = 0;
            List<string> list = new List<string>();

            if (breakpoints.Count == 0)
            {
                list.Add("No breakpoints");
                return list;
            }

            BreakPoint[] all = new BreakPoint[breakpoints.Count];
            breakpoints.Values.CopyTo(all, 0);
            for (int bpIdx = 0; bpIdx < all.Length; bpIdx++)
            {
                BreakPoint bp = all[bpIdx];

                string decoded = $"Address: {Utils.Int2Oct(bp.address)} ";
                if ((bp.type & BreakpointType.Execution) != 0)
                {
                    decoded += "Execution ";
                }
                if ((bp.type & BreakpointType.MemoryRead) != 0)
                {
                    decoded += "Read ";
                }
                if ((bp.type & BreakpointType.MemoryWrite) != 0)
                {
                    decoded += "Write ";
                }

                if ((bp.type & BreakpointType.VirtualMemoryRead) != 0)
                {
                    decoded += "VirtualRead ";
                }
                if ((bp.type & BreakpointType.VirtualMemoryWrite) != 0)
                {
                    decoded += "VirtualWrite ";
                }

                if ((bp.type & BreakpointType.LevelShift) != 0)
                {
                    decoded += "LevelShift ";
                }


                if (bp.description.Length > 0)
                {
                    decoded += $"// {bp.description}";
                }

                list.Add(decoded);
                totBps++;
            }
            list.Add($"Total number of breakpoints {totBps}");

            return list;
        }
    }
}
