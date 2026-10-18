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

namespace NDMicroCpu.Core.Common
{

    /// <summary>
    /// Odd parity for the 7-bit console characters: the ND terminal lines carry seven data bits and an odd parity bit.
    /// </summary>
    public static class Parity
    {
        /// <summary>
        /// For each 7-bit value, 1 when the parity bit must be set to make the number of ones odd.
        /// </summary>
        static public byte[] oddParity = new byte[]
        {
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            1,0,0,1,0,1,1,0,0,1,1,0,1,0,0,1,
            0,1,1,0,1,0,0,1,1,0,0,1,0,1,1,0
        };

        /// <summary>
        /// Adds the odd parity bit to a 7-bit character.
        /// </summary>
        /// <param name="byte7">
        /// The character, bit 7 clear.
        /// </param>
        /// <returns>
        /// The character with bit 7 set when the parity needs it.
        /// </returns>
        static public byte FixOddParity(byte byte7)
        {
            byte byte8 = byte7;

            if (oddParity[byte7] == 1)
            {
                byte8 |= (byte)(1 << 7);
            }

            return byte8;
        }

        /// <summary>
        /// Adds the odd parity bit to every character in a buffer, in place.
        /// </summary>
        /// <param name="byte7buf">
        /// The characters, bit 7 clear.
        /// </param>
        static public void FixOddParity(byte[] byte7buf)
        {
            for (int i = 0; i < byte7buf.Length; i++)
            {
                byte7buf[i] = FixOddParity(byte7buf[i]);
            }
        }

        /// <summary>
        /// Clears the parity bit of every character in a buffer, in place.
        /// </summary>
        /// <param name="byte7buf">
        /// The characters with parity.
        /// </param>
        static public void RemoveParity(byte[] byte7buf)
        {
            for (int i = 0; i < byte7buf.Length; i++)
            {
                byte7buf[i] = (byte)(byte7buf[i] & 0x7F);
            }
        }
    }
}
