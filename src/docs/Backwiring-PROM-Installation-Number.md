# The back-wiring PROM / installation number (VERSN, IDBS,INR = 35 octal)

**Repo:** this repository, NDMicroCpu (the ND-110/ND-120 *microcode interpreter* — the
"oracle" that RetroCore's ND-100/ND-110 test suite validates against).

**Scope:** what the back-wiring PROM is, how the `VERSN` instruction reads it out of the
microcode, and — importantly — **what this interpreter does and does not model**.

Every claim below is either (a) quoted from a file with `file:line`, or (b) explicitly tagged
**INFERENCE**. Nothing else is asserted.

---

## 1. Summary of the mechanism

The installation number ("back-wiring PROM") is a **16-byte PROM on the backplane**, *not* a
device on the ND bus. It is not reachable by `IOX`, not memory-mapped, and not reachable by
`TRA`/`TRR`. The **only** path to it is the `VERSN` instruction (opcode **140133 octal**),
which reads it through a dedicated IDB source, **`IDBS,INR` = 35 octal**.

The PROM's byte address is the **current program level, PIL[3:0]**, which `VERSN` first loads
from `A` bits 8-11 with `COMM,LDPIL`.

---

## 2. The VERSN microcode, word by word (ND-110 RASK)

Source in this repo: `microcode\ND-110-RASK.LISTING.TXT` in the NDMicroAsm repository
lines **105-128**, control-store addresses **20-26 octal**:

```
0105  000020  % READ VERSION INSTRUCTION
0107  000020  VERSN:    B,T                 ALUF,PASSD          ALUD,B
0108  000020            IDBS,ARG            T,NEXT              T,HOLD
0109  000020            13;
0111  000021            ALUF,PASSD          ALUD,Q
0112  000021            IDBS,STS            T,NEXT              T,HOLD;
0114  000022            A,A                 ALUF,PASSA          ALUD,NONE
0115  000022            IDBS,ALU            COMM,LDPIL          T,NEXT     T,HOLD;
0117  000023            B,D                 ALUF,PASSD          ALUD,B
0118  000023            IDBS,INR            T,NEXT              T,HOLD;
0120  000024            ALUF,PASSQ          ALUD,NONE
0121  000024            IDBS,ALU            COMM,LDPIL          T,NEXT     T,HOLD;
0123  000025            B,17                ALUF,PASSD          ALUD,Q
0124  000025            IDBS,BARG           T,NEXT              T,HOLD;
0126  000026            B,A                 ALUF,XORDQ          ALUD,B
0127  000026            IDBS,ALD            T,JMP               T,HOLD
0128  000026            CONT;
```

| CS | What it does |
|---|---|
| 20 | `T := 13` (octal) — the **microprogram version constant**, taken straight from the `IDBS,ARG` literal in the microcode. |
| 21 | `Q := STS` — saves the whole status word, **including the current PIL**, so it can be restored. |
| 22 | `PIL := A` (via `A,A` + `IDBS,ALU` + `COMM,LDPIL`) — the CPU is *temporarily switched* to the level named by `A` bits 8-11. **This nibble is the PROM address.** |
| 23 | `D := IDBS,INR` — **the back-wiring PROM byte** is enabled onto the IDB and latched into `D`. |
| 24 | `PIL := Q` — original STS/PIL restored. |
| 25 | `Q := 17` (octal, via `IDBS,BARG`). |
| 26 | `A := ALD XOR 17` — the automatic-load descriptor, inverted in its low nibble, then `CONT`. |

Note the version constant differs per microcode build. The ND-120 **revision L** build uses
`100014` (`microcode\ND-120-DELILAH-L.LISTING.TXT:110` in the NDMicroAsm repository,
commented `% L-VERSION`): bit 15 set = **ND-120**, low bits `014` octal = **revision L**.

### Revision-letter encoding

The revision letter is the **plain alphabet position with "I" NOT skipped**: A=1 … K=11, L=12.
So `13` octal = 11 decimal = **K** (the ND-110 RASK build), and `014` octal = 12 decimal =
**L**. SINTRAN's documented "minimum microprogram version 013" floor therefore means "at least
revision K".

---

## 3. `IDBS,INR` = 35 octal — what it physically is

Verified in the ND-120 FPGA repo (the nd-120 repository):

- `Verilog\DECODE-GateArray\DGA\circuit\DECODE_DGA_IDBS.v:32`
  `output RINRN, //! Read Installation Number from B-PLUG (RINR) - IDBS=35`
- `Verilog\CPU-BOARD-3202\circuit\ND3202D.v:83` — the CPU board takes
  `input [7:0] INR_7_0 //! INR 7:0` **in** from the B-plug …
- `Verilog\CPU-BOARD-3202\circuit\ND3202D.v:89` — … and drives
  `output [3:0] PIL, // XPIL3=B-C8, PIL2=B-B12. PIL1=B-B10, PIL0=B-B9` **out** to the B-plug.
- `Verilog\ND120_CORE.v:748` records the pin mapping of the eight data bits:
  `// INR 7:0, signal B-> B15, B4, B5, B17, B8, B7, B13, B6. (Installation number, read using IDB Source = 035)`

So on real hardware the CPU board **exports PIL and imports 8 bits**; the decode is a
combinational 16 x 8 PROM sitting on the backplane. The ND-120 FPGA models exactly that, at the
backplane level, in `Verilog\ND120_CORE.v:693` (`BACKWIRING_PROM ... BACKPLANE_INR_PROM`).

### PIL-as-address: **INFERENCE, not read from a schematic**

No backplane schematic for the PROM was found in any of these repos, and **no dump of a real
PROM exists anywhere**. That PIL[3:0] is the PROM address is a **strong inference** resting on
three independent legs:

1. The microcode sequence itself: `VERSN` has no other reason to load PIL from `A` bits 8-11
   immediately before `IDBS,INR` and restore it immediately after (CS 22 / 23 / 24 above).
2. SINTRAN's own comment says the byte number *is* the level (§5).
3. The CPU board exports `PIL[3:0]` and imports `INR_7_0` on the same plug — a 4-bit address
   out, an 8-bit byte in (`ND3202D.v:83,89`).

The ND-120 Verilog author reached the same conclusion and flagged it with the same caveat
(`Verilog\ND120_CORE.v:685-687`: *"The address is PIL[3:0] - STRONG INFERENCE, not read from a
backplane schematic (none was found)"*). Companion doc:
`Verilog\docs\backwiring-prom-installation-number.md` in the nd-120 repository.

---

## 4. The LDPIL settle bug, and the revision-L `COMM,SLOW` fix

`COMM,LDPIL` is documented in this repo as *not* taking effect immediately:

> `src\NDMicroCpu.Core\Enums.cs:22-26`
> *"Latch the upper byte of the STS register from the upper byte of IDB. The new PIL will not be
> available as the A-operand until the next microinstruction has been completed."*

In the ND-110 RASK sequence, `IDBS,INR` is sampled in the **very next microword** after the
`LDPIL`. The new PIL has not settled, so the PROM is addressed by the **current (old) PIL**, not
the requested byte index. That is the bug.

**ND-120 revision L fixes it in microcode** by inserting a padding word between the two:

```
0120  000023  % L-ONLY WORD: inserted in version L (not present in the K print)
0121  000023                                                  ALUD,NONE
0122  000023                              COMM,SLOW           T,NEXT      T,HOLD;
0124  000024          B,D                 ALUF,PASSD          ALUD,B
0125  000024          IDBS,INR                                T,NEXT      T,HOLD;
```
(`microcode\ND-120-DELILAH-L.LISTING.TXT:120-125` in the NDMicroAsm repository)

Revision K (`ND-120-DELILAH-K.LISTING.TXT`) and the ND-110 RASK do **not** have this word.

### How SINTRAN works around it

`SINTRAN\NPL-SOURCE\NPL\PH-P2-OPPSTART.NPL:3534-3570` in the NDInsight repository (`GCPUNR`) executes
`VERSN` **once on each of interrupt levels 0-7** to read bytes 0-7, with the comment:

> *"DUE TO ERROR IN MICROPROGRAM THE VERSN INSTRUCTION HAS TO BE EXECUTED ON THE LEVEL
> CORRESPONDING TO THE BYT NO. TO BE READ."*

i.e. SINTRAN makes the *actual* PIL equal the wanted byte index, so the stale-PIL read returns
the right byte anyway. This is direct corroboration that the PROM address is the PIL.

---

## 5. Decoded PROM byte layout

SINTRAN's `GCPUNR` reads only bytes **0-7**. Bytes 8-15 are never read by SINTRAN and their
meaning is unknown.

| Bytes | Meaning | "absent" sentinel |
|---|---|---|
| 0 (MSB), 1 (LSB) | `SYSNO` -> SINTRAN banner **CPU NUMBER** | word = -1 (all ones) |
| 2, 3 | `HWINFO(2)` system type -> banner **CPU TYPE** | word = -1 |
| 4 (high byte), 5 | `NLEGU` number of legal users | byte 4 = 0377 octal |
| 6, 7 | signature `INF3`, **must be 52652 octal = 21930 = 0x55AA** | if wrong: see below |
| 8-15 | unknown; SINTRAN never reads them | — |

### The 0x55AA signature gate

Bytes 6/7 form a fixed magic word **52652 octal / 0x55AA**. If it does not match, `GCPUNR`
**exits immediately** and *everything above is ignored* — SYSNO, HWINFO(2) and NLEGU keep
whatever values the system-generation tool baked into the SINTRAN image. So a PROM image that
lacks the signature is functionally identical to "no PROM fitted".

---

## 6. What THIS interpreter models — and what it does not

### 6.1 IDB source 35 *is* decoded, but it is a stub

The enum entry exists: `src\NDMicroCpu.Core\Enums.cs:609-614`
(`/// 35 oct ... Read installation number` -> `RINR`).

The dispatch in `LatchDataToIDB` handles it — `src\NDMicroCpu.Core\Cpu.cs:3468-3470`:

```csharp
case IDBSource.RINR: // Read installation number
    hw.idb.Latch(INSTALLATION_NUMBER);
    break;
```

`INSTALLATION_NUMBER` is a single **16-bit constant**, not an array:

- `Cpu.cs:29` — `readonly ushort INSTALLATION_NUMBER = 1234;`
- `Cpu.cs:129,133-137` — the constructor takes `byte InstallationNumber = 0` and only overwrites
  the field when it is non-zero, with the comment *"Set unique installation number. Normally read
  from a chip on the backplane."*

**No caller in the solution passes a value.** Every construction site uses the default:
`ND110Machine\Program.cs:22`, `ND110Machine\TheMachine.cs:17`,
`ND110Debugger\ND110DebuggerServer\Machine.cs:13` (this is the one the oracle server uses),
`ND110Cli\CliEngine.cs:37`, and all `TestND110` sites. So in practice
`INSTALLATION_NUMBER` is **always 1234 decimal = 0x04D2 = 002322 octal**.

### 6.2 There is no PROM, no array, no PIL addressing

- There is **no 16-byte array** anywhere in `ND110CPU\`.
- `IDBSource.RINR` **never reads PIL**. The byte index is simply not part of the computation.
- Consequently the whole `LDPIL` / restore dance in CS 22-24 is executed faithfully as *STS
  manipulation*, but has **zero effect on the value returned at CS 23**.

### 6.3 Is `COMM,LDPIL` modelled? Yes — but with no settle delay

`Cpu.cs:1346-1356` implements `Commands.LDPIL` as an immediate write of the IDB high byte into
the STS high byte (`hw.alu.LoadSTS_Hi(hw.idb.ReadHI())`, and
`ND110CPU\BUFALU.cs:144-148` for `LoadSTS_Hi`). The A-operand PIL selectors read the **live**
`hw.alu.PIL` with no pipeline latch (`Cpu.cs:2991` for `A,PIL`, `Cpu.cs:3019` for `A,PIL XRF`) —
contrast `A,LC`, which *does* use a pipelined latch (`Cpu.cs:3008`, `hw.mic.LC_operand`).

So the interpreter does **not** reproduce the "new PIL not available until the next
microinstruction has completed" behaviour described in its own `Enums.cs:24`. In this
interpreter, `LDPIL` is visible immediately.

### 6.4 `COMM,SLOW` is a no-op

`Cpu.cs:1592-1596`: `case Commands.SLOW:` has a `// TODO` and an empty body. This is harmless
for correctness here (it only controls microcycle length), but it means the rev-L fix word is
executed as a pure delay slot with no observable effect.

### 6.5 Net effect on `VERSN` in this oracle

Executing `VERSN` against this interpreter yields:

| Register | Modelled correctly? |
|---|---|
| `T` (microprogram version) | **Yes** — comes from the `IDBS,ARG` literal in the loaded microcode. |
| `A` (= ALD XOR 17) | **Yes** — real `IDBS,ALD` path. |
| `STS`/`PIL` save+restore | **Yes** (though without the settle delay). |
| `D` (installation-number byte) | **NO.** Always `1234` decimal, for every byte index 0-15, on every program level. |

**Consequence, stated plainly:** the `D` result of `VERSN` from this oracle is a synthetic
placeholder and carries **no hardware information whatsoever**. Any test that validates a
`VERSN` `D` value against this oracle would be validating nothing — it would merely be
asserting that the other emulator also returns 1234.

### 6.6 Current exposure in RetroCore (checked 2026-07-20)

Checked, so that this is not left as speculation:

- RetroCore's `VERSN` implementation (`Emulated.HW\ND\CPU\ND100\Instructions.SystemControlInstructions.cs:699-710` in the RetroCore repository)
  **does** model the PROM properly, as a 16-entry configurable array indexed by `A` bits 8-11
  (`cpu.VersnInstallationNumber[offset]`).
- RetroCore's `VERSN` tests
  (`Emulated.Tests\ND100\ND110_VersnIdentityTests.cs` in the RetroCore repository) are
  **self-contained** — config-driven expectations, no oracle involved.
- Grepping the oracle-driven comparison tests
  (`Emulated.Tests\ND110\ND110_CompareOpcodes.cs`, `ND110_CompareProgOpcodes.cs`,
  `MicrocodeGenerator\OpcodeMicrocodeGenerator.cs`) for `VERSN` / `140133` returns **no hits**.

So **no existing RetroCore validation is invalidated today.** The risk is forward-looking: if
`VERSN` is ever added to the oracle-compare opcode set, the `D` comparison must be excluded, or
the oracle must first be taught the PROM (§7).

---

## 7. Recommendation (NOT implemented — see §8)

Minimal, behaviour-preserving-by-default change to `ND110CPU`:

1. Replace `readonly ushort INSTALLATION_NUMBER` (`Cpu.cs:29`) with a
   `readonly byte[] installationProm = new byte[16];`.
2. Seed it in the constructor with a **valid default image**: `SYSNO` in bytes 0/1,
   `HWINFO(2)` in bytes 2/3, `NLEGU` in byte 4, and — mandatory — the signature
   `0x55AA` (52652 octal) in bytes 6/7, so `GCPUNR` accepts it.
3. Change `Cpu.cs:3469` to index by the *current* PIL nibble:
   `hw.idb.Latch(hw.alu.PIL & 0x0F ...)`. Using the **current** PIL (rather than a
   requested-byte register) is what reproduces the hardware bug, and is exactly what SINTRAN's
   level-0-7 loop expects.
4. Optionally add a settle latch so `LDPIL` becomes visible only on the following microword —
   that is the *root cause*, and modelling it would make the rev-L `COMM,SLOW` word actually
   matter. Note this touches every `LDPIL` in the microcode, so it is **not** a low-risk change
   and should be done separately, behind a flag, with the full test suite re-run.
5. Widen the constructor parameter (it is currently `byte InstallationNumber`, which cannot even
   express a 16-bit SYSNO) and plumb it through `TheMachine` / `CliEngine` /
   `ND110DebuggerServer\Machine.cs` so an image can be configured.

Steps 1-3 change the value `VERSN` returns in `D` (from constant 1234 to a PROM byte). That is a
**behaviour change to the oracle** and must be coordinated with RetroCore before landing.

The ND-120 FPGA repo already contains a working reference implementation of exactly this PROM to
copy the layout and defaults from: `Verilog\Shared\support\BACKWIRING_PROM.v` in the nd-120 repository
(instantiated at `Verilog\ND120_CORE.v:693`).

---

## 8. Status

**Nothing in the interpreter was changed by this document.** A live oracle server built from this
repo is in use by RetroCore's test suite; §7 is a proposal only.

## 9. Sources

| Claim | Source |
|---|---|
| VERSN microcode, ND-110 | `ND110Compile\uCode\ND-110-RASK.LISTING.TXT:105-128` |
| VERSN microcode + rev-L `COMM,SLOW` fix | `ND110Compile\uCode\ND-120-DELILAH-L.LISTING.TXT:108-130` |
| `LDPIL` settle wording | `ND110CPU\Enums.cs:22-26` |
| `RINR` = IDB source 35 | `ND110CPU\Enums.cs:609-614` |
| `RINR` stub implementation | `ND110CPU\Cpu.cs:3468-3470`, field at `Cpu.cs:29` |
| `LDPIL` implementation | `ND110CPU\Cpu.cs:1346-1356`, `ND110CPU\BUFALU.cs:144-148` |
| `SLOW` no-op | `ND110CPU\Cpu.cs:1592-1596` |
| `RINRN` / IDBS=35 in hardware | `Verilog\DECODE-GateArray\DGA\circuit\DECODE_DGA_IDBS.v:32` in the nd-120 repository |
| PIL out / INR in on B-plug | `Verilog\CPU-BOARD-3202\circuit\ND3202D.v:83,89` in the nd-120 repository |
| B-plug pin mapping | `Verilog\ND120_CORE.v:748` in the nd-120 repository |
| FPGA PROM model + same inference caveat | `Verilog\ND120_CORE.v:675-696` in the nd-120 repository; `Verilog\docs\backwiring-prom-installation-number.md` |
| SINTRAN `GCPUNR` level-loop workaround | `SINTRAN\NPL-SOURCE\NPL\PH-P2-OPPSTART.NPL:3534-3570` in the NDInsight repository |
| RetroCore VERSN implementation | `Emulated.HW\ND\CPU\ND100\Instructions.SystemControlInstructions.cs:699-710` in the RetroCore repository |
