//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;


/*
 BPUN FILE FORMAT - http://www.ndwiki.org/wiki/BPUN_File_Format


    +-----------+-----------+-----------+-------+-----------+-----------+-----------+---------------+-----------+
    | Preamble  |   Start   |   Boot    |   !   |   Address |   Count   |   Data    |   Checksum    |   Action  |
    +-----------+-----------+-----------+-------+-----------+-----------+-----------+---------------+-----------+

    Preamble	    May contain any characters except "!" (41 octal, 0x21). Its original use was a bootstrap loader for stand-alone CPUs. This is no longer used. But various tools still generate the bootstrap loader, including the BPUN command in NRL.
    Start	        A field containing an ASCII encoded octal start address for the program. It is terminated by a carriage return (15 octal) and optionally a line feed (12 octal).
    Boot	        A field containing an ASCII encoded octal value giving the start address of the bootstrap loader (see Pre-amble) and terminated by the "!" character (i.e. the next field, below). No longer used, but still created by tools that generate the bootstrap loader.
    "!"	            Exclamation mark: Start-of-data signal. (hex 0x21, octal 41)
    Address	        Address where the binary load of the data will start. Two-byte word in big endian format.
    Count	        Number of 16-bit words in the following Data field. Two-byte word in big endian format.
    Data	        A field containing <Count> 16-bit words. Big endian format.
    Checksum	    Arithmetic sum of all the words in the Data field, modulo 216. Two-byte word in big endian format.
    Action	        Two-byte field. If the action field is zero, execution will start at the address specified in the Start field. If not zero, the CPU will remain in OPCOM mode. The P register will contain the value read from the Start field.

 * 
 */
namespace NDMicroCpu.Core.Support.FileLoader
{
    /// <summary>
    /// A program in the ND BPUN file format: the load and start addresses, the code words and the checksum.
    /// </summary>
    /// <remarks>
    /// The format is documented at http://www.ndwiki.org/wiki/BPUN_File_Format.
    /// </remarks>
    public class BPUN
    {
        internal enum LoadState
        {
            Preamble,
            Address,
            Count,
            Data,
            Checksum,
            Action,
            FloMonCount,
            FloMonLoad,
        }


        /// <summary>
        /// octal start address for the program
        /// </summary>
        public int Start { get; internal set; }

        /// <summary>
        /// octal value giving the start address of the bootstrap loader 
        /// </summary>
        public int Boot { get; internal set; }

        /// <summary>
        /// Address where the binary load of the data will start. Two-byte word in big endian format on disk
        /// </summary>
        public int Address { get; internal set; }


        /// <summary>
        /// Stream containing the code words, 16-bit big endian.
        /// </summary>
        public MemoryStream? Code;

        /// <summary>
        /// True when the file was in the FLO-MON variant of the format.
        /// </summary>
        public bool FloMonFormat = false;

        /// <summary>
        /// The checksum the file carries.
        /// </summary>
        public int Checksum { get; internal set; }

        /// <summary>
        /// The checksum computed from the code words; differs from <see cref="Checksum"/> when the file is damaged.
        /// </summary>
        public int CalculatedChecksum { get; internal set; }

        /// <summary>
        /// Two-byte field. 
        /// If the action field is zero, execution will start at the address specified in the Start field. 
        /// If not zero, the CPU will remain in OPCOM mode. 
        /// The P register will contain the value read from the Start field.
        /// </summary>
        public int Action { get; internal set; }


        /// <summary>
        /// Address where the binary load of the data will start. Two-byte word in big endian format on disk
        /// </summary>
        public int Count { get; internal set; }

        // BPUN file format http://www.ndwiki.org/wiki/BPUN_File_Format

        /// <summary>
        /// Loads a BPUN file from disk.
        /// </summary>
        /// <param name="fileName">
        /// Path of the file.
        /// </param>
        /// <returns>
        /// True when the file was decoded.
        /// </returns>
        public bool LoadBPUN(string fileName)
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
        /// Loads a BPUN image from memory.
        /// </summary>
        /// <param name="data">
        /// The file contents.
        /// </param>
        /// <returns>
        /// True when the image was decoded.
        /// </returns>
        public bool LoadBPUN(byte[] data)
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
        /// Loads a BPUN image from a stream.
        /// </summary>
        /// <param name="ms">
        /// The stream, positioned at the start of the image.
        /// </param>
        /// <returns>
        /// True when the image was decoded.
        /// </returns>
        public bool LoadBPUN(Stream ms)
        {
            return LoadStream(ms);
        }


