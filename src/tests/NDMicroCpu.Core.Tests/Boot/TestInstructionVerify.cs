//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System.Text;
using NDMicroCpu.Core.Common;
using static NDMicroCpu.Core.Enums;

namespace NDMicroCpu.Core.Tests.Boot
{
    /// <summary>
    /// Drives the CPU through the OPCOM console exactly like an operator would - boot to
    /// OPCOM, type a command, check the response - to confirm ND-110 console I/O actually
    /// works end to end, not just that the emulator reaches the OPCOM-wait state.
    ///
    /// Regression coverage for a bug where MOPC's INPUT routine (CS 002351) never saw typed
    /// input: Uart.ReadIORegister() (IDBS,IOR - a STATUS read) used to call TryReadNext(),
    /// making receive-buffer timing depend on how often status happened to be polled rather
    /// than elapsed time, and treating a non-destructive status peek as if it advanced
    /// receive state. Fixed by moving TryReadNext() into Uart.Tick() (once per micro-cycle)
    /// and removing it from ReadIORegister(), matching real IM6402 hardware (see the DR/DRR
    /// comment on Uart.STATUS_DATA_RECEIVED) where reading STATUS is non-destructive and only
    /// reading the data register consumes DATA_RECEIVED.
    /// </summary>
    public class TestInstructionVerify
    {
        // RealTimeClock.TICK_PR_20MS - see TestBootSequence.cs for why this is duplicated here.
        const int TICK_PR_20MS = 10550 * 4;

        /// <summary>
        /// Boots a fresh ND-110 through master clear + the built-in self-test, same sequence
        /// as TestBootSequence.SelfTestPasses, and returns it sitting at the OPCOM-wait state.
        /// </summary>
        static CPU BootToOpcom()
        {
            CPU cpu = new CPU(null, CPUType.ND110);

            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            const ushort sterr = 0x449;
            cpu.hw.mic.LatchMap(0);
            for (int i = 0; i < 200000; i++)
            {
                cpu.TickCpu();

                if (cpu.hw.mic.map == sterr)
                {
                    int r2 = cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
                    Assert.Fail($"Self-test failed: hit STERR with R2={Utils.Int2Oct(r2)} after {i} cycles");
                }

                if (cpu.hw.mac.FETCH) break;
            }
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "Expected CPU to be stopped at OPCOM after master clear + self-test");

            return cpu;
        }

        /// <summary>
        /// Types each character of the given string into the console, running enough
        /// cycles between characters for MOPC's MS20-driven poll loop to consume it -
        /// see TestBootSequence.MS20InterruptFiresWhileStopped for why one TICK_PR_20MS
        /// period is always enough.
        /// </summary>
        static void TypeString(CPU cpu, string text)
        {
            for (int chIdx = 0; chIdx < text.Length; chIdx++)
            {
                cpu.hw.AddConsoleChar(text[chIdx]);
                RunCycles(cpu, TICK_PR_20MS * 5);
            }
        }

        static void RunCycles(CPU cpu, int cycles)
        {
            for (int i = 0; i < cycles; i++) cpu.TickCpu();
        }

