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




namespace NDMicroCpu.Debugger
{
    /// <summary>
    /// The machine the debugger server exposes: one CPU booted through its micro code self-test with the INSTRUCTION-B program loaded over the bus.
    /// </summary>
    public class Machine
    {
        /// <summary>
        /// The CPU the server steps and inspects.
        /// </summary>
        public CPU cpu = new CPU();

        /// <summary>
        /// Boots the CPU: master clear, self-test, then loads and starts INSTRUCTION-B over the bus.
        /// </summary>
        public void Initialize()
        {

            // Debug logging goes to FILE (%LOCALAPPDATA%\trace\file-trace.txt via
            // Logger.FileLog), NOT to NATS: with no broker reachable a NATS destination
            // used to block the process inside NATS.Client connection setup on every log
            // line (see NatsClient.connectFailed), and debugging must never depend on an
            // external service being up.
            Logger.SetDestination(Logger.LogDestination.File);
            //Logger.SetLogLevel(Logger.LogLevel.Device);
            Logger.SetLogLevel(Logger.LogLevel.Machine);
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;


            Console.WriteLine("Initializing ND-110 for remote-debugging...");

            // CPU already loaded its own microcode in its constructor; re-verify here.
            if (cpu.LoadCS())
                Console.WriteLine("uCode loaded");
            else
            {

                throw new Exception("uCode failed to load");
            }

            // Dont log microcode execution to Console
            NDMicroCpu.Core.Utils.DisableLog = true;

            // BPUN images are copied next to the exe by NDMicroCpu.Debugger.csproj (sourced from the
            // NDMicroCpu.Machine project), so this resolves on any clone. It previously pointed at a
            // hardcoded folder that only existed on one machine.
            string bpunDir = System.IO.Path.Combine(AppContext.BaseDirectory, "BPUN");

            string bpun = System.IO.Path.Combine(bpunDir, "INSTRUCTION-B.BPUN");
            //bpun = System.IO.Path.Combine(bpunDir, "FILSYS-INV-Q04.BPUN");
            //bpun = System.IO.Path.Combine(bpunDir, "RTC.BPUN");


            if (!string.IsNullOrEmpty(bpun))
            {
                if (FileHelper.LoadBPUN(bpun, cpu.memory.Raw))
                {
                    Console.WriteLine($"Loaded [{bpun}]");
                }
                else
                {
                    // Fail loudly: a silently un-loaded test program would make every opcode
                    // comparison against this server meaningless.
                    throw new Exception($"BPUN LOAD FAILED for [{bpun}]");
                }
            }



            //cpu.hw.cpu_regs.ALD = 12; // Bootstrap load from SMD disk 1540 and run (=21540)
            cpu.hw.cpu_regs.ALD = 14; // BPUN load from floppy (1560) and run 



            // Initialize CPU

            int i = 100000;

            while (cpu.hw.mac.FETCH == false)
            {
                cpu.TickCpu();
                i--;
                if (i < 0)
                {
                    throw new Exception("CPU never finised initialization");
                }
            }

            //cpu.hw.cpu_regs.STOP = false;
            cpu.hw.uart.OnTextOutput += Uart_OnTextOutput;

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
        /// <summary>
        /// Switches the micro code log on or off.
        /// </summary>
        /// <param name="enableLog">
        /// True writes the log.
        /// </param>
        public void SetLog(bool enableLog)
        {
            NDMicroCpu.Core.Utils.DisableLog = !enableLog;

            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Console logging = {enableLog}");
            Console.ResetColor();
        }

        /// <summary>
        /// Keeps only the last lines of the console text.
        /// </summary>
        /// <param name="totalText">
        /// The whole console text.
        /// </param>
        /// <returns>
        /// The text with the oldest lines dropped.
        /// </returns>
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
    }
}
