//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Text;
using NDMicroCpu.Core;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Cli
{
    /// <summary>
    /// Converts an ND-110/120 decoded microcode word (NDMicroCpu.Core.MicroCode) into readable C#-like
    /// pseudo code ("english" output). Migrated from ND120CPUEMU's Microcode2Pseudo and adapted
    /// to NDMicroCpu.Core's MicroCode field names and enum members (Commands/IDBSource/NextAddressSelect/
    /// StackControl/TestObjects/ALU*). Semantics for the command/MIS sub-ops come from the
    /// DELILAH-L listing and the assembler token descriptions, not from reversing the binary.
    /// </summary>
    public static class Microcode2Pseudo
    {
        /// <summary>
        /// Renders one micro word as C-like pseudo code: the ALU operation, the command and the sequencing, one statement per line.
        /// </summary>
        /// <param name="mc">
        /// The decoded micro word.
        /// </param>
        /// <param name="address">
        /// Its control store address, for jump targets.
        /// </param>
        /// <param name="symbols">
        /// Labels to name jump targets with, or null.
        /// </param>
        /// <returns>
        /// The pseudo code.
        /// </returns>
        public static string GeneratePseudoCode(MicroCode mc, ushort address, SymbolTable? symbols = null)
        {
            var lines = new List<string>();

            string? symbol = symbols?.GetSymbol(address);
            if (!string.IsNullOrEmpty(symbol)) lines.Add($"{symbol}:");

            string idb = GenerateIdbSource(mc);
            if (idb.Length > 0) lines.Add("  " + idb);

            string alu = GenerateAlu(mc);
            if (alu.Length > 0) lines.Add("  " + alu);

            string cmd = GenerateCommand(mc);
            if (cmd.Length > 0) lines.Add("  " + cmd);

            List<string> sequence = GenerateSequence(mc, address, symbols);
            for (int seqIdx = 0; seqIdx < sequence.Count; seqIdx++)
                if (sequence[seqIdx].Length > 0) lines.Add("  " + sequence[seqIdx]);

            if (lines.Count == 0 || (lines.Count == 1 && !string.IsNullOrEmpty(symbol)))
                lines.Add("  NOP; // No operation");

            return string.Join("\n", lines);
        }

        // ---- helpers ---------------------------------------------------------------------

        private static string Oct(int v, int width = 6) => Convert.ToString(v & 0xFFFF, 8).PadLeft(width, '0');

        // ARG immediate = A-operand (bits 12-15) as the top nibble + bits 0-11 (16-bit value).
        private static int ArgValue(MicroCode mc) => ((mc.bits12_15 & 0xF) << 12) | (mc.bits0_11 & 0xFFF);

        private static readonly string[] RegNames =
            { "Z", "D", "P", "B", "L", "A", "T", "X", "STS", "R1", "R2", "R3", "R4", "R5", "R6", "R7" };

        private static string Reg(int operand) => (operand >= 0 && operand < 16) ? RegNames[operand] : $"R?{operand}";

        // ---- IDB source ------------------------------------------------------------------

        private static string GenerateIdbSource(MicroCode mc)
        {
            switch (mc.IDBsource)
            {
                case IDBSource.none:
                case IDBSource.ALU:
                    return ""; // ALU line (if any) shows the assignment
                case IDBSource.ARG:
                    return $"IDB = {Oct(ArgValue(mc))}; // ARG microcode argument";
                case IDBSource.GPR:
                    return "IDB = GPR; // General Purpose Register";
                case IDBSource.GPR_SEXT:
                    return "IDB = SignExtend(GPR); // GPR sign-extended";
                case IDBSource.BMG:
                {
                    int bit = mc.bits12_15 & 0xF;
                    return $"IDB = {Oct(1 << bit)}; // 1<<{bit} bit-mask generator (BMG)";
                }
                case IDBSource.BARG:
                    return $"IDB = {mc.bits16_19 & 0xF}; // BARG (B-operand as argument)";
                case IDBSource.AARG:
                    return $"IDB = {Oct((mc.bits12_15 & 0xF) << 3)}; // AARG (A-operand << 3)";
                case IDBSource.STS:
                    return "IDB = STS; // Status register";
                case IDBSource.REG:
                    return $"IDB = RegFile[{Reg(mc.bits12_15 & 0xF)},{Reg(mc.bits16_19 & 0xF)}]; // register file";
                case IDBSource.SWAP:
                    return "IDB = ByteSwap(IDB); // byte swap";
                case IDBSource.DBR:
                    return "IDB = DBR; // Data Buffer Register";
                case IDBSource.PICS:
                    return "IDB = PICS; // PIC status";
                case IDBSource.PANEL:
                    return "IDB = PANEL; // panel";
                case IDBSource.IOR:
                    return "IDB = IOR; // I/O register";
                case IDBSource.MAPANS:
                    return "IDB = MAPANS; // MMU mapped-address answer";
                case IDBSource.MIPANS:
                    return "IDB = MIPANS; // MMU physical-address answer";
                case IDBSource.PGS:
                    return "IDB = PGS; // page status";
                case IDBSource.PCR:
                    return "IDB = PCR; // paging control register";
                case IDBSource.CSR:
                    return "IDB = CSR; // control status register";
                case IDBSource.ALD:
                    return "IDB = ALD; // automatic load descriptor";
                case IDBSource.PICVC:
                    return "IDB = PICV; // PIC vector";
                case IDBSource.PICMASK:
                    return "IDB = PICMASK; // PIC mask";
                case IDBSource.RINR:
                    return "IDB = RINR; // ring number";
                case IDBSource.JMPA:
                    return "IDB = JMPA; // jump address";
                default:
                    return $"IDB = {mc.IDBsource}; // {mc.IDBsource}";
            }
        }

        // ---- ALU -------------------------------------------------------------------------

        private static string GenerateAlu(MicroCode mc)
        {
            if (mc.aluDestination == ALUDestination.ALUD_NONE)
                return "";

            // Default pass-through (ADD A,Q -> Q) is noise; skip it.
            if (mc.aluFunction == ALUFunc.ADD && mc.aluSource == ALUSource.A_Q && mc.aluDestination == ALUDestination.ALUD_Q)
                return "";

            string dest = DestName(mc);
            (string a, string b) = Operands(mc.aluSource);

            string expr = mc.aluFunction switch
            {
                ALUFunc.ADD => $"{a} + {b}",
                ALUFunc.SUBR => $"{a} - {b}",
                ALUFunc.SUBS => $"{b} - {a}",
                ALUFunc.OR => $"{a} | {b}",
                ALUFunc.AND => $"{a} & {b}",
                ALUFunc.NOTRS => $"~{a}",
                ALUFunc.EXOR => $"{a} ^ {b}",
                ALUFunc.EXNOR => $"~({a} ^ {b})",
                _ => $"{a} {mc.aluFunction} {b}",
            };
            return $"{dest} = {expr};";
        }

        private static string DestName(MicroCode mc)
        {
            return mc.aluDestination switch
            {
                ALUDestination.ALUD_Q => "Q",
                ALUDestination.ALUD_B => Reg(mc.bits16_19 & 0xF),
                ALUDestination.ALUD_B_YA => Reg(mc.bits16_19 & 0xF),
                ALUDestination.ALUD_SRD => "SRD",
                ALUDestination.ALUD_SRB => "SRB",
                ALUDestination.ALUD_SLD => "SLD",
                ALUDestination.ALUD_SLB => "SLB",
                _ => "F",
            };
        }

        private static (string, string) Operands(ALUSource src)
        {
            // ND-110/120 ALU source pairs (A/D = external, Q = ALU Q, B = register B, O = 0).
            return src switch
            {
                ALUSource.A_Q => ("RegA", "Q"),
                ALUSource.A_B => ("RegA", "RegB"),
                ALUSource.D_A => ("IDB", "RegA"),
                ALUSource.D_Q => ("IDB", "Q"),
                ALUSource.D_O => ("IDB", "0"),
                _ => (src.ToString(), ""),
            };
        }

        // ---- command ---------------------------------------------------------------------

        private static string GenerateCommand(MicroCode mc)
        {
            switch (mc.command)
            {
                case Commands.NONE: return "";
                case Commands.LDPIL: return "PIL = IDB[15:8]; // load Program Interrupt Level";
                case Commands.LDGPR: return "GPR = IDB; // load General Purpose Register";
                case Commands.EWRF:
                    return $"RegFile[{Reg(mc.bits12_15 & 0xF)},{Reg(mc.bits16_19 & 0xF)}] = IDB; // write register file";
                case Commands.CLIRQ: return "InterruptRequest = false; // clear interrupt request";
                case Commands.RSDA: return "IDB = UART_DATA; // RSDA (reset data available / UART read)";
                case Commands.TBSTR: return "IDB = UART_STATUS; // TBSTR (transmit buffer status)";
                case Commands.SIOC: return $"SIOC({Oct(ArgValue(mc))}); // set I/O control register";
                case Commands.SLOW: return "SLOW_MICROCYCLE(); // timing control";
                case Commands.EPIC: return $"PIC_COMMAND(A={mc.bits12_15 & 0xF}); // enable priority interrupt command";
                case Commands.SMPID: return "SET_MICRO_PID(IDB);";
                case Commands.START: return "START_CPU(); // clear stop flip-flop";
                case Commands.SSTOP: return "STOP_CPU(); // set stop flip-flop";
                case Commands.CLRTC: return "CLEAR_RTC_INTERRUPT();";
                case Commands.CLFF: return "CLEAR_FP_FLAGS();";
                case Commands.LDLC: return "LOAD_LOOP_COUNTER(IDB);";
                case Commands.CJMP: return $"if ({CjmpCond(mc.MIS)}) goto {Oct(mc.jmpAddress)}; // conditional jump";
                case Commands.CNEXT_24: return $"NextIf({Cnext1Cond(mc.MIS)}); // conditional next";
                case Commands.CNEXT_25: return $"NextIf({Cnext2Cond(mc.MIS)}); // conditional next";
                // Commands 26/27 are MACRO jumps (Cpu.cs case Commands.JMP_26/JMP_27):
                // they compute a macro effective address from the displacement on the IDB,
                // write it to P, and set FETCH - i.e. they END the current macro
                // instruction and continue macro execution at EA. MIS selects the
                // addressing mode. JMPX with MIS=3 is CONTINUE: fetch the next macro
                // instruction at P (the normal way a microcode routine finishes).
                case Commands.JMP_26:
                    return mc.MIS switch
                    {
                        0 => "P = (P) + disp; FETCH; // macro JMP,* (P-relative)",
                        1 => "P = (B) + disp; FETCH; // macro JMP,B (B-relative)",
                        2 => "P = mem[(P|B) + disp]; FETCH; // macro JMP,I (indirect)",
                        _ => "P = (X) + disp; FETCH; // macro JMP,X (X-relative)",
                    };
                case Commands.JMP_27:
                    return mc.MIS switch
                    {
                        0 => "P = (B) + disp + (X); FETCH; // macro JMP,XB (post-indexed B-relative)",
                        1 => "P = mem[(P|B) + disp] + (X); FETCH; // macro JMP,XI (post-indexed indirect)",
                        2 => "JMPX_SPARE(); // JMPX MIS=2 (spare)",
                        _ => "CONTINUE; // done - fetch next macro instruction at P",
                    };
                // AREAD/AWRITE are MACRO memory accesses (Cpu.cs case Commands.AREAD_30..
                // AWRITE_33): the EA comes from the macro instruction's displacement +
                // the MIS-selected addressing mode, and reads land in the DBR (Data
                // Buffer Register) - a later microword moves DBR onto the IDB. The ARG
                // bits of the microword are NOT an address for these commands.
                case Commands.AREAD_30:
                    return mc.MIS switch
                    {
                        0 => "DBR = mem[(P) + disp]; // AREAD,* (P-relative read)",
                        1 => "DBR = mem[(B) + disp]; // AREAD,B (B-relative read)",
                        2 => "DBR = mem[mem[(P|B) + disp]]; // AREAD,I (indirect read)",
                        _ => "DBR = mem[(X) + disp]; // AREAD,X (X-relative read)",
                    };
                case Commands.AREAD_31:
                    return mc.MIS switch
                    {
                        0 => "DBR = mem[(B) + disp + (X)]; // AREAD,XB (post-indexed B-relative read)",
                        1 => "DBR = mem[mem[(P|B) + disp] + (X)]; // AREAD,XI (post-indexed indirect read)",
                        2 => "AREAD_SPARE(); // AREAD MIS=2 (spare)",
                        _ => "DBR = mem[lastEA + 1]; // AREAD,NEXT (read next location)",
                    };
                case Commands.AWRITE_32:
                    return mc.MIS switch
                    {
                        0 => "mem[(P) + disp] = IDB; // AWRITE,* (P-relative write)",
                        1 => "mem[(B) + disp] = IDB; // AWRITE,B (B-relative write)",
                        2 => "mem[mem[(P|B) + disp]] = IDB; // AWRITE,I (indirect write)",
                        _ => "mem[(X) + disp] = IDB; // AWRITE,X (X-relative write)",
                    };
                case Commands.AWRITE_33:
                    return mc.MIS switch
                    {
                        0 => "mem[(B) + disp + (X)] = IDB; // AWRITE,XB (post-indexed B-relative write)",
                        1 => "mem[mem[(P|B) + disp] + (X)] = IDB; // AWRITE,XI (post-indexed indirect write)",
                        2 => "mem[lastEA] = IDB; // AWRITE,HOLD (write last-used address, used by MIN)",
                        _ => "mem[lastEA + 1] = IDB; // AWRITE,NEXT (write next location)",
                    };
                case Commands.READ_34: return $"IDB = {ReadKind(mc.MIS)}; // READ";
                case Commands.WRITE_35: return $"{WriteKind(mc.MIS)} = IDB; // WRITE";
                case Commands.Command20: return $"SEGMENT_OP(MIS={mc.MIS}); // segment/paging op";
                case Commands.Command21: return $"MEMORY_OP(MIS={mc.MIS}); // memory control op";
                case Commands.Command22: return $"PAGING_OP(MIS={mc.MIS}); // paging op";
                case Commands.Command_36: return $"CONTROL_OP(MIS={mc.MIS}); // control-store op";
                case Commands.Command_37: return $"SPECIAL_OP(MIS={mc.MIS}); // special op";
                default: return $"CMD_{mc.command}(MIS={mc.MIS});";
            }
        }

        private static string CjmpCond(byte mis) => mis switch
        { 0 => "F15", 1 => "!F15", 2 => "F==0", 3 => "MF==0", _ => $"COND{mis}" };
        private static string Cnext1Cond(byte mis) => mis switch
        { 0 => "SGR", 1 => "!SGR", 2 => "CRY", 3 => "!CRY", _ => $"COND{mis}" };
        private static string Cnext2Cond(byte mis) => mis switch
        { 0 => "F15", 1 => "!F15", 2 => "F==0", 3 => "MF!=0", _ => $"COND{mis}" };
        private static string ReadKind(byte mis) => mis switch
        { 0 => "PageTable[addr]", 1 => "AltPageTable[addr]", 2 => "HOLD", _ => "EXAMINE" };
        private static string WriteKind(byte mis) => mis switch
        { 0 => "PageTable[addr]", 1 => "AltPageTable[addr]", 2 => "HOLD", _ => "DEPOSIT" };

        // ---- sequencing ------------------------------------------------------------------

        private static List<string> GenerateSequence(MicroCode mc, ushort address, SymbolTable? symbols)
        {
            var ops = new List<string>();

            // A JMP is only real when the sequencer actually selects Jump - the hardware
            // (Cpu.cs, "Latch new MAP address into MIC") only looks at jmpAddress in that
            // case. Rendering it for every word with the VECTOR bit produced spurious
            // "JMP 00000" lines on plain fall-through words.

            // Macro-jump commands (JMP 26 / JMPX 27, incl. CONTINUE) set FETCH: the next
            // micro address comes from the macro-instruction dispatch, so the word's own
            // sequencer jump field is dead - rendering it would just print "JMP 00000".
            if (mc.command == Commands.JMP_26 || mc.command == Commands.JMP_27)
                return ops;

            bool hasCond = mc.ECOND || mc.LOOP || mc.SCOND;
            if (!hasCond)
            {
                if (mc.TRUE_AddressSelect == NextAddressSelect.Jump)
                {
                    // Fall-through: an unconditional jump to address+1 is just "next" -
                    // don't clutter the listing with it.
                    if (!(mc.Vectored && mc.jmpAddress == (ushort)(address + 1)))
                        ops.Add($"JMP {JumpTarget(mc, symbols)};{JumpComment(mc)}");
                }
                else if (mc.TRUE_AddressSelect == NextAddressSelect.Return)
                    ops.Add("RETURN;");
                // Next / Repeat: fall through (Next is the default, not shown)
                return ops;
            }

            if (mc.SCOND)
                ops.Add($"TestCondition = {mc.SCOND_test}; // set condition for later ECOND");

            if (mc.ECOND)
            {
                string target = JumpTarget(mc, symbols);
                string jc = JumpComment(mc);
                bool trueJumps = mc.TRUE_AddressSelect == NextAddressSelect.Jump;
                bool falseJumps = mc.SCOND_False_AddressSelect == NextAddressSelect.Jump;
                if (trueJumps && !falseJumps) ops.Add($"if (TestCondition) goto {target};{jc}");
                else if (!trueJumps && falseJumps) ops.Add($"if (!TestCondition) goto {target};{jc}");
                else if (trueJumps && falseJumps) ops.Add($"goto {target}; // both paths{jc}");
                else ops.Add($"if (!TestCondition) goto {target}; // (uses prior test result){jc}");
            }
            else if (mc.LOOP)
            {
                ops.Add($"LOOP on {mc.SCOND_test};");
            }

            return ops;
        }

        /// <summary>
        /// Render a jump target with the REAL VECTOR-bit semantics (MicroCode.cs bit 25,
        /// Cpu.cs address latch): VECTOR set -> the full 13-bit target comes straight from
        /// the microword. VECTOR clear -> DISPATCH jump: the upper bits come from the
        /// microword but the low 4 bits are substituted at runtime, from IR(3:0) when
        /// MIS bit 0 = 0 (16-way opcode dispatch) or from the A-operand field when
        /// MIS bit 0 = 1.
        /// </summary>
        private static string JumpTarget(MicroCode mc, SymbolTable? symbols)
        {
            if (mc.Vectored)
                return Target(mc.jmpAddress, symbols);

            int baseAddr = mc.jmpAddress & 0x1FF0;
            return (mc.MIS & 1) == 0
                ? $"{Oct(baseAddr, 5)}+IR(3:0)"
                : $"{Oct(baseAddr, 5)}+Aop(3:0)";
        }

        /// <summary>Explanatory trailing comment for dispatch jumps (empty for direct jumps).</summary>
        private static string JumpComment(MicroCode mc)
        {
            if (mc.Vectored) return "";
            return (mc.MIS & 1) == 0
                ? " // dispatch: low 4 bits of target come from IR(3:0) at runtime"
                : $" // dispatch: low 4 bits of target come from the A-operand ({Reg(mc.bits12_15 & 0xF)})";
        }

        private static string Target(ushort address, SymbolTable? symbols)
        {
            string oct = Oct(address, 5);
            string? sym = symbols?.GetSymbol(address);
            return string.IsNullOrEmpty(sym) ? oct : $"{sym} // {oct}";
        }
    }
}
