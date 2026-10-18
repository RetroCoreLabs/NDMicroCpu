//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// Paging Tables for shadow memory
    /// </summary>
    public class PagingTables

    {
        /// <summary>
        /// The Shadow RAM "chip"
        /// </summary>
        internal ushort[] shadowRam;

        /// <summary>
        /// Start address of Shadow RAM
        /// </summary>
        internal uint shadowRamAddress;

        /// <summary>
        /// Start address of "Normal mode" 4 page tables
        /// </summary>
        uint shadowRamNormalMode4pt = 0xFF00; // 177400


        /// <summary>
        /// Start address of "Extended mode" 4 page tables
        /// </summary>
        uint shadowRamExtenedMode4pt = 0xFE00;  //177.000

        /// <summary>
        ///Start address of "Extended mode" 16 page tables (for MMS2 only)
        /// </summary>
        uint shadowRamExtenedMode16pt = 0xF800; // 177.400


        /// <summary>
        /// Bit 26 - Page used
        ///
        /// If PGU = 1, the page has been used. The bit is automatically set whenever the page is accessed and it remains set.
        /// The bit is cleared by program.
        /// This bit may be used in operating systems to determine which page should be swapped.
        /// </summary>
        const uint PGU_FLAG = 1 << 27;

        /// <summary>
        /// Bit 28 - Written In Page
        /// 
        /// If this bit is set, the page has been written in, and it should be written back to mass storage.
        /// If it is zero, the page has not been modified and need not be rewritten.
        /// This bit is automatically set to one the first time a write occurs and then remains set.
        /// It is cleared by program (whenever a new page is brought from mass storage).
        /// </summary>
        const uint WIP_FLAG = 1 << 28;

        //internal uint[] PageTableEntry;
        //internal int maxPagingTables;

        /// <summary>
        /// Creates the sixteen page tables, all entries zero.
        /// </summary>
        public PagingTables()
        {

            // Shadow memory
            // The memory requirements are as follows

            // Memory management MMS-1
            // Normal mode   : 4 page tables = 1 x 64 bit * 4 = 256 Words
            // Extended mode : 4 page tables = 2 x 64 bit * 4 = 512 Words

            // Memory management MMS-2
            // Normal mode   : 4  page tables = 1  x 64 bit * 4 = 256 Words
            // Extended mode : 16 page tables = 16 x 64 bit * 4 = 2048 Words
            shadowRam = new ushort[2048];
            shadowRamAddress = 0xF800; // Start of Shadow Ram for MMS2

            // Clear ShadowRam
            for (int i = 0; i < shadowRam.Length - 1; i++)
            {
                shadowRam[i] = 0;
            }
        }

        /// <summary>
        /// Convert a 16 bit pageTableEntry (as stored in memory) to a 32 bit PageTableEntry as used by the MMS
        /// Limitations: Can only convert standard page table, not extended page table.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        uint ConvertFrom16BitPTE(ushort value)
        {
            uint pte = (uint)((value & 0xFE00) << 16 | (value & 0x01FF));
            return pte;
        }


        /// <summary>
        /// Convert a 32 bit pageTableEntry (for MMS) to a 16 bit PageTableEntry (For memory)
        /// Limitations: Can only convert standard page table, not extended page table.
        /// </summary>
        /// <param name="pageTableEntry"></param>
        /// <returns></returns>
        ushort ConvertTo16BitPTE(uint pageTableEntry)
        {
            ushort res = (ushort)(((pageTableEntry & 0xFE00_0000) >> 16) | (pageTableEntry & 0x0000_01FF));
            return res;
        }


        private uint CalcPageTableAddress(uint address, bool SEXI)
        {
            uint pageTableAddress;
            if (SEXI)
            {
                // 16 page tables start at 174000 (0xF800).
                // PageTable 12 (in MMS2) is PageTable 0 (in MMS1) (12 = 0b1100 (4 bits) = 0b00 (2 bits)					
                pageTableAddress = ((address - 0xF800) & 0x7FF) >> 1; // Divide by 2 (>>1) because each pagePageTableEntry is 2 words

            }
            else
            {
                // Normal mode, 4 page tables, all start at 177400 (FF00)
                pageTableAddress = (address & 0x00ff); // 64 entries in each * 4 pages = 256 = 0xFF
            }


            return pageTableAddress;
        }


        /// <summary>
        /// Write to page tables
        /// </summary>
        /// <param name="address"></param>
        /// <param name="value"></param>
        /// <param name="SEXI"></param>
        public void Write(uint address, ushort value, bool SEXI)
        {
            if ((address < shadowRamAddress) || (address > 0xFFFF)) return;

            uint offset = address - shadowRamAddress;

            shadowRam[offset] = value;


            if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
            {
                uint pageTableEntry;
                uint pageTableAddress = CalcPageTableAddress(address, SEXI);
                uint pageTable = pageTableAddress >> 6; // 6 bits pr pageTableEntry = 64 entries pr pagetable


                if (SEXI == false)
                {
                    pageTableEntry = ConvertFrom16BitPTE(value);
                }
                else
                {
                    if ((address & 0x01) == 0)
                    {
                        // Even address
                        pageTableEntry = (uint)(value << 16 | shadowRam[offset + 1]);
                    }
                    else
                    {
                        // Odd address
                        pageTableEntry = (uint)(shadowRam[offset - 1] << 16 | value);
                    }

                }
                Logger.Log($"PT W A={Numeric.Num2Str(address)} PT={pageTable} VPN={pageTableAddress & 0x3F} SEXI={SEXI}  V={Numeric.Num2Str(value)}=> 0x{pageTableEntry:X8} ({GetPageTableEntryDebugInfo(pageTableEntry, SEXI)})", Logger.LogLevel.Device);
            }
        }


        /// <summary>
        /// Read from shadow mem/pagetables.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="SEXI"></param>
        /// <returns></returns>
        public ushort Read(uint address, bool SEXI)
        {
            ushort res;
            if ((address < shadowRamAddress) || (address > 0xFFFF)) return 0;

            uint offset = address - shadowRamAddress;

            res = shadowRam[offset];


            if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
            {
                uint pageTableEntry;
                uint pageTableAddress = CalcPageTableAddress(address, SEXI);
                uint pageTable = pageTableAddress >> 6; // 6 bits pr pageTableEntry = 64 entries pr pagetable


                if (SEXI == false)
                {
                    pageTableEntry = ConvertFrom16BitPTE(res);
                }
                else
                {
                    if ((address & 0x01) == 0)
                    {
                        // Even address
                        pageTableEntry = (uint)(res << 16 | shadowRam[offset + 1]);
                    }
                    else
                    {
                        // Odd address
                        pageTableEntry = (uint)(shadowRam[offset - 1] << 16 | res);
                    }

                }
                Logger.Log($"PT R A={Numeric.Num2Str(address)} PT={pageTable} VPN={pageTableAddress & 0x3F} SEXI={SEXI}  V={Numeric.Num2Str(res)} <= 0x{pageTableEntry:X8} ({GetPageTableEntryDebugInfo(pageTableEntry, SEXI)})", Logger.LogLevel.CPU);
            }



            return res;

        }

        /// <summary>
        /// Calculate offset into ShadowRam array 
        /// </summary>
        /// <param name="pageTable"></param>
        /// <param name="VPN"></param>
        /// <param name="SEXI"></param>
        /// <param name="ptm"></param>
        /// <returns></returns>
        internal ushort GetPTShadowAddress(uint pageTable, uint VPN, bool SEXI, PageTableMode ptm)
        {
            uint offset = 0;

            if (SEXI)
            {
                // EXTENDED MODE
                switch (ptm)
                {
                    case PageTableMode.Four:
                        offset = shadowRamExtenedMode4pt - shadowRamAddress;
                        break;
                    case PageTableMode.Sixteen: // ONLY for MMS2
                        offset = shadowRamExtenedMode16pt - shadowRamAddress;
                        break;
                }

            }
            else
            {
                // Normal mode
                offset = shadowRamNormalMode4pt - shadowRamAddress;
            }

            uint pageTableAddress = (uint)((pageTable << 6) | VPN);
            if (SEXI)
            {
                // Extended mode, PTe is stored in 2x 16 bits memory addresses
                pageTableAddress = pageTableAddress << 1; // left shift1 == *2
            }
            pageTableAddress += offset;

            return (ushort)pageTableAddress;

        }
        /// <summary>
        /// Read the page table entry from RAM.
        /// 
        /// </summary>
        /// <param name="pageTable"></param>
        /// <param name="VPN"></param>
        /// <param name="SEXI">
        /// True for the extended (SEXI) page table layout.
        /// </param>
        /// <param name="ptm">
        /// The page table mode.
        /// </param>
        /// <returns>
        /// The page table entry.
        /// </returns>
        internal uint GetPageTableEntry(uint pageTable, uint VPN, bool SEXI, PageTableMode ptm)
        {
            uint PTe = 0;

            if ((pageTable < 0) || (pageTable >= 16))
                Debug.Assert(false);


            int pageTableAddress = GetPTShadowAddress(pageTable, VPN, SEXI, ptm);

            if (SEXI)
            {
                PTe = (uint)(shadowRam[pageTableAddress] << 16 | shadowRam[pageTableAddress + 1]);
            }
            else
            {
                if (pageTable <= 3)
                {
                    // If we are in "standard" mode the RAM value is 16 bits so it needs to be converted to 32 bits PTe
                    // Also adjust for memory offset to where standard values are stored
                    PTe = ConvertFrom16BitPTE(shadowRam[pageTableAddress]);
                }
            }

            return PTe;

        }

        /// <summary>
        /// Update RAM from a PageTableEntry.
        /// Used when we update bits in the PTe, for example to set PageUsed (PGU)or PageWritten (PGW) bits
        /// </summary>
        /// <param name="pageTable"></param>
        /// <param name="VPN"></param>
        /// <param name="SEXI">
        /// True for the extended (SEXI) page table layout.
        /// </param>
        /// <param name="ptm">
        /// The page table mode.
        /// </param>
        /// <param name="PTe">Page Table Entry</param>
        /// <returns></returns>
        internal bool UpdatePageTableEntry(uint pageTable, uint VPN, bool SEXI, PageTableMode ptm, uint PTe)
        {
            if ((pageTable < 0) || (pageTable >= 16))
                Debug.Assert(false);

            int pageTableAddress = GetPTShadowAddress(pageTable, VPN, SEXI, ptm);

            if (SEXI)
            {
                shadowRam[pageTableAddress] = (ushort)(PTe >> 16);
                shadowRam[pageTableAddress + 1] = (ushort)(PTe);
            }
            else
            {
                // If we are in "standard" mode the RAM value is 16 bits so it needs to be converted from the 32 bits PTe
                // Also adjust for memory offset to where standard values are stored
                shadowRam[pageTableAddress] = ConvertTo16BitPTE(PTe);
            }

            return true;

        }

        /// <summary>
        /// Set bit 27 (PGU) to mark a page as used
        /// </summary>
        /// <param name="pageTable"></param>
        /// <param name="VPN"></param>
        /// <param name="SEXI">
        /// True for the extended (SEXI) page table layout.
        /// </param>
        /// <param name="ptm">
        /// The page table mode.
        /// </param>
        /// <param name="PTe">
        /// The page table entry to mark.
        /// </param>
        internal uint SetPageUsed(uint pageTable, uint VPN, bool SEXI, PageTableMode ptm, uint PTe)
        {
            //if ((pageTable < 0) || (pageTable >= 16))
            //	Debug.Assert(false);

            uint pageTableAddress = (pageTable << 6) | VPN;


            if ((PTe & PGU_FLAG) == 0)
            {
                PTe |= PGU_FLAG;
                UpdatePageTableEntry(pageTable, VPN, SEXI, ptm, PTe); // Update ShadowRam

                if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
                    Logger.Log($"PageTable PGU - PT={pageTable} VPN={VPN} =>  Entry=0x{PTe:X8} ({GetPageTableEntryDebugInfo(PTe, SEXI)})", Logger.LogLevel.CPU);
            }
            return PTe;
        }

        /// <summary>
        /// Set bit 28 (WIP) to mark a page as written
        /// </summary>
        /// <param name="pageTable"></param>
        /// <param name="VPN"></param>
        /// <param name="SEXI">
        /// True for the extended (SEXI) page table layout.
        /// </param>
        /// <param name="ptm">
        /// The page table mode.
        /// </param>
        /// <param name="PTe">
        /// The page table entry to mark.
        /// </param>
        internal uint SetPageWritten(uint pageTable, uint VPN, bool SEXI, PageTableMode ptm, uint PTe)
        {
            if ((pageTable < 0) || (pageTable >= 16))
                Debug.Assert(false);


            uint pageTableAddress = (pageTable << 6) | VPN;


            if ((PTe & WIP_FLAG) == 0)
            {
                PTe |= WIP_FLAG;
                UpdatePageTableEntry(pageTable, VPN, SEXI, ptm, PTe); // Update ShadowRam

                if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
                    Logger.Log($"PageTable WIP - PT={pageTable} VPN={VPN} =>  Entry=0x{PTe:X8} ({GetPageTableEntryDebugInfo(PTe, SEXI)})", Logger.LogLevel.CPU);

            }

            return PTe;
        }


        /// <summary>
        /// Decodes a page table entry into text for the debugger.
        /// </summary>
        /// <param name="PTe">
        /// The entry.
        /// </param>
        /// <param name="SEXI">
        /// True decodes the extended (SEXI) layout.
        /// </param>
        /// <returns>
        /// The protection bits, the physical page number and the flags as text.
        /// </returns>
        public string GetPageTableEntryDebugInfo(ulong PTe, bool SEXI)
        {
            string debugInfo = "";

            // Map to physical page			
            ushort PPN = 0;
            if (SEXI)
            {
                // Use lower 14-bit
                PPN = (ushort)(PTe & 0x3FFF);
            }
            else
            {
                // "normal" mode, use only the lower 9-bits
                PPN = (ushort)(PTe & 0x1FF);
            }

            PTe = PTe >> 16;

            if ((PTe & 1 << 15) != 0) debugInfo += "[WPM]";
            if ((PTe & 1 << 14) != 0) debugInfo += "[RPM]";
            if ((PTe & 1 << 13) != 0) debugInfo += "[FPM]";
            if ((PTe & 1 << 12) != 0) debugInfo += "[WIP]";
            if ((PTe & 1 << 11) != 0) debugInfo += "[PGU]";

            int ring = (int)((PTe >> 9) & 0x03);
            debugInfo += $"[R:{ring}]";

            debugInfo += $"[PPN:0x{PPN:X4}";

            return debugInfo;
        }



    }
}
