//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NDMicroCpu.Core.Support.FileLoader;
using NDMicroCpu.Core.Support.FileLoader.BPUN_Binary;

namespace NDMicroCpu.Core.Support
{
    /// <summary>
    /// Loads BPUN program images straight into a memory array, bypassing the micro code loader. Kept for tests and for the legacy debugger; the machine boots over the bus instead.
    /// </summary>
    public static class FileHelper
    {

        /// <summary>
        /// Loads the embedded INSTRUCTION-B test program into memory.
        /// </summary>
        /// <param name="memory">
        /// The memory array to fill.
        /// </param>
        /// <returns>
        /// True when the image decoded and was copied in.
        /// </returns>
        static public bool LoadDefaultBPUN(ushort[] memory)
        {
            //return LoadBPUN(FileSys.filesys_investigator, memory);
            //return LoadBPUN(RTC.rtc_bpun, memory);
            return LoadBPUN(Instructions.instructions, memory);
        }
        /// <summary>
        /// Loads a BPUN file from disk into memory.
        /// </summary>
        /// <param name="bpunfile">
        /// Path of the file.
        /// </param>
        /// <param name="memory">
        /// The memory array to fill.
        /// </param>
        /// <returns>
        /// True when the image decoded and was copied in.
        /// </returns>
        static public bool LoadBPUN(string bpunfile, ushort[] memory)
        {
            if (!File.Exists(bpunfile)) return false;

            //Logger.Dbg($"Loading BPUN from file [{bpunfile}]");

            byte[] buffer = File.ReadAllBytes(bpunfile);

            return LoadBPUN(buffer, memory);
        }

        /// <summary>
        /// Loads a BPUN image from memory into the machine memory.
        /// </summary>
        /// <param name="bytes">
        /// The image.
        /// </param>
        /// <param name="memory">
        /// The memory array to fill.
        /// </param>
        /// <returns>
        /// True when the image decoded and was copied in.
        /// </returns>
        static public bool LoadBPUN(byte[] bytes, ushort[] memory)
        {
            BPUN bpun = new();
            byte err = 0;

            if (!bpun.LoadBPUN(bytes))
            {
                return false;
            }


            if (bpun.Checksum != bpun.CalculatedChecksum)
            {
                err++;
            }

            if (bpun.Code != null)
            {
                var byteArray = bpun.Code.ToArray();

                for (int i = 0; i < byteArray.Length; i += 2)
                {

                    int memOffset = bpun.Address + (i / 2);

                    memory[memOffset] = (ushort)(byteArray[i] << 8 | (byteArray[i + 1]));
                }

                if (err != 0) return false;
            }
            return true;
        }
    }
}
