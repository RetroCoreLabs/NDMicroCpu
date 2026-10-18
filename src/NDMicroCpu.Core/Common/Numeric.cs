//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Core.Common
{
    /// <summary>
    /// Number parsing and formatting in the notations the emulators and their users write: hexadecimal, Commodore hexadecimal, decimal and octal.
    /// </summary>
    public static class Numeric
    {
        #region Numeric Setting
        /// <summary>
        /// The notation numbers are shown in.
        /// </summary>
        public enum NumericPresentation
        {
            /// <summary>
            /// Default. 
            /// </summary>
            Default,

            /// <summary>
            /// 0xFFFF - Default, used in most 8-bit Emulators
            /// </summary>
            Hex,

            /// <summary>
            /// $FFFF - Used in Commodore Emulators
            /// </summary>
            CmbHex,

            /// <summary>
            /// 65535
            /// </summary>
            Decimal,

            /// <summary>
            /// 0177777 - Used in PDP Emulator
            /// </summary>
            Octal,

            /// <summary>
            /// Use the ALIAS field (text)
            /// </summary>
            UseAlias


        };

        /// <summary>
        /// The notation used when none is given; hexadecimal by default.
        /// </summary>
        public static NumericPresentation currentNumericPresentation = NumericPresentation.Hex; // Default use hex
        #endregion

        #region Numeric Parsing
        /// <summary>
        /// Parses a 16-bit number written in any of the supported notations.
        /// </summary>
        /// <param name="value">
        /// The text; a prefix or the current notation decides how it is read.
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
        /// The text. A leading d means decimal, 0x hexadecimal, otherwise the current notation decides.
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


                // HEX 0xNNNN
                if ((value.StartsWith("0x")) | (value.StartsWith("0X")) || (currentNumericPresentation == NumericPresentation.Hex))
                {
                    if (value.Length <= 1)
                        value = "0x" + value;

                    if ((value[1] != 'x') && (value[1] != 'X'))
                        value = "0x" + value;

                    return int.Parse(value.Substring(2), System.Globalization.NumberStyles.HexNumber);
                }

                // HEX $NNNN
                if ((value.StartsWith("$")) || (currentNumericPresentation == NumericPresentation.CmbHex))
                {
                    if (value.Length <= 1)
                        value = "$" + value;

                    if (value[0] != '$')
                        value = "$" + value;

                    return int.Parse(value.Substring(1), System.Globalization.NumberStyles.HexNumber);
                }

                //Octal o012345
                if (value.StartsWith("o"))
                {
                    return Convert.ToInt32(value.Substring(1), 8);
                }


                // Octal 012345
                if ((value.StartsWith("0")) || ((value.StartsWith("1")) && (value.Length == 6)) || (currentNumericPresentation == NumericPresentation.Octal))
                {
                    return Convert.ToInt32(value, 8);
                }

                // Decimal (default)
                return int.Parse(value);
            }
            catch (Exception)
            {
                return -1;
            }
        }
        #endregion

        #region Numeric Output


        /// <summary>
        /// Formats an unsigned number in the given notation.
        /// </summary>
        /// <param name="n">
        /// The number.
        /// </param>
        /// <param name="len">
        /// Minimum number of digits, zero padded.
        /// </param>
        /// <param name="p">
        /// The notation.
        /// </param>
        /// <param name="trim">
        /// True drops the leading zeros.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Num2Str(uint n, byte len, NumericPresentation p, bool trim)
        {
            return Num2Str((int)n, len, p, trim);
        }
        /// <summary>
        /// Formats an unsigned number in the current notation.
        /// </summary>
        /// <param name="n">
        /// The number.
        /// </param>
        /// <param name="len">
        /// Minimum number of digits, zero padded.
        /// </param>
        /// <param name="trim">
        /// True drops the leading zeros.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Num2Str(uint n, byte len = 4, bool trim = false)
        {
            return Num2Str((int)n, len, currentNumericPresentation, trim);
        }


        /// <summary>
        /// Formats a 16-bit number in the current notation.
        /// </summary>
        /// <param name="n">
        /// The number.
        /// </param>
        /// <param name="len">
        /// Minimum number of digits, zero padded.
        /// </param>
        /// <param name="trim">
        /// True drops the leading zeros.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Num2Str(ushort n, byte len = 4, bool trim = false)
        {
            return Num2Str(n, len, currentNumericPresentation, trim);
        }

        /// <summary>
        /// Formats a number in the current notation.
        /// </summary>
        /// <param name="n">
        /// The number.
        /// </param>
        /// <param name="len">
        /// Minimum number of digits, zero padded.
        /// </param>
        /// <param name="trim">
        /// True drops the leading zeros.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Num2Str(int n, byte len = 4, bool trim = false)
        {
            return Num2Str(n, len, currentNumericPresentation, trim);
        }

        /// <summary>
        /// Formats a 64-bit unsigned number in the current notation.
        /// </summary>
        /// <param name="n">
        /// The number.
        /// </param>
        /// <param name="len">
        /// Minimum number of digits, zero padded.
        /// </param>
        /// <param name="trim">
        /// True drops the leading zeros.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Num2Str(ulong n, byte len = 8, bool trim = false)
        {
            return Num2Str((long)n, len, currentNumericPresentation, trim);
        }

        /// <summary>
        /// Formats a 64-bit number in the given notation.
        /// </summary>
        /// <param name="n">
        /// The number.
        /// </param>
        /// <param name="len">
        /// Minimum number of digits, zero padded.
        /// </param>
        /// <param name="p">
        /// The notation.
        /// </param>
        /// <param name="trim">
        /// True drops the leading zeros.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Num2Str(long n, byte len, NumericPresentation p, bool trim)
        {
            bool isNeg = false;
            if (len == 2)
                n = (byte)n;

            if (n < 0)
            {
                isNeg = true;
                n = -n;
            }


            string strN = GetNumericStyle();

            // cpu.Debugger.currentNumericPresentation
            switch (p)
            {
                case NumericPresentation.Hex:
                case NumericPresentation.CmbHex:
                    if (len == 4)
                    {
                        strN += $"{n:X4}";
                    }
                    else if (len == 8)
                    {
                        strN += $"{n:X8}";
                    }
                    else
                    {
                        strN += $"{n:X2}";
                    }
                    break;

                case NumericPresentation.Decimal:
                    strN = n.ToString();
                    if (isNeg)
                        strN = $"-{strN}";

                    break;

                case NumericPresentation.Octal:
                    strN = Convert.ToString(n, 8);
                    if (isNeg)
                        strN = $"-{strN}";
                    int padLen = 6;
                    if (len == 8)
                    {
                        padLen = 8;
                    }
                    if (!trim)
                    {
                        strN = strN.PadLeft(padLen, (char)'0');
                        strN = strN.PadRight(padLen + 1);
                    }
                    break;
            }

            if (trim)
                strN = strN.Trim();

            return strN;
        }

        private static string GetNumericStyle()
        {
            switch (currentNumericPresentation)
            {
                case NumericPresentation.Hex:
                    return "0x";
                case NumericPresentation.CmbHex:
                    return "$";
                default:
                    return "";
            }
        }

        /// <summary>
        /// Convert a numeric valye to a signed "string"
        /// </summary>
        /// <param name="val">signed number</param>
        /// <param name="size">8,16,32</param>
        /// <returns></returns>
        public static string Num2SignedString(ulong val, byte size)
        {
            string str = "";

            uint filter = (uint)((1 << size) - 1);
            uint signbit = (uint)(1 << size);
            bool neg = false;
            // Limit the incomming value
            val &= filter;

            filter = (uint)((1 << size - 1) - 1);

            if (val == signbit)
            {
                neg = true;
            }
            else if ((val & signbit) != 0)
            {
                val = (0 - val) & filter;
            }
            else
            {
                val &= filter;
            }

            switch (size)
            {
                case 8:
                    str = $"{val:X2}";
                    break;
                case 16:
                    str = $"{val:X4}";
                    break;
                case 32:
                    str = $"{val:X8}";
                    break;
                default:
                    str = "INVALID SIZE {size}";
                    break;
            }

            string numStyle = GetNumericStyle();

            if (neg)
            {
                str = $"-{numStyle}{str}";
            }
            else
            {
                str = $"{numStyle}{str}";
            }
            return str;
        }

        /// <summary>
        /// Not implemented; throws <see cref="NotImplementedException"/>.
        /// </summary>
        /// <param name="value">
        /// Unused.
        /// </param>
        /// <param name="busBits">
        /// Unused.
        /// </param>
        /// <returns>
        /// Never returns.
        /// </returns>
        public static string Num2UnsignedString(ulong value, int busBits)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
