# INSTRUCTION-VERIFY Test Campaign — Emulator Bugs Found and Fixed

**Date:** 2026-07-11 / 2026-07-12
**Test program:** INSTRUCTION-B (204384B, issued 83.11.01) — Norsk Data's own CPU
instruction verification program, embedded in the emulator as
`ND110Support\FileLoader\BPUN_Binary\Instructions.cs` and started with `0!` from OPCOM.
**Result:** all 14 test areas accounted for — 13 pass their level-1 test runs, SEGMENT's
restart proven to be correct ND-110 behavior. Full suite: **194/194 green**.

Everything in this document is **verified** against one of these ground-truth sources
(no guesses; where behavior could not be verified it is explicitly marked):

| Source | Used for |
|---|---|
| `ND110Compile\uCode\ND-110-RASK.LISTING.TXT` | microcode control flow (CS addresses cited below) |
| ND-06.031.1EN ND-110/ND-120 Microprogrammer's Guide (a PDF kept, uncommitted, next to the ND-100 film in the AnimationVideos repository: `films\nd100-microword\*.pdf`) | LDLC/LCOUNT/command token semantics |
| ND-120 DELILAH Verilog (`Verilog` in the nd-120 repository) | LCZ comparator, ALU control gates |
| nd100x C reference via `Emulated.Tests\ND100\ND100_ByteInstructionTests.cs` in the RetroCore repository | MOVB/MOVBF/BFILL end-state semantics |
| `nd100-definitions\specs\instructions\*.yaml` in the NDGen repository | exact macro opcode encodings |
| The INSTRUCTION-B image itself (BPUN payload disassembled by hand) | what each test actually checks |

---

## Methodology

