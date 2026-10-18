# ND-110 OPCOM (MOPC) Commands Reference

Extracted from microcode listing `ND110Compile/uCode/ND-110-RASK.LISTING.TXT`.

The ND-110 MOPC (Micro Operator Communication) is a built-in operator console implemented entirely in microcode. It communicates via the CPU's UART serial port. MOPC runs during the 20ms timer interrupt (MS20) or when the CPU is in STOP mode, processing characters from the terminal.

## Architecture Overview

### How MOPC Works

1. **Entry**: MOPC is entered via the 20ms timer interrupt (vector 1, label `MS20` at CS address 002261) or via panel request (vector 2, label `PRQ` at CS address 002266). When the CPU is stopped, MOPC polls the UART for input.

2. **Main Loop** (`MOPC`/`MRET1` at CS 002267): Checks if there's a character waiting to be output (`PRCHR` register). If output is pending and the UART is ready, it sends the character. If no output, it checks the `COPCM` subroutine to determine if MOPC should handle terminal input.

3. **Input Processing** (`INPUT` at CS 002351): Reads a character from the UART, masks to 7 bits (AND 0177), echoes it back, then dispatches based on character type:
   - **Octal digits** (0-7, ASCII 060-067): Accumulated into the `OCTNR` register
   - **Letters** (A-Z, ASCII 101-132): Scrambled into the `SCRAM` register to form command mnemonics
   - **Special characters**: Dispatched to specific handlers

4. **UART I/O**: Characters are read via `IDBS,IOR` and echoed/output via `COMM,TBSTR` (Transmit Buffer Start). The MOPC checks UART status bits: bit 4 (010) = receive data available, bit 1 (002) = transmitter ready.

### Key Internal Registers (in Register File)

| Register | Purpose |
|----------|---------|
| `OCTNR` | Current accumulated octal number |
| `OCTN2` | Secondary octal number (upper word for 32-bit) |
| `SCRAM` | Scrambled letter accumulator (encodes command name) |
| `PRCHR` | Character waiting to be printed |
| `NUMBR` | Number being printed (shifts out digits) |
| `CDIGI` | Count of remaining digits to print |
| `CNT10` | Counter for dump formatting (addresses per line) |
| `CURNR` | Current address in dump |
| `UPPNR` | Upper limit address in dump |
| `DUMPF` | Dump flag (non-zero = dump in progress) |
| `OCTAD` | Current examined address |
| `OCTA2` | Secondary address (segment for virtual examine) |
| `WRTYP` | Write type (0=memory, 1=register, 2=aux reg, 3=internal reg) |
| `WRADR` | Write address for deposit |
| `STATUS` | MOPC status word |
| `DEPOS` | Deposit flag (non-zero = octal number entered, ready to deposit) |
| `RONLY` | Read-only flag (non-zero = deposit not allowed) |
| `MANIR` | Manual instruction register |
| `BRKPT` | Breakpoint address |
| `BPFLG` | Breakpoint flag (non-zero = breakpoint mode active) |
| `SINGL` | Single-step count (for multiple single mode) |
| `DISPL` | Display type for MMM panel |
| `MACL` | Master Clear flag |
| `TXT1`/`TXT2` | Label text buffers for MMM display |
| `EXMOD` | Examine mode |
| `ACTLV` | Activity level display |
| `OPR` | Operator register |

## MOPC Input Character Dispatch

When a character is received, the MOPC dispatches as follows:

### Octal Digits (0-7)

Entry: `OCDI2` (CS 002526) -> `OCDIG` (CS 002514)

Octal digits are accumulated into the `OCTNR` register. Each new digit shifts the current value left 3 bits and ORs in the new digit. The `DEPOS` flag is also set to indicate a number has been entered. The `OCTN2` register accumulates the upper word for double-word numbers.

### Letters (A-Z)

Entry: `LETTR` (CS 002541)

Letters are scrambled into the `SCRAM` register using the `BUILD` subroutine. The letter value (masked to 5 bits) is shifted and accumulated. This allows the microcode to identify multi-letter command names by comparing the final scrambled value against known constants.

### Special Characters

| Character | ASCII (octal) | Handler | Description |
|-----------|--------------|---------|-------------|
| Space `@` | 040 | `SPACE` (CS 002533) | Reset MOPC state |
| `/` | 057 | `SLASH` (CS 002776) | Examine (memory/register) |
| `<` | 074 | `ANGBR` (CS 002632) | Start memory dump |
| `*` | 052 | `ASTRX` (CS 002637) | Print current examined address |
| CR | 015 | `CR` (CS 002641) | Execute command / deposit / next examine |
| `!` | 041 | `EXCL` (CS 002546) | Start execution at address |
| `"` | 042 | `QUOTE` (CS 002611) | Set up manual instruction |
| `Z` | 132 | `ZCHAR` (CS 002620) | Single-step mode |
| `#` | 043 | `CROSS` (CS 002553) | Memory test |
| `.` | 056 | `DOT` (CS 002633) | Set breakpoint |
| `$` | 044 | `DOLOA`/`ETLOA` (CS 002205) | Load (serial binary loader) |
| `&` | 046 | `ETLOA` (CS 002205) | Load (same as `$`) |
| Escape | 033 | `ESCAP` (CS 002530) | Exit MOPC / return to running |
| `?` | 077 | (printed by `ILLEG`) | Printed on illegal input |

---

## Commands in Detail

### Memory Examine: `<address>/`

**Syntax**: `<octal-address>/`

**Example**: `1000/` examines memory location 01000

**Microcode flow**:
1. Octal digits accumulate in `OCTNR`
2. `/` triggers `SLASH` handler (CS 002776)
3. If `SCRAM` = 0 (no letters typed), dispatches to `RWX` (memory examine)
4. `RWX` stores address in `OCTAD`, sets `WRTYP` = 0 (memory)
5. Calls `DSPLY` to read memory and `PROCT` to print the value as 6 octal digits

**Response on serial port**: The CPU prints the contents of the addressed location as a 6-digit octal number followed by a space.

**Verification**: After sending `1000/\r`, expect to receive the echo of your input followed by the 6-digit octal contents, e.g. `1000/ 000000 `.

### Virtual Memory Examine: `E<octal-address>/`

**Syntax**: `E<segment><address>/`

**Example**: `E1000/` examines virtual address 01000

**Microcode flow**: When `SCRAM` encodes "E" (value 005), triggers `CR` -> `CR22` -> `EXM02` path which sets up virtual address examination with segment information from `OCTN2`.

### Register Examine: `<regname>/`

**Syntax**: One of `S/`, `D/`, `P/`, `B/`, `L/`, `A/`, `T/`, `X/`

| Letter | Register | Value |
|--------|----------|-------|
| `S` | STS (Status) | 0 |
| `D` | D register | 1 |
| `P` | P (Program Counter) | 2 |
| `B` | B register | 3 |
| `L` | L register | 4 |
| `A` | A (Accumulator) | 5 |
| `T` | T register | 6 |
| `X` | X (Index) | 7 |

**Microcode flow** (e.g., for `A/`):
1. Letter `A` scrambled into `SCRAM`
2. `/` handler `SLASH` matches and dispatches to `AXX` (CS 003006)
3. `AXX` loads register number 5 into Q, jumps to `REGXX`
4. `REGXX` calls `OCDIX` to record address, then `RWY` sets up examine
5. `DSPLY` reads the register value, `PROCT` prints it

**Response**: The register contents as a 6-digit octal number.

### Internal Register Examine: `I<number>/`

**Syntax**: `I<octal-number>/`

**Microcode flow**: Dispatches to `IXX` (CS 003032), which sets type=2 (internal register) and reads the numbered internal register.

### Auxiliary Register Examine: `U<number>/`

**Syntax**: `U<octal-number>/`

**Microcode flow**: Dispatches to `UXX` (CS 003062), sets up auxiliary register examination.

### I/O Device Access: `IO<device-number>/`

**Syntax**: `<octal-device-address>IO/`

**Example**: `1560IO/` reads from device at IOX address 1560 (floppy controller)

**Microcode flow** (`IOOPC` at CS 003052-003060):

