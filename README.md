# NDMicroCpu

[![build](https://github.com/RetroCoreLabs/NDMicroCpu/actions/workflows/build.yml/badge.svg)](https://github.com/RetroCoreLabs/NDMicroCpu/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/RetroCoreLabs/NDMicroCpu)](https://github.com/RetroCoreLabs/NDMicroCpu/releases)
[![licence](https://img.shields.io/github/license/RetroCoreLabs/NDMicroCpu)](LICENSE)
[![downloads](https://img.shields.io/github/downloads/RetroCoreLabs/NDMicroCpu/total)](https://github.com/RetroCoreLabs/NDMicroCpu/releases)

**NDMicroCpu is an emulator of the Norsk Data ND-110 and ND-120 CPUs that runs their real micro code, one 64-bit micro word at a time, for anyone who wants to know what the hardware actually did.**

**[API documentation](https://retrocorelabs.github.io/NDMicroCpu/)** - generated from the XML docs by `.github/workflows/docs.yml`.

[Applications](#the-applications) · [Quick start](#quick-start) · [Building](#building-from-source) · [About the ND-110 and ND-120](#about-the-nd-110-and-nd-120)

**Latest builds:** https://github.com/RetroCoreLabs/NDMicroCpu/releases/latest

## The applications

| Application | What it is for | README |
|---|---|---|
| **NDMicroCpu** | The console machine: boots the CPU through its self-test, loads Norsk Data's INSTRUCTION-B test program over the bus and gives you its console | [README](src/NDMicroCpu.Machine/README.md) |
| **ndmicrocli** | A command line on the running CPU: single-step micro code, run one macro instruction with preset registers, render control store words as pseudo code | [README](src/NDMicroCpu.Cli/README.md) |
| **NDMicroCpu.Web** | The same CPU in the browser, with live views of the register file, the ALU, the interrupt chips, the memory and a micro code debugger | [README](src/NDMicroCpu.Web/README.md) |
| **ndmicrodebug** | A gRPC server so another emulator can step this CPU and compare registers and memory against it | [README](src/NDMicroCpu.Debugger/README.md) |
| **ndmicrodebug-client** | The smallest client of that server, as an example | [README](src/NDMicroCpu.Debugger.Client/README.md) |

## Why it exists

An ND-110 or ND-120 does not execute its instruction set directly. A control store of 64-bit micro
words does, one micro instruction per clock, and the macro instructions the programs see are
whatever that micro code makes them. Most emulators re-implement the instruction set from the
manuals and get the common cases right; the uncommon ones, the flags after an odd operand, the
exact timing of an interrupt, come out as the emulator's author guessed them.

This emulator takes the other road. It models the hardware the micro code runs on, the ALU and
its register file, the sequencer, the memory management, the two Am2914 interrupt chips, the UART
and the panel, and then runs the real RASK and DELILAH micro code on it. Whatever the micro code
does is what the emulator does. That makes it slow and makes it the reference: the RetroCore ND-100
emulator and the nd-120 Verilog model are both checked against it, through the gRPC debugger and
the reference traces in this repository.

## What you can use it for

- **Prove an instruction's behaviour** by running it on the real micro code with chosen registers
  and watching every micro step, with `ndmicrocli`.
- **Check another emulator** instruction by instruction over gRPC, or offline against the
  INSTRUCTION-B reference traces.
- **Read the micro code with the machine running** in the browser: the listing, the current micro
  address, and what each field of the micro word selects.
- **Run Norsk Data's own CPU verification program** and see every area pass.

## Status

No tagged release yet; `VersionPrefix` is 0.1.0.

- The ND-110 with the RASK micro code and the ND-120 with the DELILAH-L micro code both boot
  through the self-test to OPCOM, load INSTRUCTION-B over the bus and pass its test areas.
- Test suite: 276 tests, all passing (measured 18 October 2026 on net10.0). The core suite boots
  the machine many times and takes between three and seven minutes.
- The web emulator builds and runs; its publish needs the `wasm-tools` workload.
- The gRPC debugger builds and serves; the real-hardware serial link (OPCOM over COM11) was last
  used in 2023 and is not covered by tests.

## Prerequisites

| # | Requirement | Needed for | How to check |
|---|---|---|---|
| P1 | .NET SDK 10.0.302 or a later 10.0 feature band (`src/global.json`) | building | `dotnet --list-sdks` |
| P2 | PowerShell 7 or newer | `build.ps1` and the `eng` scripts | `$PSVersionTable.PSVersion` |
| P3 | The `wasm-tools` workload | publishing the web emulator only | `dotnet workload list` |

## Quick start

**Pre-built binaries** are on the [Releases page](../../releases).

```powershell
# from the repository root
.\src\build.ps1
.\src\publish\win-x64\NDMicroCpu.exe
```

The console machine boots to INSTRUCTION-B's `>` prompt; type `HELP`. For the micro code command
line, `.\src\publish\win-x64\ndmicrocli.exe` and then `boot`, `runi 170405 P=2000`, `list 2000 8`.

## Building from source

```powershell
# from the repository root
cd src
dotnet build -nodeReuse:false
dotnet test -nodeReuse:false
dotnet run --project NDMicroCpu.Web
```

Everything the build needs is on nuget.org. [CONTRIBUTING.md](.github/CONTRIBUTING.md) has the
rules the build enforces and what the test suite does.

## Repository layout

| Folder | What is in it |
|---|---|
| `src/NDMicroCpu.Core/` | The CPU and its hardware blocks, memory, control store, loaders; `microcode/` holds the control store images, symbols and listings from the NDMicroAsm assembler |
| `src/NDMicroCpu.Machine/` | The console machine, `NDMicroCpu.exe`, and the BPUN test programs |
| `src/NDMicroCpu.Cli/` | `ndmicrocli` |
| `src/NDMicroCpu.Web/` | The Blazor WebAssembly emulator |
| `src/NDMicroCpu.Debugger/`, `.Debugger.Contracts/`, `.Debugger.Client/` | The gRPC server, its contract and the sample client |
| `src/tests/` | The NUnit suites: `NDMicroCpu.Core.Tests` (CPU, PIC, MMU, boot, INSTRUCTION-B areas, trace generators) and `NDMicroCpu.Cli.Tests` |
| `src/docs/` | The technical references, the two microprogrammer's manuals, and the INSTRUCTION-B reference traces |
| `src/docfx/` | The API site |
| `src/eng/` | `sync-web-assets.ps1` and the documentation checker |

## Current limitations

- **Speed.** Every macro instruction costs tens of micro cycles, each fully modelled; the emulator
  is for understanding and checking, not for running SINTRAN at speed.
- **Peripherals.** The bus carries the floppy in binary-load mode, the paper tape reader and a
  floppy DMA controller; there is no disk, no network and no second terminal.
- **The ND-110 debugger page of the web emulator** lost its assemble-and-run panel in the move;
  the macro assembler it used was a prebuilt binary whose source is not in this repository.

## About the ND-110 and ND-120

Norsk Data was a Norwegian computer maker, in business from 1967 into the early 1990s, and the
ND-100 family was its 16-bit minicomputer line, run with the SINTRAN III operating system in
universities, industry and the military across Europe. The ND-110 and the ND-120 were its later
CPUs: microprogrammed, with the instruction set implemented by micro code in a control store, and
with the paging, the sixteen program levels and the interrupt system that made SINTRAN a
multi-user system. The micro code listings survive, and running them is the most faithful way to
emulate the machines.

## Releases

| Artifact | Target | How it is built |
|---|---|---|
| `NDMicroCpu-win-x64.zip` | Windows 64-bit; holds NDMicroCpu.exe, ndmicrocli.exe, ndmicrodebug.exe, ndmicrodebug-client.exe | `.github/workflows/release.yml`, `dotnet publish` single-file self-contained |
| `NDMicroCpu-linux-x64.tar.gz` | Linux 64-bit | same workflow, Linux runner |
| `NDMicroCpu-osx-x64.tar.gz`, `NDMicroCpu-osx-arm64.tar.gz` | macOS Intel and Apple silicon | same workflow, macOS runners |

The web emulator is not in a release; it is a site.

### Cutting a release

```bash
git tag v<VERSION>
git push origin v<VERSION>
```

The push runs the test job, then one publish job per platform, then attaches the archives to a
GitHub release named after the tag. The tag is the version number of the binaries. One failed
platform means no release at all.

## Documentation

- [ND-110 boot process](src/docs/ND110-Boot-Process.md) - power-up to OPCOM, as the RASK micro code does it
- [OPCOM commands](src/docs/opcom-commands.md) - the operator console built into the micro code
- [Single step](src/docs/single-step.md) - how the micro code executes exactly one macro instruction
- [Micro code details](src/docs/microcode-details.md) - the micro word, the sequencer and the ALU control
- [Back-wiring PROM and installation number](src/docs/Backwiring-PROM-Installation-Number.md) - what VERSN reads
- [INSTRUCTION-B SEGMENT command](src/docs/SEGMENT-Command-Disassembly.md) - annotated disassembly
- [INSTRUCTION-VERIFY test campaign](src/docs/INSTRUCTION-VERIFY-Test-Campaign.md) - the emulator bugs the program found, and the fixes
- [Reference traces](src/docs/traces/README.md) - what they are and how another emulator diffs against them
- [Manuals](src/docs/manuals/) - the ND-110/ND-120 Microprogrammer's Guide and the ND-110 version K micro program listing, OCR'd
- [DocFX site](src/docfx/README.md) - how the API reference is built

## Origins

The emulator began in 2022 as the ND-110 micro code emulator repository, which also held the micro
code assembler; the assembler moved to the NDMicroAsm repository and the emulator moved here, in
October 2026. The INSTRUCTION-B program is Norsk Data's own, image 10523D01, preserved by the
ND community.

## Contributing

See [CONTRIBUTING.md](.github/CONTRIBUTING.md). Security problems go through
[SECURITY.md](.github/SECURITY.md), never a public issue.

## Licence

MIT. See [LICENSE](LICENSE).
