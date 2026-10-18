//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;

namespace NDMicroCpu.Web.Objects
{
    /// <summary>
    /// The time the web emulator was started, shown on the home page.
    /// </summary>
    public static class CompilationDate
    {
        /// <summary>
        /// The start time.
        /// </summary>
        public static readonly DateTime Date = DateTime.Now;
    }
}
