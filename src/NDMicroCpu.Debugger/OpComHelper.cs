//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace NDMicroCpu.Debugger
{

    /// <summary>
    /// The register set of one ND-100 program level, as OPCOM prints and accepts it.
    /// </summary>
    public struct ND_Registers
    {
        /// <summary>
        /// The D register.
        /// </summary>
        public ushort D;
        /// <summary>
        /// The P register.
        /// </summary>
        public ushort P;
        /// <summary>
        /// The B register.
        /// </summary>
        public ushort B;
        /// <summary>
        /// The L register.
        /// </summary>
        public ushort L;
        /// <summary>
        /// The A register.
        /// </summary>
        public ushort A;
        /// <summary>
        /// The T register.
        /// </summary>
        public ushort T;
        /// <summary>
        /// The X register.
        /// </summary>
        public ushort X;
        /// <summary>
        /// The status register.
        /// </summary>
        public ushort STS;
    }

    /// <summary>
    /// Drives the OPCOM console of a REAL ND-110 over a serial port: register and memory reads and writes, single step.
    /// </summary>
    /// <remarks>
    /// Used to compare the emulated CPU against real hardware; far too slow for bulk comparison, which goes through the emulator.
    /// </remarks>
    public class OpComHelper
    {
        private SerialPort _serialPort;
        private int timeout = 500; // Timeout in milliseconds
        private int string_timeout = 5000; // Timeout in milliseconds (5sec)

        /// <summary>
        /// Wraps an open serial port that is connected to the console of a real machine.
        /// </summary>
        /// <param name="serialPort">
        /// The open port, 9600 bps, even parity, 7 data bits.
        /// </param>
        public OpComHelper(SerialPort serialPort)
        {
            _serialPort = serialPort;
        }




        private int WaitForChar(char c)
        {
            int startTime = Environment.TickCount;

            while (_serialPort.BytesToRead == 0)
            {
                if (Environment.TickCount - startTime >= timeout)
                {
                    Console.WriteLine($"Timeout waiting for echo on char '{c}' (0x{(int)c:X2})");
                    return 0; // No character echoed back within the timeout
                }

                Thread.Sleep(1); // Sleep to prevent tight loop
            }

            int ch = _serialPort.ReadChar();

            if (ch != 0)
                Console.Write((char)ch);
            return ch;
        }

        /// <summary>
        /// Sends one character and waits for the console to echo it.
        /// </summary>
        /// <param name="c">
        /// The character to send.
        /// </param>
        /// <returns>
        /// The echoed character, or 0 on timeout.
        /// </returns>
        public int WriteAndReadChar(char c)
        {
            _serialPort.Write(new char[] { c }, 0, 1);
            // Flush the serial port
            _serialPort.BaseStream.Flush();

            return WaitForChar(c);
        }

        /// <summary>
        /// Sends a string character by character, waiting for each echo.
        /// </summary>
        /// <param name="str">
        /// The text to send.
        /// </param>
        /// <returns>
        /// True when every character was echoed.
        /// </returns>
        public bool WriteAndReadString(string str)
        {
            for (int chIdx = 0; chIdx < str.Length; chIdx++)
            {
                char c = str[chIdx];
                if (WriteAndReadChar(c) == 0)
                {
                    Console.WriteLine($"Timeout waiting for '{c}'");
                    return false; // Abort if no echo received for a character
                }
            }
            return true; // All characters were echoed back successfully
        }


        /// <summary>
        /// Reads from the port until a given character arrives or the string timeout passes.
        /// </summary>
        /// <param name="c">
        /// The character that ends the read.
        /// </param>
        /// <returns>
        /// Everything read before the character.
        /// </returns>
        public string ReadStringUntil(char c)
        {
            int startTime = Environment.TickCount;
            StringBuilder sb = new StringBuilder();


            while (true)
            {
                int ch = WaitForChar(c);

                // Make sure we don't wait forever
                if ((Environment.TickCount - startTime >= string_timeout) || (ch == 0))
                {
                    Console.WriteLine($"Timeout waiting for reading string until char '{c}'");
                    return sb.ToString(); // No character echoed back within the timeout
                }

                if (ch == c)
                {
                    return sb.ToString();
                }

                sb.Append((char)ch);
            }
        }

        internal ushort ReadRegister(string reg, int level = 0)
        {
            string l = "";

            if (level > 0)
            {
                l = level.ToString();
            }

            WriteAndReadString($"{l}{reg}/");
            string result = ReadStringUntil(' ');

            return OctalStringToUShort(result);
        }

        internal void WriteRegister(string reg, ushort value, int level = 0)
        {
            string l = "";

            if (level > 0)
            {
                l = level.ToString();
            }

            WriteAndReadString($"{l}{reg}/");
            string result = ReadStringUntil(' ');

            WriteAndReadString(UShortToOctalString(value) + "\r");

            ReadStringUntil('#');
        }


        /// <summary>
        /// Reads the register set of a program level with OPCOM commands.
        /// </summary>
        /// <param name="level">
        /// The program level, 0 to 15.
        /// </param>
        /// <returns>
        /// The registers.
        /// </returns>
        public ND_Registers ReadAllRegisters(int level = 0)
        {
            ND_Registers regs = new ND_Registers();
            string l = "";

            if (level > 0)
            {
                l = level.ToString();
            }

            level.ToString();
            regs.D = ReadRegister($"{l}D");
            regs.P = ReadRegister($"{l}P");
            regs.B = ReadRegister($"{l}B");
            regs.L = ReadRegister($"{l}L");
            regs.A = ReadRegister($"{l}A");
            regs.T = ReadRegister($"{l}T");
            regs.X = ReadRegister($"{l}X");
            regs.STS = ReadRegister($"I1");


            return regs;
        }

        /// <summary>
        /// Writes a register set into a program level with OPCOM commands.
        /// </summary>
        /// <param name="regs">
        /// The registers to write.
        /// </param>
        /// <param name="level">
        /// The program level, 0 to 15.
        /// </param>
        public void WriteAllRegisters(ND_Registers regs, int level = 0)
        {

            WriteRegister($"D", regs.D, level);
            WriteRegister($"P", regs.P, level);
            WriteRegister($"B", regs.B, level);
            WriteRegister($"L", regs.L, level);
            WriteRegister($"A", regs.A, level);
            WriteRegister($"T", regs.T, level);
            WriteRegister($"X", regs.X, level);
            WriteRegister($"I1", regs.STS, 0);
        }

        /// <summary>
        /// Writes one memory word with an OPCOM deposit.
        /// </summary>
        /// <param name="address">
        /// The word address.
        /// </param>
        /// <param name="value">
        /// The word to write.
        /// </param>
        public void WriteMemory(ushort address, ushort value)
        {
            string a = UShortToOctalString(address);
            string v = UShortToOctalString(value);

            WriteAndReadString(a + "/");
            ReadStringUntil(' ');
            WriteAndReadString(v + "\r");

            // Read next memory value
            ReadStringUntil('#');
            string nextMem = ReadStringUntil(' ');

            // Reset back to base
            //EnterBase();
        }

        /// <summary>
        /// Write a block of data to memory
        /// 
        /// Estimated speed is 188second pr 1024 words
        /// That amounts to about 5.45 words pr second
        /// 
        /// 64KW will take estimated 200 minutes (or 3 hours and 20 minutes)
        /// </summary>
        /// <param name="address"></param>
        /// <param name="values"></param>
        /// <param name="len"></param>
        public void WriteMemory(ushort address, ushort[] values, int len)
        {
            string a = UShortToOctalString(address);

            ushort nextmem = 0;
            // Write start address
            WriteAndReadString(a + "/");
            ReadStringUntil(' ');

            for (int i = 0; i < len; i++)
            {
                if ((nextmem == values[i]) && (i != 0)) // first value we dont know yet
                {
                    WriteAndReadString("\r");
                }
                else
                {
                    string v = UShortToOctalString(values[i]);


                    WriteAndReadString(v + "\r");
                }

                // Read next memory value
                ReadStringUntil('#');
                string nextMem = ReadStringUntil(' ');
                nextmem = OctalStringToUShort(nextMem);
            }
            // Reset back to base
            //EnterBase();
        }

        /// <summary>
        /// Read one word of memory
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public ushort ReadMemory(ushort address)
        {

            // Reset back to base
            //EnterBase();

            string a = UShortToOctalString(address);

            WriteAndReadString(a + "/");
            string v = ReadStringUntil(' ');

            return OctalStringToUShort(v);
        }


        /// <summary>
        /// Read a block of data from memory
        /// 
        /// Estimated speed is 1024 words in 64.8 seconds
        /// 15.8 words pr second
        /// 
        /// 64KW will take estimated 69 minutes
        /// </summary>
        /// <param name="address"></param>
        /// <param name="values"></param>
        /// <param name="len"></param>
        public void ReadMemory(ushort address, ushort[] values, int len)
        {
            string a = UShortToOctalString(address);

            // Write start address
            WriteAndReadString(a + "/");

            for (int i = 0; i < len; i++)
            {

                string nextMem = ReadStringUntil(' ');
                values[i] = OctalStringToUShort(nextMem);



                if (i + 1 < len)
                {
                    WriteAndReadString("\r");
                    ReadStringUntil('#');
                }
            }
            // Reset back to base
            //EnterBase();
        }


        #region Execution
        /// <summary>
        /// Executes one macro instruction on the real machine with the OPCOM single step command.
        /// </summary>
        public void SingleStep()
        {
            WriteAndReadString("Z");
            WriteAndReadString("\r");
            ReadStringUntil('#');
        }


        internal void Execute(ushort start)
        {
            string a = UShortToOctalString(start);
            WriteAndReadString(a);
            WriteAndReadString("!");
            WriteAndReadString("\r");
        }

        #endregion Execution

        #region IO

        internal ushort ReadIO(ushort IOX)
        {
            string ioa = UShortToOctalString(IOX);

            // Write nnnIO/
            WriteAndReadString(ioa);
            WriteAndReadString("/IO");

            string result = ReadStringUntil(' ');
            return OctalStringToUShort(result);
        }

        internal void WriteIO(ushort IOX, ushort val)
        {

            string ioa = UShortToOctalString(IOX);

            WriteRegister("OPR", val);

            // Write nnnIO/
            WriteAndReadString(ioa);
            WriteAndReadString("/IO");

            ReadStringUntil(' ');
            WriteAndReadString("\r");
            ReadStringUntil('#');
        }
        #endregion IO


        #region Helper functions
        /* enter base state */
        internal void EnterBase()
        {
            //WriteAndReadString("F\r");
            //ReadStringUntil('#');

            WriteAndReadString("I/");
            ReadStringUntil(' ');
            WriteAndReadString("\r");
            ReadStringUntil('#');

        }



        /// <summary>
        /// Formats a word as the six octal digits OPCOM expects.
        /// </summary>
        /// <param name="value">
        /// The word.
        /// </param>
        /// <returns>
        /// Six octal digits.
        /// </returns>
        public string UShortToOctalString(ushort value)
        {
            return Convert.ToString(value, 8);
        }


        /// <summary>
        /// Parses the octal digits OPCOM prints.
        /// </summary>
        /// <param name="octalString">
        /// The digits.
        /// </param>
        /// <returns>
        /// The word.
        /// </returns>
        public ushort OctalStringToUShort(string octalString)
        {
            octalString.Trim();

            try
            {
                return (ushort)(Convert.ToUInt16(octalString, 8));
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Error converting octal string '{octalString}' to ushort: {ex.Message}");
                return 0;
            }
        }


        #endregion Helper functions

    }

}

