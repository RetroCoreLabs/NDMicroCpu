# INSTRUCTION-B "SEGMENT" Command — Annotated Disassembly

**Program:** INSTRUCTION-B (204384B, 83.11.01), image embedded in
`ND110Support\FileLoader\BPUN_Binary\Instructions.cs`, loaded at address 0.
**Date of analysis:** 2026-07-12.

**Evidence tags used below:**
- **[R]** = execution confirmed by the live P-ring trace
  (`TestInstructionVerifyAreas.Diag_SegmentRestartRing2`, ND-110 emulator, RASK microcode)
- **[S]** = statically decoded from the image; not executed on our run (or execution
  ambiguous because the emulator exposes transient P values during skips)

Opcode encodings verified against `NDGen\nd100-definitions\specs\instructions\*.yaml`
and `nd100-markdown\docs\cpu_documentation.md`. All addresses/values octal.

---

## What SEGMENT does, in one paragraph

The handler first **detects hardware capabilities** (SEX/REX extended addressing, PCR
read-back, PGS, cache CSR) and records flags. It then fills the top 2K words of memory
(`174000-177777` — the ND-100 page-table / shadow-memory area) with a test pattern,
arms the interrupt system so that **only the illegal-instruction internal interrupt**
is enabled (IIE = 020) with a **level-14 trap handler at 054714**, and executes a
sequence of **probe instructions**, checking after each one (via `TRA IIC`) whether it
trapped as illegal. Opcode `142700` acts as a machine-type gate: if it traps
(IIC = 4 — plain ND-100/CX without the customer-specified instruction), the
segment-instruction tests are **skipped**; if it executes (ND-110 RASK implements
GECO at 142700), the handler goes on to execute **CLEPT** and an **SRB/LRB**
register-block test. Failures increment in-memory error counters (`MIN`), nothing is
printed. Either way the handler ends in a cleanup (IOF, PIE/PID/IIE := 0, registers
restored) and exits through the program's **warm-restart vector**
(`[046335] = 000267`, installed at startup) — which reprints the startup banner and
returns to the `>` prompt. **The restart is the command's normal termination on every
machine type**, not an error path.

> Correction to the first analysis (commit 1bc4319): the probe condition at 054604 is
> `SKP IF DA UEQ ST` (**UN**equal), not EQL. So IIC == 4 *skips* the tests and
> IIC != 4 *runs* them — on our ND-110 microcode the GECO path was taken and the tests
> **did** execute (ring-verified below). The observable outcome (banner + prompt, no
> error print) is unchanged either way.

---

## Data cells used by the handler