        internal bool LoadStream(Stream bpunStream)
        {
            Code = null;

            LoadState loadState = LoadState.Preamble;

            string tmpString = String.Empty;
            CalculatedChecksum = 0;
            Address = 0;
            Count = 0;
            Checksum = 0;
            Action = 0;

            int currentLocationCounter = 0;
            int loadAddress = 0;
            int dataCounter = 0;
            int lastValue = 0;// value from last line


            bpunStream.Seek(0, SeekOrigin.Begin);
            int b = 0;
            // Read BPUN header
            while (b >= 0)
            {
                b = bpunStream.ReadByte();

                switch (loadState)
                {
                    case LoadState.Preamble:
                        char c = (char)(b & 0x7F); // convert to 7 bit ascii

                        if (c == '!')
                        {
                            //Logger.DbgC(c);
                            if (!String.IsNullOrEmpty(tmpString))
                            {
                                loadAddress = Utils.ParseIntValue(tmpString);
                            }
                            if (loadAddress == Start)
                            {
                                Boot = lastValue;
                            }
                            else
                            {
                                Boot = loadAddress;
                            }

                            loadState = LoadState.Address;
                            continue;
                        }
                        else if (c == '/')
                        {
                            //Logger.DbgC(c);
                            currentLocationCounter = Utils.ParseIntValue(tmpString);
                            lastValue = currentLocationCounter;

                            if (currentLocationCounter >= 0)
                                Start = currentLocationCounter;


                            // By default, set the loadAddress equal to the first place a memory address is entered
                            if (loadAddress <= 0)
                                loadAddress = currentLocationCounter;

                            tmpString = string.Empty;
                        }
                        // Look for Start and Boot address
                        else if (Char.IsNumber(c))
                        {
                            //Logger.DbgC(c);
                            tmpString += c;

                            // Watch out for unexpected long strings -
                            if (tmpString.Length > 50)
                            {
                                //Logger.DbgC("INVALID INPUT LEN!!");
                                return false;
                            }
                        }
                        else if (c == 0x0D) // Carriage-return
                        {
                            //Logger.DbgC("\r\n");
                            if (tmpString.Length > 0)
                            {

                                int data = Utils.ParseIntValue(tmpString);
                                lastValue = data;
                                /*
                                if (!foundStart)
                                {
                                    Start = Numeric.ParseIntValue(tmpString);
                                    foundStart = true;
                                }
                                else if (!foundBoot)
                                {
                                    Boot = Numeric.ParseIntValue(tmpString);
                                    foundBoot = true;
                                }
                                */
                                tmpString = String.Empty;
                            }
                        }
                        else
                        {
                            //if (c!=0)
                            //    Debug.WriteLine($"BPUN: Unexpected char {(byte)c:X2}");
                        }



                        break;

                    case LoadState.Address: // Read from disk Big Endian WORD
                        Address = b << 8;
                        b = bpunStream.ReadByte();
                        Address |= (byte)b;

                        loadState = LoadState.Count;
                        break;
                    case LoadState.Count: // Read from disk Big Endian WORD

                        Count = b << 8;
                        b = bpunStream.ReadByte();
                        Count |= (byte)b;



                        dataCounter = (Count * 2); // Count is in WORD, we read bytes

                        Code = new MemoryStream(dataCounter); // Allocate memorystream to store code 
                        loadState = LoadState.Data;
                        break;
                    case LoadState.Data: // Read number of COUNT WORDS (in bytes, that is 2x)
                        int data_word = 0;
                        if (dataCounter > 0)
                        {
                            Code?.WriteByte((byte)b);
                            dataCounter--;

                            data_word = (b << 8) & 0xFF00;
                        }



                        if (dataCounter > 0)
                        {
                            b = bpunStream.ReadByte();
                            Code?.WriteByte((byte)b);
                            dataCounter--;
                            data_word |= (b & 0xFF);
                        }

                        if (dataCounter == 0)
                        {
                            loadState = LoadState.Checksum;
                        }

                        CalculatedChecksum = (ushort)(CalculatedChecksum + data_word);
                        break;
                    case LoadState.Checksum: // Read from disk Big Endian WORD
                        Checksum = b << 8;
                        b = bpunStream.ReadByte();
                        Checksum |= (byte)b;

                        loadState = LoadState.Action;

                        if ((Address == 0) && (Count == 0) && (Checksum == 0))
                            loadState = LoadState.FloMonCount;

                        break;

                    case LoadState.Action: // Read from disk 2 bytes
                        Action = b << 8;
                        b = bpunStream.ReadByte();
                        Action |= (byte)b;

                        return true;


                    case LoadState.FloMonCount:
                        FloMonFormat = true;
                        Count = b;

                        loadState = LoadState.FloMonLoad;
                        break;
                    case LoadState.FloMonLoad:
                        int floWords = Count;
                        while (floWords > 0)
                        {
                            // Entering here the first 0x00 byte has already been read by the outside loop. Check it and contiue
                            if (b != 0) return false;

                            // Read HI bits
                            b = bpunStream.ReadByte();
                            Code?.WriteByte((byte)b);

                            // Read 0x00 and ignore
                            b = bpunStream.ReadByte();
                            if (b != 0) return false;

                            // Read LO bits
                            b = bpunStream.ReadByte();
                            data_word = (b << 8) & 0xFF00;
                            Code?.WriteByte((byte)b);

                            // Read 0x00 and ignore
                            b = bpunStream.ReadByte();
                            if (b != 0) return false;


                            floWords--;
                        }
                        return true;
                }
            }

            // Should normally never get here
            return false;
        }
    }
}
