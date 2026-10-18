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
    /// WAIT with the interrupt system OFF must stop the CPU and hand the console back
    /// to OPCOM/MOPC. Microcode chain (ND-110-RASK.LISTING.TXT):
    ///
    ///   WAIT (151000) -> WAIT1 (CS 001042) -> IDBS,STS + COND,F15 tests IONI (bit 15)
    ///     IONI = 1: WAIT2 path (CS 001047) - remove current PID bit, CHKIT level drop
    ///     IONI = 0: STOPW (CS 002133) -> STOP (CS 002134) -> STOP2 (CS 003624,
    ///               COMM,SSTOP sets the Stop flip-flop) = the MOPC operator loop
    ///
    /// This is how programs deliberately halt back to the '#' prompt - e.g.
    /// INSTRUCTION-VERIFY's " ** FATAL PROGRAM ERROR **" handler ends with IOF ; WAIT
    /// (image 021615-021616), and its OPCOM command parks the same way.
    /// </summary>
    public class TestWaitIonOff
    {
        const ushort OPC_IOF = 0xD101;  // 150401
        const ushort OPC_ION = 0xD102;  // 150402
        const ushort OPC_WAIT = 0xD200; // 151000

        CPU cpu = null!;

        [SetUp]
        public void Setup()
        {
            Numeric.currentNumericPresentation = Numeric.NumericPresentation.Octal;
            cpu = new CPU();
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        [Test]
        public void Wait_WithIonOff_StopsCpuIntoOpcom()
        {
            ushort p = (ushort)Utils.Oct2Int("400");
            cpu.memory.WritePhysicalMemory(p + 0, OPC_IOF);
            cpu.memory.WritePhysicalMemory(p + 1, OPC_WAIT);
            cpu.memory.WritePhysicalMemory(p + 2, OPC_ION); // must never be reached

            int newP = cpu.UnitTest_FetchAndExecute(p);
            Assert.That(newP, Is.EqualTo(p + 1), "P after IOF");

            // Execute the WAIT by hand: it must never reach another FETCH - the
            // microcode has to fall into the MOPC stop loop with the Stop flip-flop set.
            cpu.hw.alu.WriteRegisterE(Enums.RegisterEnum.P, (ushort)(p + 1));
            cpu.hw.mac.LatchMacAddress((ushort)(p + 1));
            cpu.UnitTest_Fetch();

            bool stopped = false;
            for (int i = 0; i < 100000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.cpu_regs.STOP)
                {
                    stopped = true;
                    break;
                }
                Assert.That(cpu.hw.mac.FETCH, Is.False,
                    "WAIT with ION off must not continue to the next macro instruction " +
                    "(microcode must take WAIT1 -> STOPW -> STOP, ND-110-RASK CS 001042/002133)");
            }

            Assert.That(stopped, Is.True,
                "WAIT with ION off must set the Stop flip-flop (COMM,SSTOP at STOP2, CS 003624) " +
                "and return control to OPCOM/MOPC");
        }
    }
}