| Address | Value (image) | Meaning (from code below) |
|---|---|---|
| 054515 | 174000 | fill start = page-table/shadow area base |
| 054516 | 162000 | fill pattern seed (loaded into A) |
| 054521-054523 | 0 | T/A/D save area (restored by `LDF` in the epilogue) |
| 054524 | 0 | error/feature counter 1 (MIN'ed / STA'ed by checks) |
| 054525 | 0 | feature flag 2 (cache bit) |
| 054526 | 0 | feature flag word (BSKP'ed at 054544) |
| 054621 | 040000 | PIE mask = bit 14 (enable level 14) |
| 054622 | 054714 | level-14 handler address (installed via `IRW 160 DP`) |
| 054623 | 054554 | \| alternate return address (BFILL/alt path) |
| 054624 | 046317 | pointer: capability flag cell in the monitor area |
| 054625 | 054566 | L (return) for the probe at 054563 |
| 054626 | 054603 | L (return) for the 142700 probe |
| 054627 | 054615 | L (return) for the CLEPT probe |
| 054635-054642 | `\r "NOT POS…` | embedded text fragment (`"NOT POS[SIBLE…]`?) — **not referenced by the executed path; use unverified** |
| 046335 | 000267 (runtime) | warm-restart vector, set at program startup, never modified during SEGMENT (watched live) |

---

## Part 1 — capability detection preamble (entry at/before 054420)

```
054420  175345  BSKP ...          [S] test a capability bit
054421  124046  JMP *+46          [S] -> 054467 (alternate detection path)
054422  150406  SEX               [S] set extended (24-bit) addressing
054423  170607  SAA 207           [S] A := sign-ext(207)
054424  150103  TRR PCR           [S] write paging control register
054425  146105  COPY SZ DA        [S] A := 0
054426  150014  TRA (fc 14)       [S] A := PCR read-back (level-indexed)
054427  131027  JAZ *+27          [S] PCR read back as 0 -> 054456
054430  040074  MIN *+74          [S] [054524]++  (feature/error count)
054431  054064  LDX *+64          [S] X := [054515] = 174000
054432  044064  LDA *+64          [S] A := [054516] = 162000
054433  146101  COPY SZ DD        [S] D := 0
```

### Fill loop — writes the page-table/shadow area 174000-177777 (1024 iterations)

```
054434  022000  STD ,X 0          [R] [X] := A, [X+1] := D
054435  146401  RADD CM1 SZ DD    [R] D := D - 1        (cycling pattern)
054436  171100  SAT 100           [R] T := 100
054437  140061  SKP IF DD EQL ST  [R] pattern wrap check (D == 100?)
054440  124002  JMP *+2           [R] -> 054442
054441  146101  COPY SZ DD        [R] D := 0            (reset pattern)
054442  173402  AAX 2             [R] X := X + 2
054443  143007  SKP IF DX LST SZ  [R] skip while X < 0 (signed)
054444  124002  JMP *+2           [R] X >= 0 (wrapped past 177777) -> 054446
054445  124367  JMP *-11          [R] loop -> 054434
```

X starts at 174000 (negative as a signed 16-bit value) and the loop runs until X wraps
through 177777 to 0 — filling exactly the top 2K words.

### Paging round-trip probe

```
054446  150410  PON               [R] paging ON
054447  054046  LDX *+46          [R] X := [054515] = 174000
054450  016000  STX ,X 0          [R] [X] := X       (write through paging)
054451  150404  POF               [R] paging OFF
054452  046000  LDA ,X 0          [R] A := [X]       (read back physically)
054453  142075  SKP IF DA UEQ SX  [R] mismatch?
054454  124002  JMP *+2           [R] match -> 054456
054455  040047  MIN *+47          [S] [054524]++     (mismatch counter)
054456  150407  REX               [R] reset extended addressing
054457  170403  SAA 3             [R] A := 3
054460  150103  TRR PCR           [R] PCR := 3
054461  150010  TRA CSR           [R] A := cache status register
054462  175025  BSKP ...          [R] test a CSR bit
054463  124003  JMP *+3           [R] -> 054466
054464  170401  SAA 1             [S] flag := 1
054465  004040  STA *+40          [S] [054525] := 1
054466  124013  JMP *+13          [R] -> 054501
```

### Alternate path 054467-054500 (entered via the BSKP at 054420) — [S], skipped on our run

```
054467  150003  TRA PGS           read page status (clears it)
054470  150003  TRA PGS
054471  131003  JAZ *+3           -> 054474
054472  170401  SAA 1
054473  004031  STA *+31          [054524] := 1
054474  150010  TRA CSR
054475  175215  BSKP ...
054476  124003  JMP *+3           -> 054501
054477  170401  SAA 1
054500  004025  STA *+25          [054525] := 1
```

### Flag consolidation

```
054501  044023  LDA *+23          [R] A := [054524]
054502  131004  JAZ *+4           [R] -> 054506
054503  044023  LDA *+23          [S] A := [054526]
054504  174365  BSET ...          [S] set a bit
054505  004021  STA *+21          [S] [054526] := A
054506  044016  LDA *+16          [R] A := [054524]
054507  156410  LDX ,X I 10,B     [R] (indexed indirect load)
054510  074015  ORA *+15          [R] A |= [054525]
054511  171401  SAT 1             [R] T := 1
054512  007002  STA I *2          [R] [[054514]] = [046317] := A   (publish flags)
054513  124015  JMP *+15          [R] -> 054530
```

---

## Part 2 — interrupt arming (ring-verified)

```
054530  150005  TRA IIC           [R] read+clear any stale internal interrupt code
054531  170420  SAA 20            [R] A := 020 (bit 4)
054532  150105  TRR IIE           [R] IIE := 020  -> ONLY "illegal instruction" enabled
054533  146105  COPY SZ DA        [R] A := 0
054534  150107  TRR PIE           [R] PIE := 0
054535  150106  TRR PID           [R] PID := 0
054536  044063  LDA *+63          [R] A := [054621] = 040000 (bit 14)
054537  150307  MST PIE           [R] PIE |= 040000 -> enable LEVEL 14 (trap level)
054540  044062  LDA *+62          [R] A := [054622] = 054714
054541  153562  IRW 160 DP        [R] level-14 P := 054714 (the trap handler below)
054542  150402  ION               [R] interrupts ON
```

From here, any instruction that traps as ILLEGAL raises IID bit 4; IIE enables it;
level 14 activates and runs the handler at 054714 (which is the cleanup/exit code —
see Part 5 — reached with L pointing back into the probe sequence via the
`COPY SA DL` return-address setups below).

---

## Part 3 — probe battery

### Probe A (feature-flag gated)

```
054543  044363  LDA *-15          [R] A := [054526]  (feature flags from Part 1)
054544  175145  BSKP ...          [R] test flag bit
054545  124013  JMP *+13          [R] flag clear -> 054560 (our path)
054546  050055  LDT *+55          [S] \
054547  146164  COPY ST DL?       [S]  | alternate probe using BFILL
054550  146106  COPY SZ DT        [S]  | (skipped on our run)
054551  140130  BFILL             [S]  |
054552  124001  JMP *+1           [S]  |
054553  150005  TRA IIC           [S]  |
054554  131403  JAF *+3           [S]  |
054555  171400  SAT 0             [S]  |
054556  043046  MIN ,X I *46      [S]  |
054557  124041  JMP *+41          [S] /
```

```
054560  044045  LDA *+45          [R] A := [054625] = 054566 (return address)
054561  146154  COPY SA DL        [R] L := 054566  (where the trap handler returns to)
054562  146106  COPY SZ DT        [R] T := 0
054563  140130  BFILL             [R] executes BFILL with T=0 (zero-length field)
054564  124001  JMP *+1           [R] (2-cycle nop)
054565  150005  TRA IIC           [R] A := IIC (0 if BFILL executed cleanly)
054566  171004  SAT 4             [R] T := 4
054567  140065  SKP IF DA EQL ST  [R] did it trap as illegal?
054570  124003  JMP *+3           [R] no trap -> 054573 (our path)
054571  143500  (extended op)     [S] trap path
054572  124014  JMP *+14          [S] -> 054606
```

### Probe B — the machine-type gate: opcode 142700

```
054573  044033  LDA *+33          [R] A := [054626] = 054603 (return address)
054574  146154  COPY SA DL        [R] L := 054603
054575  146105  COPY SZ DA        [R] A := 0
054576  146101  COPY SZ DD        [R] D := 0
054577  146103  COPY SZ DB        [R] B := 0
054600  146107  COPY SZ DX        [R] X := 0

054601  142700  <probe>           [R] *** THE GATE ***
                                      ND-100/CX: ILLEGAL -> traps, IIC := 4
                                      ND-110 RASK: GECO (customer-specified
                                      instruction; dispatch = opcode>>6 + 06000 =
                                      CS 007427 -> GECO1 at CS 004000) -> EXECUTES

054602  150005  TRA IIC           [R] A := IIC (reads and clears)
054603  171004  SAT 4             [R] T := 4
054604  142065  SKP IF DA UEQ ST  [R] skip if IIC != 4  (UEQ = UNequal!)
054605  124074  JMP *+74          [S] IIC == 4 (CX): -> 054701, SKIP the tests
054606  171400  SAT 0             [R] IIC != 4 (GECO/ND-110): CONTINUE
054607  043015  MIN ,X I *15      [R] (bookkeeping counter)
```

**On our ND-110 emulator the GECO path was taken** — ring shows 054606-054607 and the
CLEPT block below executing. On a plain ND-100/CX, the JMP at 054605 skips directly to
the cleanup: the segment tests never run there.

### Probe C — CLEPT (clear segment from page tables, opcode 140301)

```
054610  044017  LDA *+17          [R] A := [054627] = 054615 (return address)
054611  146154  COPY SA DL        [R] L := 054615
054612  146107  COPY SZ DX        [R] X := 0
054613  140301  CLEPT             [R] *** executes for real *** (walks/clears page
                                      table entries - operates on the 174000-area
                                      pattern written by the fill loop in Part 1)
054614  150005  TRA IIC           [R] A := IIC
054615  131403  JAF *+3           [R] trapped? -> 054620
054616  171400  SAT 0             [R] clean: T := 0
054617  043005  MIN ,X I *5       [R] (bookkeeping counter)
054620  124023  JMP *+23          [R] -> 054643
```

Ring-verified: CLEPT executed on the emulator **without trapping** (JAF path shows the
straight-line continuation).

---

## Part 4 — SRB/LRB register-block test (ring-verified)

`SRB` (152402) and `LRB` (152600) store/load a program level's complete register block
(8 registers) to/from memory. Level field = bits 6-3; both are used here with level 0.

```
054643  171410  SAT 10            [R] T := 10 (register-block slot 8)
054644  003061  STZ ,X I *61      [R] clear result slot
054645  171411  SAT 11            [R] T := 11 (slot 9)
054646  003057  STZ ,X I *57      [R] clear result slot
054647  054056  LDX *+56          [R] X := [054725] (block buffer address)
054650  152600  LRB 00            [R] LOAD level-0 register block from [X]
054651  170777  SAA -1            [R] A := 177777
054652  171410  SAT 10            [R]
054653  007052  STA ,X I *52      [R] seed buffer slot 8 with -1
054654  171411  SAT 11            [R]
054655  007050  STA ,X I *50      [R] seed buffer slot 9 with -1
054656  054047  LDX *+47          [R] X := buffer address
054657  152402  SRB 00            [R] STORE level-0 register block to [X]
054660  171410  SAT 10            [R]
054661  047044  LDA ,X I *44      [R] A := stored slot 8
054662  171377  SAT -1            [R]
054663  140065  SKP IF DA EQL ST  [R] slot 8 must be -1 (register preserved)
054664  124013  JMP *+13          [S] mismatch -> 054677 (error count)
054665  171411  SAT 11            [R]
054666  047037  LDA ,X I *37      [R] A := stored slot 9
054667  171377  SAT -1            [R]
054670  142065  SKP IF DA UEQ ST  [R] slot 9 must have been OVERWRITTEN by SRB
054671  124010  JMP *+10          [S] still -1 -> 054701
054672  171400  SAT 0             [R]
054673  047033  LDA ,X I *33      [S]
054674  171002  SAT 2             [S]
054675  140065  SKP IF DA EQL ST  [S]
054676  124003  JMP *+3           [S] -> 054701
054677  171400  SAT 0             [S]
054700  043026  MIN ,X I *26      [S] error counter
```

---

## Part 5 — cleanup and exit (the "restart"; ring-verified)

This block doubles as the level-14 trap handler installed in Part 2 (level-14 P was
set to 054714).

```
054701  150401  IOF               [R] interrupts OFF
054702  150005  TRA IIC           [R] clear any pending internal interrupt
054703  146105  COPY SZ DA        [R] A := 0
054704  150107  TRR PIE           [R] PIE := 0
054705  150106  TRR PID           [R] PID := 0
054706  150105  TRR IIE           [R] IIE := 0
054707  034212  LDF *-...         [R] restore T,A,D from the save area (054521-)
054710  054210  LDX *-...         [R] restore X
054711  146173  COPY SX DB        [R] B := X
054712  054205  LDX *-...         [R]
054713  125214  JMP I *-...       [R] indirect jump -> common monitor-exit code

054714  ...                       [R] (level-14 trap handler entry - falls into the
                                      same cleanup when a probe traps)
```

The monitor-exit code (shared, outside the handler):

```
046411  171400  SAT 0             [R]
046412  047017  LDA ,X I *17      [R]
046413  175315  BSKP ...          [R]
046414  124004  JMP *+4           [R]
046415  170401  SAA 1             [S]
046416  005014  STA I *14         [S]
046417  124002  JMP *+2           [S]
046420  001012  STZ I *12         [R]
046421  044311  LDA *-67          [R] restore A from [046332]
046422  050311  LDT *-67          [R] restore T from [046333]
046423  054311  LDX *-67          [R] restore X from [046334]
046424  125311  JMP I *-67        [R] jump through [046335] = 000267
                                      = the program's WARM-RESTART entry
                                      (installed at startup, never modified -
                                      watched live during the whole command)

000267  170401  SAA 1             [R] warm restart: reinitializes the monitor,
000270  005126  STA I *126        [R] prints the startup banner,
000271  135126  JPL I *126        [R] ends at the ">" prompt
   ...
```

---

## Bottom line

- SEGMENT **always** terminates through the warm restart — on ND-100/CX (probe traps,
  tests skipped) and on ND-110/GECO machines (tests run) alike. The banner reprint is
  the command's designed ending, not a crash.
- On the ND-110 emulator with RASK microcode the full GECO path executed:
  page-table-area fill, PON/POF round-trip, BFILL probe, GECO (142700), **CLEPT**
  (no trap), and the **SRB/LRB** register-block test — all ring-verified.
- Failures along the way would only increment in-memory `MIN` counters
  (054524 and cells addressed via 054725); nothing is printed by this command.
- The asserted observable contract in
  `TestInstructionVerifyAreas.Area_Segment_RestartsViaIllegalOpcodeProbe`
  (banner reprint + `>` prompt + no `*** ERROR`) is the correct expected behavior.

**Open detail (marked, not verified):** the text fragment `"NOT POS…` at 054637 and the
alternate BFILL path 054546-054557 were not executed on our configuration; their exact
use is undetermined.