1. `R4 = A` — Save the A register (working registers must not be affected)
2. `Q = OCTNR` — Read the typed device address, `COMM,LDIRV` sets up register vector
3. `R1 = Q` — R1 = IOX device address
4. `Q = OPR` — Read the OPR pseudo-register from the register file
5. `A = Q` — Load OPR value into A (this is the output data for odd-address writes)
6. `→ call IOXX1` — Execute the IOX instruction:
   - `IOXX1` (CS 000501) checks the device address range:
     - Device 30x → intercepted by `TERMX` → `TRMVC` (on-board UART, handled in microcode)
     - Device 10-13 → intercepted by `CLKX` (clock/timer, handled in microcode)
     - All others → general `COMM,IOX` on the I/O bus (address from R1, data from A)
   - For general IOX (CS 000506-000510): R1 put on address bus, A put on data bus, `COMM,IOX` issued, then `IDBS,DBR` reads response from data bus into A
7. `Q = A` — Copy result to Q, `→ call PROCT` — Print as 6-digit octal
8. `A = R4` — Restore original A register, `→ RONLY` — Return to OPCOM (deposit not allowed)

**Even vs odd device addresses** (per ND-100 I/O convention):
- **Even address** (e.g. `1560IO/`): Input — read from device. The device puts data on the bus, result is printed on the console. Data is displayed but NOT stored in any register.
- **Odd address** (e.g. `1561IO/`): Output — write to device. The data written comes from the **OPR pseudo-register** (internal register I2). The value written is printed on the console.

**None of the working registers** (S, D, P, B, L, A, T, X) are affected by the IO/ command. A is explicitly saved in R4 and restored.

**To perform an output IOX** (write to a device), first set OPR to the desired value:
```
#OPR/xxxxx yyyyyDEP     Set OPR to yyyyy
#ddddIO/                Execute IOX with device address dddd (odd = output)
```

**Response**: The IOX result (for input) or the value written (for output) as a 6-digit octal number.

### Operator Register Examine: `OPR/`

**Syntax**: `OPR/`

**Microcode flow**: Dispatches to `OPRX` (CS 003061). Reads the operator panel register.

### Active Level Examine: `ACT/`

**Syntax**: `ACT/`

**Microcode flow**: Dispatches to `ACTX` (CS 003064). Sets display type to 17 (activity).

### Register Dump: `R/`

**Syntax**: `R/`

**Microcode flow**: Dispatches to `RXX` (CS 003030) -> `TYP1` which sets type=0 and jumps to `RWY` for register display setup.

---

### Memory Deposit: `<address>/<value> CR`

**Syntax**: `<octal-address>/<current-value> <new-octal-value><CR>`

**Example**: `1000/ 000000 177777<CR>` deposits 177777 into location 01000

**Microcode flow**:
1. First examine the location with `<address>/` (the CPU prints the current contents)
2. Type the new octal value (digits accumulate in `OCTNR`, `DEPOS` flag gets set)
3. Press CR, triggering the `CR` handler (CS 002641)
4. `CR` checks `DEPOS` flag and `SCRAM` value
5. For memory deposit: `SCRAM` must be 0 (no command letters) or equal to the `DEP` scramble code (052 octal)
6. If `RONLY` flag is set, deposit is rejected (prints `?`)
7. `DEPOS` routine (CS 002716) sets segment, reads `OCTAD` for address, reads `OCTNR` for value, writes via `COMM,DERQ`
8. After deposit, auto-advances to next address and displays it

**When DEP is needed**: In **STOP mode**, DEP is not needed — just type the new value and press CR. In **RUN mode** (CPU executing), you MUST type the value followed by `DEP` instead of CR. This prevents accidental memory corruption. The microcode CR handler (CS 002711-002714) checks the CR4 path: if SCRAM matches the "DEP" scramble code (052 octal) AND RONLY is not set, the deposit proceeds. If SCRAM is non-zero and doesn't match DEP → ILLEG.

```
STOP mode:   1000/ 000000 177777<CR>     — deposit with CR
RUN mode:    1000/ 000000 177777DEP      — deposit with DEP keyword
```

**When DEP is rejected**:
- `RONLY` flag is non-zero (set by the `RONLY` routine after IO/, ACT/, and other read-only examines)
- `SCRAM` is non-zero and doesn't match the `DEP` code (052 octal) → prints `?`
- In the CR16 path (CS 002701-002714): DEPOS is checked first, then SCRAM is compared against the DEP code. If RONLY is set, the WRTYP check at CS 002714 rejects the deposit.

**Response after deposit**: The CPU auto-advances to the next memory address and prints it in the format `<next-address>/ <contents> `.

### Register Deposit

**Syntax**: `<regname>/<current-value> <new-value><CR>`

**Microcode flow** (via `CR5`/`CR9`/`CR10`/`CR11`):
- `WRTYP` = 1: Register deposit via `OPCRB` and IRV/GPR write
- `WRTYP` = 2: Auxiliary register deposit
- `WRTYP` = 3: Internal register deposit

---

### Next Examine (CR without digits)

**Syntax**: `<CR>` (after a previous examine, with no digits typed)

**Microcode flow**: If `DEPOS` = 0 (no digits entered) and `SCRAM` = 0 (no letters) and the current display is memory examine, `CR6` increments `OCTAD` and displays the next location.

**Response**: The next address and its contents.

---

### Memory Dump: `<lower><<upper><CR>`

**Syntax**: `<lower-addr><<upper-addr><CR>`

**Example**: `1000<1100<CR>` dumps memory from 01000 to 01100

**Microcode flow**:
1. First number goes into `OCTNR`
2. `<` handler `ANGBR` (CS 002632) copies `OCTNR` to `CURNR` (start address) and sets `DUMPF` (dump flag)
3. Second number (upper bound) goes into new `OCTNR`
4. CR handler detects `DUMPF` set, and `SCRAM` = 0, goes to `CR1` -> memory dump path
5. `OCTNR` + 1 stored in `UPPNR` (end address), dump begins at `DMP11`
6. Output format: each line starts with the address, then `/`, then 8 octal values, then CR/LF

**Response**: Formatted dump output:
```
001000/ 000000 000000 000000 000000 000000 000000 000000 000000
001010/ 000000 ...
```

The dump continues until `CURNR` reaches `UPPNR`. Each line prints up to 10 words (controlled by `CNT10` counter which cycles through states 0-13 octal: address, `/`, 8 data words, CR, LF).

### Register Dump: `R<CR>` or `IRD<CR>`

**Syntax**: `R<CR>` - Dump all normal registers

**Microcode flow**: `CR` handler detects `SCRAM` has encoded a letter sequence. Compares against known codes:
- `SCRAM` = scramble of "IRD" (114 octal difference check) -> Internal Register Dump: prints 16 (020 octal) internal registers
- `SCRAM` = scramble of "R" -> Register dump
- `SCRAM` = scramble of "U" (125 octal check) -> Auxiliary register dump (RDE)

**Response**: 16 octal values on formatted lines.

---

### Start Execution: `<address>!`

**Syntax**: `<octal-address>!`

**Example**: `0!` starts execution at address 0

**Microcode flow** (`EXCL` at CS 002546):
1. Checks `SCRAM` = 0 (no command letters) - if not, prints `?`
2. Checks `DEPOS` flag (an octal number was entered)
3. If `DEPOS` is set: loads `OCTNR` into the P register (program counter), then issues `COMM,START` and jumps to `ESCAP`
4. If `DEPOS` = 0 (no address given): just issues `COMM,START` and exits

**Response**: No output. The CPU starts running. The MOPC becomes inactive (no more input processing until the CPU stops again or a panel interrupt occurs).

**Verification**: After sending `<address>!\r`, the CPU should start executing. If connected via serial, you'll stop receiving MOPC prompts. Send ESC (033) to get back to MOPC if the program doesn't interact with the terminal.

---

### Stop CPU: `STOP<CR>`

**Syntax**: Type `STOP` then `<CR>`

**Microcode flow**: `CR` handler checks `SCRAM` against the "STOP" scramble code (426 octal). If matched, issues `COMM,SSTOP` which sets the stop flip-flop.

**Response**: The CPU stops. MOPC becomes fully interactive. The MOPC will start polling for input.

---

