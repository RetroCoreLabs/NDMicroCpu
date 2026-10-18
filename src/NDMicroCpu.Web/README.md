# NDMicroCpu.Web - the emulator in the browser

The same micro code CPU compiled to WebAssembly, with pages that show what the hardware is doing:
the register file, the ALU internals, the interrupt controller, the memory, a micro code debugger
that steps the control store one word at a time with the listing beside it, and a terminal page
that boots a program and talks to it.

## Run it

```powershell
# from the repository root
dotnet run --project src\NDMicroCpu.Web
```

Then open the address the development server prints (https://localhost:5001 by default). The
browser fetches the control store image and its listing from `wwwroot\Assets`; those files are
copies of `NDMicroCpu.Core\microcode` and a test fails when they drift apart. Refresh them with
`src\eng\sync-web-assets.ps1`.

Publishing (`dotnet publish -c Release`) ahead-of-time compiles the WebAssembly, which needs the
`wasm-tools` workload (`dotnet workload install wasm-tools`) and takes several minutes.

## Pages

| Page | What it shows |
|---|---|
| Micro Debugger | The control store listing with the current micro address, single step, breakpoints on micro addresses, and the decoded fields of the current micro word |
| View MicroCode | The listing on its own |
| Registerfile, ALU, Interrupts | The register file per program level, the ALU internals and the two Am2914 chips, live |
| RAM | The memory, with the page tables |
| ND-110 Debugger | Registers and memory with step, run, reset and a breakpoint at a macro address |
| Terminal | Power, reset, and the console of the running machine |
| Floppy drive | Mount a BPUN image as the floppy and boot it over the bus |

The CPU type (ND-110 or ND-120) is chosen in the preferences panel of the navigation menu and
remembered in the browser.
