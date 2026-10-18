# NDMicroCpu

A micro code level emulator of the Norsk Data ND-110 and ND-120 CPUs: it runs the real RASK and
DELILAH control store images one 64-bit micro word at a time, with the ALU, register file, memory
management, interrupt system, UART and operator panel modelled from the schematics and the
Microprogrammer's Guide. The macro instruction set is whatever the micro code makes it, which is
what makes this emulator the reference other ND-100 emulators are checked against.

- **[Guides](../docs/ND110-Boot-Process.md)** - the boot process, the OPCOM console, single step,
  the micro word, the back-wiring PROM, the INSTRUCTION-B test campaign and the reference traces.
- **[API reference](api/index.md)** - the Core library, the command line engine and the debugger
  contract, from the XML documentation.

The repository holds:

- **NDMicroCpu.Core** - the CPU and its hardware blocks, the memory, the control store loader and
  the BPUN program loader.
- **NDMicroCpu** (the Machine project) - the console emulator: boots INSTRUCTION-B over the bus and
  runs it with the keyboard as the console.
- **ndmicrocli** - a command line for exploring the micro code on the running CPU: single-step micro
  code, run one macro instruction, render control store words as pseudo code.
- **NDMicroCpu.Web** - the same CPU in the browser (Blazor WebAssembly) with register, ALU, interrupt
  and memory views.
- **ndmicrodebug** and **ndmicrodebug-client** - a gRPC server and a sample client so another emulator
  can step this CPU and compare registers and memory; the contract is in NDMicroCpu.Debugger.Contracts.

The two microprogrammer's manuals are PDFs; they are not on this site but in the repository under
`src/docs/manuals`.
