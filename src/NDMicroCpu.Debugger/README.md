# ndmicrodebug - a gRPC server in front of the micro code CPU

Another emulator can drive this CPU one macro instruction at a time and compare itself against
it: load a register set, step, and read, write or clear memory. That is how the RetroCore ND-100
emulator's instruction tests are checked against the micro code, which is the ground truth.

The contract is code-first gRPC (protobuf-net): the messages and the service interface are in
the `NDMicroCpu.Debugger.Contracts` project, and a client references that project or copies the
file. Every `[DataMember(Order = n)]` number is part of the wire format; change a field and the
other side must change too.

## Run it

```powershell
# from the repository root
dotnet run --project src\NDMicroCpu.Debugger
```

The server boots the CPU through its self-test with INSTRUCTION-B loaded, then listens for gRPC
on port 9000 (HTTP/2). With no client connected the keyboard still goes to the emulated console:
PageUp and PageDown switch the micro code log on and off, Home clears the screen.

If a serial port named COM11 can be opened the server also attaches a real ND-110/CX over its
OPCOM console at 9600 bps, for parity checks against real hardware; without the port it says so
and carries on with the emulator alone.

## The operations

| Operation | What it does |
|---|---|
| `Sync` | Loads D, P, B, L, A, T, X and STS into the current program level |
| `Step` | Runs a number of macro instructions and returns the registers; can also return every memory access the instructions made |
| `ReadMemory`, `WriteMemory` | One word, physical or through a page table |
| `ClearMemory` | Zeroes a range of physical memory, so a comparison starts from a clean image |

`ndmicrodebug-client` (the `NDMicroCpu.Debugger.Client` project) is the smallest possible client:
it connects, steps once and prints P.
