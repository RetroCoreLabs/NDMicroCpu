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

namespace NDMicroCpu.Core
{

    /// <summary>
    /// The 20 ms real-time clock of the panel: counts CPU ticks and raises the clock interrupt on program level 13 when enabled.
    /// </summary>
    public class RealTimeClock
    {

        /// <summary>
        /// Raised when the clock ticks.
        /// </summary>
        public delegate void RtcInterruptEvent();
        /// <summary>
        /// Raised every 20 ms of emulated time while the clock interrupt is enabled.
        /// </summary>
        public event RtcInterruptEvent? OnRtcInterruptEvent;


        int rtcTicks;
        //readonly int TICK_PR_20MS = 10550*7; // estimated 7 microcode instruction (on average) pr opcode.

        // used for MAC ticking
        readonly int TICK_PR_20MS = 10550 * 4;
        bool rtcInterrupt = true;
        /// <summary>
        /// True while the clock interrupt is enabled (the IOX clock control word has set it).
        /// </summary>
        public bool rtcInterruptEnabled = false;

        /// <summary>
        /// Creates the clock with its tick counter at zero.
        /// </summary>
        public RealTimeClock()
        {
            rtcTicks = 0;
        }

        /// <summary>
        /// Clears the pending clock interrupt, as the micro code command COMM,CLRRTC does.
        /// </summary>
        public void ClearRTC()
        {
            rtcInterrupt = false;
            rtcTicks = 0;
        }

        /// <summary>
        /// Ticked every micro-cycle
        /// </summary>
        public void Tick()
        {
            if (rtcTicks > TICK_PR_20MS)
            {
                ActivateInterrupt();
            }
            else
            {
                rtcTicks++;
            }
        }

        private void ActivateInterrupt()
        {
            rtcInterrupt = true;
            rtcTicks = 0;
            //Console.Write(".");
            if (OnRtcInterruptEvent != null)
                OnRtcInterruptEvent();
        }

        /// <summary>
        /// Force an RTC interrupt. Returns FALSE if interrupt already set
        /// </summary>
        /// <returns></returns>
        public bool ForceRTC()
        {
            if (rtcInterrupt) { return false; }
            ActivateInterrupt();
            return true;
        }
    }
}
