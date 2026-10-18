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

namespace NDMicroCpu.Core.Support
{
    /// <summary>
    /// Bit test and bit change helpers on a 16-bit word.
    /// </summary>
    public static class UShortExtensions
    {
        /// <summary>
        /// Whether a bit is set.
        /// </summary>
        /// <param name="value">
        /// The word.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 15.
        /// </param>
        /// <returns>
        /// True when the bit is 1.
        /// </returns>
        public static bool BIT(this ushort value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 16)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 15.");
            }

            // Use bitwise AND to check if the bit is set
            return (value & 1 << bitNo) != 0;
        }

        /// <summary>
        /// Inverts a bit.
        /// </summary>
        /// <param name="value">
        /// The word.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 15.
        /// </param>
        /// <returns>
        /// The word with the bit inverted.
        /// </returns>
        public static ushort FlipBIT(this ushort value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 16)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 15.");
            }

            // Use XOR to flop the mentioned bit
            return (ushort)(value ^ (1 << bitNo));
        }

        /// <summary>
        /// Sets a bit to 1.
        /// </summary>
        /// <param name="value">
        /// The word.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 15.
        /// </param>
        /// <returns>
        /// The word with the bit set.
        /// </returns>
        public static ushort SetBIT(this ushort value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 16)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 15.");
            }

            // Use OR to SET the mentioned bit
            return (ushort)(value | (1 << bitNo));
        }

        /// <summary>
        /// Clears a bit to 0.
        /// </summary>
        /// <param name="value">
        /// The word.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 15.
        /// </param>
        /// <returns>
        /// The word with the bit cleared.
        /// </returns>
        public static ushort ClearBIT(this ushort value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 16)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 15.");
            }

            // Use AND to CLEAR the mentioned bit
            return (ushort)(value & ~(1 << bitNo));
        }
    }
}
