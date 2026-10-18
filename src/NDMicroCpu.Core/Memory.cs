//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core
{
    /// <summary>
    /// Selects the MMU translation strategy in Memory.MapVirtualToPhysical:
    ///   Legacy      - resolve on the fly every access (walk the shadow-RAM page table + protection).
    ///   Tlb         - lazy software TLB: cache resolved translations, O(1) epoch invalidation.
    ///   ResidentMap - lean on-the-fly read of the shadow-RAM page table (which IS the flat resident
    ///                 map), with the PGU/WIP "already set?" check inlined to skip the setter call on
    ///                 repeat accesses. No cache, no allocation. Mirrors the ND-120 hardware idea that
    ///                 the page table itself is the resident map; expected ~= Legacy in software.
    /// </summary>
    public enum MmuMode
    {
        /// <summary>
        /// Walk the page table on every access; the trusted default.
        /// </summary>
        Legacy,

        /// <summary>
        /// Cache recent translations in a software TLB.
        /// </summary>
        Tlb,

        /// <summary>
        /// Keep the page table itself as the resident map, no cache and no allocation.
        /// </summary>
        ResidentMap
    }

    /// <summary>
    /// One memory access performed against the physical RAM array, captured for cross-emulator
    /// comparison. Word address, matching ND memory addressing (the RAM array is word-addressed).
    /// </summary>
    public struct MemoryTraceEntry
    {
        /// <summary>
        /// The physical word address accessed.
        /// </summary>
        public uint Address;
        /// <summary>
        /// The value read or written.
        /// </summary>
        public ushort Value;
        /// <summary>
        /// True for a write, false for a read.
        /// </summary>
        public bool IsWrite;

        /// <summary>True when this access went to the shadow / page-table window, not the ram[] array.</summary>
        public bool IsShadow;
    }

    /// <summary>
    /// The physical memory of the machine and its page table mapping: physical and virtual reads and writes, the segment register, and the memory access trace.
    /// </summary>
    public class Memory
    {
        CPU cpu = null!;   // set by the CPU through SetCpu right after construction

        /// <summary>
        /// The page tables the mapping goes through.
        /// </summary>
        public PagingTables pagingTables = new PagingTables();

        /// <summary>
        /// Per-instruction memory access trace, used by the remote-debugger comparison harness to
        /// verify that RetroCore's ND-100 performs the SAME memory accesses as the real microcode.
        ///
        /// Disabled by default: <see cref="TraceMemoryEnabled"/> gates every record, so the normal
        /// execution path pays one already-predicted branch and nothing else.
        ///
        /// Scope is deliberately narrow, to mirror what the RetroCore side can observe through its
        /// bus-level MemoryStreamer hook:
        ///   - only accesses that actually touch the `ram` array are recorded;
        ///   - shadow-memory / page-table accesses are NOT recorded (they early-return above, and on
        ///     the RetroCore side they never reach the bus either);
        ///   - out-of-range accesses are NOT recorded (they return without touching ram; on the
        ///     RetroCore side FindMemoryBank returns null and the access throws before the bus hook).
        /// Arming is the CALLER's job (see DebuggerService.Step) so that debugger reads/writes and
        /// the BPUN loader are never mistaken for something the instruction did.
        /// </summary>
        public bool TraceMemoryEnabled;

        /// <summary>
        /// Diagnostic: also record shadow / page-table window accesses in the trace. Off by default
        /// because the RetroCore side cannot see them (they never reach its bus hook), so including
        /// them makes the two traces non-comparable. Turn on to find out whether an access that looks
        /// "missing" on this side is really going through the page-table window.
        /// </summary>
        public bool TraceIncludeShadow;

        /// <summary>Accesses recorded since the last <see cref="ClearMemoryTrace"/>, in order.</summary>
        public readonly List<MemoryTraceEntry> MemoryTrace = new List<MemoryTraceEntry>(64);

        /// <summary>Drops all recorded accesses. Call immediately before stepping one instruction.</summary>
        public void ClearMemoryTrace()
        {
            MemoryTrace.Clear();
        }

        /// <summary>
        /// How many megaword memory do we allocate
        /// </summary>
        const int MegaWord = 2;

        /// <summary>
        /// Calculate Memorysize
        /// </summary>
        public const int MemorySize = 1024 * 1024 * 2;

        ushort[] ram;

        /// <summary>
        /// Reference to Internal memory array of Memory Class
        /// </summary>
        public ushort[] InternalMemoryArray { get => ram; }

        /// <summary>
        /// Creates memory of <see cref="MemorySize"/> words, all zero.
        /// </summary>
        public Memory()
        {
            ram = new ushort[MemorySize];
        }

        /// <summary>
        /// Connects the CPU whose status bits (SEXI, paging on, PIL) the mapping consults.
        /// </summary>
        /// <param name="cpuRef">
        /// The CPU that owns this memory.
        /// </param>
        public void AttachCPU(CPU cpuRef)
        {
            cpu = cpuRef;
        }

        /// <summary>
        /// The memory array itself, for loaders and tests that fill it directly.
        /// </summary>
        public ushort[] Raw { get => ram; }

        /// <summary>
        /// Zeroes a range of physical RAM. Used by the cross-emulator comparison harness to reset the
        /// long-running oracle's memory to the same clean image RetroCore's fresh-per-test machine has,
        /// so un-seeded addresses no longer return prior-test residue. Page tables (shadow storage) are
        /// untouched. count &lt;= 0 clears from start to the end of RAM.
        /// </summary>
        public void ClearRAM(int start, int count)
        {
            if (start < 0) start = 0;
            if (start >= MemorySize) return;

            int end = (count <= 0) ? MemorySize : start + count;
            if (end > MemorySize) end = MemorySize;

            Array.Clear(ram, start, end - start);
        }

        /// <summary>
        /// Whether a physical word address is inside the installed memory.
        /// </summary>
        /// <param name="address">
        /// The word address.
        /// </param>
        /// <returns>
        /// True when the address can be read or written.
        /// </returns>
        public bool ValidAddress(int address)
        {
            if ((address < MemorySize) && (address >= 0)) return true;

            return false;
        }



#if _not_
        /// <summary>
        /// Read memory indirect via PageTable
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        public ushort ReadIndirect(int address)
        {
            //TODO: Implement page-table lookup!!


            if (address > MemorySize)
            {
                Logger.Log($"MEM WRITE/Invalid Address A:{Utils.Int2Oct(address)}", Logger.LogLevel.Machine);
                return 0;
            }


            if (IndirectReadType == IndirectRead.PT)
            {
                // Read using PT
                ushort v = ram[address];
                Logger.Log($"MEM READ [PT] A:{Utils.Int2Oct(address)} V:{Utils.Int2Oct(v)}", Logger.LogLevel.Machine);                
                return v;
            }

            else
            {
                // Read via APT
                ushort v = ram[address];
                Logger.Log($"MEM READ [APT] A:{Utils.Int2Oct(address)} V:{Utils.Int2Oct(v)}", Logger.LogLevel.Machine);                
                return v;
            }
        }
#endif


        /// <summary>
        /// Reads a physical memory word.
        /// </summary>
        /// <param name="address">
        /// The word address.
        /// </param>
        /// <param name="privileged">
        /// True for an access by the CPU itself rather than a program, which bypasses the memory access trace.
        /// </param>
        /// <returns>
        /// The word, or 0 outside the installed memory.
        /// </returns>
        public ushort ReadPhysicalMemory(int address, bool privileged = false)
        {

            ushort data = ReadPhysicalMemory((uint)address, privileged);
            // Hot path (every memory read): only build the log string if Machine logging is on -
            // the $"..." interpolation would otherwise run per access and cripple WASM perf.
            if (Logger.IsLogEnabled(Logger.LogLevel.Machine))
                Logger.Log($"MEM READ A:{Utils.Int2Oct((int)address)} V:{Utils.Int2Oct(data)}", Logger.LogLevel.Machine);
            return data;
        }

        /// <summary>
        /// Reads a physical memory word.
        /// </summary>
        /// <param name="address">
        /// The word address.
        /// </param>
        /// <param name="privileged">
        /// True for an access by the CPU itself rather than a program, which bypasses the memory access trace.
        /// </param>
        /// <returns>
        /// The word, or 0 outside the installed memory.
        /// </returns>
        public ushort ReadPhysicalMemory(uint address, bool privileged = false)
        {
            if (IsAddressShadowMemory(address, privileged))
            {
                ushort sv = pagingTables.Read(address, cpu.hw.alu.STS_SEXI);

                // Shadow / page-table RAM is still a memory read the instruction performed - record it.
                // (Diagnostic: TraceIncludeShadow lets us see whether a "missing" access on this side
                // is actually going through the page-table window rather than the ram[] array.)
                if (TraceMemoryEnabled && TraceIncludeShadow)
                    MemoryTrace.Add(new MemoryTraceEntry { Address = address, Value = sv, IsWrite = false, IsShadow = true });

                return sv;
            }

            if (address > MemorySize)
            {
                Logger.Log($"MEM Read/Invalid Address A:{Numeric.Num2Str(address)}", Logger.LogLevel.Machine);
                return 0;
            }


            ushort v = ram[address];

            // Cross-emulator comparison trace. Recorded here, at the real RAM touch, so it matches
            // exactly what the RetroCore side observes at its bus hook. See TraceMemoryEnabled.
            if (TraceMemoryEnabled)
                MemoryTrace.Add(new MemoryTraceEntry { Address = address, Value = v, IsWrite = false });

            if (Logger.IsLogEnabled(Logger.LogLevel.Machine))
                Logger.Log($"MEM READ A:{Utils.Int2Oct((int)address)} V:{Utils.Int2Oct(v)}", Logger.LogLevel.Machine);
            return v;
        }

        /// <summary>
        /// Helper to avoid casting
        /// </summary>
        /// <param name="address"></param>
        /// <param name="value"></param>
        public void WritePhysicalMemory(int address, ushort value)
        {
            if (Logger.IsLogEnabled(Logger.LogLevel.Machine))
                Logger.Log($"MEM WRITE A:{Utils.Int2Oct((int)address)} V:{Utils.Int2Oct(value)}", Logger.LogLevel.Machine);
            WritePhysicalMemory((uint)address, value);
        }

        /// <summary>
        /// Writes a physical memory word.
        /// </summary>
        /// <param name="address">
        /// The word address.
        /// </param>
        /// <param name="value">
        /// The word to write.
        /// </param>
        /// <param name="privileged">
        /// True for an access by the CPU itself rather than a program, which bypasses the memory access trace.
        /// </param>
        public void WritePhysicalMemory(uint address, ushort value, bool privileged = false)
        {

            if (IsAddressShadowMemory(address, privileged))
            {
                pagingTables.Write(address, value, cpu.hw.alu.STS_SEXI);
                // Software just edited a page-table entry (PTE / ring bits / PPN) in the
                // shadow RAM - any cached translation may now be stale, so flush the TLB.
                FlushTLB();
                return;
            }

            if (address > MemorySize)
            {
                Logger.Log($"MEM WRITE/Invalid Address A:{Utils.Int2Oct((int)address)}", Logger.LogLevel.Machine);
                return;
            }

            // Cross-emulator comparison trace. Recorded here, at the real RAM touch, so it matches
            // exactly what the RetroCore side observes at its bus hook. See TraceMemoryEnabled.
            if (TraceMemoryEnabled)
                MemoryTrace.Add(new MemoryTraceEntry { Address = address, Value = value, IsWrite = true });

            if (Logger.IsLogEnabled(Logger.LogLevel.Machine))
                Logger.Log($"MEM WRITE A:{Utils.Int2Oct((int)address)} V:{Utils.Int2Oct(value)}", Logger.LogLevel.Machine);
            ram[address] = value;
        }

        internal ushort ExamineRead(ushort address)
        {

            // Combine the 64K segment register (loaded by COMM,LDSEG in the *TX microcode) with the
            // 16-bit offset to form the full 24-bit physical address - the DERQ/DepositWrite path
            // already does this; the examine path was dropping the segment and reading the bare offset.
            int pa = ((segment_register & 0xFF) << 16) | address;
            return ReadPhysicalMemory(pa, true);
        }
        internal void DepositWrite(uint address, ushort value)
        {

            uint pa = ((uint)segment_register << 16) | address;
            WritePhysicalMemory(pa, value, true);
        }

        /// <summary>
        /// The segment register that supplies the address bits above 16 for the deposit and examine console commands.
        /// </summary>
        public ushort segment_register = 0;
        internal void LoadSegmentRegister(ushort segmentreg)
        {
            segment_register = segmentreg;
        }


        /// <summary>
        /// Which page table an indirect read goes through.
        /// </summary>
        public IndirectRead IndirectReadType { get; set; }

        // Set by ReadMem
        /// <summary>
        /// The last virtual address a read was asked for, kept for the debugger.
        /// </summary>
        public int LogicalAddress { get; set; }

        #region Software TLB (translation cache)
        // -------------------------------------------------------------------------
        // Small software TLB that caches successful virtual->physical PAGE translations
        // so repeated accesses to the same page skip the full page-table walk +
        // protection recompute done in MapVirtualToPhysical. This is the hot path for
        // byte-string ops (BFILL/MOVB) and any tight paged loop which hammer only a
        // handful of pages.
        //
        // The cache is 100% transparent - it only ever returns what the slow path
        // would have returned, and it always falls through to the slow path for any
        // access that could fault or has a not-yet-performed side effect (PGU/WIP).
        //
        // KEY:  (pt selector, VPN). VPN is 6 bits (64 pages); pt is Standard/Alternative.
        //       => 128 entries, direct-mapped (index = pt*64 + VPN). The 0-15 page-table
        //       NUMBER is not part of the key, but every input that selects it (the PCR
        //       page-table bits) is folded into the context signature below, so a
        //       page-table switch flushes the whole cache - no aliasing possible.
        //
        // VALIDITY / INVALIDATION (O(1) via an epoch stamp; a hit needs entry.epoch == tlbEpoch):
        //   1) Context-signature change. Everything that steers translation besides the
        //      shadow-RAM contents and the (pt,VPN) key is captured by the pair
        //      (STS, PCR): STS carries PONI, SEXI, P and the current level PIL; PCR
        //      carries the ring, the 16-page-table-mode bit and the page-table number.
        //      We recompute sig = (STS<<16)|PCR on every translate; if it changed we
        //      bump tlbEpoch. This automatically flushes on paging enable/disable, SEXI
        //      change, P change, ring change, page-table switch, and every level/PIL
        //      change (which reloads PCR) - no need to hunt individual register writes.
        //   2) Page-table edit. Any software write into the page-table shadow RAM
        //      (WritePhysicalMemory -> pagingTables.Write) bumps tlbEpoch.
        //   3) Master clear resets STS/PCR, so (1) flushes it; FlushTLB() is also exposed.
        //
        // writeCapable: a translation cached from a READ/FETCH has NOT had WIP set. A
        // WRITE to such a page must still run the slow path once so SetPageWritten (WIP)
        // happens; we mark writeCapable only when the caching access was a WRITE. Thus a
        // WRITE only hits the cache once WIP is already set. PGU is set by both read and
        // write on the slow path before the entry is populated, so it is never skipped.
        private struct TlbEntry
        {
            public uint epoch;        // must equal tlbEpoch for the entry to be valid
            public uint physBase;     // PPN << 10 (physical page base, OR with DIP for full address)
            public bool writeCapable; // true => WIP already set, WRITE may use the cached translation
        }

        // MMU translation strategy (per-instance so each CPU can choose independently and a
        // benchmark can flip it in the SAME binary). DEFAULT = Legacy (the known-good
        // walk-every-access path) until the alternatives are 100% trusted. Selected via the CPU
        // constructor (useMmuTlb / mmuMode), CPU.MmuMode, or directly here.
        /// <summary>
        /// Which virtual-to-physical mapping strategy is in use; Legacy walks the page table on every access and is the trusted default.
        /// </summary>
        public MmuMode MmuMode { get; set; } = MmuMode.Legacy;

        // Back-compat convenience: TlbEnabled == (MmuMode == Tlb). Setting it toggles between
        // Tlb and Legacy (does not touch ResidentMode).
        /// <summary>
        /// True when the TLB strategy is in use; setting it switches between Tlb and Legacy.
        /// </summary>
        public bool TlbEnabled
        {
            get => MmuMode == MmuMode.Tlb;
            set => MmuMode = value ? MmuMode.Tlb : MmuMode.Legacy;
        }

        private readonly TlbEntry[] tlb = new TlbEntry[128];
        private uint tlbEpoch = 1;             // 0 is reserved: freshly-zeroed entries never match
        private uint tlbSig = 0xFFFFFFFF;      // last translation-context signature

        /// <summary>
        /// Invalidate the entire software TLB in O(1). Correctness-first flush hook.
        /// </summary>
        public void FlushTLB()
        {
            // Bump the epoch so every existing entry (stamped with the old epoch) misses.
            if (++tlbEpoch == 0)
            {
                // Epoch wrapped (after ~4 billion flushes) - reset and hard-clear so the
                // reserved-0 invariant and old epoch-1 stamps can never alias.
                tlbEpoch = 1;
                Array.Clear(tlb, 0, tlb.Length);
            }
        }
        #endregion


        // Derive the page-table NUMBER and the 4/16-page-table mode from the PCR. Shared by the
        // Legacy/Tlb slow walk and the ResidentMap path so they can never diverge. In 16-pt mode
        // (PCR bit 2 set) main=PCR[14:11], alt=PCR[10:7]; in 4-pt mode main=PCR[10:9], alt=PCR[8:7].
        // The alternative table is only used when STS.PTM (STS_P) is set and pt==Alternative.
        private void DerivePageTable(PageTable pt, ushort pcr, out byte pageTable, out PageTableMode ptm)
        {
            if (cpu.hw.alu.STS_P && pt == PageTable.AlternativePageTable)
            {
                if ((pcr & 1 << 2) != 0) { pageTable = (byte)((pcr >> 7) & 0xF); ptm = PageTableMode.Sixteen; }  // Alt 16x
                else { pageTable = (byte)((pcr >> 7) & 0x03); ptm = PageTableMode.Four; }                        // Alt 4x
            }
            else
            {
                if ((pcr & 1 << 2) != 0) { pageTable = (byte)((pcr >> 11) & 0xF); ptm = PageTableMode.Sixteen; } // Main 16x
                else { pageTable = (byte)((pcr >> 9) & 0x03); ptm = PageTableMode.Four; }                        // Main 4x
            }
        }

        // ResidentMap translation: the shadow-RAM page table IS the flat resident map, so resolve
        // the mapping on the fly with a single PTE read - no cache, no allocation. Behaviour is
        // identical to the Legacy walk (same ring trap, same PGU/WIP side effects, same out-of-range
        // return); the only "lean" difference is that the PGU/WIP "already set?" test is inlined so
        // we skip the SetPageUsed/SetPageWritten CALL on the hot path where the bits are already set.
        private uint ResidentTranslate(PageTable pt, ushort pcr, byte ring, uint VPN, uint DIP, AccessMode am)
        {
            DerivePageTable(pt, pcr, out byte pageTable, out PageTableMode ptm);

            bool sexi = cpu.hw.alu.STS_SEXI;                         // PTE width + PPN width depend on SEXI
            uint pte = pagingTables.GetPageTableEntry(pageTable, VPN, sexi, ptm);

            // Ring protection: current ring must be >= the page's ring, else MPV trap (matches Legacy).
            byte pageTableRing = (byte)((pte >> 25) & 0x03);
            if (!(ring >= pageTableRing))
            {
                cpu.hw.SetTrap(2); // Protect violation
                return 0;
            }

            // Physical page number: 14 bits in extended (SEXI) mode, 9 bits in normal mode.
            ushort PPN = sexi ? (ushort)(pte & 0x3FFF) : (ushort)(pte & 0x1FF);
            uint physicalAddress = ((uint)PPN << 10) | (uint)DIP;
            if (physicalAddress > Memory.MemorySize)
                return 0; // out of range (matches Legacy)

            // PGU/WIP one-shot side effects. Inline the "already set?" bit test (PGU=bit27,
            // WIP=bit28 in the 32-bit PTE) so repeat accesses skip the setter call entirely.
            const uint PGU_FLAG = 1u << 27;
            const uint WIP_FLAG = 1u << 28;
            if ((pte & PGU_FLAG) == 0)
                pte = pagingTables.SetPageUsed(pageTable, VPN, sexi, ptm, pte);
            if (am == AccessMode.WRITE && (pte & WIP_FLAG) == 0)
                pagingTables.SetPageWritten(pageTable, VPN, sexi, ptm, pte);

            return physicalAddress;
        }


        #region Virtual mem


        /// <summary>
        /// Map a 16 bit virtual address to a 16, 19 or 24 bit physical address depending on configuration.
        /// Also does memory protection
        /// </summary>
        /// <param name="virtualAddress">A 6 bit virtual address</param>
        /// <param name="am">
        /// What the access is for; checked against the page protection bits.
        /// </param>
        /// <param name="pt">
        /// Selects address mapping.
        /// </param>
        /// <returns>
        /// Up to 24-bit physical address, or -1 on error.
        /// </returns>
        public uint MapVirtualToPhysical(uint virtualAddress, AccessMode am = AccessMode.READ, PageTable pt = PageTable.StandardPageTable)
        {
            // Update HW reference


            cpu.hw.cpu_regs.lastPT = pt;

            virtualAddress = virtualAddress & 0xFFFF; // Make sure its no more than 16-bits
            byte pageTable = 0;

            // Read PCR for the current level and calculate the ring we are executing the code under.
            ushort pcr = cpu.hw.cpu_regs.PCR; // Read out the Page Control Register for the current level
            byte ring = (byte)(pcr & 0x03);  // 2 lower bits of the PCR is the Ring the current level is using


            // Ring 3 is the most powerfull, and for Ring 3 RAM will always be in the shadow of PageTable RAM
            if ((ring == 3) && (IsAddressShadowMemory(virtualAddress, false)))
            {
                return virtualAddress; // Read/WritePhysical will handle the actual access to shadow memory
            }

            /*

			The paging system maps the 16-bit virtual address from the address arithmetic into a physical address.
			The number of bits in the physical address depends on whether the "normal" (19-bit NORD10/S compatible) mode is used.
			If paging isnt enabed, virtual address maps directly to physical address - a 16 bit address


			16-bit Virtual Address
			+----------+-------+
			| 15 -  10 | 9  - 0|
			+----------+-------+
			|   VPN    | DIP   |
			+----------+-------+

			To address any location within a 1K address space, 10 address bits are required.
			These bits are refered as the displacement within a page (DIP).
			The most significant part of the virtual address (bits 10-15) us used as an address slection on the 64 locations in the page table.
			These six bits are referred to as the Virtual Page Number (VPN)


			*/

            // If memory management is not enabled, don't use mapping (physical = virtual)
            if (!cpu.hw.alu.STS_PONI)
                return (uint)(virtualAddress & 0xFFFF);


            // Calculate VPN and DIP
            uint DIP = virtualAddress & 0x3FF; // lower 10 bites - Displacement
            uint VPN = (virtualAddress >> 10) & 0x3f; // upper 6 bits - Virtual Page number

            // Recompute the translation-context signature. Any change (paging on/off, SEXI,
            // P, ring, page-table number, 16-pt mode, current level/PIL) invalidates the TLB.
            // Kept current in all modes so switching to Tlb mid-run is always coherent.
            uint sig = ((uint)cpu.hw.alu.STS << 16) | pcr;
            if (sig != tlbSig)
            {
                tlbSig = sig;
                FlushTLB();
            }

            MmuMode mode = MmuMode;

            // ---- Software TLB fast path (Tlb mode only) ---------------------------------
            // Direct-mapped index: one slot per (pt, VPN).
            int tlbIndex = (pt == PageTable.AlternativePageTable ? 64 : 0) + (int)VPN;
            ref TlbEntry tlbHit = ref tlb[tlbIndex];
            if (mode == MmuMode.Tlb && tlbHit.epoch == tlbEpoch && (am != AccessMode.WRITE || tlbHit.writeCapable))
            {
                // Cache hit: PGU was already set when this entry was populated (slow path),
                // and for WRITEs writeCapable guarantees WIP is already set - so no page-table
                // side effect is pending. Return the same physical address the slow path would.
                // (lastPT was already updated at function entry.)
                return tlbHit.physBase | DIP;
            }

            // ---- ResidentMap: lean on-the-fly read (no cache) ---------------------------
            // The shadow-RAM page table IS the flat resident map; resolve directly on the fly.
            if (mode == MmuMode.ResidentMap)
                return ResidentTranslate(pt, pcr, ring, VPN, DIP, am);

            // ---- Legacy path (and Tlb miss): full walk ---------------------------------
            DerivePageTable(pt, pcr, out pageTable, out PageTableMode ptm);

            // Find the PageTableEntry, PTe (16-bit PTE when SEXI is clear, 32-bit when set)
            uint pageTableEntry = pagingTables.GetPageTableEntry(pageTable, VPN, cpu.hw.alu.STS_SEXI, ptm);

            //// Check for page protection
            //if (!checkPageProtection(VPN, pageTable, pageTableEntry, pt, am, virtualAddress))
            //{
            //    // We should never get here, but added a return statement anyway! (Will end up here if interrupts are disabled?)
            //    return -1;
            //}


            byte pageTableRing = (byte)((pageTableEntry >> 25) & 0x03);

            //if ((pageTableRing != ring) && (am == AccessMode.FETCH))
            //	Debug.WriteLine("woot");

            // Degrade ?
            // If a program in ring 3 executes instructions assigned to rings 0,1 or 2, its ring number is reduced accordingly.
            // Such accessess are detected by hardware which automatically changes the ring number in the PCR register for the current program level.
            // NB! This degrading only occurs when lower ring instruction codes are executed, but not when data is accessed
#if _DEGRADE_
			if (((am == AccessMode.FETCH) || (am == AccessMode.READ_FETCH)) && (pageTableRing < ring) && (ring == 3)) // ?? => && (regs.PIL!=0))
			{

				if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
					Logger.Log($"[{regs.PIL}] Ring Downgrade Trap. From  Ring={ring} to PT-Ring={pageTableRing} Accessmode ={am} PGS={Numeric.Num2Str(regs.PGS)} PageTable={pageTable} VPN={VPN} PageTableEntry=0x{pageTableEntry:X8}", Logger.LogLevel.CPU);

				// Degrade!
				ring = pageTableRing;

				regs.PCR[regs.PIL] = (ushort)(regs.PCR[regs.PIL] & 0xFFFC); // Clear ring bits in PCR		
				regs.PCR[regs.PIL] = (ushort)(regs.PCR[regs.PIL] | ring); // Set bit 0-1 in PCR to the current ring level
			}
#endif



            #region Ring Protection Check
            // Check for Ring Protection
            // INFO: For the ND CPU Ring 3 is most powerfull, ring 0 least powerfull.
            // If the current level has a "ring level" that is smaller than the ring level on the page, generate a fault
            //
            // The ring bits of the appropriate PCR are compared with the ring bits of the appropriate page table entry.
            // The PCR ring bits should always be greater than or equal to the PT ring bits. If not, an internal interrupt (MPV) will be generated.
            if (!(ring >= pageTableRing))
            {

                if (Logger.IsLogEnabled(Logger.LogLevel.Device))
                    Logger.Log($" Ring Protection Violation. Ring={ring} PTRing={pageTableRing} Accessmode={am}  PT={pageTable} VPN={VPN} PTe=0x{pageTableEntry:X8}", Logger.LogLevel.Device);

                cpu.hw.SetTrap(2); // Protect violation

                return 0;
            }
            #endregion Ring Protection Check



            // Map to physical page			
            ushort PPN = 0;
            if (cpu.hw.alu.STS_SEXI)
            {
                // Use lower 14-bit
                PPN = (ushort)(pageTableEntry & 0x3FFF);
            }
            else
            {
                // "normal" mode, use only the lower 9-bits
                PPN = (ushort)(pageTableEntry & 0x1FF);
            }

            // Calculate physical address
            uint physicalAddress = ((uint)PPN << 10) | (uint)DIP;

            bool outOfRange = false;


            if (physicalAddress > Memory.MemorySize)
            {
                //Debug.Assert(false); // Should be impossible, there must be something wrong with the content in the pageing tables
                outOfRange = true;
            }


            if (outOfRange)
            {

                //cpu.hw.SetTrap(1); // page fault
                //UpdatePGS(pageTable, VPN, am);

                //if (Logger.IsLogEnabled(Logger.LogLevel.Device))
                //    Logger.Log($"[{regs.PIL}] Memory out of range. Ring={ring} PTRing={pageTableRing} Accessmode ={am} PGS={Numeric.Num2Str(regs.PGS)} PageTable={pageTable} PageTableAddress={Numeric.Num2Str(VPN)} PageTableEntry=0x{pageTableEntry:X8}", Logger.LogLevel.Device);

                //int PES = (ushort)(physicalAddress & 0xFFFF); // Lower 16 bits of physical memory
                //int PEA = (ushort)(physicalAddress >> 16 & 0xFF); // Upper 8 bits of physical memory

                //Set and LOCK regs
                //regs.SetPES((ushort)PES);
                //regs.SetPEA((ushort)PEA);

                //InternalInterruptLvl14(1 << 9, regs.PC, physicalAddress); //Interrupt Level 9=MOR (Memory out of range)
                // We should never get here, but added a return statement anyway! (Will end up here if interrupts are disabled?)
                return 0;
            }
            else
            {
                // Mark page used
                pageTableEntry = pagingTables.SetPageUsed(pageTable, VPN, cpu.hw.alu.STS_SEXI, ptm, pageTableEntry);
                if (am == AccessMode.WRITE)
                {
                    // Mark page as "written to" (so SINTRAN knows it need to store it to disk before removing page from memory)
                    pageTableEntry = pagingTables.SetPageWritten(pageTable, VPN, cpu.hw.alu.STS_SEXI, ptm, pageTableEntry);
                }

                // Populate the software TLB with this successful, in-range translation (Tlb
                // mode only; Legacy must not touch the cache). PGU (and, for a WRITE, WIP) have
                // now been set, so subsequent same-page accesses can skip the walk. writeCapable
                // is true only when this access was a WRITE (WIP just set); a page first touched
                // by READ stays writeCapable=false so a later WRITE re-runs the slow path once.
                if (mode == MmuMode.Tlb)
                {
                    tlbHit.epoch = tlbEpoch;
                    tlbHit.physBase = (uint)(PPN << 10);
                    tlbHit.writeCapable = (am == AccessMode.WRITE);
                }
            }

#if _later_eccr_
            // ECC Memory Parity Interrupt enabled?
            if ((regs.ECCR & 1 << 3) != 1) // If 1, error correction and parity error interrupt is disabled.
            {
                int eccBits = 0;

                //For debugging: Logger.Log($"Memory Parity Fault check! ECCR: {Numeric.Num2Str(regs.ECCR)}", Logger.LogLevel.Device);

                if ((regs.ECCR & 1 << 0) != 0) // Simulate memory error in bit 0
                    eccBits++;

                if ((regs.ECCR & 1 << 1) != 0) // Simulate memory error in bit 15
                    eccBits++;

                if ((regs.ECCR & 1 << 4) != 0) // Simulate memory error in bit 6
                    eccBits++;

                if (eccBits > 0)
                {
                    //if (
                    //		(eccBits > 1) || // more than 1 bit error .. or 
                    //		((eccBits == 1) && ((regs.ECCR & 1 << 2) == 1)) // ECCR allows interrupt on only 1-bit error
                    //		)
                    {

                        int PES = (ushort)(physicalAddress & 0xFFFF); // Lower 16 bits of physical memory
                        int PEA = (ushort)(physicalAddress >> 16 & 0xFF); // Upper 8 bits of physical memory
                        int errorCode = 0;


                        if (eccBits == 1)
                        {
                            // Single bit, error table  Figure 2.18, page 2-51 in ND-06.014.02
                            if ((regs.ECCR & 1 << 0) != 0) // Simulate memory error in bit 0
                                errorCode = 3;

                            if ((regs.ECCR & 1 << 1) != 0) // Simulate memory error in bit 15
                                errorCode = 0x1C; // o34

                            if ((regs.ECCR & 1 << 4) != 0) // Simulate memory error in bit 6
                                errorCode = 0x0D; // o15

                            // Set error code
                            PES |= errorCode << 8;
                        }
                        else
                        {
                            // Multiple bits
                            PES |= 1 << 13; // FATAL ERROR
                                            // no error code as its multiple bits							
                        }



                        if (am == AccessMode.FETCH)
                        {
                            PES |= 1 << 15; // Error during fetch (or DEPOSIT ot EXAM)
                        }

                        // When the error condition occurs, the content of PES and PEA is locked and not released until TRA PEA is executed.
                        // These registers do not contain correct information unless
                        // an internal interrupt with code 10 or 11(parity error and memory out of range) is detected


                        // Set and LOCK regs
                        regs.SetPES((ushort)PES);
                        regs.SetPEA((ushort)PEA);

                        Logger.Log($"Memory Parity Fault! Bits={eccBits} ErrorCode={errorCode} VirtualAddress={Numeric.Num2Str(virtualAddress)} PT={pageTable} VPN={VPN} PTe={pageTableEntry:X8} Accessmode={am} PGS={Numeric.Num2Str(regs.PGS)}", Logger.LogLevel.Device);
                        InternalInterruptLvl14(1 << 8, pageTable, regs.PC); // PTY - Memory parity Fault										
                    }
                }
            }
#endif


            // Return physical address
            return physicalAddress;
        }



        bool IsAddressShadowMemory(uint addr, bool privileged)
        {
            ushort pcr = cpu.hw.cpu_regs.PCR;
            int ring = pcr & 0x03;
            bool mms2Enabled = ((pcr & 1 << 2) != 0); // Is MMS-2 with 16-page-tables enabled on this PCR level ?

            if ((ring == 3) || (cpu.hw.alu.STS_PONI == false) || privileged)
            {
                if (cpu.hw.alu.STS_SEXI == true)
                {
                    if (mms2Enabled)
                    {
                        if ((addr >= 0xF800) && (addr <= 0xFFFF)) // SEXI enabled and Memory between 0177000 and 0177777
                            return true;
                    }
                    else
                    {
                        if ((addr >= 0xFE00) && (addr <= 0xFFFF)) // SEXI enabled and Memory between 0177000 and 0177777
                            return true;
                    }
                }
                else
                {
                    if ((addr >= 0xFF00) && (addr <= 0xFFFF))  // SEXI disabled and Memory between 0177400 and 0177777
                        return true;

                }
            }


            return false;
        }


        #endregion

    }
}
