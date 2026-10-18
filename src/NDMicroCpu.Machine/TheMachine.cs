//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Diagnostics;
using NDMicroCpu.Core;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.Support;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Machine
{
    internal class TheMachine
    {
        CPU cpu;
        CPUType cpuType;

        public TheMachine(CPUType type = CPUType.ND110)
        {
            cpuType = type;
            cpu = new CPU(null, cpuType);
        }


        public bool Initialize()
        {

            //Logger.SetDestination(Logger.LogDestination.Nats);
            //Logger.SetLogLevel(Logger.LogLevel.Device);
            //Logger.SetLogLevel(Logger.LogLevel.CPU);
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;

            // CPU already loaded its own cpuType-appropriate microcode in its constructor.
            Console.WriteLine($"Initialized {cpuType}, uCode loaded");

            // Wire console output BEFORE booting so the binary-loader boot below is visible.
            if (cpuType == CPUType.ND110)
                cpu.hw.uart.OnTextOutput += Uart_OnTextOutput;
            else
                cpu.hw.uart2661.OnTextOutput += Uart_OnTextOutput;

            NDMicroCpu.Core.Utils.DisableLog = true;

            // Boot the program FAITHFULLY over the NDBus (self-test -> OPCOM -> "1560$" binary
            // loader), the same path a real operator uses. The old shortcut blitted the BPUN
            // straight into RAM, bypassing the microcode loader entirely - kept here for reference:
            //   FileHelper.LoadBPUN(@"BPUN\INSTRUCTION-B.BPUN", cpu.memory.Raw);
            // The BPUN images are copied next to the exe by the project file; find them there, not in the current directory.
            if (!BootFromBpunOverBus(System.IO.Path.Combine(AppContext.BaseDirectory, "BPUN", "INSTRUCTION-B.BPUN")))
            {
                Console.WriteLine("BUS BOOT FAILED");
                Debug.Assert(false);
            }


            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Press [END] key to exit");
            Console.ResetColor();

            Console.WriteLine();

            return true;
        }

        void Uart_OnTextOutput(char out_char)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(out_char);
            Console.ResetColor();

            //totalText += out_char;
            //totalText = TruncateTotalText(totalText);
        }
        public string TruncateTotalText(string totalText)
        {
            // Define the maximum number of "\r\n" occurrences allowed
            int maxNewLines = 24;

            // Split the string into an array of lines using "\r\n" as the delimiter
            string[] lines = totalText.Split(new[] { "\r\n" }, StringSplitOptions.None);

            // Check if the number of lines exceeds the maximum allowed
            if (lines.Length > maxNewLines)
            {
                // Remove excess lines from the beginning of the array
                int linesToRemove = lines.Length - maxNewLines;
                string[] kept = new string[maxNewLines];
                Array.Copy(lines, linesToRemove, kept, 0, maxNewLines);
                lines = kept;
            }

            // Join the remaining lines back together with "\r\n" as the separator
            string truncatedText = string.Join("\r\n", lines);

            return truncatedText;
        }



        public void InitializeRandomData()
        {
            // Initialize some registers

            for (int i = 0; i < 10; i++)
            {
                cpu.WriteALURegister((RegisterEnum)i, (ushort)(0xFF00 | i));
            }

            for (ushort i = 0; i < 10; i++)
            {
                for (ushort j = 0; j < 15; j++)
                {
                    cpu.hw.RF.UIWrite(j, i, (ushort)((j * 16) + i));
                }
            }

            for (ushort i = 0; i < 1000; i++)
            {
                cpu.memory.WritePhysicalMemory(i, (ushort)(1000 + i));
            }
        }



        public void Run(int ticks)
        {
            while (ticks-- > 0)
                cpu.TickCpu();
        }

        /// <summary>
        /// Boot a .BPUN program FAITHFULLY over the NDBus, exactly like a real operator: bring
        /// the CPU up to the OPCOM wait state (master clear + self-test), plug the image onto the
        /// bus as the floppy's binary-load device (1560), then type the console load command
        /// "1560$" so the built-in binary loader (DOLOA/ETLOA -> ETLO1) streams the image over the
        /// bus into memory and, if the BPUN Action field is 0, starts it. This is the real thing -
        /// the microcode does the loading - unlike the old FileHelper.LoadBPUN shortcut that just
        /// blitted bytes straight into RAM. Works identically on ND-110 and ND-120 (the console
        /// command bypasses the ND-120 panel LOAD / NOISE gate).
        /// </summary>
        public bool BootFromBpunOverBus(string bpunPath)
        {
            if (!System.IO.File.Exists(bpunPath))
            {
                Console.WriteLine($"BPUN not found: {bpunPath}");
                return false;
            }
            byte[] image = System.IO.File.ReadAllBytes(bpunPath);

            // Bring the CPU to the OPCOM wait state (self-test must pass and reach a FETCH).
            cpu.hw.mic.LatchMap(0);
            int budget = cpuType == CPUType.ND110 ? 300000 : 7000000;
            bool reached = false;
            for (int i = 0; i < budget; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mac.FETCH) { reached = true; break; }
            }
            if (!reached)
            {
                Console.WriteLine("CPU never reached OPCOM (self-test may have failed)");
                return false;
            }

            // Plug the image onto the bus as the floppy binary-load device, then type "1560$".
            cpu.iobus.Attach(new NDMicroCpu.Core.NDBus.NdBinaryLoadDevice(image));
            Run(420000);                        // let MOPC settle into its input loop (~few 20ms polls)
            const string loadCmd = "1560$";
            for (int i = 0; i < loadCmd.Length; i++)
            {
                cpu.hw.AddConsoleChar(loadCmd[i]);
                Run(210000);                    // one MOPC poll period per typed char
            }
            return true;
        }


        public void RunFromConsole()
        {
            bool loop = true;
            do
            {
                Run(1000);

                if (Console.KeyAvailable)
                {
                    ushort address = cpu.hw.mac.mac_address;


                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.End)
                    {
                        loop = false;
                        break;
                    }

                    if (key.Key == ConsoleKey.Home)
                    {
                        Console.WriteLine($"IOR: {Convert.ToString((byte)cpu.hw.cpu_regs.IOControl, 2).PadLeft(8, '0')} !!");
                    }

                    //if (key.Key == ConsoleKey.Home)
                    //{
                    //    Console.WriteLine($"A:{address}");
                    //    break;
                    //}

                    //if (key.Key == ConsoleKey.PageDown)
                    //{
                    //    Console.WriteLine($"Unique addresses = {map.Count}");
                    //    int cnt = 0;
                    //    foreach (ushort key1 in map.Keys)
                    //    {
                    //        if (map[key1]) cnt++;
                    //    }
                    //    Console.WriteLine($"New addresses = {cnt}");
                    //}


                    //if (key.Key == ConsoleKey.PageUp)
                    //{

                    //    Console.WriteLine();
                    //    Console.WriteLine("New addresses...");
                    //    foreach (ushort key1 in map.Keys)
                    //    {
                    //        if (map[key1])
                    //        {
                    //            // Print the current key to the console
                    //            Console.WriteLine($"{Utils.Int2Oct(key1)}");

                    //            // Set the corresponding bool value to false
                    //            map[key1] = false;
                    //        }
                    //    }
                    //}



                    byte myKey = (byte)char.ToUpper(key.KeyChar);

                    if (
                        (key.Modifiers == ConsoleModifiers.Control) &&
                        ((myKey >= 65) && (myKey <= 90))
                       )
                    {
                        myKey = (byte)(myKey - 64);  // Make CTRL-A to 0x01, CTRL-B to 0x02..
                    }
                    if (myKey != 0)
                    {
                        cpu.hw.AddConsoleChar((char)myKey);
                    }

                }
            } while (loop);
        }
    }
}