### Master Clear: `MACL<CR>`

**Syntax**: Type `MACL` then `<CR>`

**Microcode flow**: `CR` handler checks `SCRAM` against "MACL" scramble (176 octal). Jumps to `MACL` label (CS 001752) which performs a complete machine initialization.

**Response**: The CPU is reset. All registers cleared. The MOPC restarts.

---

### Single Step: `Z<number><CR>` or Single Button

**Syntax**: `Z<count><CR>` for multiple single-step

**Microcode flow**:
- `ZCHAR` (CS 002620): First checks `BPFLG` = 0 (no breakpoint active, else illegal). Reads `OCTNR` as step count. Stores in `SINGL` register, jumps to `SINIT` -> `SPAC9` to clean up.
- `SING2` (CS 002627, from panel Single button): Checks CPU is in STOP mode (`COND,STP`), then executes one instruction via `SINGL`/`BRKZZ`.
- `BRKZZ`/`SINGL` (CS 002623): Issues `COMM,START` then immediately `COMM,SSTOP` to execute exactly one macroinstruction, then `COMM,CONTINUE`.
- After each step, `STOP2` (CS 003624) decrements `SINGL` counter. If counter > 0, executes another step. If 0, checks breakpoint.

**Response**: After each step, the CPU re-enters STOP and MOPC can display state. For `Z1<CR>`, one instruction executes, then the CPU stops.

---

### Set Breakpoint: `<address>.<CR>`

**Syntax**: `<octal-address>.<CR>`

**Microcode flow** (`DOT` at CS 002633):
1. Reads `OCTNR` into `BRKPT` register
2. Sets `BPFLG` to non-zero
3. Cleans up via `SPAC9`

When the CPU runs (after `!`), after each instruction `STOP2` checks `BPFLG`. If set, compares P register against `BRKPT`. If P = `BRKPT`, the CPU stops and prints the breakpoint character.

**Response**: No immediate output. When the breakpoint is hit, MOPC prints a character and returns to interactive mode.

---

### Manual Instruction: `"<instruction><CR>`

**Syntax**: `"<octal-opcode>`

**Microcode flow** (`QUOTE` at CS 002611):
1. Copies `OCTNR` to `BRKPT`
2. Sets `MANIR` flag
3. When `STOP` routine checks `MANIR`, it uses the value as an instruction to execute: `COMM,CLIRQ` then `COMM,MAP` to decode and execute the instruction

**Example**: `"140004<CR>` would execute a TRA 4 instruction.

---

### Examine Display: `*`

**Syntax**: `*`

**Microcode flow** (`ASTRX` at CS 002637): Reads `OCTAD` (current examine address), prints it via `PROCT`, then resets to `SPACE` state.

**Response**: Prints the current examined address as a 6-digit octal number.

---

### Load Program: `$` or `&`

**Syntax**: `$` or `&`, optionally preceded by an octal load code

**Microcode flow** (`DOLOA`/`ETLOA` at CS 002205):
1. Reads `OCTNR` as a **load code** (not just a device number). If `OCTNR` = 0, uses ALD switches (automatic load device) via `ALDLO` -> `ALDVC` vector.
2. Calls `LOAD1` (CS 002144) which decodes the load code:
   - Bit 13 = 1: Mass storage load (`MASS` at CS 002147) -- uses block-oriented IOX to boot from disk/floppy
   - Bit 13 = 0: Serial binary loader (`ETLO1` at CS 002210) -- character-at-a-time via IOX

The load code encodes both the device number and the boot method. Examples from the ALD switch table:
- `1560` = device 1560 (bit 13=0, serial binary loader)
- `21560` = device 1560 (bit 13=1, mass storage loader)
- `100000` = bit 15 set = start execution at address 020 (no loading)

### Loading from Device 300 (Console Serial Port)

**Yes, the binary loader can load from the console.** Typing `300$` would:
1. Set load code = 300 (octal). Bit 13 = 0, so the **serial binary loader** (ETLO1) is selected.
2. The device number (300) is stored in the D register.
3. The `INCH` subroutine reads characters via `IOXG` using IOX addresses derived from D (IOX 300, IOX 302, etc.).

**Critical detail from IOXG** (CS 000477): The `IOXG` routine intercepts device addresses 300-303 and handles them **locally on the CPU board** rather than issuing a bus IOX. Specifically:
- `IOXX1` (CS 000501) checks if the upper bits of the device address match 300 (line 1382-1383: XOR with 300, then mask with 3 to check 30x range)
- If device is 30x: dispatches to `TERMX` -> `TRMVC` vector (CS 003720), which accesses the **on-board UART** directly via `IDBS,IOR` (read) and `COMM,TBSTR` (write)
- If device is NOT 30x: issues a general `COMM,IOX` on the I/O bus

The `TRMVC` vector at CS 003720 maps sub-addresses:
- IOX 300 (`TRMO`): Read character from UART via `IDBS,IOR`, masks to 7 bits (AND 0377), issues `COMM,RSDA` (reset data available)
- IOX 301: Returns (no-op)
- IOX 302 (`TRM2`): Read UART status via `IDBS,IOR`, builds status word with receive-ready and transmit-ready bits
- IOX 303 (`TRM3`): Write control/data to UART
- IOX 304: Clear A register (no-op equivalent)
- IOX 305: Transmit character via `COMM,TBSTR`
- IOX 306/307: Other control functions

**This means the binary loader uses the exact same physical UART as the MOPC** when device 300 is specified. The difference is the access method: MOPC accesses the UART directly through microcode registers, while the binary loader goes through the IOX dispatch which routes 30x back to the same UART.

### Serial Binary Loader Protocol (via `$` / `&`)

The binary loader reads data character-by-character from the specified device using the `INCH` subroutine.

**Protocol** (mixed ASCII and binary):

The loader starts in ASCII mode, reading octal numbers via `ASS8`:

1. **Address phase** (ASCII): The `SEEK`/`SIKI` loop reads octal numbers separated by terminators:
   - CR (015): Terminates an octal address and assigns it as a possible start address (stored in P via R5)
   - `!` (041): Signals transition to **binary data mode** (handled by `EXFOU` at CS 002217)
   - Other non-octal characters: Continue reading

2. **Binary data phase** (after `!`): `EXFOU` switches to binary mode:
   - Read core address: 2 bytes via `BIN` subroutine -> X register (memory pointer)
   - Read word count: 2 bytes via `BIN` -> T register (counter)
   - Read data words: `STLP` loop reads T words via `BIN`, each written to memory at address X via `COMM,WRRQ,PT`. X increments, T decrements, checksum accumulates in L register (XOR running sum)
   - Read checksum: 2 bytes via `BIN`, XOR with L. If non-zero -> prints `?` (checksum error)
   - Read action code: Octal ASCII via `ASS8`. If action code = 0 -> `COMM,START` (begin execution). If non-zero -> continue loading (back to `SEEK` for next block)

3. **`BIN` subroutine** (CS 002255): Reads exactly 2 raw bytes from the device via `INCH`:
   - Read byte 1 (high byte)
   - Swap to upper position
   - Read byte 2 (low byte)
   - OR together -> 16-bit word returned in Z register

**Binary data format** (each 16-bit word):
```
[High byte] [Low byte]
```

**Complete transfer sequence for `300$`**:
```
300$                          <- typed at MOPC, activates binary loader on console
<octal-start-addr>!          <- ASCII address, then ! to switch to binary
<2-byte core addr>           <- binary: where to store in memory
<2-byte word count>          <- binary: how many words
<data word 1>                <- binary: first word (2 bytes, high first)
<data word 2>                <- binary: second word
...
<data word N>                <- binary: last word
<2-byte checksum>            <- binary: XOR of all data + running accumulator
0<CR>                        <- ASCII: action code 0 = start execution
```

Or for multiple blocks:
```
300$
<addr1>!<binary-block-1><checksum>1<CR>    <- action code 1 = continue
<addr2>!<binary-block-2><checksum>0<CR>    <- action code 0 = start
```

### Mass Storage Load

For disk/floppy boot with bit 13 set in the load code:

