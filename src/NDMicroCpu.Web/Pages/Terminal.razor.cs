//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace NDMicroCpu.Web.Pages
{
    /// <summary>
    /// The code behind the terminal page: machine power, reset, and the console text.
    /// </summary>
    public partial class Terminal
    {
        // The CPU tick loop used to live here, so the machine only ran while the Terminal page was
        // open. It now lives in the (singleton) IAppService so power + ticking are global and a
        // boot from any page works. This page just OBSERVES the machine via OnMachineBatch and
        // refreshes the console/status when it fires.

        #region Config Bit helpers - Reading OPControl register

        /// <summary>
        /// bit 6 : Set terminal #1 in OPCOM (Console) as opposed to Normal
        /// </summary>
        bool OpComEnabled { get => (appService.Cpu.hw.cpu_regs.IOControl & 1 << 6) != 0; }

        /// <summary>
        ///   bit 5 : Enable Master Clear, red LED ON 
        /// </summary>
        bool MasterClearEnabled { get => (appService.Cpu.hw.cpu_regs.IOControl & 1 << 5) != 0; }


        /// <summary>
        ///   bit 4 :  Initialization completed, green LED ON
        /// </summary>
        bool InitializationCompleted { get => (appService.Cpu.hw.cpu_regs.IOControl & 1 << 4) != 0; }

        /// <summary>
        ///  bit 2 : Enable output interrupt on level 10, from terminal #1 - UART transmit buffer empty
        /// </summary>
        bool Int10Enabled { get => (appService.Cpu.hw.cpu_regs.IOControl & 1 << 2) != 0; }


        /// <summary>
        ///  bit 1: Enable output interrupt on level 12, from terminal #1 - UART data available    
        /// </summary>
        bool Int12Enabled { get => (appService.Cpu.hw.cpu_regs.IOControl & 1 << 1) != 0; }


        /// <summary>
        ///  bit 0 : Enable clock interrupt on level 13
        /// </summary>
        bool Int13Enabled { get => (appService.Cpu.hw.cpu_regs.IOControl & 1 << 0) != 0; }

        #endregion


        bool _control = false; // Is CONTROL KEY PRESSED ?

        void HandleKey(string key, bool down)
        {
            if (appService.PowerOn)
            {
                if (key.Length > 1)
                {
                    // Special characters
                    string k = key.ToUpper();

                    switch (k)
                    {
                        case "ENTER":
                            if (down)
                            {
                                //appService.Cpu.hw.uart.AddChar((char)0x0a); // cr
                                appService.Cpu.hw.AddConsoleChar((char)0x0d); // lf                      
                            }
                            break;

                        case "SHIFT":
                            break;
                        case "CONTROL":
                            _control = down;
                            break;
                        case "ESCAPE":
                            if (down)
                                appService.Cpu.hw.AddConsoleChar((char)0x1b);

                            break;
                        default:
                            if (down)
                                Console.WriteLine("Unhandled key: " + key);
                            break;
                    }
                }
                else
                {
                    // Normal characters
                    if (down)
                    {
                        char c = Char.ToUpper(key[0]);

                        // TODO: Debug this when VS debugger works with Blazor
                        //if (_control)
                        //{
                        //    byte ctrl_char = (byte)c;
                        //    ctrl_char = (byte)(ctrl_char - 64); // Make CTRL-A to 0x01, CTRL-B to 0x02...
                        //    appService.Cpu.hw.AddConsoleChar((char)ctrl_char);
                        //}
                        //else
                        {
                            appService.Cpu.hw.AddConsoleChar(c);
                        }

                    }
                }
            }
        }
    }
}
