//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NDMicroCpu.Core.Common
{
    /// <summary>
    /// Splits a command line into its words, keeping quoted text together.
    /// </summary>
    public static class Parsing
    {
        /// <summary>
        /// Map a string to a string array
        /// </summary>
        /// <param name="inputCommand"></param>
        /// <returns></returns>
        public static string[] ParseStringToArray(string inputCommand)
        {
            // The regex pattern [\""].+?[\""]|[^ ]+ will match either a sequence of characters inside quotes ("..." or '...') or a sequence of non-space characters.
            // Trim('"') is used to remove the surrounding quotes from the matched strings.

            MatchCollection matches = Regex.Matches(inputCommand, @"[\""].+?[\""]|[^ ]+");
            string[] parts = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                parts[i] = matches[i].Value.Trim('"');
            }
            return parts;
        }
    }
}
