//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Collections.Generic;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.Threading.Tasks;
using ProtoBuf;

//
// IMPORTANT: this file is DUPLICATED, byte-for-byte, in the RetroCore repository
// (Emulated.Debugger\GRPC\ND110\Models.cs). The two repositories are intentionally decoupled
// (no ProjectReference) and share only this code-first contract. ANY change here MUST be
// mirrored there, keeping every [DataMember(Order = n)] value identical - protobuf matches
// fields by Order, not by name, so a mismatched Order silently deserializes garbage rather
// than failing loudly.
//

namespace NDMicroCpu.Debugger.Contracts
{

    #region Step

    /// <summary>
    /// Asks the micro code CPU to execute a number of macro instructions.
    /// </summary>
    [DataContract]
    public class StepRequest
    {
        /// <summary>
        /// Number of macro instructions to execute.
        /// </summary>
        [DataMember(Order = 1)]
        public uint Steps { get; set; }

        /// <summary>
        /// When true, the real-time clock interrupt is allowed to fire while stepping.
        /// </summary>
        [DataMember(Order = 2)]
        public bool RTCInterrupt { get; set; }

        /// <summary>
        /// When true, the response carries the per-instruction memory access trace
        /// (<see cref="StepResponse.MemAccesses"/>). Off by default so existing callers,
        /// and bulk stepping, pay nothing for collecting it.
        /// </summary>
        [DataMember(Order = 3)]
        public bool TraceMemory { get; set; }
    }

    /// <summary>
    /// One memory access performed by the CPU while executing an instruction.
    /// Records what the EXECUTING INSTRUCTION did - debugger and BPUN-loader accesses are excluded.
    /// </summary>
    [DataContract]
    public class MemAccess
    {
        /// <summary>
        /// Physical ND memory address, in WORDS (not bytes).
        /// </summary>
        [DataMember(Order = 1)]
        public int Address { get; set; }

        /// <summary>
        /// Value read, or value written.
        /// </summary>
        [DataMember(Order = 2)]
        public ushort Value { get; set; }

        /// <summary>
        /// True for a write, false for a read.
        /// </summary>
        [DataMember(Order = 3)]
        public bool IsWrite { get; set; }
    }

    /// <summary>
    /// The register set after a <see cref="StepRequest"/>, and optionally the memory accesses made.
    /// </summary>
    [DataContract]
    public class StepResponse
    {
        /// <summary>
        /// The D register.
        /// </summary>
        [DataMember(Order = 1)]
        public ushort D { get; set; }

        /// <summary>
        /// The P register, the program counter.
        /// </summary>
        [DataMember(Order = 2)]
        public ushort P { get; set; }

        /// <summary>
        /// The B register.
        /// </summary>
        [DataMember(Order = 3)]
        public ushort B { get; set; }

        /// <summary>
        /// The L register, the link register.
        /// </summary>
        [DataMember(Order = 4)]
        public ushort L { get; set; }

        /// <summary>
        /// The A register, the accumulator.
        /// </summary>
        [DataMember(Order = 5)]
        public ushort A { get; set; }

        /// <summary>
        /// The T register.
        /// </summary>
        [DataMember(Order = 6)]
        public ushort T { get; set; }

        /// <summary>
        /// The X register, the index register.
        /// </summary>
        [DataMember(Order = 7)]
        public ushort X { get; set; }

        /// <summary>
        /// The status register.
        /// </summary>
        [DataMember(Order = 8)]
        public ushort STS { get; set; }

        /// <summary>
        /// Number of macro instructions actually run; 0 means the step failed.
        /// </summary>
        [DataMember(Order = 9)]
        public ushort Steps { get; set; }

        /// <summary>
        /// Memory accesses performed by the executing instruction, in order.
        /// Populated only when <see cref="StepRequest.TraceMemory"/> was set; null otherwise.
        /// </summary>
        [DataMember(Order = 10)]
        public List<MemAccess>? MemAccesses { get; set; }

    }
    #endregion Step

    #region SYNC

    /// <summary>
    /// Loads a complete register set into the CPU, so the client and this CPU start an
    /// instruction from the same state.
    /// </summary>
    [DataContract]
    public class SyncRequest
    {
        /// <summary>
        /// The D register.
        /// </summary>
        [DataMember(Order = 1)]
        public ushort D { get; set; }

        /// <summary>
        /// The P register, the program counter.
        /// </summary>
        [DataMember(Order = 2)]
        public ushort P { get; set; }

        /// <summary>
        /// The B register.
        /// </summary>
        [DataMember(Order = 3)]
        public ushort B { get; set; }

        /// <summary>
        /// The L register, the link register.
        /// </summary>
        [DataMember(Order = 4)]
        public ushort L { get; set; }

        /// <summary>
        /// The A register, the accumulator.
        /// </summary>
        [DataMember(Order = 5)]
        public ushort A { get; set; }

        /// <summary>
        /// The T register.
        /// </summary>
        [DataMember(Order = 6)]
        public ushort T { get; set; }

        /// <summary>
        /// The X register, the index register.
        /// </summary>
        [DataMember(Order = 7)]
        public ushort X { get; set; }

        /// <summary>
        /// The status register.
        /// </summary>
        [DataMember(Order = 8)]
        public ushort STS { get; set; }
    }

    /// <summary>
    /// Confirms a <see cref="SyncRequest"/> by returning the program counter the CPU now has.
    /// </summary>
    [DataContract]
    public class SyncResponse
    {
        /// <summary>
        /// The P register after the registers were loaded.
        /// </summary>
        [DataMember(Order = 1)]
        public ushort P { get; set; }
    }
    #endregion SYNC


    #region ReadMemory

