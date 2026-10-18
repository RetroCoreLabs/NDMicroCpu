//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Collections.Generic;
using System.IO;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.NDBus;
using NDMicroCpu.Core.Support.FileLoader;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.NDBusTests
{
    /// <summary>
    /// End-to-end panel-LOAD binary boot over the NDBus (Phase 3). Attaches the PIO
    /// binary-load device (<see cref="NdBinaryLoadDevice"/>) at the floppy device number
    /// 1560, streams a real BPUN byte stream, selects it with ALD=1 (ALDVC entry 1), and
    /// presses the panel LOAD button. The RASK BINARY LOADER microcode (ETLO1 002210) then
    /// reads the stream byte-by-byte through the bus and lays the program into memory - the
    /// whole point of Phase 3: a real "run test code" boot path.
    ///
    /// ALD=1 -> device 1560 -> ETLO1 was established empirically by TestBinaryLoaderProbe.
    /// </summary>
    public class TestBinaryLoadBoot
    {
        const int TICK_PR_20MS = 10550 * 4;

        static CPU BootToOpcom() => BootToOpcom(CPUType.ND110);

        static CPU BootToOpcom(CPUType cpuType)
        {
            CPU cpu = new CPU(null, cpuType);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
            ushort sterr = cpuType == CPUType.ND110 ? (ushort)0x449 : (ushort)0x46E;
            int budget = cpuType == CPUType.ND110 ? 200000 : 6000000;
            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < budget; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr) Assert.Fail("Self-test failed (STERR)");
                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "not at OPCOM");
            return cpu;
        }

        // Builds a minimal, valid ND binary-load (BPUN) stream by hand:
        //   '!' start-of-data, then Address, Count, Data[Count], Checksum, Action - all
        //   big-endian 16-bit words (see NDMicroCpu.Core.Support.FileLoader.BPUN / ndwiki BPUN format).
        // Action is left NON-ZERO so the loader lays the program into memory but stays in
        // OPCOM instead of executing it - a safe, deterministic memory-load assertion.
        static byte[] BuildBpun(ushort address, ushort[] data, ushort action, int startAddr = -1)
        {
            List<byte> s = new List<byte>();
            // Preamble: the ASCII octal Start address (execution entry for action=0), read by
            // the SEEK/SIKI microcode (002211) and terminated by the "!" start-of-data mark.
            // Omitted (startAddr<0) => empty preamble => Start defaults to 0.
            if (startAddr >= 0)
            {
                string oct = System.Convert.ToString(startAddr, 8);
                for (int i = 0; i < oct.Length; i++) s.Add((byte)oct[i]);
                s.Add(0x0D); // CR terminates the Start field (SEEK loop captures it, 002213/002214)
            }
            s.Add((byte)'!');                       // start-of-data mark
            s.Add((byte)(address >> 8)); s.Add((byte)(address & 0xFF));
            s.Add((byte)(data.Length >> 8)); s.Add((byte)(data.Length & 0xFF));
            ushort checksum = 0;
            for (int i = 0; i < data.Length; i++)
            {
                s.Add((byte)(data[i] >> 8)); s.Add((byte)(data[i] & 0xFF));
                checksum = (ushort)(checksum + data[i]); // arithmetic sum, mod 2^16
            }
            s.Add((byte)(checksum >> 8)); s.Add((byte)(checksum & 0xFF));
            s.Add((byte)(action >> 8)); s.Add((byte)(action & 0xFF));
            return s.ToArray();
        }

        [Test]
        public void PanelLoad_StreamsBpunIntoMemory_ND110()
        {
            CPU cpu = BootToOpcom();

            ushort addr = (ushort)Utils.Oct2Int("1000");
            ushort[] program = { (ushort)Utils.Oct2Int("123456"), (ushort)Utils.Oct2Int("065432") };
            // Action's FIRST byte is what the loader reads (ASS8, one char, RASK 002232-002234):
            // 0 => auto-start the program; non-zero => load only and stay in OPCOM. Use a
            // non-zero high byte so this is a safe, deterministic memory-load assertion.
            byte[] bpun = BuildBpun(addr, program, action: 0x0100 /* first byte 0x01 -> stay in OPCOM */);

            // Drive it through the headless facade (the same primitive the web/local front-ends
            // call): attach the PIO device at 1560, select ALD=1, press panel LOAD.
            var loader = cpu.PanelBinaryLoad(bpun);

            // Run until the loader has consumed the stream (it reads through Address, Count,
            // Data, Checksum and a one-byte Action = 12 bytes here), plus margin for the
            // checksum test + action dispatch settling back to OPCOM.
            for (int i = 0; i < TICK_PR_20MS * 40 && loader.BytesRead < bpun.Length - 1; i++)
                cpu.TickCpu();
            for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();

            Assert.That(loader.BytesRead, Is.GreaterThanOrEqualTo(bpun.Length - 1),
                "the microcode binary loader should have streamed the BPUN image through the action byte");

            // The program words must have landed at the BPUN load address, in order.
            Assert.That(cpu.memory.ReadPhysicalMemory(addr), Is.EqualTo(program[0]),
                "first program word loaded at the BPUN address");
            Assert.That(cpu.memory.ReadPhysicalMemory((uint)(addr + 1)), Is.EqualTo(program[1]),
                "second program word loaded at address+1");
        }

        [Test]
        public void PanelLoad_ActionZero_AutoStartsLoadedProgram_ND110()
        {
            CPU cpu = BootToOpcom();
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "precondition - stopped at OPCOM");

            // A single self-jump: JMP *0 (octal 124000, mode 0 / disp 0) parks P on itself -
            // a safe, bounded "running" program that never wanders. With Action = 0 the loader
            // must COMM,START it (RASK 002235), leaving the CPU RUNNING (not stopped) with P at
            // the load address, proving the whole panel-LOAD boot path executes what it loaded.
            ushort addr = (ushort)Utils.Oct2Int("2000");
            ushort[] program = { (ushort)Utils.Oct2Int("124000") }; // JMP *0
            byte[] bpun = BuildBpun(addr, program, action: 0 /* zero -> auto-start */, startAddr: addr);

            cpu.PanelBinaryLoad(bpun);

            for (int i = 0; i < TICK_PR_20MS * 40 && cpu.hw.cpu_regs.STOP; i++)
                cpu.TickCpu();

            Assert.That(cpu.memory.ReadPhysicalMemory(addr), Is.EqualTo(program[0]),
                "the JMP *0 word must have been loaded at the BPUN address");
            Assert.That(cpu.hw.cpu_regs.STOP, Is.False,
                "action=0 must COMM,START the loaded program - the CPU should be running, not stopped at OPCOM");
            Assert.That(cpu.hw.alu.ReadRegisterE(RegisterEnum.P), Is.EqualTo(addr),
                "the auto-started program (JMP *0) parks P on itself at the load address");
        }

        [Test]
        public void ConsoleLoadCommand_1560Dollar_StreamsBpunIntoMemory_ND110()
        {
            // The operator-faithful boot: instead of the panel LOAD button, type "<devno>$" at
            // the OPCOM console. "$"/"&" (DOLOA/ETLOA, RASK 002205) is the serial binary-loader
            // command; the typed octal number is the load code / device (OCTNR -> Q -> LOAD1).
            // 1560 has bit13=0 -> BINARY loader on the floppy at device 1560 - straight over the
            // NDBus, no panel button and no ALD needed. This is the surface a UI drives by simply
            // feeding console characters.
            CPU cpu = BootToOpcom();

            ushort addr = (ushort)Utils.Oct2Int("1000");
            ushort[] program = { (ushort)Utils.Oct2Int("123456"), (ushort)Utils.Oct2Int("065432") };
            byte[] bpun = BuildBpun(addr, program, action: 0x0100 /* load only */);

            var loader = new NdBinaryLoadDevice(bpun); // floppy 1560
            cpu.iobus.Attach(loader);

            // Let MOPC reach its console input loop, then type the load command one char at a time.
            for (int i = 0; i < TICK_PR_20MS * 10; i++) cpu.TickCpu();
            string cmd = "1560$";
            for (int ci = 0; ci < cmd.Length; ci++)
            {
                cpu.hw.AddConsoleChar(cmd[ci]);
                for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();
            }
            for (int i = 0; i < TICK_PR_20MS * 40 && loader.BytesRead < bpun.Length - 1; i++)
                cpu.TickCpu();

            Assert.That(loader.BytesRead, Is.GreaterThan(0),
                "typing 1560$ at OPCOM should activate the binary loader on the floppy over the bus");
            Assert.That(cpu.memory.ReadPhysicalMemory(addr), Is.EqualTo(program[0]),
                "console 1560$ load: first program word landed at the BPUN address");
            Assert.That(cpu.memory.ReadPhysicalMemory((uint)(addr + 1)), Is.EqualTo(program[1]),
                "console 1560$ load: second program word landed at address+1");
        }

        [Test]
        public void ConsoleLoadCommand_1560Dollar_StreamsBpunIntoMemory_ND120()
        {
            // Same console "1560$" boot on DELILAH-L. The typed-command path (DOLOA/ETLOA) goes
            // straight to LOAD1 -> ETLO1 and does NOT go through the panel LOAD / NOISE gate, so
            // unlike the panel button it needs no STOP-first dance on ND-120.
            CPU cpu = BootToOpcom(CPUType.ND120);

            ushort addr = (ushort)Utils.Oct2Int("1000");
            ushort[] program = { (ushort)Utils.Oct2Int("123456"), (ushort)Utils.Oct2Int("065432") };
            byte[] bpun = BuildBpun(addr, program, action: 0x0100 /* load only */);

            var loader = new NdBinaryLoadDevice(bpun);
            cpu.iobus.Attach(loader);

            for (int i = 0; i < TICK_PR_20MS * 10; i++) cpu.TickCpu();
            string cmd = "1560$";
            for (int ci = 0; ci < cmd.Length; ci++)
            {
                cpu.hw.AddConsoleChar(cmd[ci]);
                for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();
            }
            for (int i = 0; i < TICK_PR_20MS * 40 && loader.BytesRead < bpun.Length - 1; i++)
                cpu.TickCpu();

            Assert.That(loader.BytesRead, Is.GreaterThan(0),
                "ND-120: typing 1560$ at OPCOM should activate the binary loader on the floppy over the bus");
            Assert.That(cpu.memory.ReadPhysicalMemory(addr), Is.EqualTo(program[0]),
                "ND-120 console 1560$ load: first program word landed at the BPUN address");
            Assert.That(cpu.memory.ReadPhysicalMemory((uint)(addr + 1)), Is.EqualTo(program[1]),
                "ND-120 console 1560$ load: second program word landed at address+1");
        }

        [Test]
        public void ConsoleLoadCommand_400Dollar_BootsFromPaperTapeReader_ND110()
        {
            // Phase 5: the paper-tape reader. It is the same PIO byte-stream device as the floppy
            // binary loader, just at device 400 - so typing "400$" at the OPCOM console boots from
            // the paper tape exactly as "1560$" boots from the floppy. (This is the "400$" the
            // classic ND operators typed to load from paper tape.)
            CPU cpu = BootToOpcom();

            ushort addr = (ushort)Utils.Oct2Int("1000");
            ushort[] program = { (ushort)Utils.Oct2Int("111111"), (ushort)Utils.Oct2Int("222222") };
            byte[] tape = BuildBpun(addr, program, action: 0x0100 /* load only */);

            var reader = NdBinaryLoadDevice.PaperTapeReader(tape); // device 400
            cpu.iobus.Attach(reader);

            for (int i = 0; i < TICK_PR_20MS * 10; i++) cpu.TickCpu();
            string cmd = "400$";
            for (int ci = 0; ci < cmd.Length; ci++)
            {
                cpu.hw.AddConsoleChar(cmd[ci]);
                for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();
            }
            for (int i = 0; i < TICK_PR_20MS * 40 && reader.BytesRead < tape.Length - 1; i++)
                cpu.TickCpu();

            Assert.That(reader.BytesRead, Is.GreaterThan(0),
                "typing 400$ at OPCOM should activate the paper-tape reader at device 400 over the bus");
            Assert.That(cpu.memory.ReadPhysicalMemory(addr), Is.EqualTo(program[0]),
                "paper-tape 400$ load: first program word landed at the load address");
            Assert.That(cpu.memory.ReadPhysicalMemory((uint)(addr + 1)), Is.EqualTo(program[1]),
                "paper-tape 400$ load: second program word landed at address+1");
        }

        [Test]
        public void PanelLoad_StreamsBpunIntoMemory_ND120()
        {
            // Same end-to-end boot as ND-110, on DELILAH-L. Proves the shared bus + the CPU-aware
            // facade (which for ND-120 presses STOP first to clear the NOISE FF that gates LOAD,
            // and uses ALD=14 for ALDVC entry 1) really boot a BPUN over the bus on the ND-120 too.
            CPU cpu = BootToOpcom(CPUType.ND120);

            ushort addr = (ushort)Utils.Oct2Int("1000");
            ushort[] program = { (ushort)Utils.Oct2Int("123456"), (ushort)Utils.Oct2Int("065432") };
            byte[] bpun = BuildBpun(addr, program, action: 0x0100 /* load only */);

            var loader = cpu.PanelBinaryLoad(bpun);

            for (int i = 0; i < TICK_PR_20MS * 40 && loader.BytesRead < bpun.Length - 1; i++)
                cpu.TickCpu();
            for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();

            Assert.That(loader.BytesRead, Is.GreaterThanOrEqualTo(bpun.Length - 1),
                "ND-120: the binary loader should have streamed the BPUN through the action byte");
            Assert.That(cpu.memory.ReadPhysicalMemory(addr), Is.EqualTo(program[0]),
                "ND-120: first program word loaded at the BPUN address");
            Assert.That(cpu.memory.ReadPhysicalMemory((uint)(addr + 1)), Is.EqualTo(program[1]),
                "ND-120: second program word loaded at address+1");
        }

        // One of the shipped .BPUN images, from the Machine project's BPUN folder in the source tree.
        // Returns null if not found.
        static string? FindBpun(string name)
        {
            string candidate = Path.Combine(TestPaths.BpunDir(), name);
            return File.Exists(candidate) ? candidate : null;
        }

        [Test]
        public void ConsoleLoad_InstructionB_BootsAndPrintsBanner_ND110()
        {
            // Validate the exact path NDMicroCpu.Machine.BootFromBpunOverBus now uses by default: stream
            // the real INSTRUCTION-B.BPUN over the bus via the console "1560$" command and let it
            // auto-start (Action=0), then confirm the program actually runs by catching its banner
            // on the console. This is the true end-to-end "boot a real program over the bus" proof.
            string? path = FindBpun("INSTRUCTION-B.BPUN");
            if (path == null)
                Assert.Ignore("INSTRUCTION-B.BPUN not found (NDMicroCpu.Machine not built) - skipping");

            CPU cpu = BootToOpcom();
            System.Text.StringBuilder outText = new System.Text.StringBuilder();
            void OnText(char c) => outText.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;
            try
            {
                var loader = new NdBinaryLoadDevice(File.ReadAllBytes(path));
                cpu.iobus.Attach(loader);

                for (int i = 0; i < TICK_PR_20MS * 10; i++) cpu.TickCpu();
                string cmd = "1560$";
                for (int ci = 0; ci < cmd.Length; ci++)
                {
                    cpu.hw.AddConsoleChar(cmd[ci]);
                    for (int i = 0; i < TICK_PR_20MS * 5; i++) cpu.TickCpu();
                }
                // Stream the whole image (46 KB, one byte per INCH) then run the started program
                // until its banner appears. Bounded generously.
                for (int chunk = 0; chunk < 200; chunk++)
                {
                    for (int i = 0; i < TICK_PR_20MS * 20; i++) cpu.TickCpu();
                    if (outText.ToString().Replace("\0", "").Contains("INSTRUCTION")) break;
                }
                string text = outText.ToString().Replace("\0", "");
                Assert.That(loader.BytesRead, Is.GreaterThan(1000),
                    "the loader should have streamed a large chunk of INSTRUCTION-B over the bus");
                Assert.That(text, Does.Contain("INSTRUCTION"),
                    "INSTRUCTION-B, booted over the bus via console 1560$, should print its banner");
            }
            finally { cpu.hw.uart.OnTextOutput -= OnText; }
        }

        [Test]
        public void PanelLoad_RealBpunFile_LoadsProgramWordsIntoMemory_ND110()
        {
            // Ground truth: decode the real file with the production BPUN loader so we know the
            // load Address and the exact program words it contains, independent of the microcode.
            string? path = FindBpun("RTC.BPUN");
            if (path == null)
                Assert.Ignore("RTC.BPUN not found (NDMicroCpu.Machine not built) - skipping real-file boot test");

            byte[] raw = File.ReadAllBytes(path);
            BPUN decoded = new BPUN();
            Assert.That(decoded.LoadBPUN(raw), Is.True, "production BPUN decoder should parse RTC.BPUN");
            Assert.That(decoded.Code, Is.Not.Null, "decoded BPUN should contain code");
            byte[] codeBytes = decoded.Code.ToArray();
            ushort loadAddr = (ushort)decoded.Address;
            int words = codeBytes.Length / 2;
            Assert.That(words, Is.GreaterThan(0), "RTC.BPUN should contain at least one code word");

            // Re-wrap the REAL RTC driver code words in a load-only envelope (Action != 0). The
            // shipped RTC.BPUN has Action = 0 and would auto-start, and a running RTC driver
            // self-modifies memory - so streaming the raw file can't be a stable memory check.
            // Wrapping the genuine 1984 code words load-only lets us assert they load bit-for-bit
            // over the bus without the driver executing and mutating itself.
            ushort[] realWords = new ushort[words];
            for (int i = 0; i < words; i++)
                realWords[i] = (ushort)((codeBytes[i * 2] << 8) | codeBytes[i * 2 + 1]);
            byte[] bpun = BuildBpun(loadAddr, realWords, action: 0x0100 /* load only */);

            CPU cpu = BootToOpcom();
            var loader = cpu.PanelBinaryLoad(bpun);

            for (int i = 0; i < TICK_PR_20MS * 120 && loader.BytesRead < bpun.Length - 1; i++)
                cpu.TickCpu();

            Assert.That(loader.BytesRead, Is.GreaterThan(0), "the loader should have streamed the image");

            // Every real RTC.BPUN code word must have landed at the load address, matching the
            // production decoder word-for-word.
            for (int i = 0; i < words; i++)
                Assert.That(cpu.memory.ReadPhysicalMemory((uint)(loadAddr + i)), Is.EqualTo(realWords[i]),
                    $"RTC.BPUN word {i} loaded at octal {Utils.Int2Oct((ushort)(loadAddr + i))} over the bus");
        }
    }
}