The `MASS` loader (CS 002147) performs standard ND-100 device I/O:
1. IOX N+1 (0): Set core address (where to load)
2. IOX N+1 (0): Set core address again
3. IOX N+3 (0): Set block address (which disk block)
4. IOX N+7 (2000): Set word counter
5. IOX N+5 (4): Activate device
6. Poll IOX N+4 for status bit 2 (device finished)
7. On completion: start execution at address 0

### Quick Program Transfer via Serial Port

**Method 1: Binary loader from console (fastest serial method)**

1. CPU must be in STOP mode
2. Type `300$` to start binary loader on console device
3. From your transfer tool, send the binary loader protocol data (see above)
4. The loader reads at full UART speed -- 2 raw bytes per word is ~3x more efficient than ASCII octal

**Method 2: Manual deposit loop (simpler, slower)**

1. `<address>/` - examine first location
2. `<value><CR>` - deposit value and auto-advance to next address
3. Repeat step 2 for consecutive locations
4. `<start-address>!` - start execution

This is about 3x slower than binary mode: 6+ ASCII characters per word (6 octal digits + CR) vs. 2 binary bytes per word.

**Method 3: If your device is NOT the console**

If you have a separate serial device (not device 300), specify its device number: `<device>$`. The IOXG routine will issue general `COMM,IOX` on the bus for non-30x devices.

---

## Panel Interrupt Vector Table (PANVC)

The `PANVC` table at CS address 003760 maps panel interrupt codes to handlers:

| Vector Offset | CS Address | Label | Function |
|---------------|-----------|-------|----------|
| 0 | 003760 | `STOP` | Stop CPU |
| 1 | 003761 | `MS20` | 20ms timer interrupt |
| 2 | 003762 | `PRQ` | Panel request (MOPC entry) |
| 3 | 003763 | `SING2` | Single step button |
| 4 | 003764 | `LOAD` | Load button |
| 5 | 003765 | `CONT` | Continue button |
| 6 | 003766 | `RSTRT` | Restart |
| 7 | 003767 | `MACL` | Master Clear |

All vectors decrement the P register (B,P ALUF,B-1 ALUD,B) before jumping to their handler, to compensate for the P increment that happened during the interrupt.

---

## Verifying CPU Response from a Serial Tool

### General Protocol

1. **Send command characters** one at a time. Each character is echoed back by the MOPC.
2. **Wait for echo**: After sending each character, wait to receive the echo before sending the next. The MOPC echoes immediately via `COMM,TBSTR`.
3. **Parse response**: After CR, the CPU sends a response (if any). Responses are octal numbers as ASCII characters (060-067), spaces (040), `/` (057), CR (015), LF (012).

### Detecting STOP Mode

When the CPU is stopped, MOPC actively polls for input. You can detect this by:
- Sending a Space (040): If echoed back, MOPC is active (CPU is stopped)
- Sending CR: If you get `?` back, MOPC is running but your input was invalid
- No echo at all: CPU is running and MOPC is not processing input

### Command Response Patterns

| Command | Send | Expected Response |
|---------|------|------------------|
| Memory examine | `1000/\r` | Echo + `001000/ XXXXXX ` (6-digit octal value + space) |
| Register examine | `A/\r` | Echo + `XXXXXX ` |
| Memory deposit | `1000/ ... 177777\r` | Echo + next address `001001/ XXXXXX ` |
| Start execution | `0!\r` | Echo only, then silence (CPU running) |
| Stop CPU | `STOP\r` | Echo + CR/LF |
| Memory dump | `1000<1010\r` | Echo + formatted dump lines |
| Single step | `Z1\r` | Echo, one instruction executes, then CPU stops |
| Reset | `MACL\r` | Echo, CPU resets |
| Breakpoint | `1000.\r` | Echo, breakpoint set |
| Escape | `\033` | Exits MOPC cleanly |

### Error Response

If an illegal command is entered, MOPC prints `?` (ASCII 077) via the `ILLEG` routine (CS 002413) and goes to read-only mode (`RONLY`).

### Timing Considerations

- MOPC runs in microcode, sharing CPU cycles. When the CPU is running, MOPC only gets service during the 20ms timer interrupt.
- When the CPU is stopped, MOPC gets all CPU cycles and is highly responsive.
- The binary loader runs with the CPU fully dedicated to the transfer (no multitasking).
- UART baud rate is configured independently of MOPC (typically 9600 baud on the ND-110).

---

## ALD (Automatic Load Device) Switch Values

The `ALDVC` vector table at CS 003560 maps ALD switch positions to load codes. The load code determines the boot device and mode:

| Switch (octal) | Load Code | Description |
|----------------|-----------|-------------|
| 0 | 000000 | No device |
| 1 | 001560 | Device 1560 (floppy?) |
| 2 | 020500 | Device 500 with flag |
| 3 | 021540 | Device 1540 with flag |
| 4 | 000400 | Device 400 |
| 5 | 001600 | Device 1600 |
| 6 | 021560 | Device 1560 with flag |
| 7 | 000000 | No device |
| 10 | 100000 | Bit 15 set (start in address 20) |
| 11-16 | 10xxxx | Same as 1-6 but with bit 15 set |
| 17 | 100000 | Bit 15 set |

Bit 15 of the load code: If set, start execution at address 020 instead of loading from device.
Bit 13 of the load code: If set, mass storage boot; if clear, serial binary loader.

---

## Transferring Programs via Serial Port

### BPUN File Transfer (Fastest Method)