    /// <summary>
    /// Reads one word of memory, physical or through a page table.
    /// </summary>
    [DataContract]
    public class ReadMemRequest
    {
        /// <summary>
        /// The word address to read.
        /// </summary>
        [DataMember(Order = 1)]
        public int Address { get; set; }

        /// <summary>
        /// True reads physical memory, false reads virtual memory through a page table.
        /// </summary>
        [DataMember(Order = 2)]
        public bool PhysicalMem { get; set; }

        /// <summary>
        /// For a virtual read, the page table number to translate through.
        /// </summary>
        [DataMember(Order = 3)]
        public ushort PageTable { get; set; }

        /// <summary>
        /// For a virtual read, true uses the alternative page table.
        /// </summary>
        [DataMember(Order = 4)]
        public bool IsAPT { get; set; }
    }

    /// <summary>
    /// The word a <see cref="ReadMemRequest"/> read.
    /// </summary>
    [DataContract]
    public class ReadMemResponse
    {
        /// <summary>
        /// The word read.
        /// </summary>
        [DataMember(Order = 1)]
        public ushort Data { get; set; }
    }
    #endregion ReadMemory


    #region WriteMemory

    /// <summary>
    /// Writes one word of memory, physical or through a page table.
    /// </summary>
    [DataContract]
    public class WriteMemRequest
    {
        /// <summary>
        /// The word address to write.
        /// </summary>
        [DataMember(Order = 1)]
        public int Address { get; set; }

        /// <summary>
        /// The word to write.
        /// </summary>
        [DataMember(Order = 2)]
        public ushort Value { get; set; }

        /// <summary>
        /// True writes physical memory, false writes virtual memory through a page table.
        /// </summary>
        [DataMember(Order = 3)]
        public bool PhysicalMem { get; set; }

        /// <summary>
        /// For a virtual write, the page table number to translate through.
        /// </summary>
        [DataMember(Order = 4)]
        public ushort PageTable { get; set; }

        /// <summary>
        /// For a virtual write, true uses the alternative page table.
        /// </summary>
        [DataMember(Order = 5)]
        public bool IsAPT { get; set; }

    }

    /// <summary>
    /// Confirms a <see cref="WriteMemRequest"/>.
    /// </summary>
    [DataContract]
    public class WriteMemResponse
    {
        /// <summary>
        /// True when the word was written.
        /// </summary>
        [DataMember(Order = 1)]
        public bool WriteSucceeded;
    }
    #endregion WriteMemory


    #region ClearMemory
    /// <summary>
    /// Zeroes a range of the oracle's physical RAM so a per-opcode comparison test starts from the
    /// SAME clean image RetroCore uses (RetroCore builds a fresh, zeroed machine in every [SetUp];
    /// the oracle is one long-running server whose RAM otherwise persists between tests, so un-seeded
    /// addresses returned prior-test residue - the register-block "divergences" were exactly this).
    /// Only the ram[] array is cleared; page tables live in separate shadow storage and are untouched.
    /// </summary>
    [DataContract]
    public class ClearMemRequest
    {
        /// <summary>
        /// First physical word address to clear.
        /// </summary>
        [DataMember(Order = 1)]
        public int Start { get; set; }

        /// <summary>
        /// Number of words to clear. 0 = clear the whole RAM from Start.
        /// </summary>
        [DataMember(Order = 2)]
        public int Count { get; set; }
    }

    /// <summary>
    /// Confirms a <see cref="ClearMemRequest"/>.
    /// </summary>
    [DataContract]
    public class ClearMemResponse
    {
        /// <summary>
        /// True when the range was cleared.
        /// </summary>
        [DataMember(Order = 1)]
        public bool Cleared { get; set; }
    }
    #endregion ClearMemory


    /// <summary>
    /// The gRPC service another emulator uses to drive this micro code CPU and compare itself
    /// against it: load registers, step, and read, write or clear memory.
    /// </summary>
    [ServiceContract]
    public interface IDebuggerService
    {
        /// <summary>
        /// Executes macro instructions and returns the registers afterwards.
        /// </summary>
        /// <param name="request">
        /// How many instructions to run and what to record.
        /// </param>
        /// <returns>
        /// The register set and, when asked for, the memory accesses made.
        /// </returns>
        [OperationContract]
        ValueTask<StepResponse> Step(StepRequest request);

        /// <summary>
        /// Loads a register set into the CPU.
        /// </summary>
        /// <param name="request">
        /// The registers to load.
        /// </param>
        /// <returns>
        /// The program counter the CPU now has.
        /// </returns>
        [OperationContract]
        ValueTask<SyncResponse> Sync(SyncRequest request);

        /// <summary>
        /// Reads one word of memory.
        /// </summary>
        /// <param name="request">
        /// The address and the address space to read.
        /// </param>
        /// <returns>
        /// The word read.
        /// </returns>
        [OperationContract]
        ValueTask<ReadMemResponse> ReadMemory(ReadMemRequest request);

        /// <summary>
        /// Writes one word of memory.
        /// </summary>
        /// <param name="request">
        /// The address, the address space and the word to write.
        /// </param>
        /// <returns>
        /// Whether the write succeeded.
        /// </returns>
        [OperationContract]
        ValueTask<WriteMemResponse> WriteMemory(WriteMemRequest request);

        /// <summary>
        /// Zeroes a range of physical memory.
        /// </summary>
        /// <param name="request">
        /// The first word and the number of words to clear.
        /// </param>
        /// <returns>
        /// Whether the range was cleared.
        /// </returns>
        [OperationContract]
        ValueTask<ClearMemResponse> ClearMemory(ClearMemRequest request);


    }
}