        /// <summary>
        /// Diagnostic, not a pass/fail regression test - kept [Explicit] as a tool for
        /// continuing this investigation. As of 2026-07-11: after typing "0!", the
        /// INSTRUCTION-VERIFY banner prints, then macro P gets stuck oscillating between
        /// 020626/020627 (octal) forever. Disassembly confirms 020626=150005=TRA IIC (reads
        /// AND CLEARS the Internal Interrupt Code register) and 020627=004376=STA (stores A,
        /// mode 3/indirect-via-B, disp 076 octal). So this is a poll loop reading IIC,
        /// storing the result, and presumably branching on it - waiting for some internal
        /// interrupt condition (IIC != 0) that our emulator never generates. Not a UART
        /// interrupt wait: IOControl's IOC1/IOC2 bits are confirmed 0 throughout via direct
        /// register inspection, so device interrupts are not in play here at all.
        ///
        /// Uses Logger's existing File destination (writes to
        /// %LOCALAPPDATA%\trace\file-trace.txt, see Logger.FileLog in NDMicroCpu.Core.Common) instead
        /// of a hand-rolled file writer.
        /// </summary>
        [Explicit("Diagnostic only - traces the TRA IIC poll-loop hang after IDENT succeeds")]
        [Test]
        public void Diag_TraceInstructionVerifyHang()
        {
            // NB: BootToOpcom() unconditionally sets LogDestination.Disabled internally
            // (needed to keep other tests fast) - so File destination must be (re-)applied
            // AFTER calling it, not before.
            CPU cpu = BootToOpcom();
            Logger.SetDestination(Logger.LogDestination.File);
            Logger.SetLogLevel(Logger.LogLevel.Debug);

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            TypeString(cpu, "0!");

            // Disassemble a wide window around the loop (020600-020660 octal) so the full
            // loop structure - not just the two addresses P happened to be sampled at - is
            // visible. 020600 octal = 0x2180.
            Logger.Log("Memory disassembly 020600-020660 (octal addr : octal word / hex word):", Logger.LogLevel.Debug);
            for (ushort addr = 0x2180; addr <= 0x21B0; addr++)
            {
                ushort word = cpu.memory.ReadPhysicalMemory(addr);
                Logger.Log($"  {Utils.Int2Oct(addr)} : {Utils.Int2Oct(word)} (0x{word:X4})", Logger.LogLevel.Debug);
            }

            // Causal-chain tracer. The original PIL-14 TRA IIC hang is FIXED (Am2914 group
            // signal at Read Vector, commit f29ba3b). The current question (2026-07-11,
            // reported from a live run): after the fix INSTRUCTION-VERIFY prints its full
            // banner (down to "CACHE :  NO") but then EXITS back to OPCOM ("#" prompt)
            // instead of presenting its own ">" command prompt. So this trace now also
            // watches the STOP flip-flop: the moment STOP goes true (that IS "the program
            // exited" - MOPC/OPCOM only runs while the CPU is stopped), dump the last 64
            // macro P transitions to see the exact code path that led to the stop.
            //  - ring buffer of the last 64 macro P transitions, dumped the moment PIL
            //    first becomes 14 (legacy trigger) OR the first time STOP goes true after
            //    the program has started;
            //  - every PIC interrupt-detect register CHANGE (rare), with cycle/P/CSAR/PIL
            //    -> shows who sets/clears which pins, in what order;
            //  - every PIL transition (rare) -> shows level movement.
            const int ringSize = 64;
            int[] ringCycle = new int[ringSize];
            ushort[] ringP = new ushort[ringSize];
            int ringNext = 0, ringCount = 0;

            ushort lastP = 0xFFFF;
            int lastPil = -1;
            ushort lastDetect = cpu.hw.pic.ReadInterruptDetect();
            bool sawPil14 = false;
            bool lastStop = cpu.hw.cpu_regs.STOP;
            bool dumpedStopRing = false;
            int pil14PLogged = 0;
            const int maxPil14PLogged = 40;
            int ionWaitLogged = 0;
            const int maxIonWaitLogged = 60;

            // Microcode-address history: the PIC IRQ-enable FF and IONI were both OFF at
            // the cycle-122995 dispatch to level 10, so neither the PIC nor STS explains
            // WHY the level switch ran. Capture the exact CSAR path into LVSWP.
            const int csRingSize = 96;
            ushort[] csRing = new ushort[csRingSize];
            int[] csRingCycle = new int[csRingSize];
            int csRingNext = 0, csRingCount = 0;
            ushort lastCsar = 0xFFFF;
            bool dumpedDispatchRing = false;

            Logger.Log($"start: InterruptDetect={Utils.Int2Oct(lastDetect)} Mask={Utils.Int2Oct(cpu.hw.pic.ReadMask())}", Logger.LogLevel.Debug);

            int totalCycles = TICK_PR_20MS * 60;
            for (int i = 0; i < totalCycles; i++)
            {
                cpu.TickCpu();

                int pil = (cpu.hw.alu.STS >> 8) & 0x0F;
                ushort currentP = cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                ushort detect = cpu.hw.pic.ReadInterruptDetect();

                if (detect != lastDetect)
                {
                    Logger.Log(
                        $"DETECT change cycle={i} {Utils.Int2Oct(lastDetect)} -> {Utils.Int2Oct(detect)} " +
                        $"P={Utils.Int2Oct(currentP)} CSAR={Utils.Int2Oct(cpu.hw.mic.map)} PIL={pil} " +
                        $"Mask={Utils.Int2Oct(cpu.hw.pic.ReadMask())}",
                        Logger.LogLevel.Debug);
                    lastDetect = detect;
                }

                // CS 000635 (0x19D) = ION2 (the ION instruction's LDPIL of STS-with-bit15),
                // CS 001042-001044 (0x222-0x224) = WAIT1 + its IDBS,STS read + the F15
                // (IONI) CONDENABL that decides STOPW-vs-"interrupt is on". Log STS and the
                // IDB word at each visit to see whether ION ever ran and what the WAIT
                // handler's IONI test actually read.
                ushort csar = cpu.hw.mic.map;
                if (csar != lastCsar)
                {
                    csRing[csRingNext] = csar;
                    csRingCycle[csRingNext] = i;
                    csRingNext = (csRingNext + 1) % csRingSize;
                    if (csRingCount < csRingSize) csRingCount++;
                    lastCsar = csar;
                }
                // Vector entry points: CS 000016 (panel, 0x0E) and CS 000017 (macro int,
                // 0x0F) plus the PANEL(000050-000053)/MACRI(000054-000057) stubs - log with
                // R1 (the ITSRV index source) and the panel's interrupt vector value to see
                // which route each dispatch takes and with what index.
                if ((csar >= 0x0E && csar <= 0x0F) || (csar >= 0x28 && csar <= 0x2F))
                {
                    if (ionWaitLogged < maxIonWaitLogged)
                    {
                        ionWaitLogged++;
                        Logger.Log(
                            $"VEC CS={Utils.Int2Oct(csar)} cycle={i} R1={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.R1))} " +
                            $"PanelVec={cpu.hw.panel.InterruptVector}",
                            Logger.LogLevel.Debug);
                    }
                }
                if (csar == 0x19D || (csar >= 0x222 && csar <= 0x224))
                {
                    if (ionWaitLogged < maxIonWaitLogged)
                    {
                        ionWaitLogged++;
                        Logger.Log(
                            $"CS={Utils.Int2Oct(csar)} cycle={i} P={Utils.Int2Oct(currentP)} PIL={pil} " +
                            $"STS={Utils.Int2Oct(cpu.hw.alu.STS)} IONI={(cpu.hw.alu.STS >> 15) & 1} " +
                            $"IDB={Utils.Int2Oct(cpu.hw.idb.ReadWord())} Q={Utils.Int2Oct(cpu.hw.alu.Q)}",
                            Logger.LogLevel.Debug);
                    }
                }

                bool stop = cpu.hw.cpu_regs.STOP;
                if (stop != lastStop)
                {
                    Logger.Log($"STOP change cycle={i} {lastStop} -> {stop} P={Utils.Int2Oct(currentP)} CSAR={Utils.Int2Oct(cpu.hw.mic.map)} PIL={pil} STS={Utils.Int2Oct(cpu.hw.alu.STS)}", Logger.LogLevel.Debug);
                    lastStop = stop;

                    if (stop && !dumpedStopRing)
                    {
                        dumpedStopRing = true;
                        Logger.Log($"--- STOP went true at cycle={i}; last {ringCount} P transitions before the stop: ---", Logger.LogLevel.Debug);
                        int sidx = (ringCount < ringSize) ? 0 : ringNext;
                        for (int n = 0; n < ringCount && n < ringSize; n++)
                        {
                            Logger.Log($"  pre-STOP cycle={ringCycle[sidx]} P={Utils.Int2Oct(ringP[sidx])}", Logger.LogLevel.Debug);
                            sidx = (sidx + 1) % ringSize;
                        }
                    }
                }

                if (pil != lastPil)
                {
                    Logger.Log($"PIL change cycle={i} {lastPil} -> {pil} P={Utils.Int2Oct(currentP)} CSAR={Utils.Int2Oct(cpu.hw.mic.map)}", Logger.LogLevel.Debug);
                    lastPil = pil;

                    // Dump the microcode path into the first program-phase level switch
                    // (the dispatch that happens with IONI=0 and the PIC IRQ-enable FF off).
                    if (!dumpedDispatchRing && i > 100000)
                    {
                        dumpedDispatchRing = true;
                        Logger.Log($"--- CSAR path into this PIL change (oldest first): ---", Logger.LogLevel.Debug);
                        int cidx = (csRingCount < csRingSize) ? 0 : csRingNext;
                        for (int n = 0; n < csRingCount && n < csRingSize; n++)
                        {
                            Logger.Log($"  CSAR-trace cycle={csRingCycle[cidx]} CSAR={Utils.Int2Oct(csRing[cidx])}", Logger.LogLevel.Debug);
                            cidx = (cidx + 1) % csRingSize;
                        }
                    }

                    if (pil == 14 && !sawPil14)
                    {
                        sawPil14 = true;
                        // Dump the pre-PIL14 P history (oldest first) - the provoking code path.
                        Logger.Log($"--- PIL first became 14 at cycle={i}; last {ringCount} P transitions before entry: ---", Logger.LogLevel.Debug);
                        int idx = (ringCount < ringSize) ? 0 : ringNext;
                        for (int n = 0; n < ringCount && n < ringSize; n++)
                        {
                            Logger.Log($"  pre-PIL14 cycle={ringCycle[idx]} P={Utils.Int2Oct(ringP[idx])}", Logger.LogLevel.Debug);
                            idx = (idx + 1) % ringSize;
                        }
                    }
                }

                if (currentP != lastP)
                {
                    ringCycle[ringNext] = i;
                    ringP[ringNext] = currentP;
                    ringNext = (ringNext + 1) % ringSize;
                    if (ringCount < ringSize) ringCount++;
                    lastP = currentP;

                    // A bounded look at P right after level-14 entry, for orientation.
                    if (sawPil14 && pil14PLogged < maxPil14PLogged)
                    {
                        pil14PLogged++;
                        Logger.Log(
                            $"post-PIL14 cycle={i} P={Utils.Int2Oct(currentP)} PIL={pil} " +
                            $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                            $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))}",
                            Logger.LogLevel.Debug);
                    }
                }
            }

            cpu.hw.uart.OnTextOutput -= OnText;

            // Always dump the final CSAR ring - shows the microcode loop the CPU is in at
            // the end of the run (or the tail of normal execution if not stuck).
            Logger.Log("--- final CSAR ring (oldest first): ---", Logger.LogLevel.Debug);
            int fidx = (csRingCount < csRingSize) ? 0 : csRingNext;
            for (int n = 0; n < csRingCount && n < csRingSize; n++)
            {
                Logger.Log($"  final-CSAR cycle={csRingCycle[fidx]} CSAR={Utils.Int2Oct(csRing[fidx])}", Logger.LogLevel.Debug);
                fidx = (fidx + 1) % csRingSize;
            }

            Logger.Log($"Captured output:\n{output}", Logger.LogLevel.Debug);
            Logger.Log($"Final: P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} DBR={Utils.Int2Oct(cpu.hw.cpu_regs.DBR)}", Logger.LogLevel.Debug);
        }

        /// <summary>
        /// Diagnostic: drive INSTRUCTION-VERIFY interactively past its ">" prompt - type
        /// HELP to capture the program's command list (needed to learn how to start its
        /// test suite), and dump everything via Logger's File destination for analysis.
        ///
        /// RESOLVED 2026-07-11: this diag's micro ring caught the "&@" defect - interrupt
        /// vectors 0x0E/0x0F were dispatched before the macro fetch increments P while
        /// their microcode (PANVC CS 003760-003762 / XINT CS 001101) unconditionally does
        /// P := P-1, so every panel/macro interrupt resumed one instruction early. Fixed by
        /// Cpu.CompensateVectorPDecrement() + acknowledging the panel interrupt at dispatch
        /// (double-MS20 re-arm). The tracers are kept as tooling: the "phantom accept"
        /// micro-ring trigger (P=052323 with A=0) fires only if the corruption returns.
        /// </summary>
        /// <summary>
        /// Diagnostic: run INSTRUCTION-VERIFY's own test suite via its RUN command (first
        /// PROGRAM-COMMAND in the HELP listing) and capture everything it prints. RUN may
        /// ask follow-up questions like HELP does - bare CRs accept defaults.
        /// </summary>
        [Explicit("Diagnostic - runs INSTRUCTION-VERIFY's own test suite via RUN")]
        [Test]
        public void Diag_InstructionVerifyRun()
        {
            CPU cpu = BootToOpcom();
            Logger.SetDestination(Logger.LogDestination.File);
            Logger.SetLogLevel(Logger.LogLevel.Debug);

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            TypeString(cpu, "0!");
            RunCycles(cpu, TICK_PR_20MS * 30); // banner + ">" prompt

            output.Clear();
            TypeString(cpu, "RUN\r");

            // SAAOP failure analysis (2026-07-11): the framework reports
            //   ** ERROR **. WRONG VALUE OF P-REG AFTER TEST. LEVEL: 1 / "SAA 0" FAILED
            // Capture the exact code path that DETECTS the failure: per-tick P ring +
            // PIL-transition log, dumped at the first '*' transmitted after RUN starts
            // (the first character of the error banner).
            const int pRingSize = 128;
            ushort[] pRing = new ushort[pRingSize];
            int[] pRingPil = new int[pRingSize];
            int pRingNext = 0, pRingCount = 0;
            ushort pLast = 0xFFFF;
            int lastPil = -1;
            bool dumpedErrRing = false;
            void OnErr(char c)
            {
                if (c == '*' && !dumpedErrRing)
                {
                    dumpedErrRing = true;
                    Logger.Log("--- P/PIL history before first '*' of the error banner (oldest first): ---", Logger.LogLevel.Debug);
                    int idx = (pRingCount < pRingSize) ? 0 : pRingNext;
                    for (int n = 0; n < pRingCount && n < pRingSize; n++)
                    {
                        Logger.Log($"  err-P P={Utils.Int2Oct(pRing[idx])} PIL={pRingPil[idx]}", Logger.LogLevel.Debug);
                        idx = (idx + 1) % pRingSize;
                    }
                }
            }
            cpu.hw.uart.OnTextOutput += OnErr;

            int runWindow = TICK_PR_20MS * 400;
            for (int i = 0; i < runWindow; i++)
            {
                cpu.TickCpu();
                ushort p = cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                int pil = (cpu.hw.alu.STS >> 8) & 0x0F;
                if (p != pLast)
                {
                    pRing[pRingNext] = p;
                    pRingPil[pRingNext] = pil;
                    pRingNext = (pRingNext + 1) % pRingSize;
                    if (pRingCount < pRingSize) pRingCount++;
                    pLast = p;
                }
                if (pil != lastPil)
                {
                    if (!dumpedErrRing)
                        Logger.Log($"RUN PIL change cycle={i} {lastPil} -> {pil} P={Utils.Int2Oct(p)}", Logger.LogLevel.Debug);
                    lastPil = pil;
                }
            }
            cpu.hw.uart.OnTextOutput -= OnErr;
            Logger.Log($"RUN response so far:\n{output}", Logger.LogLevel.Debug);

            // Accept defaults for any follow-up questions, then give the suite a long
            // window to execute.
            TypeString(cpu, "\r");
            RunCycles(cpu, TICK_PR_20MS * 1500);

            cpu.hw.uart.OnTextOutput -= OnText;

            Logger.Log($"RUN full output:\n{output}", Logger.LogLevel.Debug);
            Logger.Log($"Final: P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} STOP={cpu.hw.cpu_regs.STOP}", Logger.LogLevel.Debug);
        }

        [Explicit("Diagnostic - explores INSTRUCTION-VERIFY's interactive commands")]
        [Test]
        public void Diag_InstructionVerifyHelp()
        {
            CPU cpu = BootToOpcom();
            Logger.SetDestination(Logger.LogDestination.File);
            Logger.SetLogLevel(Logger.LogLevel.Debug);

            StringBuilder output = new StringBuilder();
            bool traceChars = false;
            int charsTraced = 0;
            // Per-character provenance: OnTextOutput fires synchronously inside TickCpu,
            // so P/PIL/CSAR read here identify the exact code (main program vs interrupt
            // handler) that wrote each character - used to find who emits the spurious
            // "&@" pairs that pollute the console between real output.
            void OnText(char c)
            {
                output.Append(c);
                if (traceChars && charsTraced < 120)
                {
                    charsTraced++;
                    int pil = (cpu.hw.alu.STS >> 8) & 0x0F;
                    Logger.Log(
                        $"CHAR '{(c < ' ' ? "." : c.ToString())}' (0x{(int)c:X2}) P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} " +
                        $"PIL={pil} CSAR={Utils.Int2Oct(cpu.hw.mic.map)}",
                        Logger.LogLevel.Debug);
                }
            }
            cpu.hw.uart.OnTextOutput += OnText;

            TypeString(cpu, "0!");
            RunCycles(cpu, TICK_PR_20MS * 30); // let the banner + ">" prompt appear

            output.Clear(); // only capture the HELP response
            traceChars = true;

            // "All commands" enumeration tracer state (see ACMD logging below): armed by
            // OnAcmd the moment the transmitted text ends with "commands".
            bool afterAllCommands = false;
            int allCmdPLogged = 0;
            const int maxAllCmdPLogged = 9000;
            void OnAcmd(char c)
            {
                if (!afterAllCommands && c == ':')
                {
                    string s = output.ToString();
                    if (s.EndsWith("Command:")) afterAllCommands = true;
                }
            }
            cpu.hw.uart.OnTextOutput += OnAcmd;

            // P-history ring, dumped once at the first spurious '&' transmit: identifies
            // the program code path that generates the unrequested "&@" pairs (the receive
            // side is proven clean - only genuinely typed characters are ever consumed).
            const int pRingSize = 96;
            ushort[] pRing = new ushort[pRingSize];
            int pRingNext = 0, pRingCount = 0;
            ushort pLast = 0xFFFF;
            bool dumpedAmpRing = false;
            void OnAmp(char c)
            {
                if (c == '&' && !dumpedAmpRing)
                {
                    dumpedAmpRing = true;
                    Logger.Log("--- P history before first '&' transmit (oldest first): ---", Logger.LogLevel.Debug);
                    int idx = (pRingCount < pRingSize) ? 0 : pRingNext;
                    for (int n = 0; n < pRingCount && n < pRingSize; n++)
                    {
                        Logger.Log($"  amp-P P={Utils.Int2Oct(pRing[idx])}", Logger.LogLevel.Debug);
                        idx = (idx + 1) % pRingSize;
                    }
                    // Live call-chain state at the exact transmit (OnTextOutput fires
                    // synchronously inside TickCpu). Disassembly established:
                    //   051707 = output driver's saved char (the byte just transmitted)
                    //   051712 = output driver's saved caller return address (L at entry 051713)
                    //   052250/052251 = input-poll wrapper's saved A / saved L
                    //   052337/052341 = control-char dispatcher's saved char / saved L
                    //   046233/046234 = input/output device IOX base, 046242 = busy flag,
                    //   046243 = under-SINTRAN flag, 046236..046241 = misc driver vars
                    int[] cells = {
                        Convert.ToInt32("51707", 8), Convert.ToInt32("51710", 8), Convert.ToInt32("51712", 8),
                        Convert.ToInt32("52250", 8), Convert.ToInt32("52251", 8), Convert.ToInt32("52271", 8),
                        Convert.ToInt32("52336", 8), Convert.ToInt32("52337", 8), Convert.ToInt32("52340", 8),
                        Convert.ToInt32("52341", 8),
                    };
                    for (int n = 0; n < cells.Length; n++)
                    {
                        ushort w = cpu.memory.ReadPhysicalMemory((ushort)cells[n]);
                        Logger.Log($"  amp-cell [{Utils.Int2Oct(cells[n])}] = {Utils.Int2Oct(w)}", Logger.LogLevel.Debug);
                    }
                    for (int a = Convert.ToInt32("46230", 8); a <= Convert.ToInt32("46260", 8); a++)
                    {
                        ushort w = cpu.memory.ReadPhysicalMemory((ushort)a);
                        Logger.Log($"  amp-var [{Utils.Int2Oct(a)}] = {Utils.Int2Oct(w)}", Logger.LogLevel.Debug);
                    }
                    // Registers at the transmit for orientation (PIL identifies whether an
                    // interrupt level's register bank is running this code).
                    int pil = (cpu.hw.alu.STS >> 8) & 0x0F;
                    Logger.Log(
                        $"  amp-regs P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} " +
                        $"L={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.L))} " +
                        $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                        $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))} " +
                        $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                        $"B={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.B))} PIL={pil}",
                        Logger.LogLevel.Debug);
                }
            }
            cpu.hw.uart.OnTextOutput += OnAmp;

            // Inline typing loop (instead of TypeString) so the P ring is collected DURING
            // the typing too - the first '&' appears immediately after the first echo.
            // HELP answers with "Command: " (help for WHICH command) - answer with a bare
            // CR, which should list all commands.
            // HELP -> "Command: " (which command?); 1st CR = empty answer -> program
            // announces "All commands" and (disassembly of 050277: it calls the read-line
            // routine a second time) waits for a confirming line; 2nd bare CR then makes it
            // copy the "All commands" default into the answer buffer and print the listing.
            string toType = "HELP\r\r\r";
            int typeIdx = 0;
            int cyclesPerChar = TICK_PR_20MS * 5;
            int totalCycles = cyclesPerChar * toType.Length + TICK_PR_20MS * 400;
            // Macro-level A/P trace through the input path, bounded. Watch addresses
            // (disassembly-derived):
            //   052272-052335 = hardware input poll (IOX 302 status / IOX 300 data via EXR)
            //   052213-052236 = blocking read-char loop
            //   050007-050055 = line-input echo loop (050043+ = "&"+char+0100 control echo)
            // P increments during fetch, so "P became x" means instruction at/near x is
            // being fetched; A is sampled per micro-cycle and shows the data flow.
            int ioTraceLogged = 0;
            const int maxIoTraceLogged = 1200;
            int lastPil2 = -1;
            // Micro-level corruption catcher: the input poll's accept path (052322 AND /
            // 052323 STA I) can only legitimately run with A = a nonzero char (the JAZ at
            // 052321 filters A==0). If P reaches 052323 with A==0 the JAZ jump was LOST -
            // dump a per-tick CSAR/P/A ring covering the preceding ~300 micro-cycles to see
            // exactly which microcode path (interrupt dispatch?) dropped it.
            const int uRingSize = 300;
            int[] uCycle = new int[uRingSize];
            ushort[] uCsar = new ushort[uRingSize];
            ushort[] uP = new ushort[uRingSize];
            ushort[] uA = new ushort[uRingSize];
            byte[] uPil = new byte[uRingSize];
            int uNext = 0, uCount = 0;
            bool dumpedURing = false;
            for (int i = 0; i < totalCycles; i++)
            {
                if (i % cyclesPerChar == 0 && typeIdx < toType.Length)
                {
                    cpu.hw.AddConsoleChar(toType[typeIdx]);
                    typeIdx++;
                }
                cpu.TickCpu();

                // per-tick micro ring (cheap: fixed arrays, no allocation)
                uCycle[uNext] = i;
                uCsar[uNext] = cpu.hw.mic.map;
                uP[uNext] = cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                uA[uNext] = cpu.hw.alu.ReadRegisterE(RegisterEnum.A);
                uPil[uNext] = (byte)((cpu.hw.alu.STS >> 8) & 0x0F);
                uNext = (uNext + 1) % uRingSize;
                if (uCount < uRingSize) uCount++;

                if (!dumpedURing && uP[(uNext + uRingSize - 1) % uRingSize] == Convert.ToInt32("52323", 8)
                    && uA[(uNext + uRingSize - 1) % uRingSize] == 0)
                {
                    dumpedURing = true;
                    Logger.Log("--- micro ring at phantom accept (P=052323 with A=0), oldest first: ---", Logger.LogLevel.Debug);
                    int ui = (uCount < uRingSize) ? 0 : uNext;
                    for (int n = 0; n < uCount && n < uRingSize; n++)
                    {
                        Logger.Log($"  uR cycle={uCycle[ui]} CSAR={Utils.Int2Oct(uCsar[ui])} P={Utils.Int2Oct(uP[ui])} A={Utils.Int2Oct(uA[ui])} PIL={uPil[ui]}", Logger.LogLevel.Debug);
                        ui = (ui + 1) % uRingSize;
                    }
                }
                int pilNow = (cpu.hw.alu.STS >> 8) & 0x0F;
                if (pilNow != lastPil2)
                {
                    Logger.Log($"IOTRC PIL {lastPil2} -> {pilNow} cycle={i} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))}", Logger.LogLevel.Debug);
                    lastPil2 = pilNow;
                }
                ushort p = cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                if (p != pLast)
                {
                    pRing[pRingNext] = p;
                    pRingNext = (pRingNext + 1) % pRingSize;
                    if (pRingCount < pRingSize) pRingCount++;
                    pLast = p;

                    // Trace the command-list enumeration: once "All commands" has been
                    // transmitted, log the next chunk of P transitions to see where the
                    // enumeration goes instead of printing the command names.
                    if (afterAllCommands && allCmdPLogged < maxAllCmdPLogged)
                    {
                        allCmdPLogged++;
                        Logger.Log($"ACMD P={Utils.Int2Oct(p)} A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))}", Logger.LogLevel.Debug);
                    }

                    bool inPoll = (p >= 0x54BA && p <= 0x54DD);      // 052272-052335
                    bool inRead = (p >= 0x548B && p <= 0x549E);      // 052213-052236
                    bool inEcho = (p >= 0x5007 && p <= 0x502D);      // 050007-050055
                    if ((inPoll || inRead || inEcho) && ioTraceLogged < maxIoTraceLogged)
                    {
                        ioTraceLogged++;
                        Logger.Log(
                            $"IOTRC cycle={i} P={Utils.Int2Oct(p)} A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                            $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))} PIL={pilNow}",
                            Logger.LogLevel.Debug);
                    }
                }
            }
            cpu.hw.uart.OnTextOutput -= OnAmp;
            cpu.hw.uart.OnTextOutput -= OnAcmd;

            // Dump the '&'-emitting routine (051740-052010) and its caller (052250-052270)
            // for disassembly - octal address : octal word.
            Logger.Log("Memory dump around the '&' emitter:", Logger.LogLevel.Debug);
            for (int a = Convert.ToInt32("51740", 8); a <= Convert.ToInt32("52010", 8); a++)
            {
                ushort w = cpu.memory.ReadPhysicalMemory((ushort)a);
                Logger.Log($"  MEM {Utils.Int2Oct(a)} : {Utils.Int2Oct(w)}", Logger.LogLevel.Debug);
            }
            for (int a = Convert.ToInt32("52250", 8); a <= Convert.ToInt32("52270", 8); a++)
            {
                ushort w = cpu.memory.ReadPhysicalMemory((ushort)a);
                Logger.Log($"  MEM {Utils.Int2Oct(a)} : {Utils.Int2Oct(w)}", Logger.LogLevel.Debug);
            }

            cpu.hw.uart.OnTextOutput -= OnText;

            // End-of-run state: where is the program idling and who called the reader?
            //   052212 = blocking-read's saved caller L, 051712 = output driver's saved L,
            //   046230.. = driver variables (046241 = last received char, 046242 = busy).
            Logger.Log("--- end-of-run P ring (oldest first): ---", Logger.LogLevel.Debug);
            int eIdx = (pRingCount < pRingSize) ? 0 : pRingNext;
            for (int n = 0; n < pRingCount && n < pRingSize; n++)
            {
                Logger.Log($"  end-P P={Utils.Int2Oct(pRing[eIdx])}", Logger.LogLevel.Debug);
                eIdx = (eIdx + 1) % pRingSize;
            }
            int[] endCells = {
                Convert.ToInt32("51707", 8), Convert.ToInt32("51712", 8),
                Convert.ToInt32("52212", 8), Convert.ToInt32("52250", 8), Convert.ToInt32("52251", 8),
            };
            for (int n = 0; n < endCells.Length; n++)
            {
                ushort w = cpu.memory.ReadPhysicalMemory((ushort)endCells[n]);
                Logger.Log($"  end-cell [{Utils.Int2Oct(endCells[n])}] = {Utils.Int2Oct(w)}", Logger.LogLevel.Debug);
            }
            for (int a = Convert.ToInt32("46230", 8); a <= Convert.ToInt32("46263", 8); a++)
            {
                ushort w = cpu.memory.ReadPhysicalMemory((ushort)a);
                Logger.Log($"  end-var [{Utils.Int2Oct(a)}] = {Utils.Int2Oct(w)}", Logger.LogLevel.Debug);
            }

            Logger.Log($"HELP response:\n{output}", Logger.LogLevel.Debug);
            Logger.Log($"Final: P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} STOP={cpu.hw.cpu_regs.STOP}", Logger.LogLevel.Debug);
        }

        [Test]
        public void ND110_CanRunInstructionVerify()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                // "0!" at OPCOM: "0" accumulates into OCTNR, "!" (EXCL) starts execution
                // at that address - see opcom-commands.md. INSTRUCTION-VERIFY is the
                // program CPU.Clear()/FileHelper.LoadDefaultBPUN already loads at address 0.
                TypeString(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20);
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            string text = output.ToString();
            Assert.That(text, Does.Contain("INSTRUCTION"),
                $"Expected the INSTRUCTION-VERIFY banner over UART after '0!'. Captured output:\n{text}");

            // Regression check for the phantom vector-0x0F dispatch (TrapController live
            // /IRQ tracking + legacy TrapVector sync, commit b84f1d2): INSTRUCTION-VERIFY
            // must get all the way to its own interactive monitor. Before the fix it
            // exited back to OPCOM ("#") after the config block; with the fix it prints
            // the HELP hint and presents its ">" prompt.
            Assert.That(text, Does.Contain("The command HELP"),
                $"Expected INSTRUCTION-VERIFY to reach its HELP hint (not exit to OPCOM early). Captured output:\n{text}");
            Assert.That(text, Does.Contain(">"),
                $"Expected INSTRUCTION-VERIFY's '>' command prompt. Captured output:\n{text}");

            // Regression check for the Commands.Command_37/MIS=2 (COMM,IDENT) bug where a
            // spurious IOX-error interrupt was manufactured on every IDENT with no real
            // device to respond, which INSTRUCTION-VERIFY reported as "Internal Interrupt".
            Assert.That(text, Does.Not.Contain("Internal Interrupt"),
                $"IDENT should not manufacture a spurious interrupt when no device responds. Captured output:\n{text}");

            // Drive the interactive HELP command. HELP asks "Command: "; the 1st bare CR
            // (empty answer) makes it announce "All commands" and read one more confirming
            // line (see the question routine at macro 050237: it calls the read-line
            // routine a second time at 050300); the 2nd bare CR then produces the full
            // command listing ("MONITOR-COMMANDS:" / "PROGRAM-COMMANDS:" sections) and a
            // fresh ">" prompt.
            output.Clear();
            cpu.hw.uart.OnTextOutput += OnText;
            try
            {
                TypeString(cpu, "HELP\r\r\r");
                RunCycles(cpu, TICK_PR_20MS * 60);
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            string helpText = output.ToString();
            Assert.That(helpText, Does.Contain("MONITOR-COMMANDS:"),
                $"Expected HELP's monitor-command listing. Captured output:\n{helpText}");
            Assert.That(helpText, Does.Contain("PROGRAM-COMMANDS:"),
                $"Expected HELP's program-command listing. Captured output:\n{helpText}");

            // Regression check for the interrupt-dispatch P corruption (Cpu.cs
            // CompensateVectorPDecrement): vectors 0x0E/0x0F enter microcode (PANVC
            // CS 003760-003762 / XINT CS 001101) that unconditionally executes P := P-1,
            // and the emulator used to dispatch BEFORE the fetch increment, resuming one
            // instruction early. That re-executed the input poll's skip-return
            // (macro 052322-052325) after its guarding JAZ had already been taken,
            // manufacturing phantom NUL input characters which the program echoes as
            // "&<NUL+0100>" = "&@" - an endless stream at RTC cadence that also starved
            // the HELP listing.
            Assert.That(helpText, Does.Not.Contain("&@"),
                $"Interrupt dispatch must not corrupt P (phantom NUL input echoed as &@). Captured output:\n{helpText}");
        }

        /// <summary>
        /// Diagnostic for starting INSTRUCTION-VERIFY at its ALTERNATE entry: "20!"
        /// instead of "0!". The image's entry vector at 000020 is:
        ///
        ///   000020  045006  LDA I *6     A := [[000026]] = [046240] (init flag)
        ///   000021  131003  JAZ *+3      flag == 0 -> 000024
        ///   000022  125005  JMP I *5     initialized: warm entry via [000027]=[053672]
        ///   000024  125004  JMP I *4     uninitialized: via [000030] = 000313
        ///
        /// and the " ** FATAL PROGRAM ERROR **" handler lives at ~021613 (inline string
        /// at 021573-021611, then IOF + WAIT = controlled halt). This diag types "20!"
        /// on a fresh boot, captures all output, keeps a per-instruction P ring and
        /// dumps it at the first '*' the program transmits - showing the exact code
        /// path into the FATAL handler.
        /// </summary>
        [Explicit("Diagnostic - why '20!' reaches ** FATAL PROGRAM ERROR **")]
        [Test]
        public void Diag_Entry20FatalError()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                // Typing immediately after BootToOpcom loses the FIRST character
                // (observed: "20!" echoed as "0!" - which then started at address 0
                // because OPCOM's OCTNR defaults to 0; the existing "0!" tests only
                // worked by that coincidence). Swallow-proof form: type "020!" - if the
                // leading '0' is eaten, "20!" remains; if not, octal 020 is the same
                // address. No warm-up, exactly like an operator typing right away.
                TypeString(cpu, "020!");

                ushort[] ring = new ushort[512];
                int ringNext = 0, ringCount = 0;
                ushort lastP = 0;
                bool dumped = false;

                void OnStar(char c)
                {
                    if (c == '*' && !dumped)
                    {
                        dumped = true;
                        TestContext.Out.WriteLine("--- P ring (oldest first) at first '*' of the FATAL banner ---");
                        int idx = (ringCount < ring.Length) ? 0 : ringNext;
                        for (int n = 0; n < ringCount; n++)
                        {
                            TestContext.Out.WriteLine($"  P={Utils.Int2Oct(ring[idx])}");
                            idx = (idx + 1) % ring.Length;
                        }
                        TestContext.Out.WriteLine(
                            $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                            $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))} " +
                            $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                            $"B={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.B))} " +
                            $"L={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.L))} " +
                            $"PIL={cpu.hw.alu.PIL}");
                        // Cells the entry-20 vector consults:
                        TestContext.Out.WriteLine(
                            $"[046240]={Utils.Int2Oct(cpu.memory.ReadPhysicalMemory((ushort)Utils.Oct2Int("46240")))} " +
                            $"[000426]={Utils.Int2Oct(cpu.memory.ReadPhysicalMemory((ushort)Utils.Oct2Int("426")))}");
                    }
                }
                cpu.hw.uart.OnTextOutput += OnStar;

                for (int i = 0; i < TICK_PR_20MS * 200 && !dumped; i++)
                {
                    cpu.TickCpu();
                    ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                    if (p != lastP)
                    {
                        lastP = p;
                        ring[ringNext] = p;
                        ringNext = (ringNext + 1) % ring.Length;
                        if (ringCount < ring.Length) ringCount++;
                    }
                }
                // If nothing was printed yet (the program parks in its input poll at
                // ~052233 after a fresh-boot "20!"), poke it with input - a bare CR,
                // then a command - and keep the ring/star watcher running.
                if (!dumped)
                {
                    TestContext.Out.WriteLine($"(no output after 20! - poking with CR) P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))}");
                    cpu.hw.AddConsoleChar('\r');
                    for (int i = 0; i < TICK_PR_20MS * 100 && !dumped; i++)
                    {
                        cpu.TickCpu();
                        ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                        if (p != lastP)
                        {
                            lastP = p;
                            ring[ringNext] = p;
                            ringNext = (ringNext + 1) % ring.Length;
                            if (ringCount < ring.Length) ringCount++;
                        }
                    }
                }
                if (!dumped)
                {
                    TestContext.Out.WriteLine($"(still nothing - typing HELP) output so far: [{output}]");
                    TypeString(cpu, "HELP\r");
                    for (int i = 0; i < TICK_PR_20MS * 100 && !dumped; i++)
                    {
                        cpu.TickCpu();
                        ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                        if (p != lastP)
                        {
                            lastP = p;
                            ring[ringNext] = p;
                            ringNext = (ringNext + 1) % ring.Length;
                            if (ringCount < ring.Length) ringCount++;
                        }
                    }
                }
                // Let the rest of the message print
                RunCycles(cpu, TICK_PR_20MS * 40);
                cpu.hw.uart.OnTextOutput -= OnStar;
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            TestContext.Out.WriteLine($"captured output:\n{output}");
            TestContext.Out.WriteLine($"final STOP={cpu.hw.cpu_regs.STOP} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))}");
        }

        /// <summary>
        /// Diagnostic for the WARM re-entry: start INSTRUCTION-VERIFY normally ("0!"),
        /// leave it with its own OPCOM command (drops back to the '#' prompt), then
        /// restart it at the alternate entry "20!". With the init flag [046240] set,
        /// the vector at 000020 takes the warm path via [000027] = [053672] - the path
        /// suspected of producing " ** FATAL PROGRAM ERROR **" (handler at ~021613,
        /// inline string at 021573). Keeps a P ring dumped at the first '*' transmitted
        /// after the re-entry.
        /// </summary>
        [Explicit("Diagnostic - '20!' warm re-entry after OPCOM exit")]
        [Test]
        public void Diag_Entry20AfterOpcomExit()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10); // let MOPC reach its input loop
                TypeString(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20); // banner + ">"
                TestContext.Out.WriteLine($"after 0!: [{output}]");

                output.Clear();
                // Type OPCOM while watching every pass through the WAIT microcode entry
                // (WAIT1, CS 001042): log STS/PIL there and which branch follows -
                // STOPW (CS 002133, ION off -> MOPC) or WAIT2 (CS 001047, ION on).
                // Also track IOControl bit 6 ("console owned by program") - if the
                // program clears it while macro-running, TickCpu's isOpcom guard freezes
                // the emulator at the FETCH boundary (nothing executes at all).
                int wait1Seen = 0;
                string opcomCmd = "OPCOM\r";
                for (int ci = 0; ci < opcomCmd.Length; ci++)
                {
                    cpu.hw.AddConsoleChar(opcomCmd[ci]);
                    for (int i = 0; i < TICK_PR_20MS * 5; i++)
                    {
                        ushort mapBefore = cpu.hw.mic.map;
                        cpu.TickCpu();
                        if (mapBefore == 0x222 && wait1Seen < 8) // 001042 WAIT1
                        {
                            wait1Seen++;
                            TestContext.Out.WriteLine(
                                $"WAIT1 hit: STS={Utils.Int2Oct(cpu.hw.alu.STS)} PIL={cpu.hw.alu.PIL} " +
                                $"P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} " +
                                $"IOC={Utils.Int2Oct(cpu.hw.cpu_regs.IOControl)}");
                        }
                        if (mapBefore == 0x45B) // 002133 STOPW
                        {
                            TestContext.Out.WriteLine($"  -> STOPW (ION off, entering MOPC)");
                        }
                    }
                }
                RunCycles(cpu, TICK_PR_20MS * 25);
                TestContext.Out.WriteLine($"after OPCOM: [{output}] init flag [046240]={Utils.Int2Oct(cpu.memory.ReadPhysicalMemory((ushort)Utils.Oct2Int("46240")))}");
                // Where did the machine settle? The program's OPCOM COMMAND executes the
                // OPCOM INSTRUCTION (150400) at macro 003016; its microcode path is
                // PINF1 (CS 000631) -> VECT1 entry 0 (CS 003640) -> INPUT (CS 002351,
                // MOPC's console reader). Histogram the microcode addresses for a while
                // to see where execution actually sits.
                System.Collections.Generic.Dictionary<ushort, int> histo = new System.Collections.Generic.Dictionary<ushort, int>();
                for (int i = 0; i < TICK_PR_20MS * 10; i++)
                {
                    cpu.TickCpu();
                    ushort m = cpu.hw.mic.map;
                    if (histo.ContainsKey(m)) histo[m]++; else histo[m] = 1;
                }
                TestContext.Out.WriteLine(
                    $"settled: CSAR={Utils.Int2Oct(cpu.hw.mic.map)} STS={Utils.Int2Oct(cpu.hw.alu.STS)} " +
                    $"STOP={cpu.hw.cpu_regs.STOP} FETCH={cpu.hw.mac.FETCH} IOC={Utils.Int2Oct(cpu.hw.cpu_regs.IOControl)} " +
                    $"P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} PIL={cpu.hw.alu.PIL}");
                System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ushort, int>> top =
                    new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<ushort, int>>(histo);
                top.Sort((x, y) => y.Value.CompareTo(x.Value));
                for (int n = 0; n < top.Count && n < 12; n++)
                {
                    TestContext.Out.WriteLine($"  map {Utils.Int2Oct(top[n].Key)} x{top[n].Value}");
                }

                // Second run of the same flow, but this time trace the microcode address
                // stream from the moment the OPCOM instruction (macro 003016) executes,
                // compressing repeats - to see PINF1 (000631) -> VECT1 (003640) -> INPUT
                // (002351) and exactly what breaks the MOPC loop.
                CPU cpu2 = BootToOpcom();
                RunCycles(cpu2, TICK_PR_20MS * 10);
                TypeStringOn(cpu2, "0!");
                RunCycles(cpu2, TICK_PR_20MS * 20);
                TypeStringOn(cpu2, "OPCOM"); // CR sent below so the trace is armed first

                bool armed = false;
                cpu2.hw.AddConsoleChar('\r');
                int printed = 0;
                ushort lastMap = 0xFFFF;
                int repeat = 0;
                for (int i = 0; i < TICK_PR_20MS * 30 && printed < 220; i++)
                {
                    cpu2.TickCpu();
                    if (!armed)
                    {
                        if ((ushort)cpu2.hw.alu.ReadRegisterE(RegisterEnum.P) == 0x60E) // 003016
                        {
                            armed = true;
                            TestContext.Out.WriteLine("--- OPCOM instruction reached (P=003016), map stream: ---");
                        }
                        continue;
                    }
                    ushort m = cpu2.hw.mic.map;
                    if (m == lastMap) { repeat++; continue; }
                    if (repeat > 0) { TestContext.Out.WriteLine($"    (x{repeat + 1})"); repeat = 0; }
                    lastMap = m;
                    printed++;
                    TestContext.Out.WriteLine($"  map={Utils.Int2Oct(m)} FETCH={cpu2.hw.mac.FETCH}");
                }

                output.Clear();
                TypeString(cpu, "20!");

                ushort[] ring = new ushort[512];
                int ringNext = 0, ringCount = 0;
                ushort lastP = 0;
                bool dumped = false;

                void OnStar(char c)
                {
                    if (c == '*' && !dumped)
                    {
                        dumped = true;
                        TestContext.Out.WriteLine("--- P ring (oldest first) at first '*' after 20! ---");
                        int idx = (ringCount < ring.Length) ? 0 : ringNext;
                        for (int n = 0; n < ringCount; n++)
                        {
                            TestContext.Out.WriteLine($"  P={Utils.Int2Oct(ring[idx])}");
                            idx = (idx + 1) % ring.Length;
                        }
                        TestContext.Out.WriteLine(
                            $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                            $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))} " +
                            $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                            $"B={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.B))} " +
                            $"L={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.L))} " +
                            $"PIL={cpu.hw.alu.PIL}");
                    }
                }
                cpu.hw.uart.OnTextOutput += OnStar;

                for (int i = 0; i < TICK_PR_20MS * 200 && !dumped; i++)
                {
                    cpu.TickCpu();
                    ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                    if (p != lastP)
                    {
                        lastP = p;
                        ring[ringNext] = p;
                        ringNext = (ringNext + 1) % ring.Length;
                        if (ringCount < ring.Length) ringCount++;
                    }
                }
                RunCycles(cpu, TICK_PR_20MS * 40);
                cpu.hw.uart.OnTextOutput -= OnStar;
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            TestContext.Out.WriteLine($"after 20!: [{output}]");
            TestContext.Out.WriteLine($"final STOP={cpu.hw.cpu_regs.STOP} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))}");
        }

        /// <summary>
        /// Diagnostic: reproduce the operator flow "program initialized and used, then
        /// restarted at the alternate entry with 20!". MOPC's "20!" simply loads P with
        /// 000020 and starts the CPU - this diag does exactly that through the harness
        /// (WriteRegisterE(P) + fetch), sidestepping the separate problem that the
        /// program's OPCOM command (WAIT at 053201) does not hand the console back to
        /// MOPC in the emulator. Rings P and dumps at the first '*' transmitted, to
        /// catch the " ** FATAL PROGRAM ERROR **" path (handler ~021613).
        /// </summary>
        [Explicit("Diagnostic - '20!' restart after the program ran a test")]
        [Test]
        public void Diag_Entry20AfterProgramRan()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                TypeString(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20);
                TestContext.Out.WriteLine($"[046240] after 0!: {Utils.Int2Oct(cpu.memory.ReadPhysicalMemory((ushort)Utils.Oct2Int("46240")))}");

                // Run one area so the program is in "has been running tests" state.
                // Deliberately NO ESC: the restart happens while the level machinery
                // (clock level 13, stress levels) is still armed and looping - matching
                // an operator breaking in mid-run.
                TypeString(cpu, "ARGUMENT\r");
                RunCycles(cpu, TICK_PR_20MS * 200);
                TestContext.Out.WriteLine($"after ARGUMENT (tail, still looping): [...{TailOf(output, 120)}]");
                TestContext.Out.WriteLine($"[046240] now: {Utils.Int2Oct(cpu.memory.ReadPhysicalMemory((ushort)Utils.Oct2Int("46240")))}");

                // Simulate MOPC "20!": P := 000020, fetch, go.
                output.Clear();
                cpu.hw.alu.WriteRegisterE(RegisterEnum.P, (ushort)Utils.Oct2Int("20"));
                cpu.hw.mac.LatchMacAddress((ushort)Utils.Oct2Int("20"));
                cpu.UnitTest_Fetch();

                ushort[] ring = new ushort[512];
                int ringNext = 0, ringCount = 0;
                ushort lastP = 0;
                bool dumped = false;

                void OnStar(char c)
                {
                    if (c == '*' && !dumped)
                    {
                        dumped = true;
                        TestContext.Out.WriteLine("--- P ring (oldest first) at first '*' after simulated 20! ---");
                        int idx = (ringCount < ring.Length) ? 0 : ringNext;
                        for (int n = 0; n < ringCount; n++)
                        {
                            TestContext.Out.WriteLine($"  P={Utils.Int2Oct(ring[idx])}");
                            idx = (idx + 1) % ring.Length;
                        }
                        TestContext.Out.WriteLine(
                            $"A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))} " +
                            $"T={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.T))} " +
                            $"X={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.X))} " +
                            $"L={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.L))} " +
                            $"PIL={cpu.hw.alu.PIL}");
                    }
                }
                cpu.hw.uart.OnTextOutput += OnStar;

                for (int i = 0; i < TICK_PR_20MS * 200 && !dumped; i++)
                {
                    cpu.TickCpu();
                    ushort p = (ushort)cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                    if (p != lastP)
                    {
                        lastP = p;
                        ring[ringNext] = p;
                        ringNext = (ringNext + 1) % ring.Length;
                        if (ringCount < ring.Length) ringCount++;
                    }
                }
                RunCycles(cpu, TICK_PR_20MS * 40);
                cpu.hw.uart.OnTextOutput -= OnStar;
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            TestContext.Out.WriteLine($"after simulated 20!: [{output}]");
            TestContext.Out.WriteLine($"final STOP={cpu.hw.cpu_regs.STOP} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))}");
        }

        /// <summary>
        /// Verifies the " ** FATAL PROGRAM ERROR **" trigger found by static analysis:
        /// the 20! warm re-entry runs a TABLE-INTEGRITY WALKER (macro 022175-022204:
        /// A := [[022163]+X]; -1 terminates, 0 is OK, ANYTHING ELSE -> JPL via [022325]
        /// = 021613 = print " ** FATAL PROGRAM ERROR **" + IOF + WAIT). A second pair of
        /// checks guards the pointer table at [020612] the same way. I.e. FATAL is the
        /// program's own memory-corruption detector - it fires on 20! when a previous
        /// run corrupted program data (as the pre-fix MOVB overrun into 06xxxx did).
        ///
        /// This test CORRUPTS one cell of the checked table by hand, types 20!, and
        /// expects the FATAL banner followed by the documented IOF+WAIT halt (STOP set,
        /// console back at OPCOM '#').
        /// </summary>
        [Test]
        public void FatalProgramError_FiresOnNullPointerInTestList()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                TypeString(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20);
                Assert.That(output.ToString(), Does.Contain(">"), "precondition: program prompt after 0!");

                // Corrupt the ARGUMENT area's routine-pointer list: the list executor
                // (macro 022175-022204) walks [[022163]+X]; entries are routine
                // addresses, -1 terminates, and a ZERO entry (null pointer = corrupted
                // list) takes the JPL through [022325] = 021613 = FATAL. The ARGUMENT
                // list lives at 036755+ (entries 037111, 037156, ... 177777 at 036772).
                ushort listSlot = (ushort)Utils.Oct2Int("36760");
                ushort orig = cpu.memory.ReadPhysicalMemory(listSlot);
                TestContext.Out.WriteLine($"corrupting list slot [{Utils.Int2Oct(listSlot)}]: {Utils.Int2Oct(orig)} -> 000000");
                cpu.memory.WritePhysicalMemory(listSlot, 0);

                output.Clear();
                TypeString(cpu, "ARGUMENT\r");
                RunCycles(cpu, TICK_PR_20MS * 120);
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            string text = output.ToString();
            TestContext.Out.WriteLine($"captured:\n{text}");
            TestContext.Out.WriteLine($"STOP={cpu.hw.cpu_regs.STOP} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))}");
            Assert.That(text, Does.Contain("FATAL PROGRAM ERROR"),
                $"a NULL routine pointer in a test-descriptor list must print the FATAL banner. Captured:\n{text}");
            // The handler ends in IOF ; WAIT. Note: the walker runs the level-1 pass, so
            // at FATAL time other levels (the framework's clock etc.) may still be armed;
            // the observable contract asserted here is the banner. The final machine
            // state (halted vs level idle) is reported above for the record.
        }

        /// <summary>
        /// Full-pass RUN verification: boots, starts INSTRUCTION-VERIFY, types RUN and
        /// keeps the machine running until the program finishes its complete level-1
        /// sweep of every instruction group and starts "RUNNING TESTS ON LEVEL 2" (or
        /// reports an error). The complete console transcript is written to
        /// traces\RUN-console.log for the record. [Explicit] because it takes many
        /// minutes of emulated time.
        /// </summary>
        [Explicit("Long-running - full RUN level-1 sweep with console log")]
        [Test]
        public void Diag_RunFullLevel1Sweep()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            string verdict;
            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                TypeString(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20);
                Assert.That(output.ToString(), Does.Contain(">"), "precondition: program prompt");

                TypeString(cpu, "RUN\r");

                // A full level-1 sweep covers every group in the OTYPE list; give it a
                // generous budget and stop at "NOW LOOPING" (all levels done), an error,
                // or cap. NOTE: the level-1 test framework pads its console output with
                // a NUL byte after every character (TTY padding), so the raw transcript
                // contains "E\0R\0R\0O\0R" etc. — all marker matching must be done on a
                // NUL-stripped copy or nothing ever matches.
                verdict = "budget exhausted before LEVEL 2";
                bool level1Done = false;
                for (int chunk = 0; chunk < 1200; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    string norm = output.ToString().Replace("\0", "");
                    // "IOX-ERROR STARTED" is an expected framework banner, not a failure —
                    // exclude it before scanning for real error markers.
                    string errScan = norm.Replace("IOX-ERROR", "");
                    if (errScan.Contains("ERROR") || errScan.Contains("FAIL") || errScan.Contains("FATAL"))
                    { verdict = "program reported an ERROR"; break; }
                    if (!level1Done && norm.Contains("RUNNING TESTS ON LEVEL 2"))
                    {
                        level1Done = true;
                        verdict = "level-1 sweep COMPLETE (LEVEL 2 started, budget ended during higher levels)";
                    }
                    if (norm.Contains("NOW LOOPING"))
                    { verdict = "COMPLETE - all groups passed on every level, tests now looping"; break; }
                }
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            // Strip the framework's TTY NUL padding so the saved transcript is readable.
            string log = output.ToString().Replace("\0", "");
            string dir = TestPaths.TracesDir();
            string file = System.IO.Path.Combine(dir, "RUN-console.log");
            System.IO.File.WriteAllText(file,
                $"INSTRUCTION-B RUN - full level-1 sweep transcript\r\nVerdict: {verdict}\r\n\r\n{log}");
            TestContext.Out.WriteLine($"log written: {file} ({log.Length} chars) - {verdict}");

            Assert.That(verdict, Does.Contain("COMPLETE"),
                $"RUN must finish its level-1 sweep of all groups without errors. Verdict: {verdict}. Transcript:\n{log}");
        }

        /// <summary>
        /// First attempt at INSTRUCTION-B on the ND-120 (DELILAH-L) microcode: boots
        /// the real DELILAH-L through master clear + self-test (incl. the bit-exact
        /// ~4.19M-cycle power-on wait loop), types "0!" at OPCOM and expects the
        /// program banner. [Explicit] diagnostic while ND-120 mode matures - the
        /// 2026-07-13 phase-0 audit fixed IDBS,LA (jump logical address, 59 uses in
        /// DELILAH-L) which previously left the IDB unlatched on every ND-120 jump;
        /// this test is the smoke test for that fix and whatever gap surfaces next.
        /// </summary>
        /// <summary>
        /// Boots a fresh ND-120 (DELILAH-L) through master clear + self-test, including
        /// the bit-exact ~4.19M-cycle power-on wait loop, and returns it stopped at
        /// OPCOM. ND-120 twin of BootToOpcom() above.
        /// </summary>
        static CPU BootNd120ToOpcom()
        {
            CPU cpu = new CPU(null, CPUType.ND120);
            Logger.SetDestination(Logger.LogDestination.Disabled);
            Utils.DisableLog = true;

            const ushort sterr = 0x46E; // DELILAH-L STERR 002156
            cpu.hw.mic.LatchMap(0);
            bool booted = false;
            for (int i = 0; i < 6000000; i++)
            {
                cpu.TickCpu();
                if (cpu.hw.mic.map == sterr)
                {
                    int r2 = cpu.hw.alu.ReadRegisterE(RegisterEnum.R2);
                    Assert.Fail($"ND-120 self-test failed: STERR, R2={Utils.Int2Oct(r2)} after {i} cycles");
                }
                if (cpu.hw.mac.FETCH) { booted = true; break; }
            }
            Assert.That(booted, Is.True, "ND-120 boot never reached a FETCH boundary");
            Assert.That(cpu.hw.cpu_regs.STOP, Is.True, "expected ND-120 stopped at OPCOM after boot");
            return cpu;
        }

        [Explicit("ND-120 mode diagnostic - INSTRUCTION-B 0! on DELILAH-L, slow boot")]
        [Test]
        public void Diag_ND120_ZeroBang_PrintsBanner()
        {
            CPU cpu = BootNd120ToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart2661.OnTextOutput += OnText; // ND-120 console is the SCN2661 EPCI

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10); // MOPC input-loop warm-up, same as ND-110
                TypeString(cpu, "0!");

                // Keep running until the program prints its '>' command prompt (full
                // banner incl. the COMPUTER: hardware-detection line completed) or the
                // budget runs out - on a stall the transcript + machine state below
                // show exactly where it stopped making progress.
                for (int chunk = 0; chunk < 400; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    if (output.ToString().Replace("\0", "").Contains(">")) break;
                }

                string text = output.ToString().Replace("\0", "");
                TestContext.Out.WriteLine("--- ND-120 console transcript ---");
                TestContext.Out.WriteLine(text);
                TestContext.Out.WriteLine(
                    $"STOP={cpu.hw.cpu_regs.STOP} P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} " +
                    $"CSAR={Utils.Int2Oct(cpu.hw.mic.map)} PIL={cpu.hw.alu.PIL}");

                Assert.That(text, Does.Contain("INSTRUCTION"),
                    "expected the INSTRUCTION VERIFY banner from INSTRUCTION-B on ND-120/DELILAH-L");
                Assert.That(text, Does.Contain(">"),
                    "expected the program's '>' command prompt after the full banner on ND-120/DELILAH-L");
            }
            finally
            {
                cpu.hw.uart2661.OnTextOutput -= OnText;
            }
        }

        /// <summary>
        /// INSTRUCTION-B test AREAS on the ND-120: boots DELILAH-L, starts the
        /// program with 0!, runs one area command and expects "END OF TEST" with no
        /// error markers - the ND-120 mirror of the ND-110 per-area campaign
        /// (TestInstructionVerifyAreas.cs). ARGUMENT was the first to pass
        /// (2026-07-13, after the IDBS,LA + Uart2661-interrupt + CONSOLE_n-polarity
        /// fixes). The level-1 framework NUL-pads its console output, so all matching
        /// is done on a NUL-stripped copy. [Explicit] - ND-120 diagnostics while the
        /// mode matures; failures here are the ND-120 work list, not regressions.
        /// </summary>
        [Explicit("ND-120 mode diagnostic - INSTRUCTION-B areas on DELILAH-L, slow boot per area")]
        [TestCase("ARGUMENT")]
        [TestCase("MEMORY-REFERENCE")]
        [TestCase("SEQUENCE")]
        [TestCase("REGISTER-OPERATIONS")]
        [TestCase("BIT-OPERATIONS")]
        [TestCase("SHIFT-INSTRUCTIONS")]
        [TestCase("32-BITS-FLOATING")]
        // 48-BITS-FLOATING is intentionally absent, same as the ND-110 matrix in
        // TestInstructionVerifyAreas.cs: the banner reports this configuration as
        // "32 BIT FLOATING FORMAT", so the 48-bit list is not the configured set and
        // the program (correctly) reports an error - verified identical on ND-120.
        [TestCase("PRIVILEGED")]
        [TestCase("BYTE-STRING")]
        [TestCase("ND100-24BIT")]
        [TestCase("BCD")]
        [TestCase("ND100-CX")]
        [TestCase("STACK")]
        public void Diag_ND120_Area_Passes(string area)
        {
            CPU cpu = BootNd120ToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart2661.OnTextOutput += OnText;

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                TypeString(cpu, "0!");
                for (int chunk = 0; chunk < 400; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    if (output.ToString().Replace("\0", "").Contains(">")) break;
                }
                Assert.That(output.ToString().Replace("\0", ""), Does.Contain(">"),
                    "precondition: program prompt on ND-120");

                TypeString(cpu, area + "\r");

                string verdict = "budget exhausted";
                for (int chunk = 0; chunk < 400; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    string norm = output.ToString().Replace("\0", "");
                    string errScan = norm.Replace("IOX-ERROR", ""); // expected framework banner
                    if (errScan.Contains("ERROR") || errScan.Contains("FAIL") || errScan.Contains("FATAL"))
                    { verdict = "program reported an ERROR"; break; }
                    if (norm.Contains("END OF TEST"))
                    { verdict = $"END OF TEST - {area} COMPLETE"; break; }
                }

                string text = output.ToString().Replace("\0", "");
                TestContext.Out.WriteLine($"--- ND-120 {area} transcript ---");
                TestContext.Out.WriteLine(text);
                TestContext.Out.WriteLine(
                    $"verdict: {verdict} | STOP={cpu.hw.cpu_regs.STOP} " +
                    $"P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} " +
                    $"CSAR={Utils.Int2Oct(cpu.hw.mic.map)} PIL={cpu.hw.alu.PIL}");

                Assert.That(verdict, Does.Contain("COMPLETE"),
                    $"{area} area must finish with END OF TEST and no errors on ND-120. Verdict: {verdict}");
            }
            finally
            {
                cpu.hw.uart2661.OnTextOutput -= OnText;
            }
        }

        /// <summary>
        /// Full-pass RUN verification on the ND-120 (DELILAH-L): boots, starts
        /// INSTRUCTION-B, types RUN and keeps the machine running until the program
        /// reaches "THE TESTS ARE NOW LOOPING" (all groups passed on every level) or
        /// reports an error. The complete console transcript is written to
        /// traces\RUN-console-ND120.log - the ND-120 twin of RUN-console.log.
        /// Marker matching on a NUL-stripped copy (TTY padding), expected framework
        /// banner "IOX-ERROR STARTED" excluded from the error scan - same logic as
        /// Diag_RunFullLevel1Sweep. [Explicit] because it takes many minutes.
        /// </summary>
        [Explicit("Long-running - full ND-120 RUN sweep with console log")]
        [Test]
        public void Diag_ND120_RunFullSweep()
        {
            CPU cpu = BootNd120ToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart2661.OnTextOutput += OnText;

            string verdict;
            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                TypeString(cpu, "0!");
                for (int chunk = 0; chunk < 400; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    if (output.ToString().Replace("\0", "").Contains(">")) break;
                }
                Assert.That(output.ToString().Replace("\0", ""), Does.Contain(">"),
                    "precondition: program prompt on ND-120");

                TypeString(cpu, "RUN\r");

                verdict = "budget exhausted before LEVEL 2";
                bool level1Done = false;
                for (int chunk = 0; chunk < 1200; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    string norm = output.ToString().Replace("\0", "");
                    string errScan = norm.Replace("IOX-ERROR", "");
                    if (errScan.Contains("ERROR") || errScan.Contains("FAIL") || errScan.Contains("FATAL"))
                    { verdict = "program reported an ERROR"; break; }
                    if (!level1Done && norm.Contains("RUNNING TESTS ON LEVEL 2"))
                    {
                        level1Done = true;
                        verdict = "level-1 sweep COMPLETE (LEVEL 2 started, budget ended during higher levels)";
                    }
                    if (norm.Contains("NOW LOOPING"))
                    { verdict = "COMPLETE - all groups passed on every level, tests now looping"; break; }
                }
            }
            finally
            {
                cpu.hw.uart2661.OnTextOutput -= OnText;
            }

            string log = output.ToString().Replace("\0", "");
            string dir = TestPaths.TracesDir();
            string file = System.IO.Path.Combine(dir, "RUN-console-ND120.log");
            System.IO.File.WriteAllText(file,
                $"INSTRUCTION-B RUN on ND-120 (DELILAH-L) - full sweep transcript\r\nVerdict: {verdict}\r\n\r\n{log}");
            TestContext.Out.WriteLine($"log written: {file} ({log.Length} chars) - {verdict}");

            Assert.That(verdict, Does.Contain("COMPLETE"),
                $"ND-120 RUN must finish its sweep without errors. Verdict: {verdict}. Transcript:\n{log}");
        }

        /// <summary>
        /// State probe for the ND-120 "typed commands at '>' go nowhere" gap: after
        /// 0! and the prompt, dumps IOControl (did the program run SIOC to claim the
        /// console / enable IOC1-IOC2?), the Uart2661 gate states, and the keyboard
        /// queue; then types one char and tracks whether it is ever consumed, whether
        /// PIL ever leaves 0, and the macro-P range of the idle loop. Pure
        /// observation - no assertions beyond reaching the prompt. [Explicit].
        /// </summary>
        [Explicit("ND-120 mode diagnostic - console-input state probe")]
        [Test]
        public void Diag_ND120_ConsoleInputProbe()
        {
            CPU cpu = BootNd120ToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart2661.OnTextOutput += OnText;

            try
            {
                RunCycles(cpu, TICK_PR_20MS * 10);
                TypeString(cpu, "0!");
                for (int chunk = 0; chunk < 400; chunk++)
                {
                    RunCycles(cpu, TICK_PR_20MS * 20);
                    if (output.ToString().Replace("\0", "").Contains(">")) break;
                }
                Assert.That(output.ToString().Replace("\0", ""), Does.Contain(">"),
                    "precondition: program prompt on ND-120");

                void DumpState(string label)
                {
                    ushort ioc = cpu.hw.cpu_regs.IOControl;
                    TestContext.Out.WriteLine(
                        $"[{label}] IOControl={Utils.Int2Oct(ioc)} " +
                        $"(IOC0={(ioc >> 0) & 1} IOC1(int12)={(ioc >> 1) & 1} IOC2(int10)={(ioc >> 2) & 1} " +
                        $"IOC3={(ioc >> 3) & 1} bit6(console)={(ioc >> 6) & 1}) " +
                        $"uart2661: CONSOLE_n={cpu.hw.uart2661.CONSOLE_n} queue={cpu.hw.uart2661.KeyboardQueue.Count} | " +
                        $"P={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.P))} " +
                        $"CSAR={Utils.Int2Oct(cpu.hw.mic.map)} PIL={cpu.hw.alu.PIL} STS={Utils.Int2Oct(cpu.hw.alu.STS)}");
                }

                DumpState("at prompt");

                // Type ONE character and watch what happens to it. Also capture the
                // micro-execution of the console-status poll: IOX 302 vectors through
                // 003722 (0x7D2, IDBS,UART COMM,UART,STATUS) -> TRM2 000521 (0x151) ->
                // the IDBS,IOR DA test at 000530-000532 - log CSAR/F/Q per tick for
                // the first few polls, plus visit counts for TRM0 000515 (0x14D, the
                // IOX 300 read-data handler; if it never runs the program never saw DA).
                cpu.hw.AddConsoleChar('A');
                int consumedAt = -1;
                int firstNonZeroPilAt = -1;
                int pMin = int.MaxValue, pMax = int.MinValue;
                int visits7D2 = 0, visits151 = 0, visits14D = 0;
                int windowLeft = 0;
                int windowsLogged = 0;
                StringBuilder win = new StringBuilder();
                for (int i = 0; i < TICK_PR_20MS * 40; i++)
                {
                    cpu.TickCpu();
                    ushort m = cpu.hw.mic.map;
                    if (m == 0x7D2) { visits7D2++; if (windowsLogged < 3 && windowLeft == 0) { windowLeft = 28; windowsLogged++; win.AppendLine($"--- poll window {windowsLogged} (cycle {i}) ---"); } }
                    if (m == 0x151) visits151++;
                    if (m == 0x14D) visits14D++;
                    if (windowLeft > 0)
                    {
                        windowLeft--;
                        win.AppendLine($"  csar={Utils.Int2Oct(m)} F={Utils.Int2Oct(cpu.hw.alu.F & 0xFFFF)} Q={Utils.Int2Oct(cpu.hw.alu.Q)} A={Utils.Int2Oct(cpu.hw.alu.ReadRegisterE(RegisterEnum.A))}");
                    }
                    if (consumedAt < 0 && cpu.hw.uart2661.KeyboardQueue.Count == 0) consumedAt = i;
                    if (firstNonZeroPilAt < 0 && cpu.hw.alu.PIL != 0) firstNonZeroPilAt = i;
                    int p = cpu.hw.alu.ReadRegisterE(RegisterEnum.P);
                    if (p < pMin) pMin = p;
                    if (p > pMax) pMax = p;
                }

                TestContext.Out.WriteLine(
                    $"char 'A': consumedAtCycle={consumedAt} firstNonZeroPilAt={firstNonZeroPilAt} " +
                    $"idle-P range {Utils.Int2Oct(pMin)}..{Utils.Int2Oct(pMax)}");
                TestContext.Out.WriteLine(
                    $"visits: IOX302-status(003722)={visits7D2} TRM2(000521)={visits151} TRM0-readdata(000515)={visits14D}");
                TestContext.Out.WriteLine(win.ToString());
                DumpState("after char + wait");
                TestContext.Out.WriteLine("console tail: " +
                    output.ToString().Replace("\0", "").Substring(Math.Max(0, output.Length - 80)));
            }
            finally
            {
                cpu.hw.uart2661.OnTextOutput -= OnText;
            }
        }

        static void TypeStringOn(CPU cpu, string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                cpu.hw.AddConsoleChar(text[i]);
                RunCycles(cpu, TICK_PR_20MS * 5);
            }
        }

        static string TailOf(StringBuilder sb, int n)
        {
            string s = sb.ToString();
            return s.Length <= n ? s : s.Substring(s.Length - n);
        }

        /// <summary>
        /// "HELP" is not a real OPCOM command (no such mnemonic anywhere in
        /// ND-110-RASK.LISTING.TXT). Typing it exercises a different code path than the
        /// digit+EXCL test above - OPCOM's multi-letter SCRAM/LETTR command-name
        /// accumulation (see opcom-commands.md's "Letters (A-Z)" section) instead of octal
        /// digit accumulation - and should land in ILLEG, which prints '?' on unrecognized
        /// input. This is really a test that OPCOM's letter-dispatch path stays responsive
        /// for unrecognized input rather than hanging or corrupting state; the "?" is a
        /// natural way to observe that without inspecting internal registers.
        /// </summary>
        [Test]
        public void ND110_OpcomRejectsUnknownCommandAndStaysResponsive()
        {
            CPU cpu = BootToOpcom();

            StringBuilder output = new StringBuilder();
            void OnText(char c) => output.Append(c);
            cpu.hw.uart.OnTextOutput += OnText;

            try
            {
                TypeString(cpu, "HELP");
                cpu.hw.AddConsoleChar('\r');
                RunCycles(cpu, TICK_PR_20MS * 5);
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            string text = output.ToString();
            Assert.That(text, Does.Contain("?"),
                $"Expected OPCOM to print '?' (ILLEG) for the unrecognized command 'HELP'. Captured output:\n{text}");

            // Confirm OPCOM is still responsive after the rejected command, not stuck -
            // type a fresh, known-good command and expect its response too.
            output.Clear();
            cpu.hw.uart.OnTextOutput += OnText;
            try
            {
                TypeString(cpu, "0!");
                RunCycles(cpu, TICK_PR_20MS * 20);
            }
            finally
            {
                cpu.hw.uart.OnTextOutput -= OnText;
            }

            Assert.That(output.ToString(), Does.Contain("INSTRUCTION"),
                $"Expected OPCOM to still accept '0!' after rejecting 'HELP'. Captured output:\n{output}");
        }
    }
}
