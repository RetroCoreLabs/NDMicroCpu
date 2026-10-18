//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Core;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Cli
{
    /// <summary>
    /// The shared command logic for the ND-110/120 microcode CLI, mirroring the design of the
    /// old ND120CPUEMU SimulatorEngine: every method returns a <see cref="string"/> instead of
    /// writing to the console, so the same logic is driven by both the interactive REPL
    /// (<see cref="Program"/>) and the unit tests.
    ///
    /// It drives the WORKING NDMicroCpu.Core (which boots ND-110 and ND-120 through self-test), and
    /// adds the two things NDMicroCpu.Core itself lacks: an interactive front-end and the pseudo-C
    /// "english" microcode output (<see cref="Microcode2Pseudo"/>).
    /// </summary>
    public class CliEngine
    {
        private CPU _cpu;
        private CPUType _cpuType;

        /// <summary>Set once <see cref="Boot"/> reaches FETCH; runi auto-boots when false.</summary>
        private bool _booted;

        /// <summary>Microcode-address breakpoints (control-store addresses, compared against mic.map).</summary>
        private readonly HashSet<ushort> _breakpoints = new();

        // ND-120 self-test error handler (STERR). From ND-120-DELILAH-L.LISTING.TXT via
        // TestND110/Boot/TestBootSequence.cs: ND-120 STERR = 002156 octal = 0x46E,
        // ND-110 STERR = 002111 octal = 0x449.
        private static ushort SterrAddress(CPUType t) => t == CPUType.ND110 ? (ushort)0x449 : (ushort)0x46E;

        /// <summary>
        /// Creates the engine with a fresh CPU of the given type; the CPU boots on the first command that needs it.
        /// </summary>
        /// <param name="cpuType">
        /// ND-110 or ND-120.
        /// </param>
        public CliEngine(CPUType cpuType = CPUType.ND120)
        {
            _cpuType = cpuType;
            // The CPU loads its own cpuType-appropriate microcode in its constructor.
            _cpu = new CPU(null, cpuType);

            // Logging must stay off: boot/self-test runs 10^5-10^6 ticks and the NATS log sink
            // blocks per call (see TestBootSequence.cs).
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;
        }

        /// <summary>Direct access to the CPU for advanced scenarios / tests.</summary>
        public CPU Cpu => _cpu;
        /// <summary>
        /// Which CPU type the engine drives.
        /// </summary>
        public CPUType CpuType => _cpuType;

        /// <summary>Current microcode address (the "map" / control-store address).</summary>
        public ushort CurrentMicroAddress => _cpu.hw.mic.map;
        /// <summary>
        /// The current micro code address as six octal digits.
        /// </summary>
        public string CurrentMicroAddressOctal => Oct(_cpu.hw.mic.map);

        private static string Oct(ushort v) => Convert.ToString(v, 8).PadLeft(6, '0');
        private static string Oct(int v) => Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0');

        /// <summary>
        /// Boot the CPU from master clear (micro address 0) through the built-in self-test to
        /// the first macro-instruction FETCH boundary. Reports failure (with the R2 error code)
        /// if execution reaches STERR, mirroring the boot test.
        /// </summary>
        public string Boot(int maxCycles = 400000)
        {
            _cpu.hw.mic.LatchMap(0);
            ushort sterr = SterrAddress(_cpuType);

            for (int i = 0; i < maxCycles; i++)
            {
                _cpu.TickCpu();

                if (_cpu.hw.mic.map == sterr)
                {
                    int r2 = _cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
                    return $"SELF-TEST FAILED: hit STERR with R2={Oct(r2)} after {i} cycles";
                }

                if (_cpu.hw.mac.FETCH)
                {
                    _booted = true;
                    return $"Boot OK: reached FETCH after {i} cycles. Micro addr={CurrentMicroAddressOctal}, STOP={_cpu.hw.cpu_regs.STOP}";
                }
            }
            return $"Boot did NOT reach FETCH within {maxCycles} cycles (micro addr={CurrentMicroAddressOctal})";
        }

        /// <summary>Execute one microcode step; returns the new micro address.</summary>
        public string StepMicro()
        {
            ushort before = _cpu.hw.mic.map;
            _cpu.StepMicroCode();
            return $"micro {Oct(before)} -> {CurrentMicroAddressOctal}";
        }

        /// <summary>
        /// Run microcode until the next macro-instruction FETCH boundary OR a microcode
        /// breakpoint — the old console's `s!` semantics. Own tick loop (not
        /// <c>cpu.StepMacroCode()</c>) because the CPU itself knows nothing about breakpoints.
        /// </summary>
        public string StepMacro(int maxTicks = 100000)
        {
            // Same "if stopped, start it" behaviour as cpu.StepMacroCode() (STOP's setter
            // is internal to NDMicroCpu.Core, hence the UnitTest_ debugger hook).
            if (_cpu.hw.cpu_regs.STOP) _cpu.UnitTest_SetStop(false);

            for (int i = 0; i < maxTicks; i++)
            {
                _cpu.TickCpu();

                if (_breakpoints.Contains(_cpu.hw.mic.map))
                    return $"*** BREAKPOINT at micro addr {CurrentMicroAddressOctal} after {i + 1} ticks ***";

                if (_cpu.hw.mac.FETCH)
                    return $"reached FETCH. Micro addr={CurrentMicroAddressOctal}";
            }
            return $"did NOT reach FETCH within {maxTicks} ticks (micro addr={CurrentMicroAddressOctal})";
        }

        /// <summary>
        /// Run ONE macro instruction to completion on the working CPU (`runi`).
        /// Boots first if needed (the instruction must run from the post-self-test FETCH
        /// state), applies REG=val presets, writes the opcode into memory at P, then uses
        /// <c>cpu.UnitTest_FetchAndExecute(P)</c> — which clears STOP, latches the MAC
        /// address, fetches, and ticks until the NEXT FETCH — and reports the registers.
        /// Unlike the abandoned ND120CPUEMU `runi`, this executes on a core whose macro
        /// fetch/dispatch actually works, so the result is real.
        ///
        /// Usage: runi &lt;opcode-octal&gt; [REG=val ...]   e.g. runi 170405   (SAA 5: A := 5)
        /// </summary>
        public string RunInstruction(string[] parts, bool trace = false)
        {
            if (parts.Length < 2)
            {
                return $"Usage: {parts[0]} <opcode-octal> [REG=val ...]\n" +
                       "  Sets registers, writes the opcode to memory at P, runs ONE macro\n" +
                       "  instruction to completion (next FETCH), then shows the registers.\n" +
                       "  Registers: Zero,D,P,B,L,A,T,X,STS,R1-R7 (octal values, 0x for hex).\n" +
                       "  runit additionally traces EVERY microcode step: address, pseudo-C,\n" +
                       "  and the ACTUAL IDB value + register changes after each step.\n" +
                       "  Example: runi 170405        (SAA 5 -> A=000005)\n" +
                       "  Example: runit 172403 A=2   (AAA 3 traced -> A=000005)";
            }

            if (!TryParseNumber(parts[1], out ushort opcode))
                return $"Invalid opcode '{parts[1]}' (octal digits, or 0x prefix for hex).";

            var sb = new System.Text.StringBuilder();

            // runi needs the CPU past self-test and at a FETCH boundary; boot on demand.
            if (!_booted)
            {
                string boot = Boot();
                sb.AppendLine($"(auto-boot) {boot}");
                if (!_booted)
                    return sb.ToString(); // boot failed - report and stop
            }

            // 1) Apply REG=val presets. A preset P also becomes the execution address.
            ushort address = (ushort)_cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
            for (int i = 2; i < parts.Length; i++)
            {
                string arg = parts[i];
                int eq = arg.IndexOf('=');
                if (eq <= 0)
                    return $"Bad register argument '{arg}'. Use REG=value (e.g. A=5).";

                string regName = arg.Substring(0, eq);
                string regVal = arg.Substring(eq + 1);

                if (!TryParseRegister(regName, out RegisterEnum reg))
                    return $"Invalid register '{regName}'. Use Zero,D,P,B,L,A,T,X,STS,R1-R7.";
                if (!TryParseNumber(regVal, out ushort v))
                    return $"Invalid value '{regVal}' for {regName} (octal, or 0x hex).";

                _cpu.hw.alu.WriteRegisterE(reg, v);
                if (reg == RegisterEnum.P)
                    address = v;
                sb.AppendLine($"set {reg} = {Oct(v)}");
            }

            // 2) Plant the opcode at the execution address (physical write; paging is off
            //    in this state so physical == virtual).
            _cpu.memory.WritePhysicalMemory((int)address, opcode);
            sb.AppendLine($"mem[{Oct(address)}] = {Oct(opcode)} (opcode)");

            // 3) Fetch + execute exactly one macro instruction (runs until the next FETCH).
            int result;
            if (trace)
            {
                result = ExecuteTraced(address, sb);
            }
            else
            {
                result = _cpu.UnitTest_FetchAndExecute(address);
            }
            if (result < 0)
            {
                sb.AppendLine("*** instruction did NOT complete within the 100000-step budget ***");
                return sb.ToString();
            }

            sb.AppendLine($"executed one instruction; next FETCH at P={Oct(result)}");
            sb.AppendLine();
            sb.AppendLine(RegDump());
            sb.AppendLine($"STS={Oct(_cpu.hw.alu.ReadRegisterE(RegisterEnum.STS))}");
            return sb.ToString();
        }

        /// <summary>Registers snapshotted around every traced micro-step (order = display order).</summary>
        private static readonly RegisterEnum[] TraceRegs =
        {
            RegisterEnum.P, RegisterEnum.A, RegisterEnum.D, RegisterEnum.T,
            RegisterEnum.X, RegisterEnum.B, RegisterEnum.L, RegisterEnum.STS
        };

        /// <summary>
        /// The traced twin of <c>cpu.UnitTest_FetchAndExecute</c>: same fetch + tick-to-FETCH
        /// loop, but for EVERY microcode step it appends the control-store address, the
        /// pseudo-C for that word, and a comment with the ACTUAL post-step IDB value and
        /// every program-visible register that changed. Returns the new P, or -1 on the
        /// safety cap (mirroring the CPU API).
        /// </summary>
        private int ExecuteTraced(ushort address, System.Text.StringBuilder sb)
        {
            // Same preamble as UnitTest_FetchAndExecute (STOP's setter is internal).
            _cpu.UnitTest_SetStop(false);
            _cpu.hw.alu.WriteRegisterE(RegisterEnum.P, address);
            _cpu.hw.mac.LatchMacAddress(address);
            _cpu.UnitTest_Fetch();

            sb.AppendLine($"--- microcode trace (opcode fetched from {Oct(address)}) ---");

            // Register snapshot for change detection (no LINQ, fixed-size array).
            ushort[] before = new ushort[TraceRegs.Length];
            int steps = 0;
            const int maxSteps = 100000;

            do
            {
                ushort csAddr = _cpu.hw.mic.map;

                // Pseudo-C of the word ABOUT to execute (fresh decode from the control store).
                ulong word = _cpu.hw.cs.Read(csAddr);
                var mc = new MicroCode();
                mc.DecodeMicroCode(word);
                sb.AppendLine($"CS {Oct(csAddr)}:");
                sb.Append(Microcode2Pseudo.GeneratePseudoCode(mc, csAddr, _symbols));
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.AppendLine();

                for (int i = 0; i < TraceRegs.Length; i++)
                    before[i] = (ushort)_cpu.hw.alu.ReadRegisterE(TraceRegs[i]);

                _cpu.TickCpu();
                steps++;

                // ACTUAL values after the step: IDB latch + every register that changed.
                sb.Append($"  // actual: IDB={Oct(_cpu.hw.idb.idb_data)}");
                for (int i = 0; i < TraceRegs.Length; i++)
                {
                    ushort now = (ushort)_cpu.hw.alu.ReadRegisterE(TraceRegs[i]);
                    if (now != before[i])
                        sb.Append($"  {TraceRegs[i]}: {Oct(before[i])}->{Oct(now)}");
                }
                sb.AppendLine();

                if (steps >= maxSteps) return -1;
            } while (!_cpu.hw.mac.FETCH);

            sb.AppendLine($"--- {steps} microcode steps ---");
            return _cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
        }

        /// <summary>Write a register by name (value octal, or 0x-prefixed hex).</summary>
        public string WriteRegister(string name, string valStr)
        {
            if (!TryParseRegister(name, out RegisterEnum reg))
                return $"Unknown register '{name}'. Use Zero,D,P,B,L,A,T,X,STS,R1-R7.";
            if (!TryParseNumber(valStr, out ushort v))
                return $"Invalid value '{valStr}' (octal, or 0x hex).";
            _cpu.hw.alu.WriteRegisterE(reg, v);
            return $"{reg}={Oct(v)}";
        }

        /// <summary>Read a memory word (16-bit physical address, octal).</summary>
        public string ReadMemory(string addrStr)
        {
            if (!TryParseNumber(addrStr, out ushort a))
                return $"Invalid address '{addrStr}' (octal, or 0x hex).";
            ushort v = _cpu.memory.ReadPhysicalMemory((int)a);
            return $"mem[{Oct(a)}] = {Oct(v)}";
        }

        /// <summary>Write a memory word (16-bit physical address, octal).</summary>
        public string WriteMemory(string addrStr, string valStr)
        {
            if (!TryParseNumber(addrStr, out ushort a))
                return $"Invalid address '{addrStr}' (octal, or 0x hex).";
            if (!TryParseNumber(valStr, out ushort v))
                return $"Invalid value '{valStr}' (octal, or 0x hex).";
            _cpu.memory.WritePhysicalMemory((int)a, v);
            return $"mem[{Oct(a)}] = {Oct(v)}";
        }

        // ===== BREAKPOINTS (microcode addresses) =====

        /// <summary>Add a microcode-address breakpoint (octal). StepMacro stops on it.</summary>
        public string AddBreakpoint(string addrStr)
        {
            if (!TryParseNumber(addrStr, out ushort a))
                return $"Invalid address '{addrStr}' (octal, or 0x hex).";
            return _breakpoints.Add(a)
                ? $"breakpoint added at micro addr {Oct(a)}"
                : $"breakpoint already set at micro addr {Oct(a)}";
        }

        /// <summary>Remove a microcode-address breakpoint (octal).</summary>
        public string RemoveBreakpoint(string addrStr)
        {
            if (!TryParseNumber(addrStr, out ushort a))
                return $"Invalid address '{addrStr}' (octal, or 0x hex).";
            return _breakpoints.Remove(a)
                ? $"breakpoint removed at micro addr {Oct(a)}"
                : $"no breakpoint at micro addr {Oct(a)}";
        }

        /// <summary>List active microcode breakpoints, sorted, in octal.</summary>
        public string ListBreakpoints()
        {
            if (_breakpoints.Count == 0) return "no breakpoints set";
            // No LINQ: copy to an array and sort.
            ushort[] addrs = new ushort[_breakpoints.Count];
            _breakpoints.CopyTo(addrs);
            Array.Sort(addrs);
            var sb = new System.Text.StringBuilder($"{addrs.Length} breakpoint(s):");
            for (int i = 0; i < addrs.Length; i++)
                sb.Append($" {Oct(addrs[i])}");
            return sb.ToString();
        }

        /// <summary>Remove all microcode breakpoints.</summary>
        public string ClearBreakpoints()
        {
            int n = _breakpoints.Count;
            _breakpoints.Clear();
            return $"cleared {n} breakpoint(s)";
        }

        /// <summary>Read a register by name (Zero/D/P/B/L/A/T/X/STS/R1-R7).</summary>
        public string ReadRegister(string name)
        {
            if (!TryParseRegister(name, out RegisterEnum reg))
                return $"Unknown register '{name}'. Use Zero,D,P,B,L,A,T,X,STS,R1-R7.";
            ushort v = _cpu.hw.alu.ReadRegisterE(reg);
            return $"{reg}={Oct(v)}";
        }

        /// <summary>Dump the program-visible registers.</summary>
        public string RegDump()
        {
            RegisterEnum[] regs =
            {
                RegisterEnum.P, RegisterEnum.A, RegisterEnum.D, RegisterEnum.T,
                RegisterEnum.X, RegisterEnum.B, RegisterEnum.L
            };
            var sb = new System.Text.StringBuilder("regs:");
            for (int i = 0; i < regs.Length; i++)
                sb.Append($" {regs[i]}={Oct(_cpu.hw.alu.ReadRegisterE(regs[i]))}");
            return sb.ToString();
        }

        /// <summary>
        /// A one-screen status: CPU type, boot state, micro address, registers and breakpoints.
        /// </summary>
        /// <returns>
        /// The status text.
        /// </returns>
        public string Status()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CPU type      : {_cpuType}");
            sb.AppendLine($"Micro address : {CurrentMicroAddressOctal} (octal)");
            sb.AppendLine($"FETCH         : {_cpu.hw.mac.FETCH}");
            sb.AppendLine($"STOP          : {_cpu.hw.cpu_regs.STOP}");
            sb.AppendLine(RegDump());
            return sb.ToString();
        }

        /// <summary>Set the current microcode address (octal, or 0x-prefixed hex).</summary>
        public string SetMicroAddress(string addr)
        {
            if (!TryParseNumber(addr, out ushort a))
                return $"Invalid address '{addr}' (octal, or 0x hex).";
            _cpu.hw.mic.LatchMap(a);
            return $"micro address set to {Oct(a)}";
        }

        /// <summary>Show the current microcode's raw decoded fields.</summary>
        public string FieldsCurrent()
        {
            var mc = _cpu.microCode;
            if (mc == null) return "(no current microcode)";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CS {CurrentMicroAddressOctal}: command={mc.command} IDBS={mc.IDBsource} MIS={mc.MIS}");
            sb.AppendLine($"  jmp={Oct(mc.jmpAddress)} LOOP={mc.LOOP} ECOND={mc.ECOND} SCOND={mc.SCOND} VECT={mc.Vectored}");
            sb.AppendLine($"  T.addr={mc.TRUE_AddressSelect} T.stack={mc.TRUE_StackControl}");
            return sb.ToString();
        }

        private readonly SymbolTable _symbols = new();

        /// <summary>Render the current microcode as pseudo-C ("english").</summary>
        public string PseudoCurrent()
        {
            var mc = _cpu.microCode;
            if (mc == null) return "(no current microcode)";
            return Microcode2Pseudo.GeneratePseudoCode(mc, CurrentMicroAddress, _symbols);
        }

        /// <summary>Render an arbitrary microcode word (by decoding it) as pseudo-C.</summary>
        public string Pseudo(MicroCode mc, ushort address)
            => Microcode2Pseudo.GeneratePseudoCode(mc, address, _symbols);

        /// <summary>Load a symbols file (LABEL=octal) for pseudo-C labels; returns count.</summary>
        public string LoadSymbols(string path)
        {
            int n = _symbols.LoadFromFile(path);
            return $"loaded {n} symbols from {path}";
        }

        /// <summary>Decode the control-store word at an octal address and render it as pseudo-C.</summary>
        public string PseudoAt(string octalAddr)
        {
            if (!TryParseNumber(octalAddr, out ushort a))
                return $"Invalid address '{octalAddr}'.";
            ulong word = _cpu.hw.cs.Read(a);
            var mc = new MicroCode();
            mc.DecodeMicroCode(word);
            return $"CS {Oct(a)}:\n" + Microcode2Pseudo.GeneratePseudoCode(mc, a, _symbols);
        }

        /// <summary>
        /// List a RANGE of control-store words as pseudo-C ("english") — the microcode
        /// listing view: `list &lt;start&gt; [count]`, count defaults to 8, capped at 256.
        /// </summary>
        public string PseudoRange(string startStr, string? countStr = null)
        {
            if (!TryParseNumber(startStr, out ushort start))
                return $"Invalid start address '{startStr}' (octal, or 0x hex).";

            int count = 8;
            if (countStr != null)
            {
                // Count is DECIMAL - "list 20 10" means 10 words, not 8.
                if (!int.TryParse(countStr, out count) || count < 1)
                    return $"Invalid count '{countStr}' (decimal, >= 1).";
                if (count > 256) count = 256;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                ushort a = (ushort)(start + i);
                ulong word = _cpu.hw.cs.Read(a);
                // Fresh MicroCode per word: DecodeMicroCode may not reset every field.
                var mc = new MicroCode();
                mc.DecodeMicroCode(word);
                sb.AppendLine($"CS {Oct(a)}:");
                sb.AppendLine(Microcode2Pseudo.GeneratePseudoCode(mc, a, _symbols));
            }
            return sb.ToString();
        }

        private static bool TryParseRegister(string name, out RegisterEnum reg)
        {
            return Enum.TryParse(name, ignoreCase: true, out reg) && Enum.IsDefined(typeof(RegisterEnum), reg);
        }

        private static bool TryParseNumber(string s, out ushort value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            try
            {
                value = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToUInt16(s.Substring(2), 16)
                    : Convert.ToUInt16(s, 8);
                return true;
            }
            catch { return false; }
        }
    }
}
