//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace NDMicroCpu.Core.Support.FileLoader
{
    /// <summary>
    /// A program in the ND PROG file format: the header addresses and the code to load.
    /// </summary>
    /// <remarks>
    /// The format is documented at http://www.ndwiki.org/wiki/PROG_File_Format.
    /// </remarks>
    public class PROG
    {
        // PROG file format: http://www.ndwiki.org/wiki/PROG_File_Format

        /// <summary>
        /// The address execution starts at.
        /// </summary>
        public int StartAddress { get; internal set; }

        /// <summary>
        /// The address execution restarts at.
        /// </summary>
        public int RestartAddress { get; internal set; }

        /// <summary>
        /// The first address the code occupies in bank 1.
        /// </summary>
        public int FirstAddressBank1 { get; internal set; }
        /// <summary>
        /// The last address the code occupies in bank 1.
        /// </summary>
        public int LastAddressBank1 { get; internal set; }

        /// <summary>
        /// The first address the code occupies in bank 2.
        /// </summary>
        public int FirstAddressBank2 { get; internal set; }
        /// <summary>
        /// The last address the code occupies in bank 2.
        /// </summary>
        public int LastAddressBank2 { get; internal set; }


        /// <summary>
        /// Number of memory banks used
        /// </summary>
        public int Banks { get; set; }

        /// <summary>
        /// The code words, as read from the file; null until a load succeeds.
        /// </summary>
        public Stream? Code;

        /// <summary>
        /// Loads a PROG file from disk.
        /// </summary>
        /// <param name="fileName">
        /// Path of the file.
        /// </param>
        /// <returns>
        /// True when the header was read and the code is available.
        /// </returns>
        public bool LoadPROG(string fileName)
        {
            // Load file into decoded structure with metdatdata.
            // WORD arry should be ready to be read by caller (if successfull) to inserto into RAM or otherwise manipulate data
            if (!File.Exists(fileName))
            {
                //Logger.Dbg($"LoadBPUN: File doesnt exist [{fileName}]", Logger.LogLevel.Error);
                return false;
            }


            FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);

            return LoadStream(fs);
        }

        /// <summary>
        /// Loads a PROG image from memory.
        /// </summary>
        /// <param name="data">
        /// The file contents.
        /// </param>
        /// <returns>
        /// True when the header was read and the code is available.
        /// </returns>
        public bool LoadPROG(byte[] data)
        {

            if (data is null)
            {
                //Logger.Dbg($"LoadBPUN: No data", Logger.LogLevel.Error);
                return false;
            }


            // Load file into decoded structure with metdatdata.
            // WORD arry should be ready to be read by caller (if successfull) to inserto into RAM or otherwise manipulate data
            MemoryStream ms = new MemoryStream(data);

            return LoadStream(ms);
        }



        /// <summary>
        /// Reads one 16-bit big-endian word from a stream.
        /// </summary>
        /// <param name="s">
        /// The stream, positioned at the word.
        /// </param>
        /// <returns>
        /// The word.
        /// </returns>
        public int ReadWord(Stream s)
        {
            int w = 0;
            int b = 0;


            b = s.ReadByte();
            if (b < 0) return -1;
            w = b << 8;


            b = s.ReadByte();
            if (b < 0) return -1;
            w |= (byte)b;

            return w;
        }

        internal bool LoadStream(Stream progStream)
        {
            Code = progStream;

            progStream.Seek(0, SeekOrigin.Begin);

            StartAddress = ReadWord(progStream);
            RestartAddress = ReadWord(progStream);
            FirstAddressBank1 = ReadWord(progStream);
            LastAddressBank1 = ReadWord(progStream);
            FirstAddressBank2 = ReadWord(progStream);
            LastAddressBank2 = ReadWord(progStream);



            if ((FirstAddressBank2 == 0xFFFF) && (LastAddressBank2 == 0x00))
            {
                Banks = 1;
            }
            else
            {
                Banks = 2;
            }
            return true;
        }
    }
}
