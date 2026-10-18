# ndmicrocli - the ND-110/ND-120 micro code command line

An interactive console front-end on the working `NDMicroCpu.Core` emulator core, with pseudo-C
("english") microcode output. It boots the real DELILAH / DELILAH-L microcode through the
CPU self-test, single-steps microcode, runs single macro instructions to completion, and
renders any control-store word as readable C-like pseudo code.

**Why it exists:** the sibling `ND120CPUEMU` repo (a from-scratch Verilog-faithful ND-120
emulator) has a core that fails CPU self-test TEST 7 and never boots. This CLI migrates the
useful parts of that project's console — the interactive command set and the
`Microcode2Pseudo` renderer — onto the `NDMicroCpu.Core` core in this repo, which boots both
ND-110 and ND-120 microcode cleanly to the first macro-instruction FETCH.

## How to run

From the repository root:

```powershell
# Start the CLI (default CPU type: ND-120 / DELILAH-L microcode)
dotnet run --project src\NDMicroCpu.Cli

# Start with ND-110 microcode instead
dotnet run --project src\NDMicroCpu.Cli -- --nd110
```

From anywhere else, use the full path to the project file:

```powershell
dotnet run --project src\NDMicroCpu.Cli\NDMicroCpu.Cli.csproj
dotnet run --project src\NDMicroCpu.Cli\NDMicroCpu.Cli.csproj -- --nd110
```

You get the `ND> ` prompt. A minimal first session:

```text
ND> boot                    run master clear + self-test to the first FETCH
ND> runi 170405 P=2000      execute SAA 5 -> reports A=000005
ND> runit 172403 A=2 P=2000 execute AAA 3 traced: every microcode step with
                            pseudo-C + actual IDB/register values
ND> list 0 8                list 8 microcode words as pseudo-C
ND> help                    full command list
ND> quit                    leave
```

Command-line arguments:

| Argument | Effect |
|----------|--------|
| *(none)* | ND-120 CPU type, loads the DELILAH-L microcode |
| `--nd120` | Same as default, explicit |
| `--nd110` | ND-110 CPU type, loads the DELILAH (ND-110) microcode |

## Build and test

```powershell
# Build only
dotnet build src\NDMicroCpu.Cli\NDMicroCpu.Cli.csproj -nodeReuse:false

# Tests (NUnit, covers boot, pseudo-C rendering, runi, breakpoints)
dotnet test src\tests\NDMicroCpu.Cli.Tests\NDMicroCpu.Cli.Tests.csproj -nodeReuse:false
```

## Conventions

- **ALL addresses and values are OCTAL** unless prefixed with `0x` for hex.
- Register names: `Zero, D, P, B, L, A, T, X, STS, R1-R7` (case-insensitive).
- Microcode addresses are control-store addresses (what the MIC `map` register holds).

## Commands

### Execution

| Command | Description |
|---------|-------------|
| `boot` | Master clear + run the built-in CPU self-test to the first macro FETCH. Reports `Boot OK` with the cycle count, or `SELF-TEST FAILED` with the R2 error code if execution reaches STERR. |
| `s` / `step` | Execute ONE microcode instruction; shows old → new micro address. |
| `s!` / `stepmacro` | Run microcode until the next macro-instruction FETCH boundary **or** a microcode breakpoint, whichever comes first. |
| `runi <opcode> [REG=val ...]` | Run ONE macro instruction to completion — see below. |
| `runit <opcode> [REG=val ...]` | Same as `runi` but **traces every microcode step**: CS address + pseudo-C + a `// actual:` comment with the real IDB value and register changes. Alias: `runi!`. See below. |

### `runi` — run a single macro instruction

`runi` executes one real macro instruction on the booted CPU and shows the resulting
registers. It:

1. Boots the CPU first if it has not been booted yet (the instruction must run from the
   post-self-test FETCH state).
2. Applies any `REG=val` presets (a preset `P` also selects the execution address).
3. Writes the opcode into memory at `P`.
4. Fetches and executes exactly one instruction (runs until the next FETCH).
5. Dumps the program-visible registers and STS.

```text
ND> runi 170405 P=2000        SAA 5:  A := 5, executes at 002000
ND> runi 172403 A=2 P=2000    AAA 3 with A preset to 2  ->  A = 000005
ND> runi 044000 P=2000        LDA 0,(P)-relative - any 16-bit opcode works
```

Because the underlying core's macro fetch/dispatch genuinely works, the reported register
results are real execution results (validated by unit tests in `NDMicroCpu.Cli.Tests`).

### `runit` — run a single macro instruction WITH a full microcode trace

`runit` (alias `runi!`) does everything `runi` does, and additionally prints, for EVERY
microcode step the instruction executes:

