# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

NDMicroCpu: the ND-110/ND-120 CPU emulated at micro code level. It runs the real RASK and
DELILAH control store images; the macro instruction set is whatever the micro code makes it.
This emulator is the ORACLE the RetroCore ND-100 emulator and the nd-120 Verilog model are
checked against, so fidelity beats speed and beats convenience. The README at the repository root
is the user-facing description; this file is the working notes.

## Layout

The repository root holds only `README.md`, `LICENSE`, `.gitignore`, `.gitattributes` and
`.github/`. Everything else is under `src/`:

- `src/NDMicroCpu.Core/` - the CPU. `Cpu.cs` holds the four-phase micro instruction loop
  (`ExecuteMicroCode`), `CpuInternals.cs` wires the hardware blocks: `BUFALU` (ALU and register
  file), `MIC` (sequencer), `MAC` (memory address), `Memory` and `PagingTables`,
  `InterruptSubSystem` with two `Am2914`, `Uart` (ND-110) or `Uart2661` (ND-120), `Panel`,
  `RealTimeClock`, `TrapController`, and the `NDBus` devices. `Support/` has the BPUN and PROG
  loaders with INSTRUCTION-B embedded; `Common/` the logger, number formatting, parity and the
  software floating point. `microcode/` holds the `.CODE` images, symbols and listings.
- `src/NDMicroCpu.Machine/` - the console machine (`NDMicroCpu.exe`), `BPUN/` test programs.
- `src/NDMicroCpu.Cli/` - `ndmicrocli`, the micro code command line and the pseudo code renderer.
- `src/NDMicroCpu.Web/` - Blazor WebAssembly. `wwwroot/Assets` has copies of the micro code;
  `eng/sync-web-assets.ps1` refreshes them and a test fails when they drift.
- `src/NDMicroCpu.Debugger/` (`ndmicrodebug`), `.Debugger.Contracts/`, `.Debugger.Client/`.
- `src/tests/NDMicroCpu.Core.Tests/` - 250 tests; `Boot/` boots the machine and runs
  INSTRUCTION-B; the `[Explicit]` trace generators write `src/docs/traces/`.
  `TestPaths.cs` finds every file by walking up to `NDMicroCpu.slnx`; never hardcode a path.
- `src/docs/` - technical references, `manuals/` (two OCR'd PDFs), `traces/`. `src/docfx/` - the
  API site.

Run `dotnet` from `src/`, always with `-nodeReuse:false`.

## Rules that the build enforces

- `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and `GenerateDocumentationFile` are on in
  `Directory.Build.props`. A public member without an XML comment, a wrong file header or a
  style violation fails the build. Fix the cause; never add `NoWarn` or a `#pragma`.
- Every `.cs` file starts with the header in `.editorconfig` (`file_header_template`).
- No LINQ, no `foreach`, no FluentAssertions. Index-based `for` loops.
- `dotnet format` applies nullability fixers that are wrong for test fixtures: a field that
  `SetUp` always creates is `= null!`, not nullable. Check its diff before accepting it.

## Rules about the emulation

- Emulate the mechanism, not the answer. No C# shortcut that produces the value the micro code
  would; no code keyed on a specific micro address to make one case pass. If the mechanism is
  unknown, find it in the listing or the schematics, or leave it failing loudly.
- The micro code is the authority, then the ND-110/ND-120 Microprogrammer's Guide
  (ND-06.031.1 EN, in `src/docs/manuals`), then the schematics in the nd-120 repository. Another
  emulator agreeing is not verification.
- Logging to NATS was removed on purpose; the logger has Console, Trace, Event and File. Tests
  must never enable logging at the Opcodes level, it costs millions of lines.

## Facts worth knowing

- Boot takes about 33,000 cycles to reach FETCH; INSTRUCTION-B is booted over the bus with the
  console command `1560$` (floppy binary load), the same way an operator would.
- The ND-120 DELILAH-L micro code is version L; the `COMM,LDIRV` patch variant exists in the
  NDMicroAsm repository for the IOX 30x console path.
- The gRPC contract in `NDMicroCpu.Debugger.Contracts` is mirrored in the RetroCore repository;
  `[DataMember(Order = n)]` numbers are the wire format.
- The nd-120 Verilog project diffs its execution against `src/docs/traces`; regenerate them with
  the `[Explicit]` generators (`dotnet test --filter "Name~GenerateTrace"`) after a CPU change
  that is meant to change behaviour, and say so in the commit.