The BPUN file format (see [ndwiki.org/wiki/BPUN_File_Format](http://www.ndwiki.org/wiki/BPUN_File_Format)) is the native byte stream consumed by the MOPC binary loader. **A BPUN file can be sent directly to the serial port with no conversion, encoding, or protocol wrapping.** The file bytes are exactly what the microcode expects to receive.

This works because the BPUN format was designed as the serial input format for the binary loader. Every field in a BPUN file maps 1:1 to a stage in the `ETLO1` microcode loader:

| BPUN File Field | Microcode Stage | Format |
|---|---|---|
| Preamble (optional bootstrap) | `SEEK`/`SIKI` via `ASS8` -- scans for octal digits, ignores other chars | ASCII, any chars except `!` |
| Start address + CR | `ASS8` reads octal ASCII, CR stores value in P (via R5) | ASCII octal + CR (015) |
| Boot address + `!` | `ASS8` reads octal, `!` triggers `EXFOU` -- switch to binary mode | ASCII octal + `!` (041) |
| Load address | `BIN` reads 2 bytes -> X register (memory write pointer) | Binary, 2 bytes, big-endian |
| Word count | `BIN` reads 2 bytes -> T register (loop counter) | Binary, 2 bytes, big-endian |
| Data words | `STLP` loop: `BIN` reads word, `COMM,WRRQ,PT` writes to memory at X, X++, T-- | Binary, 2 bytes each, big-endian |
| Checksum | `BIN` reads 2 bytes, compared against accumulated sum in L register | Binary, 2 bytes, big-endian, additive sum mod 2^16 |
| Action | Read by microcode; 0 = start execution, non-zero = stay in OPCOM | 2 bytes |

**Checksum**: The microcode accumulates a running additive sum in the L register (`STLP` at CS 002226: `A,Z B,L ALUF,A+B ALUD,B,YA`). After all data words, it reads the checksum from the file and XORs it with L (`ALUF,XORAB` at CS 002230). If the result is zero, the checksum matches. This is an **arithmetic sum mod 2^16**, matching the BPUN spec. The BPUN.cs parser in `ND110Support/FileLoader/BPUN.cs` confirms this (line 289: `CalculatedChecksum = (ushort)(CalculatedChecksum + data_word)`).

**Action field**: If zero, the microcode issues `COMM,START` and begins execution at the Start address (already in P). If non-zero, the CPU stays in OPCOM mode with P set to the Start address.

### Binary Loader Execution Flow After `300$`

After `300$` is typed at the MOPC, the microcode takes over the CPU completely and runs the binary loader in a tight polling loop. There is no multitasking — the CPU does nothing but read bytes from the serial port until the transfer is complete or an error occurs.

#### Step-by-step flow:

1. **`ETLOA` → `LOAD1` → `ETLO1`** (CS 002205-002210): Reads `OCTNR` (=300) as the device number, stores in D register. Calls `DVACT` which activates the device (IOX 303 with value 4005 — configures UART for 8-bit mode) and enables receive interrupts.

2. **`SEEK`/`SIKI` loop** (CS 002211-002216, ASCII phase): Reads the BPUN preamble character by character via `ASS8` → `INCH`. Each byte is polled from the UART (IOX 302 for status, IOX 300 for data). The loop dispatches based on the terminating character:
   - **CR** (015): The octal number just accumulated is stored as a possible start address (R5 → P register). Loop back to `SEEK` for more.
   - **`!`** (041): End of preamble — jumps to `EXFOU` to begin binary data phase.
   - **Other non-octal char**: Not CR and not `!` — loop back to `SIKI` to keep reading.

3. **`EXFOU`** (CS 002217, binary phase entry): Sets STS = 0xFF (8-bit data mask). Then reads three fields via `BIN` (2 raw bytes each, high byte first):
   - Core address → X register (memory write pointer)
   - Word count → T register (loop counter)

4. **`STLP` loop** (CS 002223-002226, binary data transfer): Reads exactly T words:
   - `BIN` reads 2 bytes → Z register (one 16-bit word)
   - `COMM,WRRQ,PT` writes the word to physical memory at address X
   - X incremented (next memory address)
   - T decremented (words remaining)
   - L += Z (accumulate additive checksum in L register)
   - When T reaches 0 (COND,F=0 at CS 002224), falls out of the loop

5. **Checksum verification** (CS 002227-002231): `BIN` reads a 2-byte checksum from the file. XORs it with the accumulated sum in L (`ALUF,XORAB` at CS 002230):
   - **Match** (XOR = 0, F=0): Checksum OK, proceed to action code
   - **Mismatch** (XOR ≠ 0): Jumps to `ILLEG` (CS 002413) which prints `?` and goes to `RONLY` → `SPACE` → `CONT`. CPU returns to MOPC idle.

6. **Action code** (CS 002232-002235): `ASS8` reads an ASCII octal number from the serial stream. The result is in R2:

   - **Action = 0**: Issues `COMM,START` (starts the CPU running), then jumps to `ESCAP`. The CPU **begins execution at the start address** (already in P from the SEEK phase).
   - **Action ≠ 0**: Jumps directly to `ESCAP` without `COMM,START`. The CPU **stays in MOPC stop mode** with P set to the start address. This allows the operator to inspect memory or start manually.

7. **`ESCAP`** (CS 002530, cleanup): Sets STATUS bit 6 (~CONSOLE = normal terminal mode) via `WSIOC`/`COMM,SIOC`. Falls through to `RONLY` → `SPACE` (full MOPC state reset: clears MANIR, BPFLG, SINGL, TXT1, TXT2) → `CONT` (`COMM,CONTINUE` resumes macro-instruction execution).

#### How the microcode knows the transfer is complete

The protocol is **entirely length-prefixed**. The microcode does not detect end-of-file, timeout, or any special termination character:

| What tells the microcode to stop | Mechanism |
|---|---|
| End of preamble | The `!` character (041) terminates the `SEEK`/`SIKI` ASCII scanning loop |
| End of data block | The word count field (read via `BIN` into T) — `STLP` reads exactly T words and stops |
| Data integrity | The 2-byte checksum (additive sum mod 2^16) — XOR comparison with running sum in L |
| End of transfer | The action code — the last field read by `ASS8`, after which the loader either starts execution or returns to MOPC |

**If the serial stream stops mid-transfer**, `INCH` will poll IOX 302 (data ready) in an infinite loop forever. There is no timeout. The only way to break out is a panel interrupt (STOP button, master clear, or similar hardware intervention).

#### Summary: outcome after transfer

| Action field | COMM,START issued? | CPU state | P register |
|---|---|---|---|
| 0 | Yes | **Running** — executing from start address | Start address from preamble |
| ≠ 0 | No | **Stopped** — back in MOPC interactive mode | Start address from preamble |

### How to Transfer a BPUN File

From a host connected to the console serial port:

1. Ensure the CPU is in STOP mode (send `STOP\r` and wait for echo, or the CPU may already be stopped)
2. Send `300$` to activate the binary loader on the console device (or just `$` if ALD switches are configured for console)
3. **Send the BPUN file bytes directly to the serial port** -- the entire file, byte for byte, with no modification
4. The microcode reads and processes each byte via `INCH` -> `IOXG` -> `TRMVC` (on-board UART)
5. When complete: if Action = 0, the CPU starts executing at the Start address; if Action != 0, MOPC returns to interactive mode

No handshaking, no framing, no escape sequences. The binary loader calls `INCH` in a tight loop -- it reads one byte at a time from device 300 and doesn't care whether the bytes come from a human typing or from a file being streamed.

**Flow control consideration**: The binary loader polls `INCH` which polls the UART status (IOX 302, checking bit 3 for data ready) in a tight microcode loop. At typical baud rates (9600), the microcode loop is fast enough that no flow control is needed -- the CPU consumes bytes faster than the UART delivers them. At higher baud rates, hardware flow control (RTS/CTS) on the serial connection would prevent overrun.

### UART Configuration: 7-Bit vs 8-Bit and Parity

The ND-110 console UART is typically described as using "7-bit even parity" for terminal communication. This raises the question: how does the binary loader transfer full 8-bit data?

The answer involves two mechanisms: (1) DVACT configures the UART for 8-bit mode via IOX 303/TRM3, and (2) the microcode applies a software mask that switches between 7-bit and 8-bit at the `!` delimiter.

#### Software Data Masking via STS Register

The `ETLO1` binary loader sets up two different data masks via the STS register:

- **ASCII phase** (before `!`): STS = BMG[7] - 1 = 0x007F (7-bit mask), set at CS 002146
- **Binary phase** (after `!`): STS = BMG[8] - 1 = 0x00FF (8-bit mask), set by `EXFOU` at CS 002217

After reading each byte, `INCH` (CS 002242) does `A,A B,STS ALUF,ANDAB ALUD,NONE COMM,LDGPR` — masking the received data with STS. In the ASCII preamble, bit 7 is stripped; in the binary data section, all 8 bits are preserved.

#### IOR Register Layout (from Uart.cs)

The on-board UART I/O register read via `IDBS,IOR` is a 16-bit word:

```
Bits 0-7:   UART DATA (8 bits always present)
Bit 8:      FRAMING ERROR
Bit 9:      PARITY ERROR
Bit 10:     OVERRUN ERROR
Bit 11:     CONSOLE~ (from IOControl bit 6)
Bit 12:     LOCK~
Bit 13:     EAUTO~
Bit 14:     DATA RECEIVED
Bit 15:     TRANSMIT BUFFER EMPTY
```

#### Parity Error Is Reported But Never Checked

The UART hardware **does** report parity error via IOR bit 9. The `TRM2` handler (IOX 302 status read, CS 000520) faithfully maps these error bits into the returned status word — it shifts IOR bits 8-10 down to bits 5-7 in the IOX 302 result, matching the documented status register format:

```
IOX 302 result bit 4: error summary (any of bits 5-7 set)
IOX 302 result bit 5: framing error (from IOR bit 8)
IOX 302 result bit 6: parity error (from IOR bit 9)
IOX 302 result bit 7: overrun error (from IOR bit 10)
```

**However, no MOPC or binary loader code ever checks these error bits:**

- **MOPC INPUT** (CS 002353): Reads `IDBS,IOR` -> Q, tests only bit 14 (data received), masks data with AND 0177. Never tests bits 8-10.
- **Binary loader INCH** (CS 002236): Calls IOX 302 via IOXG, tests only bit 3 (data ready) of the result. Never tests bits 4-7 (error flags).
- **TRMO** (IOX 300 read, CS 000515): Reads `IDBS,IOR` -> Q, tests only bit 11 (CONSOLE~), masks data with AND 0377. Never tests bits 8-10.

| Mode | Data mask | Parity/error check |
|------|-----------|-------------------|
| MOPC terminal input | AND 0177 (7 bits) | **None** |
| Binary loader, ASCII phase | AND STS=0x7F (7 bits) | **None** |
| Binary loader, binary phase | AND STS=0xFF (8 bits) | **None** |

#### IOX 300-307: On-Board UART Handlers (TRMVC Vector Table)

Device addresses 300-307 are intercepted by `IOXG` (CS 000501) and dispatched to the `TRMVC` vector table (CS 003720) instead of going on the I/O bus. The IOXG check is: XOR upper address bits with 300, then mask lower 2 bits with 3 — matching the range 300-303 (and by extension 304-307 in the next vector entries).

| IOX | CS addr | Handler | Function |
|-----|---------|---------|----------|
| 300 | 003720 | `TRMO` (CS 000515) | **Read data**: `IDBS,IOR` -> Q, mask AND 0377, return 8-bit data in A. Also issues `COMM,RSDA` if CONSOLE active. |
| 301 | 003721 | (no-op) | Returns immediately, no action |
| 302 | 003722 | `TRM2` (CS 000520) | **Read status**: reads `IDBS,IOR` into A, builds IOX 302 status word with data-ready (bit 3), error flags (bits 4-7 from IOR bits 8-10), and interrupt enable (bit 0) |
| 303 | 003723 | `TRM3` (CS 000540) | **Write control**: configures word length, parity, stop bits, interrupt enable. **Inverts input** (`R2 = ~A`) before processing. Writes result to STATUS + `COMM,SIOC` |
| 304 | 003724 | (no-op) | Clears A register, returns |
| 305 | 003725 | (direct) | **Transmit data**: issues `COMM,TBSTR` with A register value, returns |
| 306 | 003726 | `TRM6` (CS 000547) | **Write status**: reads IOR, configures TX interrupt enable based on transmitter ready state |
| 307 | 003727 | `TRM7` (CS 000554) | **Write control**: configures RX interrupt enable, writes to STATUS + `COMM,SIOC` |

#### IOX 303 / TRM3: DVACT Configures 8-Bit Word Length

The TRMVC entry for IOX 303 **inverts the A register** (`R2 = ~A`) before jumping to TRM3. This means the IOX 303 control register spec values are inverted relative to what TRM3 stores in STATUS/SIOC.

Tracing `DVACT`'s value 4005 (octal) through TRM3:

```
Input:     A = 4005 octal = 0000 1000 0000 0101
TRMVC:     R2 = ~A = ~4005 = 1111 0111 1111 1010

TRM3 tests R2 bit 0: = 0 (original bit 0 was 1 = interrupt enable ON)
  → Takes F=0 path, jumps to TRM31

TRM31:
  R3 = 74002 octal    = 0111 1000 0000 0010  (mask for bits 1,11,12,13,14)
  R2 = R2 AND R3      = 0111 0000 0000 0010  = 070002 octal

  Q = old_STATUS AND 175 octal  (preserves STATUS bits 0,2,3,4,5,6)
  Q = R2 OR Q                    (merges in bits 1,11,12,13,14 from control)

  → WSIOC: writes Q to STATUS register + COMM,SIOC
```

Resulting bits from the 4005 control value (after inversion and masking):

| Bit | Value in 070002 | IOX 303 meaning | Effect |
|-----|-----------------|-----------------|--------|
| 1 | 1 | Enable RX interrupt | RX data available interrupt enabled |
| 11 | **0** | Word length bit 11 | } |
| 12 | **0** | Word length bit 12 | } **bit 11=0, bit 12=0 → 8-bit word length** |
| 13 | 1 | Stop bits | 1 stop bit |
| 14 | 1 | Parity enable | Parity checking enabled |

