//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core.Common;

namespace NDMicroCpu.Core.Tests.NDCPU
{
    /// <summary>
    /// Round-trip tests for the privileged internal-register instructions on PID and PIE:
    /// TRR (replace), TRA (read), MST (or-set), MCL (and-clear). This is the exact check
    /// sequence INSTRUCTION-VERIFY's PRIVILEGED command runs (test code at 036200-036360
    /// in the INSTRUCTION-B image, error text "*** ERROR ***   MCL/MST/TRA/TRR FAILED"):
    ///
    ///   TRR PIE 0 ; TRR STS 0
    ///   MST PID 177777 ; TRA PID == 177777
    ///   MST PID 0      ; TRA PID == 177777   (MST can only ADD bits)
    ///   MCL PID 0      ; TRA PID == 177777   (MCL with 0 clears nothing)
    ///   MCL PID 177777 ; TRA PID == 0        (modulo bits masked by the program)
    ///   ... and the same ladder for PIE.
    ///
    /// PIE is kept 0 while PID is loaded with all-ones (and vice versa PID is cleared
    /// before PIE gets bits), so no level switch can trigger during the ladder - same
    /// trick the original test program uses. ION stays off as well.
    ///
    /// Microcode paths under test (ND-110-RASK.LISTING.TXT):
    ///   TRR PID: TRPID CS 003706 -> RPID1 CS 001000 (PICFM byte-swap + SMPID + scratch)
    ///   TRA PID: TAPID CS 003666 -> APID1 CS 000653 (probes the Am2914 pair per level
    ///            via LMSK/LOSTS + COND,IRQ in an LCOUNT loop, ORs the software scratch)
    ///   TRR PIE: TRPIE CS 003707 -> RPIE1,  TRA PIE: TAPIE CS 003667 -> APIE1
    ///   MST/MCL: read-modify-write wrappers around the same paths.
    /// </summary>
    public class TestPrivilegedRegisters
    {
        // Opcodes (octal): TRA=150000+fc, TRR=150100+fc, MCL=150200+fc, MST=150300+fc
        // with fc: STS=01, PID=06, PIE=07 (MST/MCL are only legal on STS/PID/PIE).
        const ushort OPC_TRA_PID = 0xD006;  // 150006
        const ushort OPC_TRR_PID = 0xD046;  // 150106
        const ushort OPC_MCL_PID = 0xD086;  // 150206
        const ushort OPC_MST_PID = 0xD0C6;  // 150306
        const ushort OPC_TRA_PIE = 0xD007;  // 150007
        const ushort OPC_TRR_PIE = 0xD047;  // 150107
        const ushort OPC_MCL_PIE = 0xD087;  // 150207
        const ushort OPC_MST_PIE = 0xD0C7;  // 150307
        const ushort OPC_TRA_STS = 0xD001;  // 150001
        const ushort OPC_TRR_STS = 0xD041;  // 150101
        const ushort OPC_MCL_STS = 0xD081;  // 150201
        const ushort OPC_MST_STS = 0xD0C1;  // 150301
        const ushort OPC_COPY_SZ_DA = 0xCC45; // 146105 COPY SZ DA (A := 0) - a plain ROP

        CPU cpu = null!;
        ushort progP;

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;
            cpu = new CPU();
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
            progP = (ushort)Utils.Oct2Int("400");
        }

        /// <summary>
        /// Executes one instruction with A preset, returns the resulting A register.
        /// </summary>
        ushort Exec(ushort opcode, ushort aBefore)
        {
            cpu.memory.WritePhysicalMemory(progP, opcode);
            cpu.WriteALURegister(Enums.RegisterEnum.A, aBefore);
            int newP = cpu.UnitTest_FetchAndExecute(progP);
            Assert.That(newP, Is.EqualTo(progP + 1), $"P after opcode {Utils.Int2Oct(opcode)}");
            progP++;
            return (ushort)cpu.ReadALURegister(Enums.RegisterEnum.A);
        }

        ushort Read(ushort traOpcode) => Exec(traOpcode, 0);

