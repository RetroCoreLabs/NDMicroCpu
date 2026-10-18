//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace NDMicroCpu.Core
{
    /*
     
     This provides the logical (virtual) 16—bit memory address
     to the Memory Management System (MMS). It includes
     arithmetic operations to speed the computation of memory
     addresses, avoiding reference to the ALU for such purposes.
    
     Microinstruction command decodes specify the memory
     reference computation required.


 
    */
    /// <summary>
    /// Macroinstruction address controller
    /// </summary>
    public class MAC
    {

        /// <summary>
        /// mac adddress (next macro opcode is to be fetched from this address)
        /// </summary>
        public ushort mac_address { get; set; } = 0;


        /// <summary>
        /// The memory address where the opcode was fetched from
        /// Used for calculating effective address
        /// </summary>
        public ushort fetch_address { get; internal set; } = 0;


        /// <summary>
        /// Latches the address the next memory cycle uses.
        /// </summary>
        /// <param name="address">
        /// The address.
        /// </param>
        public void LatchMacAddress(ushort address)
        {
            mac_address = address;
        }

        /// <summary>
        ///  Was the last operation a FETCH
        /// </summary>
        public bool FETCH { get; internal set; }


        /// <summary>
        /// Steps the latched address to the next word, as the P register increments after a fetch.
        /// </summary>
        public void CalculateNextMacAddress()
        {
            mac_address++;
        }


        internal void SetEXM(ushort v)
        {
            // Load Examine mode
        }
    }
}