Per-area testing instead of the all-in-one `RUN` command (user's suggestion): each of the
program's special commands (`ARGUMENT`, `SEQUENCE`, `STACK`, …) runs one instruction
group's tests on level 1 and prints `== END OF TEST ==` on success or `*** ERROR` /
`"..." FAILED (...)` lines on failure. The harness
(`TestND110\Boot\TestInstructionVerifyAreas.cs`) boots to OPCOM, starts the program,
types the command, waits for the level-1 result, then sends ESC to abort the endless
level loop.

For every program-reported failure the same loop was applied:

1. **Locate the failing check in the image** — find the error string, walk back to the
   test descriptor / check code, disassemble by hand
   (scratchpad tool parses the BPUN payload: load address 0, big-endian words).
2. **Distill it into a fast unit test** that reproduces the exact register/memory setup
   (67 ms instead of a 2-minute boot) — these tests stay in the suite as regressions.
3. **Micro-trace** the emulator cycle by cycle (`CPU.UnitTest_Fetch()` +
   `TickCpu()` loops logging CSAR, ALU F/Q, LC, flags) to find the divergence.
4. **Verify the intended behavior in a ground-truth source**, fix, re-run everything.

---

## Bug 1 — Software program levels 1–9 never entered (CHKIT loop-counter exit)

**Symptom:** every test in `RUN` failed with
`** ERROR **. WRONG VALUE OF P-REG AFTER TEST. LEVEL: 1` — e.g. `"SAA 0" FAILED (SAAOP)`.

**How the program tests:** for every instruction it writes the test address into the
target level's P (`IRW 10 DP`), activates the level (`MST PID` bit 1), lets the test run
to its `WAIT`, then reads the level's P back (`IRR 10 DP`) and compares it against the
address after the WAIT (test descriptor at image 036774-037040, runner at 022542,
checker `SKP IF DA EQL ST` at 037001).

**Root cause:** software levels 1–9 are entered purely by microcode — the CHKIT scan
(RASK CS 001062-001071) shifts PID∧PIE left one bit per pass (CS 001070, `ALUD,SLD` +
`LCOUNT`, exit condition `COND,F11`) and **the loop counter's exit value IS the found
level number** (`% NEW LEVEL -> Q`, CS 001071). The emulator decremented LC on every
LOOP pass *including the final condition-true exit pass* (which runs one cycle late due
to the documented condition-detection delay), so every level came out one too low:
level 1 was delivered as "level 0" = no switch, ever. Trace evidence: 480 PIL
transitions during RUN, none to levels 1–9.

**Fix (`Cpu.cs`, `Execute_4_ConditionControl`):** the LCOUNT decrement only happens on
repeat (condition-false) passes — matching the token's own definition
(`ND110Tokens.cs`: "COUNT LOOP-COUNTER : IF FALSE 'RETURN'&'HOLD'"). LCZ-terminated
loops are unaffected (decrement at LC=0 was already a no-op).

**Regression test:** `TestND110\NDCPU\TestProgramLevels.cs` —
`MstPid_EntersLevel1_AndWaitReturnsToLevel0` distills the exact SAAOP scenario
(IRW / TRR PIE / ION / MST PID → level 1 runs SAA+WAIT → back to level 0 with saved
P = WAIT+1, read back via IRR).

---

## Bug 2 — Double shifts: rotate hardwired into the end input (SAD2OP)

**Symptom:** `"SAD" FAILED (SAD2OP)` — image test 035004: A=100000, D=0, `SAD 1`
must give A=0, D=0, M=1.

**Root cause:** in the ALU shift-linkage block (`Cpu.cs`), the double-shift cases
(ALUD i6=0) hardwired the 32-bit *rotate* linkage: on left shifts `QRI = RLI`
unconditionally fed A's bit 15 back into D's bit 0. A plain (arithmetic) `SAD` must
shift zeros in at the bottom; only `SAD ROT` wraps.

**Fix:** both double-shift branches now select the END input by shift mode
(plain = zero / sign-extend, ROT = wrap, ZIN = zero, LIN = M flag) while keeping the
fixed INTERNAL Q↔RAM link. Note: the internal link is load-bearing elsewhere — the
CHKIT scan's `ARG 100013` deliberately keeps Q15=1 as the scan terminator feeding RAM0.

**Fallout fixed in the same pass:** `TestInstructions.ShiftExample3` encoded plain
`SAD 20` (0154620) but asserted the reference manual's example that explicitly says
"Rotate shift is specified" — corrected to `SAD ROT 20` (0155620). It had only passed
because of this bug.

---

## Bug 3 — M flag re-clocked by the non-shifting extra pass (SAD2OP's BSKP check)

**Symptom:** after fixing bug 2, SAD2OP still failed — A and D correct, but
`BSKP ... SSM` saw M=0 instead of 1.

**Root cause:** in `ALUM,IR-SHIFT` mode the documented extra microinstruction after the
loop counter reaches 0 ("does not shift the ALU result", ND-06.031.1EN) decays a double
shift's `ALUD_SLD` (110) to 010 = `ALUD_B_YA`. The M-update condition
(`i8==1 || ALUD_B_YA`, added 2023-09-10 in commit f84be75) fired on that pass and
re-clocked M from the *unshifted* result's edge bit — destroying the real disappearing
bit captured one cycle earlier.

**Fix:** M only clocks while an actual shift happens (`i8 == 1`).

---

## Bug 4 — UP0 (shift direction) latched instead of pipelined (SAD4OP)

**Symptom:** `"SAD SHR" FAILED (SAD4OP)` — image test 035152: A=100000, D=0,
`SAD SHR 1` must give A=140000 (sign extend), D=0, M=0. The emulator returned
D = a copy of A.

**Root cause:** the shared shift microinstruction (RASK CS 000237,
`B,A ALUF,PASSB ALUM,IR ALUD,SRD`, Q holding D) derives ALU-I7 from **/UP** each cycle.
A right shift loads LC = −n and counts up; when LC reaches 0, /UP flips so the extra
pass decays to the harmless `ALUD_B_YA` for BOTH directions. The emulator latched UP0
once at `LatchLC`, so the right-shift extra pass decayed to 000 = `ALUD_Q` and executed
`Q := F` — overwriting D. (A *live* UP0 is wrong the other way: the shift pass itself
already sees LC=0 and would shift left.)

**Fix (`MIC.cs`):** UP0 is re-sampled once per microinstruction in `clockLZ0` from the
pre-count counter value — the same one-cycle pipeline as LCZ.

**Regression tests:** `TestND110\NDCPU\TestShiftSAD.cs` — SADOP/SAD2OP/SAD4OP mirrors
plus internal-link checks, and a `[Explicit]` micro-trace diagnostic.

---

## Bug 5 — STS static overflow (O) not sticky (PRIVILEGED area)

**Symptom:** `*** ERROR *** MCL/MST/TRA/TRR FAILED`, repeating. P-ring diagnostic
located the failing check: image 036351-036360 — `MST STS 377` then, after an
intervening `COPY SZ DA`, `TRA STS & 0257` must still equal 0257; it read 0217
(bit 5 = O missing).

**Root cause:** `STSOperation.ENABLE_SET_COQ` (`STS,EA`) assigned
`O := OVERFLOW_OUT` directly. O is the STATIC overflow — the token spec quoted in the
code itself says `OVF \/ STS O -> STS O`: set on overflow, sticky until cleared
explicitly (TRR/MCL STS). Any non-overflowing ROP was silently clearing it.

**Fix:** `O := OVERFLOW_OUT || O`.

**Regression tests:** `TestND110\NDCPU\TestPrivilegedRegisters.cs` — full
TRR/TRA/MST/MCL ladders for PID, PIE and STS (the STS ladder includes the exact
`COPY SZ DA` between checks). Note: the PID/PIE ladders passed *before* this fix —
proof that the area's failure was STS, not the Am2914 paths.

---

## Bug 6 — LC operand path not pipelined (BYTE-STRING area, MOVB) — the big one

**Symptom:** `"MOVB" FAILED / FIRST ERROR AT : 000000 / NO. WRONG WORDS: 000377 /
EXPECTED 025052 / FOUND 062121`. Live capture of the descriptors
(diag hooked at the MOVB in the image, P=043003): source A=056121 D=001672 (954 bytes),
dest X=062121 T=007776 (4094 bytes) — and 062121, the FOUND value, is the destination
*address*.

**Root cause chain (micro-traced in `TestByteString.Diag_MovbMicroTrace`):**

1. Small MOVBs (≤ a few words) use the byte loop and were correct. Large ones take the
   **fast word loop** (FWL, RASK CS 001705-001725): bursts of 8 words are read into
   register-file registers **indexed by the loop counter** (`B,LC` at CS 001710) and
   written back out via `A,LC` (CS 001714) plus a fixed prime `A,7` (CS 001725) whose
   IDB value doubles as the `WRRQ` write address (R3 placed on IDB by CS 001723).
2. With the emulator's *live* LC as operand, the reads filed words into registers
   6..0 while the write prime fetched **register 7 = the user X register** — the
   destination address value observed in the copied data. One register short, one
   word of garbage per burst.
3. **Ground truth** (ND-06.031.1EN, command 17 LDLC): *"The modified loop counter will
   not be available as an A-operand until the next microinstruction has been
   completed."* — the operand path sees the counter through a pipeline latch:
   instruction N uses the value the counter had at the START of instruction N−1.
4. That one rule aligns every LC-indexed ring in the microcode simultaneously:
   FWL reads cover 7..0, FWL writes cover prime-7 + 6..0, and LVSWP's level-switch
   register save/restore (CS 001121/001126, operands 7,7,6..0 over nine passes)
   finally includes **register 7 (X)** — which the live-LC emulator had silently
   been skipping on every level switch.

**Fix:** new `MIC.LC_operand` latch, clocked once per microinstruction in
`Cpu.InternalExecuteMicroCode` with the pre-modification counter value; the three
`A,LC` / `B,LC` operand-select cases read the latch instead of the live counter.

**Regression tests:** `TestND110\NDCPU\TestByteString.cs` — MOVB reference semantics
(always-skip return, min(srcLen,destLen), D/T end state per the nd100x C reference)
plus the exact 954-into-4094-byte area case with canary words. This fix also made the
STACK area pass (INIT/ENTER/LEAVE/ELEAV use LC-indexed register access too).

---

## Non-bugs found on the way

### BCD and ND100-CX "failures" — harness false positives
Both areas pass. A fast area reaches the program's looping stage before ESC lands, and
the stress levels legitimately print `== IOX-ERROR STARTED ==` — the harness's naive
`Contains("ERROR")` matched it. Markers corrected to `*** ERROR` / `FAILED`, and
`THE TESTS ARE NOW LOOPING` recognized as a success marker.

### MOVB "wrong P" — correct skip return
`MOVB`/`MOVBF`/`BFILL` always take the **skip return** (P+2). Verified against the
nd100x C reference via RetroCore's `ND100_ByteInstructionTests`; the first draft of the
unit test wrongly expected P+1.

---

## SEGMENT — restart verified as CORRECT behavior (not a bug)

**Observed:** typing `SEGMENT` reprints the full startup banner and returns to `>`.

**Verified explanation** (full annotated disassembly with all memory addresses and
instructions: `src\docs\SEGMENT-Command-Disassembly.md`):

1. The handler detects hardware capabilities (SEX/REX, PCR, PGS, CSR), fills the
   page-table/shadow area 174000-177777 with a pattern, arms IIE = 020 (illegal
   instruction only) with a level-14 trap handler, then runs a **probe battery**.
2. Opcode **142700** at 054601 is the machine-type gate, checked with
   `TRA IIC ; SAT 4 ; SKP IF DA **UEQ** ST` (054602-054605 — UEQ = UNequal):
   *IIC == 4* (plain ND-100/CX, where 142700 is illegal) **skips** the segment tests;
   *IIC != 4* runs them. ND-110 RASK microcode dispatches 142700
   (opcode>>6 + 06000 = CS 007427) to **GECO**, the customer-specified instruction
   (GECO1 at CS 004000), which executes normally — so on our emulator the tests RAN:
   **CLEPT** (054613, completed without trapping, ring-verified) and the **SRB/LRB**
   register-block test (054643-054676).
   *(Correction: commit 1bc4319's message/comment read the condition as EQL and
   concluded the tests were skipped — the UEQ decode and the P-ring prove the
   opposite. The observable outcome is the same.)*
3. On BOTH machine types the handler ends in the same cleanup (IOF, PIE/PID/IIE := 0,
   registers restored, 054701-054713) and exits through the **warm-restart vector**
   `[046335] = 000267` (installed at startup, never modified — watched live,
   `Diag_SegmentContinuationWatch`). The banner reprint is the command's DESIGNED
   termination; failures only increment in-memory `MIN` counters, nothing is printed.

Corroboration: ILLIN/PRIVI microcode entries never executed during SEGMENT
(`Diag_SegmentIllegalInstruction`, 0 hits — consistent with GECO executing);
P-ring shows 054434..054714 → 046411 → 000267 → banner
(`Diag_SegmentRestartRing2`). The behavior is asserted by
`Area_Segment_RestartsViaIllegalOpcodeProbe` (banner reprint + prompt + no error
print).

---

## Final state

| Area | Result |
|---|---|
| ARGUMENT | pass |
| MEMORY-REFERENCE | pass |
| SEQUENCE | pass |
| REGISTER-OPERATIONS | pass |
| BIT-OPERATIONS | pass |
| SHIFT-INSTRUCTIONS | pass (bugs 2-4) |
| 32-BITS-FLOATING | pass |
| PRIVILEGED | pass (bug 5) |
| BYTE-STRING | pass (bug 6) |
| ND100-24BIT | pass |
| BCD | pass (program's own "(NIY)"; harness marker fix) |
| ND100-CX | pass (harness marker fix) |
| STACK | pass (bug 6) |
| SEGMENT | pass — verified deliberate restart; GECO path ran CLEPT + SRB/LRB (see `SEGMENT-Command-Disassembly.md`) |

**Test suite:** 194/194 (180 unit tests + 14 area system tests).
48-BITS-FLOATING is intentionally not in the matrix: the program banner reports this
configuration as "32 BIT FLOATING FORMAT", so the 48-bit list is not the configured
instruction set (OTYPE 12, ND-100 CX 32F).

**Commits (chronological):**

| Commit | Content |
|---|---|
| `f29b3b3`…`94a102f` | (previous round) RDVC group signal, phantom 0x0F dispatch, LCZ/CLFF, NATS fail-fast, interrupt-return P off-by-one |
| `b2761c7` | bug 1 (CHKIT LC exit), bugs 2-4 (SAD), TestProgramLevels/TestShiftSAD/TestPrivilegedRegisters/TestInstructionVerifyAreas, `CPU.UnitTest_Fetch()` |
| `69a030a` | bug 5 (sticky O) + STS ladder test + PRIVILEGED P-ring diag |
| `b697de7` | bug 6 (`MIC.LC_operand`) + TestByteString + patient-ESC harness |
| `0488a6e` | harness error markers, SEGMENT as (then) open question |
| `1bc4319` | SEGMENT verified: GECO probe analysis, `Area_Segment_RestartsViaIllegalOpcodeProbe` |

## Reference traces for cross-emulator validation

The per-area work above is packaged as machine-comparable reference traces for
validating OTHER emulators (primarily the ND-120/DELILAH project):
[`traces\README.md`](traces/README.md) describes the format; one
`traces\TRACE-INSTRUCTION-VERIFY-<AREA>.md` per command. Each trace records,
per macro instruction, the octal address/opcode and the full register state
(A D T X B L P STS, scratch R1-R7, ALU Q/F, GPR, LC), and per microinstruction
the control-store address, the **symbol** from `ND-110-RASK.SYMBOLS.TXT`
(portable across microcode generations - DELILAH addresses differ, labels
mostly match), register deltas and the RASK source line. The detail window
arms at the first instruction executed on a test level (PIL 1-9). Generator:
`TestND110\Boot\InstructionVerifyTraceGenerator.cs`
(`dotnet test --filter "Name~GenerateTrace"`).

**Known remaining gaps (documented, not regressions):**
- `MIC.LatchLC` approximates LCZ with compare nibble LCC = 0 (full non-zero LCC
  decoding not implemented; no current microcode path exercises it — see the ground
  truth comment in `MIC.cs`).
- `QSEL_1_0` Q-register mux port from CGA Verilog still pending (task list #9); the
  pre-port ND-110-schematic logic is in place and validated by the suite.