**Because TRM3 inverts the input**, the original 4005 value with bit 11=1 becomes bit 11=0 in STATUS. Combined with bit 12=0, the IOX 303 spec gives **8-bit word length**, not 7-bit.

This means `DVACT` configures the UART for **8 data bits + parity + 1 stop bit** via the STATUS/SIOC mechanism. The bits 11-14 are stored in the STATUS scratch register and written to hardware via `COMM,SIOC`.

#### Summary: How BPUN Transfer Works on Device 300

The microcode uses two independent mechanisms to handle 7-bit ASCII and 8-bit binary on the same serial port:

1. **Hardware configuration via IOX 303/TRM3/SIOC**: `DVACT` writes 4005 to IOX 303. After TRM3's inversion and masking, the STATUS/SIOC word has bits 11=0, 12=0 → **8-bit word length**, bit 14=1 → parity enabled, bit 13=1 → 1 stop bit. The UART hardware is configured for 8 data bits on the wire.

2. **Software masking via STS register**: The microcode applies a data mask after reading:
   - MOPC INPUT: AND 0177 (7 bits) — strips bit 7 for terminal ASCII
   - Binary loader ASCII phase: AND STS=0x7F — same 7-bit mask
   - Binary loader binary phase (after `!`): AND STS=0xFF — full 8 bits used

3. **Parity is never validated** by the microcode in any mode. The UART hardware reports parity errors in IOR bit 9, and TRM2 (IOX 302) relays them as bit 6 in the status word, but no MOPC or binary loader code ever tests these error bits.

**For sending BPUN files**: The host should use **8 data bits** at the matching baud rate. The UART is configured for 8-bit word length by DVACT. The preamble ASCII section is masked to 7 bits by the microcode (STS=0x7F), and the binary section after `!` uses all 8 bits (STS=0xFF). Parity errors are ignored.

#### Emulator Implementation

The emulator's `COMM,SIOC` handler in Cpu.cs extracts UART configuration from IOControl bits 11-14 and passes them to `Uart.ConfigureFromSIOC()`. The Uart class applies:

- **Word length masking**: `DataMask` is set to 0x7F (7-bit) or 0xFF (8-bit) based on IOControl bit 12. The mask is applied in `try_read_next()` when a byte is dequeued from the KeyboardQueue into the IOR data bits.
- **Parity error detection**: When parity is enabled (IOControl bit 14 = 0), the UART computes even parity over the data bits and sets IOR bit 9 (PARITY_ERROR) if the parity bit in the received byte doesn't match.

Default state (IOControl = 0): 7-bit word length, parity enabled (7E1 terminal mode).
After DVACT's IOX 303 = 4005: 8-bit word length, parity disabled (8N1 binary loader mode).

Files modified: `ND110CPU/Uart.cs`, `ND110CPU/Cpu.cs`.

### Example: Python Transfer Script

```python
import serial
import time

def send_bpun(port, baud, bpun_path):
    ser = serial.Serial(port, baud, timeout=1)

    # 1. Stop the CPU
    ser.write(b'STOP\r')
    time.sleep(0.5)

    # 2. Activate binary loader on console (device 300)
    ser.write(b'300$')
    time.sleep(0.1)

    # 3. Send the BPUN file directly
    with open(bpun_path, 'rb') as f:
        data = f.read()
    ser.write(data)

    ser.close()
```

### Alternative: Octal Deposit Loop (Slower, Simpler)

For small amounts of data or interactive use, deposit word-by-word:

1. `<address>/` -- examine first location
2. `<value><CR>` -- deposit and auto-advance to next address
3. Repeat step 2 for consecutive locations
4. `<start-address>!` -- start execution

This is roughly 3x slower than BPUN transfer: each word requires 6+ ASCII characters (octal digits + CR) vs. 2 binary bytes in the BPUN data section.

### Mass Storage Boot

For loading from disk/floppy, use a load code with bit 13 set (e.g. `21560$`). The `MASS` loader (CS 002147) performs block-oriented IOX to the disk controller. This is not a serial transfer -- it uses the disk I/O bus.

---

## Appendix: OPCOM Binary Loader in C

The following C code is a direct translation of the ND-110 MOPC binary loader microcode
(labels `ETLO1` through `ESCAP` in `ND-110-RASK.LISTING.TXT`). It implements the exact
protocol that the microcode executes when `$` or `&` is typed at the MOPC, showing how
the CPU reads a BPUN file byte-by-byte from a serial device.

This can serve as:
- A reference for implementing a host-side BPUN sender
- A test harness for validating BPUN files before sending
- Documentation of the exact protocol behavior

