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
using NDMicroCpu.Core.Common;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{

    /// <summary>
    /// Internal Data Bus
    /// </summary>
    public class IDB
    {

        /// <summary>
        /// The word on the bus.
        /// </summary>
        public ushort idb_data;


        /// <summary>
        /// Drives a word onto the bus.
        /// </summary>
        /// <param name="data">
        /// The word.
        /// </param>
        /// <param name="source">
        /// A name of the driver, for the micro code log.
        /// </param>
        public void Latch(ushort data, string source = "")
        {
            string ext = "";
            if (!string.IsNullOrEmpty(source))
                ext = $" <= {source}";


            Logger.Log($"IDBS.Latch({Utils.Int2Oct(data)}) {ext}", Logger.LogLevel.Opcodes);
            idb_data = data;
        }

        internal void Latch(int data, string source = "")
        {
            string ext = "";
            if (!string.IsNullOrEmpty(source))
                ext = $" <= {source}";

            Logger.Log($"IDBS.Latch({Utils.Int2Oct(data)}) {ext}", Logger.LogLevel.Opcodes);
            idb_data = (ushort)data;
        }

        /// <summary>
        /// The low byte of the word on the bus.
        /// </summary>
        /// <returns>
        /// Bits 7-0.
        /// </returns>
        public byte ReadLO()
        {
            byte b = (byte)(idb_data & 0xFF);
            Logger.Log($"IDBS.ReadLO({Utils.Int2Oct(b)}) ", Logger.LogLevel.Opcodes);

            return b;
        }

        /// <summary>
        /// The high byte of the word on the bus.
        /// </summary>
        /// <returns>
        /// Bits 15-8.
        /// </returns>
        public byte ReadHI()
        {

            byte b = (byte)(idb_data >> 8 & 0xFF);
            Logger.Log($"IDBS.ReadHI({Utils.Int2Oct(b)})", Logger.LogLevel.Opcodes);

            return b;
        }

        /// <summary>
        /// The whole word on the bus.
        /// </summary>
        /// <returns>
        /// Bits 15-0.
        /// </returns>
        public ushort ReadWord()
        {
            Logger.Log($"IDBS.Read({Utils.Int2Oct(idb_data)})", Logger.LogLevel.Opcodes);
            return idb_data;
        }

        /// <summary>
        /// Return byteswap of data
        /// </summary>
        /// <returns></returns>
        public ushort ReadSwap()
        {

            return (ushort)(ReadLO() << 8 | ReadHI());
        }


    }
}
