//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Web.Objects
{
    /// <summary>
    /// Class containing information about first and last line of the source kode for one specific microcode
    /// (One microcode can be one or more lines).
    /// </summary>
    public class MicroCodeLineInfo
    {
        /// <summary>
        /// The first listing line of the micro instruction.
        /// </summary>
        public int FirstLine { get; set; }
        /// <summary>
        /// The last listing line of the micro instruction.
        /// </summary>
        public int LastLine { get; set; }

        /// <summary>
        /// Creates the line range.
        /// </summary>
        /// <param name="firstLine">
        /// The first listing line.
        /// </param>
        /// <param name="lastLine">
        /// The last listing line.
        /// </param>
        public MicroCodeLineInfo(int firstLine, int lastLine)
        {
            FirstLine = firstLine;
            LastLine = lastLine;
        }
    }
}