- the control-store address (`CS <addr>:`)
- the pseudo-C ("english") rendering of that microcode word
- a `// actual:` comment with the REAL runtime values after the step — the IDB latch
  value and every program-visible register (P A D T X B L STS) that changed, as
  `old->new`

Real captured output — `AAA 3` (add argument 3 to A) with A preset to 2:

```text
ND> runit 172403 A=2 P=2000
set A = 000002
set P = 002000
mem[002000] = 172403 (opcode)
--- microcode trace (opcode fetched from 002000) ---
CS 007724:
  IDB = SignExtend(GPR); // GPR sign-extended
  A = IDB + RegA;
  CONTINUE; // done - fetch next macro instruction at P
  // actual: IDB=000003  A: 000002->000005
--- 1 microcode steps ---
executed one instruction; next FETCH at P=002001

regs: P=002001 A=000005 D=000000 T=000000 X=000000 B=000000 L=000000
STS=000000
```

You can see the whole story in one screen: the DELILAH-L word at CS 007724 implements
the AAA argument instruction (`A = IDB + RegA` with the sign-extended argument on the
IDB), the actual IDB value that flowed was 000003, and A really went 000002 → 000005.

Memory-reference instructions produce multi-step traces, one block per microcode word.
Real captured output — `LDA 2` (P-relative load): plant a known value at the effective
address first with `mem write`, then trace the load:

```text
ND> boot
Boot OK: reached FETCH after 33409 cycles. Micro addr=000000, STOP=True
ND> mem write 2002 12345
mem[002002] = 012345
ND> runit 044002 P=2000
set P = 002000
mem[002000] = 044002 (opcode)
--- microcode trace (opcode fetched from 002000) ---
CS 006440:
  DBR = mem[(P) + disp]; // AREAD,* (P-relative read)
  JMP 00170;
  // actual: IDB=010000
CS 000170:
  IDB = DBR; // Data Buffer Register
  A = IDB ^ 0;
  CONTINUE; // done - fetch next macro instruction at P
  // actual: IDB=012345  A: 000000->012345
--- 2 microcode steps ---
executed one instruction; next FETCH at P=002001

regs: P=002001 A=012345 D=000000 T=000000 X=000000 B=000000 L=000000
STS=000000
```

The trace shows the LDA mechanics exactly as the hardware does it: the entry word at
CS 006440 issues the macro memory read (AREAD,* — P-relative, result lands in the Data
Buffer Register) and jumps to CS 000170, which moves the DBR onto the IDB and into A,
then finishes with CONTINUE (fetch the next macro instruction at P) — and the
`// actual:` comments confirm the real loaded value 012345 arriving in A.

### How jumps are rendered (VECTOR bit / dispatch / fall-through)

The sequencing output follows the real hardware semantics (microword bit 25, the VECTOR
bit, as implemented in `NDMicroCpu.Core\Cpu.cs` and `MicroCode.cs`):

- `JMP 00170;` — direct jump: the full 13-bit target comes straight from the microword.
- An unconditional jump to the NEXT address is plain fall-through and is not shown at all.
- `JMP 06440+IR(3:0); // dispatch: low 4 bits of target come from IR(3:0) at runtime` —
  dispatch jump (VECTOR bit clear): the low 4 bits of the target are substituted at
  runtime from the instruction register (MIS bit 0 = 0) or from the A-operand field
  (MIS bit 0 = 1). This is how 16-way opcode dispatch works.
- `CONTINUE; // done - fetch next macro instruction at P` — command JMPX with MIS=3:
  the standard way a microcode routine ends the current macro instruction.
- `P = (B) + disp; FETCH; // macro JMP,B (B-relative)` — commands JMP/JMPX (26/27) are
  MACRO jumps: they compute the macro effective address, write P and set FETCH. For
  these words (and CONTINUE) no micro-level `JMP` is printed, because the next micro
  address comes from the macro-instruction dispatch, not from the word's jump field.
- `DBR = mem[(P) + disp]; // AREAD,* (P-relative read)` — AREAD/AWRITE are macro memory
  accesses: the EA comes from the macro instruction's displacement and MIS-selected
  addressing mode; reads land in the DBR (a later microword moves DBR onto the IDB).

### Microcode display

| Command | Description |
|---------|-------------|
| `-` or `pseudo` or `.` | Show the CURRENT microcode word as C-like pseudo code. |
| `- <addr>` / `pseudo <addr>` / `. <addr>` | Decode the control-store word at the octal address and show it as pseudo code. |
| `list <start> [count]` | List a RANGE of control-store words as pseudo code — the "readable microcode listing" view. Start address octal; count decimal (default 8, max 256). |
| `,` | Show the current microcode word's raw decoded fields (command, IDB source, MIS, jump address, condition/stack controls). |
| `mic <addr>` | Set the microcode address (latches the MIC `map`). |
| `symbols <file>` | Load a `LABEL=octal` symbol file so pseudo-code output shows human-readable labels for jump targets. |

