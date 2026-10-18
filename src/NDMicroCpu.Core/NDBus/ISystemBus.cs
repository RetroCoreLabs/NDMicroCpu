//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Core.NDBus
{
    /// <summary>
    /// The memory side of the I/O bus, as seen by a DMA-capable device. A bus master
    /// (floppy/disk/tape DMA controller) reads and writes main memory through this
    /// interface during a transfer - the machine provides the concrete implementation
    /// over its physical memory.
    ///
    /// Addresses here are BYTE addresses. The ND devices work in 16-bit words and a
    /// device's own DMA helper converts a word (core) address to a byte address by
    /// shifting left one (byteAddress = wordAddress &lt;&lt; 1), matching the reused
    /// RetroCore NDBusFloppyDMA convention (DMABus.WriteMemory16(coreAddress &lt;&lt; 1, ...)).
    /// </summary>
    public interface ISystemBus
    {
        /// <summary>Read one 16-bit word from main memory at the given BYTE address.</summary>
        int ReadMemory16(uint byteAddress);

        /// <summary>Write one 16-bit word to main memory at the given BYTE address.</summary>
        void WriteMemory16(uint byteAddress, ushort data);
    }

    /// <summary>
    /// ISystemBus implementation over the emulator's <see cref="Memory"/>. Converts the
    /// device's byte address back to the word address the ND memory is indexed by
    /// (word = byte &gt;&gt; 1) and uses the physical-memory path (DMA is not paged - it
    /// addresses physical core directly, like the real bus masters).
    /// </summary>
    public sealed class MemoryDmaBus : ISystemBus
    {
        private readonly Memory _memory;

        /// <summary>
        /// Gives DMA devices access to the machine memory.
        /// </summary>
        /// <param name="memory">
        /// The memory to read and write.
        /// </param>
        public MemoryDmaBus(Memory memory)
        {
            _memory = memory;
        }

        /// <summary>
        /// Reads a 16-bit word for a DMA transfer.
        /// </summary>
        /// <param name="byteAddress">
        /// The byte address; the word address is half of it.
        /// </param>
        /// <returns>
        /// The word.
        /// </returns>
        public int ReadMemory16(uint byteAddress)
        {
            return _memory.ReadPhysicalMemory(byteAddress >> 1);
        }

        /// <summary>
        /// Writes a 16-bit word for a DMA transfer.
        /// </summary>
        /// <param name="byteAddress">
        /// The byte address; the word address is half of it.
        /// </param>
        /// <param name="data">
        /// The word to write.
        /// </param>
        public void WriteMemory16(uint byteAddress, ushort data)
        {
            _memory.WritePhysicalMemory(byteAddress >> 1, data);
        }
    }
}
