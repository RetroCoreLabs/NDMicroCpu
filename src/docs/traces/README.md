# INSTRUCTION-VERIFY Reference Traces

Reference execution traces from the **ND-110 emulator** (this repository, RASK
microcode) for cross-validating **other emulators** — primarily the ND-120
(DELILAH microcode) project. Generated 2026-07-12 from Norsk Data's own CPU
verification program INSTRUCTION-B (204384B, issued 83.11.01), whose 13 testable
area commands all pass on this emulator (see
[`../INSTRUCTION-VERIFY-Test-Campaign.md`](../INSTRUCTION-VERIFY-Test-Campaign.md)).

## Files

One file per INSTRUCTION-VERIFY area command:

```
TRACE-INSTRUCTION-VERIFY-ARGUMENT.md            SAA/AAA/SAX/AAX/SAT/AAT/SAB/AAB
TRACE-INSTRUCTION-VERIFY-MEMORY-REFERENCE.md    STZ/STA/.../LDA/.../ADD/SUB/AND/ORA/MPY/LBYT/SBYT
TRACE-INSTRUCTION-VERIFY-SEQUENCE.md            JMP/JPL/JAP/JAN/JAZ/JAF/JXN/JPC/JNC/JXZ/SKP
TRACE-INSTRUCTION-VERIFY-REGISTER-OPERATIONS.md RADD/RSUB/RAND/RORA/REX/SWAP/COPY/RMPY/RDIV/MIX3/EXR
TRACE-INSTRUCTION-VERIFY-BIT-OPERATIONS.md      BLDA/BSTA/BSTC/BLDC/BANC/BORC/BAND/BORA/BSKP/BSET
TRACE-INSTRUCTION-VERIFY-SHIFT-INSTRUCTIONS.md  SHA/SHD/SHT/SAD
TRACE-INSTRUCTION-VERIFY-32-BITS-FLOATING.md    DNZ/NLZ/FMU/FDV/FAD/FSB
TRACE-INSTRUCTION-VERIFY-48-BITS-FLOATING.md    (this machine is configured 32F - see the file's summary)
TRACE-INSTRUCTION-VERIFY-PRIVILEGED.md          LRB/SRB, MST/MCL PID/PIE, TRA/TRR STS
TRACE-INSTRUCTION-VERIFY-BYTE-STRING.md         BFILL/MOVB/MOVBF
TRACE-INSTRUCTION-VERIFY-ND100-24BIT.md         SEX/REX/EXAM/DEPO/LDATX/STATX/LDDTX/STDTX/LDBTX
TRACE-INSTRUCTION-VERIFY-BCD.md                 (program reports "(NIY)" - not implemented in this 1983 version)
TRACE-INSTRUCTION-VERIFY-ND100-CX.md            MOVEW/TSET/RDUS
TRACE-INSTRUCTION-VERIFY-STACK.md               INIT/ENTER/LEAVE/ELEAV
TRACE-INSTRUCTION-VERIFY-RUN.md                 the all-in-one command under full stress
                                                (levels 10-14 live); arms strictly at the
                                                first PIL 1-9 instruction, so its window
                                                shows test instructions interleaved with
                                                framework/clock code
```

(SEGMENT is intentionally absent: on ND-110-generation microcode the program's
own 142700 probe finds GECO implemented and the command ends in a designed warm
restart — see [`../SEGMENT-Command-Disassembly.md`](../SEGMENT-Command-Disassembly.md).)

## Format

All values are **octal**.

Each file has a header, then one `###` section per macro instruction:

```
### #7  `037001 : 140065`  PIL=0
`A=037031 D=... T=037031 X=... B=... L=... P=037002 STS=... R1=... ... Q=... F=... GPR=... LC=...`

| csar | symbol | changed | microcode |
|---|---|---|---|
| 001042 | WAIT1   | F=110200          | `WAIT1: ALUD,NONE IDBS,ALU T,JMP T,PUSH PVCHK;` |
| 001070 | CHKIT+6 | R3=000001 LC=000012 | `B,R3 ALUF,PASSB ALUM,MIC ALUD,SLD IDBS,ALU T,NEXT T,POP LCOUNT;` |
```

- **Section head**: `fetch-address : opcode-word`, current program level (PIL),
  and the COMPLETE register state at fetch: `A D T X B L P STS` (current
  level's bank), scratch `R1-R7` (current level), Am2901 `Q` register, ALU
  output latch `F`, general-purpose shift register `GPR`, loop counter `LC`.
- **Micro rows**, one per microinstruction executed by that macro instruction:
  - `csar` — ND-110 RASK control-store address (**not portable** to ND-120)
  - `symbol` — from `ND110Compile\uCode\ND-110-RASK.SYMBOLS.TXT`: exact label,
    or `LABEL+n` (octal offset from the nearest preceding label). **This is the
    portable cross-reference**: the ND-120 DELILAH listing has different
    addresses but (mostly) the same routine labels.
  - `changed` — only the watched registers that changed during that
    microinstruction (the section head always carries full state)
  - `microcode` — the original RASK listing source text for that address

### Capture window

The detailed window **arms at the first macro instruction executed on a test
level (PIL 1-9)** — i.e. the first instruction under test — and records the
next 400 macro instructions across all levels: the test instructions, the
framework's runner/check code (level 0), and any level-13 clock interrupts
that interleave. The console/dispatch preamble (~10k instructions) is skipped;
the run summary at the bottom of each file reports the skipped count, total
instruction count to `== END OF TEST ==`, and the console output.

## Regenerating

```powershell
dotnet test TestND110\TestND110CPU.csproj --filter "Name~GenerateTrace"
# or a single area:
dotnet test TestND110\TestND110CPU.csproj --filter "Name~GenerateTrace" --filter "Name~ARGUMENT"
```

Generator: `TestND110\Boot\InstructionVerifyTraceGenerator.cs` ([Explicit],
one `[TestCase]` per area). Knobs: `MACRO_CAP` (detailed instructions, 400) and
`TICK_CAP` in the same file.

## Cross-emulator validation workflow (ND-120)

1. Start with **ARGUMENT** — it already passes on the ND-120, so a clean
   comparison calibrates the method itself (pure register arithmetic, no
   memory addressing, no I/O).
2. Comparison rules for the validating side (human or LLM):
   - **Macro rows must match exactly**: fetch address, opcode, full register
     state including PIL and R1-R7.
   - **Micro rows are behavioral reference**: compare per-SYMBOL (what each
     labeled routine step changes), never per-address.
   - Level-13 clock instructions in the trace are real and timing-dependent —
     report them separately from genuine register divergences.
3. On divergence, report: first diverging macro address, register, expected vs
   actual, and the microcode routine (by symbol) that produced the value.
4. Then move to a failing area; STACK and BYTE-STRING exercise the LC-indexed
   register-file ring buffers (LVSWP, MOVB fast word loop) that were the
   subtlest bugs found in this emulator — see the campaign document.
