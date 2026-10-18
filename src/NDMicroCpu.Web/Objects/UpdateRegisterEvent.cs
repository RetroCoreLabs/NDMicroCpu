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
    /// The event arguments a register view raises when a register is read for display or written by the user.
    /// </summary>
    public class UpdateRegisterEvent : EventArgs
    {
        /// <summary>
        /// Creates the arguments.
        /// </summary>
        /// <param name="register">
        /// The register number.
        /// </param>
        /// <param name="value">
        /// The value read or written.
        /// </param>
        public UpdateRegisterEvent(ushort register, int value)
        {
            Register = register;
            Value = value;
            handled = false;
        }

        /// <summary>
        /// The register number.
        /// </summary>
        public ushort Register { get; internal set; }
        /// <summary>
        /// The value read or written; a handler may change it.
        /// </summary>
        public int Value { get; set; }

        /// <summary>
        /// Set by a handler that has dealt with the event, so later handlers skip it.
        /// </summary>
        public bool handled { get; set; }
    }
}
