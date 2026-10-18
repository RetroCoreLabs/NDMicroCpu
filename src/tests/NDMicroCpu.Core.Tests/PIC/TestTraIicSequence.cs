//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core;
using static NDMicroCpu.Core.AMD.Am2914;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.PIC
{
    /// <summary>
    /// Replays, at the InterruptSubSystem/Am2914 level, the EXACT PIC command sequence real
    /// ND-110 microcode issues in the "TRA IIC" routine (AIIC1, CS 000676-000707 in
    /// ND-110-RASK.LISTING.TXT), then asserts device interrupt pins and checks whether /IRQ
    /// (and therefore the IIC computation that depends on it) responds.
    ///
    /// AIIC1's traced command sequence (see prior investigation, addresses in octal):
    ///   000676  PIC,RMSK  -> R6 := current mask               (SAVE - not needed for this test)
    ///   000677  AB,IIE    -> Q := IIE register                (ND-specific, not modeled here)
    ///   000700  Q := IIE AND 37760(oct)                       (keep bits 4..13)
    ///   000701  PIC,LMSK <- INVQ  -> mask := NOT(Q)            (temporary mask, unmask enabled internal levels)
    ///   000702  Q := 25(oct)                                  (status fence value)
    ///   000703  PIC,ION -> enable IRQ request flip-flop
    ///   000704  PIC,LOSTS <- Q(25 oct) -> status fence := 25(oct)
    ///   000706  COND,IRQ -> sample the chip pair's combined /IRQ pin
    ///   000715  PIC,LMSK <- R6 -> mask restored (epilogue, not needed for this test)
    ///
    /// This test does NOT drive the IIE register (that is ND-100 architecture state outside
    /// the Am2914 pair, not modeled by this test) - instead it directly loads the mask the
    /// routine would compute (all-enabled, 0x0000) to isolate whether the Am2914 pair itself
    /// correctly reports /IRQ once IOC-driven device pins are asserted, matching the exact
    /// ION + LOSTS(25 octal) configuration the real routine uses.
    /// </summary>
    public class TestTraIicSequence
    {
        InterruptSubSystem pic = null!;   // created in SetUp before every test

        [SetUp]
        public void Setup()
        {
            pic = new InterruptSubSystem();
        }

        /// <summary>
        /// Replays AIIC1's PIC,LMSK / PIC,ION / PIC,LOSTS sequence with an all-enabled mask
        /// (0x0000) and the exact status fence value (25 octal) the real routine loads, then
        /// asserts INT10/INT12 and checks /IRQ.
        ///
        /// FINDING (2026-07-11, confirmed by running this test against the real chip model):
        /// /IRQ does NOT assert for INT10 or INT12 under this exact fence. Status fence 25(oct)
        /// decodes to a 3-bit status register value of 5 (0b10101 & 0x7). Per the Am2914
        /// datasheet, "an interrupt request output will occur if the vector is greater than
        /// or equal to status." INT10's priority-encoder value is 0 (InterruptPin.INT10 = pin
        /// 0 on pic_low) and INT12's is 2 (pin 2) - both below the fence of 5, so /IRQ
        /// correctly stays deasserted. This is CORRECT chip behavior, not a bug: it proves
        /// AIIC1's "TRA IIC" routine is deliberately fenced to only see priority levels >= 5,
        /// i.e. it is NOT designed to detect device interrupts INT10-INT13 (pins 0-3) at all -
        /// those live below the fence. Per InterruptPin (Enums.cs), pins >= 5 are the genuine
        /// internal-fault sources this routine targets: PROTV(5)/PAGEF(6)/ROR_II(7) on
        /// pic_low, Z(8)/II6(9)/IOXERR(10)/PARERR(11)/MOR(12)/POWFAIL(13)/INT14(14)/INT15(15)
        /// on pic_high. So the original "TRA IIC always returns 0" hang is very likely NOT
        /// caused by a missing UART interrupt at all - it depends on one of those internal-
        /// fault pins being asserted, which is a different investigation.
        /// </summary>
        [TestCase(InterruptPin.INT10)]
        [TestCase(InterruptPin.INT12)]
        public void ReplayAiic1Sequence_WithRealStatusFence_AndDevicePinSet(InterruptPin pin)
        {
            // PIC,LMSK <- 0x0000: unmask everything (this is what AIIC1 computes when all
            // internal-interrupt sources are IIE-enabled; using 0 here isolates the /IRQ
            // mechanism itself from the ND-specific IIE register, which is not modeled here).
            pic.LatchMaskBus(0x0000);
            pic.Exec(Am2914Instruction.LDM);

            // PIC,ION
            pic.Exec(Am2914Instruction.ENIN);

            // PIC,LOSTS <- 25(octal) = 0b10101 = status(bits 0-2)=5, high-chip GE(bit3)=0(asserted), low-chip GE(bit4)=1(not asserted)
            pic.ExecuteLoadStatus(0b10101);

            pic.SetInterruptPIN(pin);

            bool irqAsserted = pic.ReadInterruptRequestPin == false; // active low

            Assert.That(irqAsserted, Is.False,
                $"{pin}'s priority-encoder value is below AIIC1's status fence (25 oct -> status=5), so /IRQ " +
                $"correctly should NOT assert for this pin under this exact setup - see Am2914 datasheet: " +
                "\"an interrupt request output will occur if the vector is greater than or equal to status\". " +
                "If this now passes with True, something changed the fence/pin-priority mapping - re-verify " +
                "against ND-110-RASK.LISTING.TXT CS 000702-000704 before assuming it's correct.");
        }

        /// <summary>
        /// Same sequence but with the status fence cleared to 0 (accept any priority) - isolates
        /// whether AIIC1's specific "25 octal" fence value (not general PIC plumbing) is what
        /// prevents low-numbered device pins like INT10/INT12 from ever asserting /IRQ.
        /// </summary>
        [TestCase(InterruptPin.INT10)]
        [TestCase(InterruptPin.INT12)]
        public void ReplayAiic1Sequence_WithZeroStatusFence_AndDevicePinSet(InterruptPin pin)
        {
            pic.LatchMaskBus(0x0000);
            pic.Exec(Am2914Instruction.LDM);

            pic.Exec(Am2914Instruction.ENIN);

            // Status fence = 0 : accept any priority, both chips' Group Enable asserted (bits 3,4 = 0)
            pic.ExecuteLoadStatus(0x0000);

            pic.SetInterruptPIN(pin);

            bool irqAsserted = pic.ReadInterruptRequestPin == false;

            Assert.That(irqAsserted, Is.True,
                $"Expected /IRQ to assert after setting {pin} with mask=all-enabled, ION, and a zero status " +
                "fence (accept any priority). If this STILL fails, the bug is in the base PIC pin/mask/IRQ " +
                "plumbing, not the AIIC1 status fence value.");
        }

        /// <summary>
        /// Regression test for the INSTRUCTION-VERIFY "0!" TRA IIC hang (root-caused
        /// 2026-07-11): with a lone RTC request pending (pin 3 = BINT13, LOW chip) and the
        /// status fence left at LOSTS 000000 by the microcode's descending IIC fence-scan
        /// (both chips' Group Enable inputs asserted), Read Vector must report the LOW
        /// group's /GS asserted with vector 3 - i.e. IDB value 0b10011: bit4 = lo /GS,
        /// bit3 = hi /GS (clear!), bits 0-2 = pin number. The pre-fix Group Signal
        /// implementation mirrored only the GE inputs latched by the last Load Status, so
        /// this exact state returned 0b11000 (BOTH group signals asserted, vector 0) - the
        /// microcode read the hi-group bit, jumped to EXT14 ("internal interrupt"), entered
        /// level 14 with no internal fault pending in pins 4-13, and the level-14 handler's
        /// TRA IIC poll loop then hung forever at macro P 020626/020627.
        /// </summary>
        [Test]
        public void ReadVector_LoneLowChipRequest_AfterLostsZero_ReportsLowGroupOnly()
        {
            pic.LatchMaskBus(0x0000);
            pic.Exec(Am2914Instruction.LDM);
            pic.Exec(Am2914Instruction.ENIN);

            // The dispatch-time state from the traced hang: fence scan ended with
            // LOSTS 000000 - status 0, BOTH Group Enable inputs asserted.
            pic.ExecuteLoadStatus(0x0000);

            pic.SetInterruptPIN(InterruptPin.INT13); // pin 3, LOW chip - the 20ms RTC

            ushort vector = pic.ExecuteReadVector();

            Assert.That(vector & 0b111, Is.EqualTo(3), "Vector bits 0-2 must identify pin 3 (BINT13/RTC)");
            Assert.That(vector & (1 << 4), Is.Not.EqualTo(0), "LOW group /GS (bit 4) must be asserted - the winning request is on the low chip");
            Assert.That(vector & (1 << 3), Is.EqualTo(0), "HIGH group /GS (bit 3) must NOT be asserted - nothing is pending on the high chip; " +
                "a spurious hi /GS here sends the microcode down the EXT14/level-14 internal-interrupt path and hangs TRA IIC");
        }

        /// <summary>
        /// Companion to the test above: when requests are pending on BOTH chips, the HIGH
        /// chip (pins 8-15: internal faults + BINT14/15) must win exclusively - its Ripple
        /// Disable output feeds the LOW chip's Interrupt Disable input in the ND-100
        /// schematic, so device interrupts can never outrank internal faults. Exactly one
        /// group signal may be reported; the microcode dispatches on whichever /GS bit it
        /// sees first.
        /// </summary>
        [Test]
        public void ReadVector_RequestsOnBothChips_HighChipWinsExclusively()
        {
            pic.LatchMaskBus(0x0000);
            pic.Exec(Am2914Instruction.LDM);
            pic.Exec(Am2914Instruction.ENIN);
            pic.ExecuteLoadStatus(0x0000);

            pic.SetInterruptPIN(InterruptPin.INT13);   // pin 3, LOW chip
            pic.SetInterruptPIN(InterruptPin.POWFAIL); // pin 13, HIGH chip (bit 5 there)

            ushort vector = pic.ExecuteReadVector();

            Assert.That(vector & (1 << 3), Is.Not.EqualTo(0), "HIGH group /GS (bit 3) must be asserted - POWFAIL outranks the RTC");
            Assert.That(vector & (1 << 4), Is.EqualTo(0), "LOW group /GS (bit 4) must NOT be asserted while the high chip holds the winning request");
            Assert.That(vector & 0b111, Is.EqualTo(5), "Vector bits 0-2 must identify pin 13 = bit 5 within the high chip");
        }

        /// <summary>
        /// Sanity check: with the (now-fixed) Master Clear default state - no explicit LMSK/ION/LOSTS at
        /// all - does simply asserting a device pin ever produce /IRQ? Confirms the MasterClear
        /// mask-polarity fix (0x00 = all enabled, not 0xFF = all inhibited) took effect, since
        /// InterruptRequestEnabled_FlipFlop also defaults to true per Master Clear.
        /// </summary>
        [TestCase(InterruptPin.INT10)]
        [TestCase(InterruptPin.INT12)]
        public void FreshChipPair_AfterMasterClearOnly_DevicePinAssertsIrq(InterruptPin pin)
        {
            // No LMSK/ION/LOSTS - rely entirely on Master Clear's reset state.
            pic.SetInterruptPIN(pin);

            bool irqAsserted = pic.ReadInterruptRequestPin == false;

            Assert.That(irqAsserted, Is.True,
                $"Expected /IRQ to assert after Master Clear (mask=all-enabled, IRQ-enable=set, status=0) " +
                $"and setting {pin}, with no further PIC setup at all.");
        }
    }
}
