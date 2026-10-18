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
    /// Number parsing helpers kept from the original support library; the same rules as <see cref="NDMicroCpu.Core.Common.Numeric"/>.
    /// </summary>
    public static class Utils
    {
        #region Numeric Parsing
        /// <summary>
        /// Parses a 16-bit number written in any of the supported notations.
        /// </summary>
        /// <param name="value">
        /// The text.
        /// </param>
        /// <returns>
        /// The number.
        /// </returns>
        public static UInt16 ParseShortValue(string value)
        {
            return ((UInt16)ParseIntValue(value));
        }

        /// <summary>
        /// Parses a number written in any of the supported notations.
        /// </summary>
        /// <param name="value">
        /// The text; a prefix or the current notation decides how it is read.
        /// </param>
        /// <returns>
        /// The number.
        /// </returns>
        public static int ParseIntValue(string value)
        {
            value = value.Trim();
            try
            {

                // DEC dNNNN
                if ((value.StartsWith("d")) | (value.StartsWith("D")))
                {
                    return int.Parse(value.Substring(1));
                }


                // HEX $NNNN
                if (value.StartsWith("$"))
                {
                    return int.Parse(value.Substring(1), System.Globalization.NumberStyles.HexNumber);
                }

                //Octal o012345
                if (value.StartsWith("o"))
                {
                    return Convert.ToInt32(value.Substring(1), 8);
                }

                return Convert.ToInt32(value, 8);
            }
            catch (Exception)
            {
                return -1;
            }
        }
        #endregion
    }
}
