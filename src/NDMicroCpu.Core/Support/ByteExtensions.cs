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
    /// Bit test and bit change helpers on a byte.
    /// </summary>
    public static class ByteExtensions
    {
        /// <summary>
        /// Whether a bit is set.
        /// </summary>
        /// <param name="value">
        /// The byte.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 7.
        /// </param>
        /// <returns>
        /// True when the bit is 1.
        /// </returns>
        public static bool BIT(this byte value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 7.");
            }

            // Use bitwise AND to check if the bit is set
            return (value & 1 << bitNo) != 0;
        }

        /// <summary>
        /// Inverts a bit.
        /// </summary>
        /// <param name="value">
        /// The byte.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 7.
        /// </param>
        /// <returns>
        /// The byte with the bit inverted.
        /// </returns>
        public static byte FlipBIT(this byte value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 7.");
            }

            // Use XOR to flop the mentioned bit
            return (byte)(value ^ (1 << bitNo));
        }

        /// <summary>
        /// Sets a bit to 1.
        /// </summary>
        /// <param name="value">
        /// The byte.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 7.
        /// </param>
        /// <returns>
        /// The byte with the bit set.
        /// </returns>
        public static byte SetBIT(this byte value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 7.");
            }

            // Use OR to SET the mentioned bit
            return (byte)(value | (1 << bitNo));
        }

        /// <summary>
        /// Clears a bit to 0.
        /// </summary>
        /// <param name="value">
        /// The byte.
        /// </param>
        /// <param name="bitNo">
        /// The bit, 0 to 7.
        /// </param>
        /// <returns>
        /// The byte with the bit cleared.
        /// </returns>
        public static ushort ClearBIT(this byte value, int bitNo)
        {
            if (bitNo < 0 || bitNo >= 8)
            {
                throw new ArgumentOutOfRangeException(nameof(bitNo), "Bit number must be between 0 and 7.");
            }

            // Use AND to CLEAR the mentioned bit
            return (byte)(value & ~(1 << bitNo));
        }
    }
}
