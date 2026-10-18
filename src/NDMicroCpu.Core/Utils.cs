//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// The micro code log of the CPU and the octal helpers the whole emulator uses.
    /// </summary>
    public static class Utils
    {

        /// <summary>
        /// Receives one log line.
        /// </summary>
        /// <param name="msg">
        /// The text.
        /// </param>
        /// <param name="eol">
        /// True when the line is complete.
        /// </param>
        public delegate void LogMsg(string msg, bool eol);
        /// <summary>
        /// Raised for every log line, for a user interface to collect.
        /// </summary>
        public static event LogMsg? OnLogMsg;


        /// <summary>
        /// True switches the micro code log off; it costs a lot when the CPU runs millions of cycles.
        /// </summary>
        public static bool DisableLog { get; set; } = false;

        /// <summary>
        /// Ends the current log line.
        /// </summary>
        public static void LogEOL()
        {
            if (DisableLog) return;
            Console.WriteLine("");
            Logger.Log("", Logger.LogLevel.Machine);
        }

        /// <summary>
        /// Writes to the micro code log unless it is disabled.
        /// </summary>
        /// <param name="msg">
        /// The text.
        /// </param>
        /// <param name="eol">
        /// True ends the line.
        /// </param>
        public static void Log(string msg, bool eol = false)
        {
            if (DisableLog) return;
            LogAlways(msg, eol);
        }

        /// <summary>
        /// Writes to the micro code log even when it is disabled.
        /// </summary>
        /// <param name="msg">
        /// The text.
        /// </param>
        /// <param name="eol">
        /// True ends the line.
        /// </param>
        public static void LogAlways(string msg, bool eol = false)
        {

            if (OnLogMsg != null)
            {
                OnLogMsg(msg, eol);
            }
            else
            {
                Logger.LogC(msg, Logger.LogLevel.Machine);

                //Console.Write(msg);
                if (eol)
                    // Console.WriteLine("");
                    Logger.Log("", Logger.LogLevel.Machine);
            }
        }

        /// <summary>
        /// Formats a number as six octal digits, the notation the ND documentation uses.
        /// </summary>
        /// <param name="val">
        /// The number.
        /// </param>
        /// <param name="forceOctal">
        /// True formats octal even when the current notation is another one.
        /// </param>
        /// <returns>
        /// The formatted number.
        /// </returns>
        public static string Int2Oct(int val, bool forceOctal = false)
        {
#if false // set to true for HEX/BINARY debug log (when octal numbers fry you brain, this is the place to change it)

            string hex;
            string binary;
            if (val > 0xFF)
            {
                hex = $"{val:X4}";
                binary = Convert.ToString(val, 2).PadLeft(16, '0'); 
            }
            else
            {
                hex = $"{val:X2}";
                binary = Convert.ToString(val, 2).PadLeft(8, '0');
            }


            if (forceOctal)
            {
                binary = "o" + Convert.ToString(val, 8).PadLeft(6, '0');
            }


            return $"0x{hex} [{binary}]";
            
#else
            return Convert.ToString(val, 8).PadLeft(6, '0');
#endif
        }

        /// <summary>
        /// Parses octal digits.
        /// </summary>
        /// <param name="oct">
        /// The digits.
        /// </param>
        /// <returns>
        /// The number.
        /// </returns>
        public static int Oct2Int(string oct)
        {
            return Convert.ToInt32(oct, 8);
        }
    }
}
