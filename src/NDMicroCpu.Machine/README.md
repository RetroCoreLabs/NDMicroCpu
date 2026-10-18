# NDMicroCpu - the console machine

The emulated ND-110 or ND-120 as a console program: it brings the CPU up through the micro code
self-test to the OPCOM prompt, plugs the INSTRUCTION-B test program onto the bus as the floppy's
binary-load device, types the load command the way an operator would, and from then on the
keyboard is the machine's console terminal.

## Run it

```powershell
# from the repository root
dotnet run --project src\NDMicroCpu.Machine
```

The executable is `NDMicroCpu.exe` (self-contained copies are on the Releases page). The
program starts as an ND-110; the CPU type is chosen in `Program.cs`.

## What you see

The console shows what the machine's terminal would show: the OPCOM prompt after the self-test,
then INSTRUCTION-B's banner and its `>` prompt. Type its commands (`HELP`, `RUN`, an area name
such as `ARGUMENT`) as on the real machine. Ctrl-C is sent to the machine, not to the program;
close the window to stop.

## Files

| File | Purpose |
|---|---|
| `Program.cs` | Creates the machine and hands the console to it |
| `TheMachine.cs` | Boots the CPU, boots a BPUN image over the bus, runs the console loop |
| `BPUN\` | The test programs that can be booted: INSTRUCTION-B, RTC, FILSYS-INV and paging-b. Copied next to the executable at build |