### Registers and memory

| Command | Description |
|---------|-------------|
| `reg read <name>` | Read a register, e.g. `reg read A`. |
| `reg write <name> <value>` | Write a register, e.g. `reg write A 1234` (octal) or `reg write A 0x1234`. |
| `regdump` | Dump the program-visible registers (P A D T X B L). |
| `mem read <addr>` | Read a memory word (16-bit physical address). |
| `mem write <addr> <value>` | Write a memory word. |

### Breakpoints (microcode addresses)

| Command | Description |
|---------|-------------|
| `bp <addr>` | Add a breakpoint at a microcode address. `s!` stops when the MIC map hits it. |
| `!bp <addr>` | Remove the breakpoint at that address. |
| `bplist` | List all breakpoints (sorted, octal). |
| `bpclear` | Remove all breakpoints. |

### Misc

| Command | Description |
|---------|-------------|
| `status` | CPU type, current micro address, FETCH/STOP flags, register dump. |
| `help` | Command list. |
| `quit` / `exit` | Leave the CLI. |

## Example session

Real captured output (ND-120 / DELILAH-L):

```text
ND-110/120 microcode CLI — CPU type ND120. Type 'help' for commands, 'quit' to exit.
Loaded Control Store, length= 0x1317
ND> boot
Boot OK: reached FETCH after 33409 cycles. Micro addr=000000, STOP=True
ND> runi 170405 P=2000
set P = 002000
mem[002000] = 170405 (opcode)
executed one instruction; next FETCH at P=002001

regs: P=002001 A=000005 D=000000 T=000000 X=000000 B=000000 L=000000
STS=000000

ND> bp 725
breakpoint added at micro addr 000725
```

Reading microcode as pseudo-C (real captured output):

```text
ND> list 0 3
CS 000000:
  IDB = 142001; // ARG microcode argument
  RegFile[R4,R1] = IDB; // write register file
  JMP 02001;
CS 000001:
  IDB = 000100; // 1<<6 bit-mask generator (BMG)
  SET_MICRO_PID(IDB);
  JMP 00030;
CS 000002:
  IDB = 000040; // 1<<5 bit-mask generator (BMG)
  SET_MICRO_PID(IDB);
  JMP 00030;
```

When `s!` runs into an armed breakpoint it reports
`*** BREAKPOINT at micro addr <addr> after <n> ticks ***` instead of `reached FETCH`.

## Architecture

Two thin layers over the `NDMicroCpu.Core` library — no CPU logic lives in the CLI:

| File | Role |
|------|------|
| `Program.cs` | Thin REPL: reads a line, dispatches, prints the returned string. |
| `CliEngine.cs` | ALL command logic. Every method returns a `string` (never writes to the console), so the identical logic is driven by both the REPL and the NUnit tests. |
| `Microcode2Pseudo.cs` | Renders a decoded 64-bit `MicroCode` word as C-like pseudo code (IDB source, ALU op, command, branching), with symbol-table support. Ported from the ND120CPUEMU project and adapted to `NDMicroCpu.Core`'s field/enum names. |
| `SymbolTable.cs` | `LABEL=octal` symbol file loader used by the pseudo-code renderer. |

Key `NDMicroCpu.Core` APIs used: `cpu.TickCpu()`, `cpu.StepMicroCode()`, `cpu.hw.mic.map` /
`LatchMap`, `cpu.hw.mac.FETCH`, `cpu.hw.alu.ReadRegisterE` / `WriteRegisterE`,
`cpu.memory.ReadPhysicalMemory` / `WritePhysicalMemory`, `cpu.hw.cs.Read` +
`MicroCode.DecodeMicroCode`, and `cpu.UnitTest_FetchAndExecute` (the debugger API behind
`runi`). The breakpoint-aware `s!` uses `cpu.UnitTest_SetStop(false)` because the STOP
flip-flop's setter is internal to the CPU library.

## Tests

`NDMicroCpu.Cli.Tests` (NUnit) validates the CLI end-to-end on the real microcode:

- Boots BOTH ND-120 and ND-110 to FETCH with no self-test error.
- Renders all 2538 loaded DELILAH-L microcode words as pseudo-C without crashing.
- `runi` executes real instructions and the register results are asserted
  (SAA/AAA argument instructions with known outcomes).
- Breakpoint add/list/remove/clear, and a deterministic breakpoint-hit test for `s!`.

```powershell
dotnet test src\tests\NDMicroCpu.Cli.Tests\NDMicroCpu.Cli.Tests.csproj -nodeReuse:false
```

## History / design notes