        [Test]
        public void Pid_Trr_Tra_Mst_Mcl_Ladder()
        {
            // Keep PIE 0 the whole time so a full PID can't cause a level switch.
            Exec(OPC_TRR_PIE, 0);

            Exec(OPC_TRR_PID, 0);
            Assert.That(Read(OPC_TRA_PID), Is.EqualTo(0), "TRA PID after TRR PID 0");

            Exec(OPC_MST_PID, 0xFFFF);
            Assert.That(Read(OPC_TRA_PID), Is.EqualTo(0xFFFF), "TRA PID after MST PID 177777");

            Exec(OPC_MST_PID, 0);
            Assert.That(Read(OPC_TRA_PID), Is.EqualTo(0xFFFF), "MST PID 0 must not clear anything");

            Exec(OPC_MCL_PID, 0);
            Assert.That(Read(OPC_TRA_PID), Is.EqualTo(0xFFFF), "MCL PID 0 must not clear anything");

            Exec(OPC_MCL_PID, 0xFFFF);
            Assert.That(Read(OPC_TRA_PID), Is.EqualTo(0), "TRA PID after MCL PID 177777");

            // TRR with a specific pattern must replace, not merge.
            Exec(OPC_MST_PID, 0x00F0);
            Exec(OPC_TRR_PID, 0x1402);
            Assert.That(Read(OPC_TRA_PID), Is.EqualTo(0x1402), "TRR PID must REPLACE the register");
            Exec(OPC_TRR_PID, 0);
        }

        [Test]
        public void Pie_Trr_Tra_Mst_Mcl_Ladder()
        {
            // Keep PID 0 the whole time so a full PIE can't cause a level switch.
            Exec(OPC_TRR_PID, 0);

            Exec(OPC_TRR_PIE, 0);
            Assert.That(Read(OPC_TRA_PIE), Is.EqualTo(0), "TRA PIE after TRR PIE 0");

            Exec(OPC_MST_PIE, 0xFFFF);
            Assert.That(Read(OPC_TRA_PIE), Is.EqualTo(0xFFFF), "TRA PIE after MST PIE 177777");

            Exec(OPC_MST_PIE, 0);
            Assert.That(Read(OPC_TRA_PIE), Is.EqualTo(0xFFFF), "MST PIE 0 must not clear anything");

            Exec(OPC_MCL_PIE, 0);
            Assert.That(Read(OPC_TRA_PIE), Is.EqualTo(0xFFFF), "MCL PIE 0 must not clear anything");

            Exec(OPC_MCL_PIE, 0xFFFF);
            Assert.That(Read(OPC_TRA_PIE), Is.EqualTo(0), "TRA PIE after MCL PIE 177777");

            Exec(OPC_MST_PIE, 0x00F0);
            Exec(OPC_TRR_PIE, 0x1402);
            Assert.That(Read(OPC_TRA_PIE), Is.EqualTo(0x1402), "TRR PIE must REPLACE the register");
            Exec(OPC_TRR_PIE, 0);
        }

        // 0257 octal: STS bits 0-3 (PTM,TG,K,Z), 5 (O) and 7 (M) - the same mask
        // INSTRUCTION-VERIFY uses (image word 036416). C (bit 6) and Q (bit 4) are
        // excluded because they legitimately change with every arithmetic instruction.
        const ushort STS_CHECK_MASK = 0x00AF;

        /// <summary>
        /// The STS ladder from INSTRUCTION-VERIFY's PRIVILEGED MCL/MST/TRA/TRR test
        /// (image 036344-036406). The crucial detail: O (bit 5) is the STATIC overflow -
        /// "OVF \/ STS O -> STS O" per the STS,EA microcode token - so an ordinary
        /// non-overflowing ALU/ROP instruction between MST STS and TRA STS must NOT clear
        /// it. The COPY SZ DA in the middle reproduces the exact instruction the original
        /// test executes between its checks.
        /// </summary>
        [Test]
        public void Sts_Mst_Mcl_Ladder_StaticOverflowIsSticky()
        {
            Exec(OPC_TRR_STS, 0);
            Assert.That((ushort)(Read(OPC_TRA_STS) & STS_CHECK_MASK), Is.EqualTo(0),
                "STS & 0257 after TRR STS 0");

            Exec(OPC_MST_STS, 0x00FF);
            Exec(OPC_COPY_SZ_DA, 0); // plain ROP - must not clear the static O flag
            Assert.That((ushort)(Read(OPC_TRA_STS) & STS_CHECK_MASK), Is.EqualTo(STS_CHECK_MASK),
                "STS & 0257 after MST STS 377 + a non-overflowing ROP (O must be sticky)");

            Exec(OPC_MST_STS, 0);
            Assert.That((ushort)(Read(OPC_TRA_STS) & STS_CHECK_MASK), Is.EqualTo(STS_CHECK_MASK),
                "MST STS 0 must not clear anything");

            Exec(OPC_MCL_STS, 0);
            Assert.That((ushort)(Read(OPC_TRA_STS) & STS_CHECK_MASK), Is.EqualTo(STS_CHECK_MASK),
                "MCL STS 0 must not clear anything");

            Exec(OPC_MCL_STS, 0x00FF);
            Assert.That((ushort)(Read(OPC_TRA_STS) & STS_CHECK_MASK), Is.EqualTo(0),
                "STS & 0257 after MCL STS 377");
        }
    }
}
