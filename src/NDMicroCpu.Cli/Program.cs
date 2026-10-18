//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NDMicroCpu.Cli;
using static NDMicroCpu.Core.Enums;

// ND-110/120 microcode CLI: an interactive front-end on the working NDMicroCpu.Core, with pseudo-C
// ("english") microcode output. Thin REPL — all command logic lives in CliEngine.

CPUType cpuType = CPUType.ND120;
for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--nd110", StringComparison.OrdinalIgnoreCase)) cpuType = CPUType.ND110;
    if (string.Equals(args[i], "--nd120", StringComparison.OrdinalIgnoreCase)) cpuType = CPUType.ND120;
}

Console.WriteLine($"ND-110/120 microcode CLI — CPU type {cpuType}. Type 'help' for commands, 'quit' to exit.");
var engine = new CliEngine(cpuType);

while (true)
{
    Console.Write("ND> ");
    string? line = Console.ReadLine();
    if (line == null) break;
    line = line.Trim();
    if (line.Length == 0) continue;

    string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    string cmd = parts[0].ToLowerInvariant();

    if (cmd is "quit" or "exit") break;

    string output = Dispatch(engine, cmd, parts);
    if (output.Length > 0) Console.WriteLine(output);
}

static string Dispatch(CliEngine engine, string cmd, string[] parts)
{
    switch (cmd)
    {
        case "help":
            return Help();
        case "boot":
            return engine.Boot();
        case "status":
            return engine.Status();
        case "s":
        case "step":
            return engine.StepMicro();
        case "s!":
        case "stepmacro":
            return engine.StepMacro();
        case "-":
            // "-" alone: current microcode as pseudo-C; "- <addr>": pseudo-C at that address.
            if (parts.Length >= 2) return engine.PseudoAt(parts[1]);
            return engine.PseudoCurrent();
        case ",":
            return engine.FieldsCurrent();
        case "regdump":
            return engine.RegDump();
        case "reg":
            if (parts.Length >= 3 && parts[1].ToLowerInvariant() == "read")
                return engine.ReadRegister(parts[2]);
            if (parts.Length >= 4 && parts[1].ToLowerInvariant() == "write")
                return engine.WriteRegister(parts[2], parts[3]);
            return "Usage: reg read <name> | reg write <name> <value>";
        case "mem":
            if (parts.Length >= 3 && parts[1].ToLowerInvariant() == "read")
                return engine.ReadMemory(parts[2]);
            if (parts.Length >= 4 && parts[1].ToLowerInvariant() == "write")
                return engine.WriteMemory(parts[2], parts[3]);
            return "Usage: mem read <octal-addr> | mem write <octal-addr> <value>";
        case "mic":
            if (parts.Length >= 2) return engine.SetMicroAddress(parts[1]);
            return "Usage: mic <octal-address>";
        case "runi":
            return engine.RunInstruction(parts);
        case "runit":
        case "runi!":
            return engine.RunInstruction(parts, trace: true);
        case "pseudo":
        case ".":
            if (parts.Length >= 2) return engine.PseudoAt(parts[1]);
            return engine.PseudoCurrent();
        case "list":
            if (parts.Length >= 3) return engine.PseudoRange(parts[1], parts[2]);
            if (parts.Length >= 2) return engine.PseudoRange(parts[1]);
            return "Usage: list <octal-start> [count]  (count decimal, default 8)";
        case "symbols":
            if (parts.Length >= 2) return engine.LoadSymbols(parts[1]);
            return "Usage: symbols <file>  (LABEL=octal lines)";
        case "bp":
            if (parts.Length >= 2) return engine.AddBreakpoint(parts[1]);
            return "Usage: bp <octal-addr>";
        case "!bp":
            if (parts.Length >= 2) return engine.RemoveBreakpoint(parts[1]);
            return "Usage: !bp <octal-addr>";
        case "bplist":
            return engine.ListBreakpoints();
        case "bpclear":
            return engine.ClearBreakpoints();
        default:
            return $"Unknown command '{cmd}'. Type 'help'.";
    }
}

static string Help()
{
    return string.Join('\n', new[]
    {
        "Commands (all addresses/values OCTAL unless 0x-prefixed):",
        "",
        "Execution:",
        "  boot                       - run master clear + self-test to first FETCH",
        "  s | step                   - single microcode step",
        "  s! | stepmacro             - run to next macro FETCH or breakpoint",
        "  runi <opcode> [REG=val ..] - run ONE macro instruction to completion:",
        "                               boots if needed, sets registers, writes the",
        "                               opcode at P, executes, shows the registers.",
        "                               e.g. runi 170405        (SAA 5 -> A=5)",
        "                               e.g. runi 172403 A=2    (AAA 3 -> A=5)",
        "  runit | runi! <opcode> ..  - same as runi but TRACES every microcode step:",
        "                               CS address + pseudo-C + '// actual:' comment",
        "                               with the real IDB value and register changes",
        "",
        "Microcode display:",
        "  -  [addr] | pseudo [addr]  - microcode as C-like pseudo code (current or at addr)",
        "  .  [addr]                  - same as pseudo",
        "  list <start> [count]       - list a RANGE of microcode as pseudo code",
        "                               (count decimal, default 8, max 256)",
        "  ,                          - current microcode raw field decoding",
        "  mic <addr>                 - set the microcode address",
        "  symbols <file>             - load LABEL=octal symbol file for pseudo-C labels",
        "",
        "Registers & memory:",
        "  reg read <name>            - read a register (Zero,D,P,B,L,A,T,X,STS,R1-R7)",
        "  reg write <name> <value>   - write a register",
        "  regdump                    - dump program-visible registers",
        "  mem read <addr>            - read a memory word",
        "  mem write <addr> <value>   - write a memory word",
        "",
        "Breakpoints (microcode addresses):",
        "  bp <addr>                  - add breakpoint (s! stops when mic addr hits it)",
        "  !bp <addr>                 - remove breakpoint",
        "  bplist                     - list breakpoints",
        "  bpclear                    - clear all breakpoints",
        "",
        "Misc:",
        "  status                     - micro address, FETCH/STOP, registers",
        "  help                       - this list",
        "  quit | exit                - leave",
    });
}