```c
/*
 * ND-110 OPCOM Binary Loader — C reference implementation
 *
 * Translated from ND-110 microcode (ND-110-RASK.LISTING.TXT):
 *   ETLO1  (CS 002210) — Binary loader entry, device activation
 *   DVACT  (CS 002243) — Activate device (IOX N+3 with 4005)
 *   SEEK   (CS 002211) — Store possible start address
 *   SIKI   (CS 002212) — Read octal numbers from serial stream
 *   ASS8   (CS 002245) — Subroutine: read ASCII octal number, return on non-octal char
 *   INCH   (CS 002236) — Subroutine: read one byte from device (polls until data ready)
 *   EXFOU  (CS 002217) — '!' found: switch to binary mode, read address/count
 *   BIN    (CS 002255) — Subroutine: read two bytes, combine into 16-bit word (big-endian)
 *   STLP   (CS 002223) — Loop: read data words, write to memory, accumulate checksum
 *   ESCAP  (CS 002530) — Cleanup: set terminal to normal mode, reset MOPC state
 *
 * The loader has two phases:
 *   1. ASCII phase (before '!'): reads octal addresses from 7-bit ASCII characters
 *   2. Binary phase (after '!'): reads raw 8-bit binary data (address, count, data, checksum)
 *
 * The word length mask (STS register) switches from 0x7F to 0xFF at the '!' delimiter.
 * DVACT configures the UART for 8-bit word length via IOX 303/TRM3/SIOC (value 4005,
 * which after TRM3 inversion sets IOControl bit 12=1 for 8-bit mode).
 *
 * Protocol matches the BPUN file format: http://www.ndwiki.org/wiki/BPUN_File_Format
 */

#include <stdio.h>
#include <stdint.h>
#include <stdbool.h>

/* ---- Hardware abstraction ---- */

/*
 * Read one byte from the serial device.
 *
 * Corresponds to microcode subroutine INCH (CS 002236):
 *   1. IOX N+2: poll status register, test bit 3 (data ready)
 *   2. Loop until data ready
 *   3. IOX N: read data byte
 *   4. Mask with STS register (0x7F in ASCII phase, 0xFF in binary phase)
 *   5. IOX N+3: re-activate device (DVACT with 4005)
 *
 * In the microcode, INCH blocks in a tight polling loop with no timeout.
 * If no byte arrives, the CPU hangs until a panel interrupt (STOP/MACL).
 */
typedef uint8_t (*read_byte_fn)(void *ctx);

/*
 * Write one 16-bit word to physical memory at the given address.
 *
 * Corresponds to microcode COMM,WRRQ,PT in the STLP loop (CS 002226).
 * Memory is written through the page tables (PT mode), so the address
 * is a physical address in the ND-110's 128KB memory space.
 */
typedef void (*write_word_fn)(void *ctx, uint16_t address, uint16_t word);


/* ---- ASS8: Read ASCII octal number from serial stream ----
 *
 * Microcode: ASS8 (CS 002245) / ASS81 (CS 002247) / ASS82 (CS 002246)
 *
 * Reads characters one at a time via INCH. Accumulates octal digits
 * (ASCII '0'-'7', codes 060-067) into a 16-bit number by shifting left 3
 * and OR-ing the digit value. Stops on the first non-octal character.
 *
 * Returns:
 *   result->value  = accumulated octal number (0 if no digits read)
 *   result->last_char = the non-octal character that terminated the number
 *
 * In the microcode, the accumulated number is stored in OCTNR (register file)
 * and the last character's classification is returned in R2/R3 for the caller
 * to dispatch on (CR, '!', or other).
 */
typedef struct {
    uint16_t value;      /* Accumulated octal number */
    uint8_t  last_char;  /* The non-octal character that stopped accumulation */
} octal_result_t;

static octal_result_t read_octal(read_byte_fn read_byte, void *ctx, uint8_t mask)
{
    octal_result_t r;
    r.value = 0;

    for (;;) {
        uint8_t ch = read_byte(ctx) & mask;

        /* Check if octal digit: ASCII '0' (060) through '7' (067) */
        if (ch >= '0' && ch <= '7') {
            /*
             * Microcode OCDIG (CS 002514): shift current value left 3 bits,
             * OR in new digit. This is done via three rotate-left-one-bit
             * operations on OCTNR followed by masking and OR with the digit.
             */
            r.value = (r.value << 3) | (ch - '0');
        } else {
            /* Non-octal character terminates the number */
            r.last_char = ch;
            return r;
        }
    }
}


/* ---- BIN: Read one 16-bit word as two binary bytes ----
 *
 * Microcode: BIN (CS 002255)
 *
 * Reads exactly 2 bytes from the device via INCH:
 *   1. Read first byte (high byte)
 *   2. Swap to upper 8 bits (microcode uses IDBS,SWAP)
 *   3. Read second byte (low byte)
 *   4. OR together to form 16-bit word
 *
 * The result is returned in the Z register in the microcode.
 * Both bytes are read with the current STS mask (0xFF in binary phase).
 *
 * Wire format: [high_byte] [low_byte]  (big-endian, network byte order)
 */
static uint16_t read_bin_word(read_byte_fn read_byte, void *ctx)
{
    uint8_t high = read_byte(ctx);  /* INCH → swap to upper position */
    uint8_t low  = read_byte(ctx);  /* INCH → lower position */
    return (uint16_t)((high << 8) | low);
}


/* ---- Result codes from the binary loader ---- */
typedef enum {
    LOAD_OK_START   = 0,  /* Action=0: program loaded, start execution at P */
    LOAD_OK_STOP    = 1,  /* Action≠0: program loaded, stay in MOPC, P=start addr */
    LOAD_ERR_CHKSUM = 2,  /* Checksum mismatch: prints '?' in MOPC */
} load_result_t;


/* ---- Main binary loader: ETLO1 ----
 *
 * Microcode: ETLO1 (CS 002210) through ESCAP (CS 002530)
 *
 * This is the complete binary loader as executed by the ND-110 microcode
 * when '$' or '&' is typed at the MOPC with a device number.
 *
 * Parameters:
 *   read_byte   — function to read one byte from the serial device (blocks until ready)
 *   write_word  — function to write one 16-bit word to CPU memory
 *   ctx         — opaque context passed to read_byte and write_word
 *   start_addr  — [out] receives the program start address (P register value)
 *
 * Returns:
 *   LOAD_OK_START   — loaded successfully, action=0, CPU should start at *start_addr
 *   LOAD_OK_STOP    — loaded successfully, action≠0, CPU stays stopped, P=*start_addr
 *   LOAD_ERR_CHKSUM — checksum mismatch, transfer failed
 *
 * Protocol overview:
 *   1. DVACT activates device (IOX N+3 with 4005 → configures UART 8-bit mode)
 *   2. SEEK/SIKI loop reads ASCII preamble (7-bit masked) looking for addresses and '!'
 *   3. '!' triggers EXFOU which switches to 8-bit binary mode
 *   4. Binary phase reads: address (2 bytes), count (2 bytes), data (count words), checksum (2 bytes)
 *   5. Checksum verified (additive sum mod 2^16)
 *   6. Action code read as ASCII octal: 0=start, non-zero=stay stopped
 */
static load_result_t opcom_binary_loader(
    read_byte_fn  read_byte,
    write_word_fn write_word,
    void         *ctx,
    uint16_t     *start_addr)
{
    /*
     * Phase 1: ASCII preamble (SEEK/SIKI loop)
     *
     * Microcode labels: SEEK (CS 002211), SIKI (CS 002212), ASS8
     *
     * STS mask = 0x7F (7-bit) — set at CS 002146 before ETLO1:
     *   "A,7 B,STS ALUF,D-1 ALUD,B IDBS,BMG"
     *   → STS = BMG[7] - 1 = 0x0080 - 1 = 0x007F
     *
     * The loop reads octal numbers from the serial stream.
     * Each number terminated by CR is stored as a possible start address.
     * The '!' character ends the preamble and starts the binary phase.
     * Any other non-octal character is ignored (continues reading).
     */
    uint8_t ascii_mask = 0x7F;
    *start_addr = 0;

    for (;;) {
        octal_result_t r = read_octal(read_byte, ctx, ascii_mask);

        /*
         * SEEK (CS 002211): "A,R5 B,P ALUF,PASSA ALUD,B"
         * → P = R5 (the last octal number read as possible start address)
         *
         * The start address is updated every time an octal number is
         * followed by CR. Multiple addresses may appear in the preamble;
         * only the last one before '!' is used as P.
         */
        *start_addr = r.value;

        /*
         * SIKI (CS 002213): After ASS8 returns, check the terminating character.
         *
         * R2 holds the character classification from ASS8.
         * The microcode compares against:
         *   - 15 (CR): "A,R2 B,15 ALUF,A-D COND,F=0" at CS 002213
         *     If CR: loop back to SEEK (store start addr, read next number)
         *   - Then checks if char was '!' (comparison with L at CS 002214)
         *     If '!': jump to EXFOU (binary phase)
         *   - Otherwise: loop back to SIKI (ignore char, read next number)
         */
        if (r.last_char == '\r') {
            /* CR: start address recorded, continue reading preamble */
            continue;
        }

        if (r.last_char == '!') {
            /* '!' found: end of preamble, enter binary data phase */
            break;
        }

        /* Other non-octal character: ignore, keep reading.
         * The microcode loops back to SIKI (CS 002216). */
    }


    /*
     * Phase 2: Binary data (EXFOU → STLP → checksum → action)
     *
     * EXFOU (CS 002217): "A,10 B,STS ALUF,D-1 ALUD,B IDBS,BMG"
     *   → STS = BMG[8] - 1 = 0x0100 - 1 = 0x00FF (8-bit mask)
     *
     * From this point, all reads via INCH use the full 8-bit byte.
     * The UART was already configured for 8-bit word length by DVACT.
     */

    /* Read core address: where to load in memory.
     * EXFOU (CS 002220): "B,L ALUF,ZERO ALUD,B ... BIN"
     *   → L = 0 (clear checksum accumulator)
     *   → X = BIN result (core address from 2-byte big-endian field)
     */
    uint16_t checksum_acc = 0;  /* L register: additive checksum accumulator */
    uint16_t load_address = read_bin_word(read_byte, ctx);  /* → X register */

    /* Read word count: how many 16-bit words to load.
     * EXFOU (CS 002221): "A,Z B,X ALUF,PASSA ALUD,B ... BIN"
     *   → T = BIN result (word count from 2-byte big-endian field)
     */
    uint16_t word_count = read_bin_word(read_byte, ctx);    /* → T register */

    /*
     * STLP loop (CS 002223-002226): Read data words, write to memory.
     *
     * For each word:
     *   BIN          → Z = read 16-bit word (2 bytes, big-endian)
     *   WRRQ,PT      → write Z to memory at address X (physical, via page table)
     *   X++          → increment memory address ("ALUF,B+1 ALUD,B,YA")
     *   T--          → decrement word count ("ALUF,B-1 ALUD,B")
     *   L += Z       → accumulate checksum ("ALUF,A+B ALUD,B,YA" at CS 002226)
     *   if T == 0    → exit loop (COND,F=0 at CS 002224)
     *
     * Note: the checksum is additive (mod 2^16), NOT XOR.
     * The XOR at CS 002230 is only for the final comparison.
     */
    uint16_t addr = load_address;
    uint16_t remaining = word_count;

    while (remaining > 0) {
        uint16_t word = read_bin_word(read_byte, ctx);  /* BIN → Z register */

        write_word(ctx, addr, word);                     /* COMM,WRRQ,PT */

        checksum_acc = (uint16_t)(checksum_acc + word);  /* L += Z (mod 2^16) */
        addr++;                                          /* X++ */
        remaining--;                                     /* T-- */
    }


    /*
     * Checksum verification (CS 002227-002231):
     *
     *   BIN → read 2-byte checksum from file into Z
     *   "A,Z B,L ALUF,XORAB ALUD,NONE" → test Z XOR L
     *   COND,F=0: if XOR result is 0, checksum matches
     *
     * The file's checksum field should equal the additive sum (mod 2^16)
     * of all data words. The XOR comparison is just a way to test equality:
     * if file_checksum == accumulated_sum, then XOR == 0.
     */
    uint16_t file_checksum = read_bin_word(read_byte, ctx);

    if ((file_checksum ^ checksum_acc) != 0) {
        /*
         * Checksum mismatch: microcode jumps to ILLEG (CS 002413)
         * which prints '?' to the terminal, then goes to RONLY → SPACE → CONT.
         * The CPU returns to MOPC idle state. The loaded data is in memory
         * but cannot be trusted.
         */
        return LOAD_ERR_CHKSUM;
    }


    /*
     * Action code (CS 002232-002235):
     *
     * ASS8 reads an ASCII octal number from the serial stream.
     * This is back in ASCII mode (though the mask is still 0xFF,
     * ASS8 only processes ASCII digits 060-067).
     *
     * The result is in R2:
     *   R2 = 0: "COMM,START" issued (CS 002235), then ESCAP
     *     → CPU starts executing at the address in P (set during SEEK phase)
     *   R2 ≠ 0: jump to ESCAP directly (CS 002233)
     *     → CPU stays stopped, P = start address, returns to MOPC
     *
     * Note: the action code terminator character is consumed by ASS8
     * but not acted upon. Typically it's a CR or end of file.
     */
    octal_result_t action = read_octal(read_byte, ctx, 0xFF);

    if (action.value == 0) {
        /* Action = 0: start execution.
         * Microcode: "COMM,START T,JMP ESCAP" at CS 002235.
         * ESCAP sets STATUS bit 6 (normal terminal mode) via WSIOC/COMM,SIOC,
         * then falls through RONLY → SPACE → CONT (COMM,CONTINUE).
         * The CPU begins executing at *start_addr. */
        return LOAD_OK_START;
    } else {
        /* Action ≠ 0: stay in MOPC.
         * Microcode: jumps to ESCAP without COMM,START (CS 002233).
         * ESCAP → RONLY → SPACE → CONT.
         * The CPU returns to MOPC stop mode with P = *start_addr. */
        return LOAD_OK_STOP;
    }
}


/* ---- Example: load a BPUN file from a byte buffer ---- */

/*
 * Context for reading from an in-memory buffer.
 * Simulates what happens when a BPUN file is streamed to the serial port.
 */
typedef struct {
    const uint8_t *data;
    size_t         length;
    size_t         pos;
    uint16_t       memory[65536]; /* 64K words of ND-110 memory */
} loader_ctx_t;

/*
 * Read one byte from the buffer.
 * In real hardware, this would poll the UART via IOX 302/300
 * in the INCH microcode loop until a byte is available.
 */
static uint8_t buffer_read_byte(void *ctx)
{
    loader_ctx_t *c = (loader_ctx_t *)ctx;
    if (c->pos >= c->length) {
        /*
         * No more data. On real hardware, INCH would poll forever.
         * In a test harness, this is an error condition.
         */
        fprintf(stderr, "ERROR: unexpected end of data at offset %zu\n", c->pos);
        return 0;
    }
    return c->data[c->pos++];
}

/*
 * Write one word to the simulated memory.
 * On real hardware: COMM,WRRQ,PT writes to physical memory via page tables.
 */
static void buffer_write_word(void *ctx, uint16_t address, uint16_t word)
{
    loader_ctx_t *c = (loader_ctx_t *)ctx;
    c->memory[address] = word;
}

/*
 * Load a BPUN file from a byte buffer, using the same protocol
 * the ND-110 microcode uses when reading from the serial port.
 */
int load_bpun_buffer(const uint8_t *bpun_data, size_t bpun_length)
{
    loader_ctx_t ctx;
    ctx.data   = bpun_data;
    ctx.length = bpun_length;
    ctx.pos    = 0;

    for (int i = 0; i < 65536; i++)
        ctx.memory[i] = 0;

    uint16_t start_addr = 0;
    load_result_t result = opcom_binary_loader(
        buffer_read_byte,
        buffer_write_word,
        &ctx,
        &start_addr);

    switch (result) {
    case LOAD_OK_START:
        printf("Load OK: start execution at %06o\n", start_addr);
        return 0;
    case LOAD_OK_STOP:
        printf("Load OK: stay in MOPC, P = %06o\n", start_addr);
        return 0;
    case LOAD_ERR_CHKSUM:
        printf("Load FAILED: checksum mismatch\n");
        return 1;
    }
    return 1;
}
```
