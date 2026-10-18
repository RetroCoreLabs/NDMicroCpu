//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

//#define _DEBUG_
//#define _DETECT_BOOT_PROBLEM_
//#define _TIMER_ // for measuring CPU speed..


using System;
using System.Diagnostics;
using NDMicroCpu.Core.AMD;
using NDMicroCpu.Core.Common;
using NDMicroCpu.Core.Support;
using static NDMicroCpu.Core.Enums;

// References
// * http://sintran.com/sintran/hardware/nd-100/nd-324141.html

namespace NDMicroCpu.Core
{


    /// <summary>
    /// The ND-110/ND-120 CPU at micro code level: fetches 64-bit micro words from the control store and executes them in four phases, driving the ALU, the register file, memory, the interrupt system, the UART and the panel.
    /// </summary>
    /// <remarks>
    /// The main loop is in ExecuteMicroCode; the micro code itself comes from the .CODE images the NDMicroAsm assembler builds from the RASK and DELILAH listings.
    /// </remarks>
    public class CPU
    {
        #region Variables 

        /// <summary>
        /// What CPU type are we emulating (110 or 120)
        /// </summary>
        CPUType cpuType = CPUType.ND110;

        readonly ushort INSTALLATION_NUMBER = 1234;

        /// <summary>
        /// Disable internal RTC when running in Debug Server mode
        /// </summary>
        bool internalRtcDisabled = false;

        /// <summary>
        /// All internal HW are available through this object
        /// </summary>
        public CpuInternals hw { get; private set; }


        /// <summary>
        /// Decoded Microcode
        /// </summary>
        public MicroCode microCode { get; private set; }


        /// <summary>
        /// Dynamic memory
        /// </summary>
        public Memory memory;

        /// <summary>
        /// The Norsk Data I/O bus. Devices (floppy/disk/tape DMA controllers, tape
        /// readers) attach here; the COMM,IOX microcommand routes IOX register access to
        /// them, they DMA into <see cref="memory"/>, and they raise interrupts through the
        /// PIC. Inert (no devices) by default - see NDMicroCpu.Core/NDBus/NDBus.cs.
        /// </summary>
        public NDMicroCpu.Core.NDBus.NDBus iobus;


        /// <summary>
        /// it would take 786,432 clock ticks before the CPU counts for 20 ms using a 39.3216 MHz oscillator.        
        /// </summary>
        public const long TICK_20MS = 786432;

        /// <summary>
        /// keep track of number of micro instructions executed 
        /// </summary>
        long _ucInstructionCount = 0;

        /// <summary>
        /// Number of micro instructions that has been executed
        /// </summary>
        public long MicroInstructionCount { get => _ucInstructionCount; }


        /// <summary>
        /// IDB value on start of microinstruction execution. Used to read IDB value written to bus in last microInstruction (there is some clock-timings that needs the previos, including memory access)
        /// </summary>
        ushort start_IDB_value = 0;

#if _TIMER_
        Stopwatch stopwatch = new Stopwatch();
        long mhzCounter = 0;
#endif

        void Clear()
        {
            _ucInstructionCount = 0;

            // (Re)load BPUN to memory
            FileHelper.LoadDefaultBPUN(memory.Raw);
        }

        #endregion Variables 

        #region Constructor and Initialization

        /// <summary>
        /// Runtime selector for the MMU translation path: true = software-TLB fast path,
        /// false = legacy walk-every-access. Proxies Memory.TlbEnabled so it can be flipped
        /// at any time. Defaults to the legacy path (see Memory.TlbEnabled) until the TLB is
        /// 100% trusted; opt in via the constructor's useMmuTlb parameter or by setting this.
        /// </summary>
        public bool UseMmuTlb
        {
            get => memory.TlbEnabled;
            set => memory.TlbEnabled = value;
        }

        /// <summary>
        /// Full MMU translation-strategy selector (Legacy / Tlb / ResidentMap). Proxies
        /// Memory.MmuMode so it can be chosen at init or flipped at any time. UseMmuTlb above is
        /// the two-state convenience over the same setting.
        /// </summary>
        public MmuMode MmuMode
        {
            get => memory.MmuMode;
            set => memory.MmuMode = value;
        }

        /// <summary>
        /// Creates a CPU with its hardware and loads the control store for the CPU type.
        /// </summary>
        /// <param name="mem">
        /// The memory to use, or null to create a new one.
        /// </param>
        /// <param name="cpu_type">
        /// ND-110 or ND-120.
        /// </param>
        /// <param name="InstallationNumber">
        /// The installation number the back-wiring PROM returns to VERSN.
        /// </param>
        /// <param name="useMmuTlb">
        /// True selects the TLB mapping strategy instead of the page table walk.
        /// </param>
        public CPU(Memory? mem = null, CPUType cpu_type = CPUType.ND110, byte InstallationNumber = 0, bool useMmuTlb = false)
        {
            cpuType = cpu_type;

            // Set unique installation number. Normally read from a chip on the backplane.
            if (InstallationNumber != 0)
            {
                this.INSTALLATION_NUMBER = InstallationNumber;
            }

            if (mem != null)
            {
                memory = mem;
            }
            else
            {
                memory = new Memory();
                memory.AttachCPU(this);
            }

            // Select MMU translation path (legacy vs software TLB). Applies whether the
            // Memory was supplied or created here.
            memory.TlbEnabled = useMmuTlb;

            microCode = new MicroCode();
            hw = new CpuInternals(cpuType);

            // The I/O bus (the "IO BUS" the COMM,IOX handler TODO refers to). Constructed
            // here because it needs physical memory for DMA, which lives on the CPU. Inert
            // until devices are attached, so machines with nothing plugged in are unchanged.
            iobus = new NDMicroCpu.Core.NDBus.NDBus(new NDMicroCpu.Core.NDBus.MemoryDmaBus(memory));

            Clear();
            LoadCS();
#if _TIMER_
            stopwatch.Start();
            mhzCounter = 0;
#endif
        }


        /// <summary>
        /// When running in Debug Server mode, we dont want the RTC to go off by internal timer ticks.
        /// It will be synced with remote debugg client
        /// </summary>
        public void DisableInternalRTC()
        {
            internalRtcDisabled = true;
        }
        /// <summary>
        /// Puts the CPU back in its power-up state: registers, ALU, interrupt system and panel cleared.
        /// </summary>
        public void ResetCpu()
        {

            hw.cpu_regs.Clear();
            hw.cpu_regs.STOP = true;
            hw.alu.Clear();
            hw.RF.Clear();
            hw.RF1.Clear();
            hw.RF2.Clear();
            hw.RF3.Clear();
            hw.uart?.Reset();
            hw.uart2661?.Reset();
            hw.mic.LatchMap(0);

            this.Clear();
        }

        /// <summary>
        /// Load ControlStore from byte array
        /// In a real world ND-110 this would be loaded from PROM
        /// </summary>
        /// <param name="data">
        /// The .CODE image bytes.
        /// </param>
        /// <returns>
        /// Number of micro words loaded.
        /// </returns>
        public int LoadCSArray(byte[] data)
        {
            return hw.cs.LoadFromByteArray(data, cpuType == CPUType.ND110);
        }

        /// <summary>
        /// Load ControlStore from this CPU's own microcode file (NDMicroCpu.Core/MicroCode/*.CODE,
        /// deployed next to the built assembly via NDMicroCpu.Core.csproj).
        /// In a real world ND-110 this would be loaded from PROM.
        /// The CPU owns the filename - it is derived from cpuType, not passed in by the caller.
        /// </summary>
        /// <returns></returns>
        public bool LoadCS()
        {
            string fileName = GetMicrocodeFileName(cpuType);
            string fullPath = System.IO.Path.Combine(AppContext.BaseDirectory, "MicroCode", fileName);
            return hw.cs.LoadFromFile(fullPath, cpuType == CPUType.ND110);
        }

        private static string GetMicrocodeFileName(CPUType type) => type switch
        {
            CPUType.ND110 => "ND-110-RASK.CODE",
            CPUType.ND120 => "ND-120-DELILAH-L.CODE",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No microcode file mapped for this CPU type")
        };




        #endregion Constructor and Initialization

        #region Vectors

        const ushort UC_MOPC = 0x4B7;

        #endregion Vectors

        #region API for UnitTest and UI Debugger ONLY

        /// <summary>
        /// Perform just the macro-instruction fetch (same first step as
        /// UnitTest_FetchAndExecute) without executing any microinstructions.
        /// Lets a unit test drive TickCpu() itself to trace microcode cycle by cycle.
        /// Caller must have set P and latched the MAC address first.
        /// </summary>
        public void UnitTest_Fetch()
        {
            FetchFromRAM();
        }

        /// <summary>
        /// Set or clear the STOP flip-flop from a front-end (CLI / debugger).
        /// The flip-flop's setter is internal to NDMicroCpu.Core; this is the supported external
        /// hook, in the same spirit as the other UnitTest_* debugger APIs (StepMacroCode
        /// clears STOP itself, but a breakpoint-aware stepper that ticks the CPU manually
        /// needs to clear it too).
        /// </summary>
        public void UnitTest_SetStop(bool stop)
        {
            hw.cpu_regs.STOP = stop;
        }

        /// <summary>
        /// Execute instruction at given memory instruction.
        /// Used by Unit Tests
        /// </summary>
        /// <param name="address">
        /// The memory address of the instruction to fetch and execute.
        /// </param>
        /// <returns>-1 if invalid address, else number of microinstructions </returns>
        public int UnitTest_FetchAndExecute(ushort address)
        {

            Logger.Log($"UnitTest => Execute instruction at given memory instruction. Address={Utils.Int2Oct(address)}", Logger.LogLevel.Machine);


            int maxCnt = 100000;
            hw.cpu_regs.STOP = false; // for unit tests we must make sure the CPU is not stopped

            hw.alu.WriteRegisterE(RegisterEnum.P, address);
            hw.mac.LatchMacAddress(address);

            FetchFromRAM();// Fetch            
            do
            {
                TickCpu();
                if (maxCnt-- <= 0) return -1; // give up after 100.000 instructions

            } while (hw.mac.FETCH == false); // run until FETCH is set

            return hw.alu.ReadRegisterE(RegisterEnum.P);
        }


        /// <summary>
        /// Run Microcode directly, without being called from a MacroCode
        /// Used to trigger TRAP's from UnitTest
        /// </summary>
        /// <param name="maxExecute">
        /// Safety cap on the number of micro instructions executed before giving up.
        /// </param>
        public void UnitTest_RunUntilFetch(int maxExecute = 10000)
        {
            do
            {
                TickCpu();

                maxExecute--;
                if (maxExecute <= 0) return; // Stop after a fixed amount of microcode instructions

                // If FETCH is set, then a macro instruction is to be loaded, exit happily
            } while (hw.mac.FETCH == false);
        }


        /// <summary>
        /// Executes one macro instruction: runs micro code from the current address until the next fetch.
        /// </summary>
        /// <returns>
        /// True when the instruction completed, false when the CPU stopped.
        /// </returns>
        public bool StepMacroCode()
        {
            // max # of microcode steps, just for safety..
            int maxExecute = 100000;

            // If CPU is stopped, start it
            if (hw.cpu_regs.STOP) hw.cpu_regs.STOP = false;

            // Now step the macrocode, using
            do
            {
                TickCpu();

                maxExecute--;
                if (maxExecute <= 0) return false; // Stop after a fixed amount of microcode instructions

                // If FETCH is set, then a macro instruction is to be loaded, exit happily
            } while (hw.mac.FETCH == false);

            return true;
        }


        /// <summary>
        /// Step microcode.
        /// NB! Microcode must already be loaded
        /// </summary>
        public void StepMicroCode()
        {
            InternalExecuteMicroCode();  // Will return -1 when micro instruction is done (meaning, its ready for a new macro instruction)
        }

        #endregion API for UnitTest and UI Debugger ONLY


        #region Binary-load boot facade (NDBus Phase 3/4)
        /// <summary>
        /// Plug a boot image onto the I/O bus and press the panel LOAD button - the headless
        /// primitive behind "boot a BPUN over the bus" for both the web and local front-ends.
        ///
        /// This attaches a <see cref="NDMicroCpu.Core.NDBus.NdBinaryLoadDevice"/> (the PIO byte-stream
        /// "binary load" personality of the floppy at octal 1560), selects it via ALD=1
        /// (ALDVC 003560 jump-table entry 1 = 1560 binary load, established by
        /// TestBinaryLoaderProbe), and raises the panel LOAD interrupt. The RASK BINARY LOADER
        /// microcode (ETLO1 002210) then streams the image, lays the program into memory and -
        /// if the BPUN Action field is 0 - COMM,STARTs it at the BPUN Start address.
        ///
        /// PRECONDITION: the CPU must already have booted to the OPCOM wait state (master clear
        /// + self-test complete, STOP set) - LOAD is only honoured from that state. The caller
        /// keeps ticking (TickCpu) after this returns; the returned device's BytesRead / IsActive
        /// let a UI show load progress.
        ///
        /// <paramref name="bpunImage"/> is the RAW bytes of a .BPUN file / ND binary-load stream
        /// (Preamble '!' Address Count Data Checksum Action, big-endian) - identical to what
        /// NDMicroCpu.Core.Support.FileLoader.BPUN decodes; we do NOT parse it, the microcode does.
        /// </summary>
        /// <returns>The attached device (for progress/telemetry), so a UI can watch the load.</returns>
        public NDMicroCpu.Core.NDBus.NdBinaryLoadDevice PanelBinaryLoad(byte[] bpunImage)
        {
            NDMicroCpu.Core.NDBus.NdBinaryLoadDevice device = new NDMicroCpu.Core.NDBus.NdBinaryLoadDevice(bpunImage);
            iobus.Attach(device);

            // ND-120 has a NOISE flip-flop (RegisterFile "AB,NOISE": set on MACL, reset on STOP)
            // that INHIBITS the LOAD button until the CPU has passed through the STOP handler
            // (DELILAH-L 002203) - so the faithful operator sequence is STOP, then LOAD. Press
            // STOP and let the microcode process it (clearing NOISE) before LOAD, else the ND-120
            // LOAD microcode (002252) reads NOISE!=0 and bails to CONT without loading. ND-110 has
            // no NOISE FF (LOAD gates on STP), so this is skipped there and its behaviour is unchanged.
            if (cpuType == CPUType.ND120)
            {
                hw.panel.PushPanelButton(Panel.PanelButton.STOP);
                // One panel/RTC poll period is enough for the STOP interrupt to be taken and the
                // STOP handler to clear NOISE; 150000 micro-cycles (~3+ RTC periods) is safe margin.
                for (int i = 0; i < 150000; i++) TickCpu(); // let the STOP handler clear NOISE
            }

            // ALD selects the ALDVC (003560) jump-table entry = boot device. Entry 1 = floppy 1560
            // binary load. ND-120's IDBS,ALD inverts bits 0-3, so entry 1 needs ALD=14 there
            // (LC = (ALD & 0xF) ^ 0xF = 1) versus ALD=1 on ND-110. Established by TestBinaryLoaderProbe.
            hw.cpu_regs.ALD = (ushort)(cpuType == CPUType.ND120 ? 14 : 1);
            hw.panel.PushPanelButton(Panel.PanelButton.LOAD); // raise PanelVector.LOAD -> LOAD microcode
            return device;
        }
        #endregion Binary-load boot facade (NDBus Phase 3/4)


        static int lastAddress = 0;


        #region PUBLIC API FOR CPU
        /// <summary>
        /// Tick the machine
        /// 
        /// In the ND-110, the main crystal oscillator is a 39.3216 MHz oscillator.
        /// </summary>
        /// <returns></returns>
        public void TickCpu()
        {


            // Disabled when running in Debug Server mode?
            if (!this.internalRtcDisabled)
            {
                hw.panel.Tick(hw.cpu_regs.STOP);// Tick Real Time Clock inside PANEL

                if (cpuType == CPUType.ND110)
                {
                    hw.uart?.Tick();
                }
                else
                {
                    hw.uart2661?.Tick();
                }
            }

            // Clock the I/O bus every micro-cycle (drives device DMA/delay/completion) and
            // fold any device interrupts into the PIC. No devices attached => no-op, so
            // this is behaviour-neutral for a bare machine. Levels are 10-13; the device
            // interrupt-bit positions match those ND levels (see INDBusDevice.InterruptBits).
            iobus.Clock();
            int busIrq = iobus.PendingInterruptBits();
            if (busIrq != 0)
            {
                if ((busIrq & (1 << 10)) != 0) hw.SetInternalInterrupt(InterruptPin.INT10);
                if ((busIrq & (1 << 11)) != 0) hw.SetInternalInterrupt(InterruptPin.INT11);
                if ((busIrq & (1 << 12)) != 0) hw.SetInternalInterrupt(InterruptPin.INT12);
                if ((busIrq & (1 << 13)) != 0) hw.SetInternalInterrupt(InterruptPin.INT13);
            }

#if _DETECT_BOOT_PROBLEM_

            if (hw.mic.map == 0x43B)
            {
                Utils.DisableLog = false;
            }

            if (hw.mic.map == 0x449) // 002111 STERR
            {
                int r2 = hw.alu.ReadRegisterE(RegisterEnum.R2);
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"STERR: {Utils.Int2Oct(r2)} from {Utils.Int2Oct(lastAddress)}");
                Console.ResetColor();

                var a = Console.ReadLine();
                return;
            }

#endif
#if _TIMER_
    
            mhzCounter++;
            if (stopwatch.ElapsedMilliseconds> 1000 * 10)
            {
                mhzCounter= mhzCounter / 10;
                Console.WriteLine($"MHZ = {mhzCounter.ToString("#,#")}");
                stopwatch.Restart();
                mhzCounter = 0;
            }
#endif
            bool isOpcom = (hw.cpu_regs.IOControl & 1 << 6) == 0;

            // When FETCH is false, load next microcode and execute, or OPcom is enabled


            #region Check for Interrupt/TRAP

            if (_InterruptDetected)
            {
                if (hw.mac.FETCH)
                {


                    if (hw.PanelInterruptDetected())
                    {
                        //hw.SetTrap(0x0E); // Set PANEL interrupt bit (octal 016)

                        //if (hw.panel.InterruptVector == Enums.PanelVector.MS20)
                        //{
                        //    if (hw.IsRTCInterruptEnabled())
                        //    {
                        //        hw.SetInternalInterrupt(Enums.InterruptPin.INT13);                                
                        //    }
                        //} 

                        hw.ClearPanelInterrupt();
                    }

                    // Read status from Interrupt Controller.
                    // Allow only these interrupts to run when CPU is fetching a new MACRO opcode
                    if (hw.pic.ReadInterruptRequestPin == false) // active low
                    {

                        Logger.Log($"Interrupt detected!", Logger.LogLevel.Machine);
                        hw.SetTrap(0x0F); //017 Macro Interrupt 
                    }
                }
            }


            int vector = hw.GetTrapVector();



            // Vector 0-13 creates immediate TRAP, while 14 and 15 is treated like interrupt, ie waiting for the next FETCH to be set
            if (vector >= 0)
            {
                //Utils.DisableLog = false; // enable logging                


                switch (vector)
                {
                    case 0x0E: // Panel Interrupt
                               // Wait for the Macrocode to finish and then execute the Panel Interrupt vector.
                               // When MacroCode is finished the FETCH flag will be set
                               // (ie dont jmp until macrocode is finished)
                        if (hw.mac.FETCH)
                        {
                            Logger.Log($"DISPATCH vector=0x0E (panel) liveIRQ={!hw.pic.ReadInterruptRequestPin} picEnabled={hw.pic.InterruptRequestEnabled}", Logger.LogLevel.Debug);
                            _InterruptDetected = false;

                            hw.mac.FETCH = false;
                            hw.ClearTrapVector(vector);

                            // Acknowledge the panel interrupt NOW (at dispatch), not at the
                            // next FETCH-with-_InterruptDetected: TrapController's
                            // UpdateTrapVectorsFromSignals() runs every micro-cycle and
                            // re-armed vector 0x0E from the still-set detect flip-flop while
                            // the MS20 service itself was running (FETCH low), so every 20ms
                            // panel tick was serviced TWICE - each pass through PANVC
                            // (CS 003760-003762) doing its own P := P-1 (traced 2026-07-11
                            // in Diag_InstructionVerifyHelp's micro ring).
                            hw.ClearPanelInterrupt();

                            CompensateVectorPDecrement();
                            hw.mic.LatchMap((ushort)vector);
                        }
                        break;
                    case 0x0F: // Macro Interrupt

                        // Wait for the FETCH for interrupts (ie dont go to vector until macrocode is finished).
                        if (hw.mac.FETCH)
                        {
                            Logger.Log($"DISPATCH vector=0x0F (macro int) liveIRQ={!hw.pic.ReadInterruptRequestPin} picEnabled={hw.pic.InterruptRequestEnabled}", Logger.LogLevel.Debug);
                            _InterruptDetected = false;

                            hw.mac.FETCH = false;
                            hw.ClearTrapVector(vector);
                            CompensateVectorPDecrement();
                            hw.mic.LatchMap((ushort)vector);
                        }
                        break;
                    default:
                        Logger.Log($"TRAP detected, Vector = {vector:X2} ", Logger.LogLevel.Machine);
                        // TRAP, jump to vector (exit macrocode if its executing)
                        hw.mac.FETCH = false;
                        hw.ClearTrapVector(vector);
                        hw.mic.LatchMap((ushort)vector);

                        break;
                }

            }

            #endregion Check for TRAP


            // Latch interrupt ?
            if ((hw.pic.ReadInterruptRequestPin == false) || (hw.PanelInterruptDetected()))
            {
                _InterruptDetected = true;
            }


            if (hw.mac.FETCH)
            {
                if (isOpcom) // opcom owns the console (IOC bit 6 = 0) - keep the log quiet
                {
                    Utils.DisableLog = true;
                }

                // BUG FIX (2026-07-12): "console assigned to OPCOM" must NOT stop macro
                // execution. The microcode's own MOPC-while-running design proves it:
                // the OPCOM instruction (150400, PINF1 CS 000631 -> VECT1 entry 0 ->
                // INPUT CS 002351) polls the console ONCE, and its no-data path DISPL
                // (CS 002417) ends with "T,JMP CONT" - continue the macro program.
                // Console characters typed afterwards are serviced by microcode at
                // instruction boundaries. The old code skipped the fetch entirely
                // whenever IOC bit 6 was clear, freezing the whole emulator (FETCH
                // pending forever, no microcode either): INSTRUCTION-VERIFY's OPCOM
                // command hung exactly like that. Stopping macro execution is the STOP
                // flip-flop's job (checked below), not the console assignment's.
                {
                    if (!this.hw.cpu_regs.STOP)
                    {
                        // Running, not stopped! - Load next opcode
                        {
                            ushort macroAddr = (ushort)(hw.alu.ReadRegisterE(RegisterEnum.P));

                            //if (macroAddr == 0x10) // 020 oct
                            //   Utils.DisableLog = false; // enable logging

                            //Console.WriteLine($"FETCH from # {Utils.Int2Oct(macroAddr)}");

                            //if (macroAddr == 0xAF)
                            //    Utils.DisableLog = false;

                            hw.mac.LatchMacAddress(macroAddr);
                            FetchFromRAM();

                            //TODO: When do i calculate the new P ? Check IDBS 34-JMPA LA(15-10), CA (9-0) for Updating P in ALU ( case IDBSource.JMPA:)
                            //hw.mac.CalculateNextMacAddress();
                            //hw.alu.WriteRegisterE(RegisterEnum.P, hw.mac.mac_address);

                            if (this.internalRtcDisabled)
                            {
                                // Macro "opcode" ticking on new FETCH MAC to be in sync with ND-100 emulator
                                hw.panel.Tick(hw.cpu_regs.STOP);// Tick Real Time Clock inside PANEL
                                if (cpuType == CPUType.ND110)
                                {
                                    hw.uart?.Tick();
                                }
                                else
                                {
                                    hw.uart2661?.Tick();
                                }
                            }
                        }
                    }
                }
            }

            //if (!hw.mac.FETCH)
            else
            {
                if (LoadMicroCode())
                {
                    lastAddress = hw.mic.map;

                    Logger.Log($"LOAD microcode. Address mic={Numeric.Num2Str(lastAddress)} mac={Numeric.Num2Str(hw.mac.fetch_address)} ", Logger.LogLevel.Opcodes);

                    //if ((hw.mic.map == 0x141) && (hw.mac.fetch_address ==  0x5688)) // mic = 0x13b mac=0x5403
                    //   Console.WriteLine("IOX13");
                    InternalExecuteMicroCode();  // during the execution of the microcode the interrupt detect may be cleared

                    // ---------------------------------------------------------------------
                    // ND-120 MASTER-CLEAR POWER-ON DELAY SHORT-CIRCUIT (emulator-level only)
                    // ---------------------------------------------------------------------
                    // The ND-120 DELILAH-L microcode is kept BIT-EXACT to the EPROM dumps, so
                    // (unlike RASK) its source cannot be patched. Its master-clear routine runs
                    // the real "% WAITING LOOP 0.5 - 1 SECOND" delay:
                    //
                    //   MACL3: 002003  "B,1  ALUF,PASSD  ALUD,Q"      -> seeds Q = 1
                    //          002003   ... MACL4 CONDENABL           -> enter inner delay loop
                    //   MACL4: 001777  "IDBS,ALU ALUF,Q+1 ALUD,Q  MACL4 CONDENABL"
                    //                   -> Q := Q+1, loop on itself until Q wraps 0xFFFF->0
                    //                      = 65536 inner micro-cycles per pass
                    //   002000 "B,R1 ALUF,B-1 ALUD,B  T,JMP MACL3"    -> outer loop, R1 x64
                    //
                    // That is 64 * 65536 = 4,194,304 pure-wait micro-cycles before SELFT even
                    // starts (see ND-120-DELILAH-L.LISTING.TXT lines 5105-5123, and the
                    // BootCycleBudget comment in TestND110/Boot/TestBootSequence.cs).
                    //
                    // RASK (ND-110) avoids this because its microcode SOURCE was hand-patched:
                    // MACL3 at octal 001754 seeds "B,177777" instead of "B,1" so the very first
                    // MACL4 "Q+1" wraps immediately and the inner loop exits after ONE cycle
                    // ("changed to avoid delay during MACL. Wait not neccesary in emulator
                    // (Ronny: 01.01.2023)", ND-110-RASK.LISTING.TXT line 4773).
                    //
                    // We reproduce EXACTLY that ND-110 semantics for ND-120 here, at the
                    // emulator level, WITHOUT touching the bit-exact DELILAH-L binary: right
                    // after the MACL3 seed microinstruction (octal 002003 = 0x403) has run and
                    // loaded Q = 1, we overwrite Q with 0xFFFF. The following MACL4 "Q+1" then
                    // wraps on its first iteration, so each of the 64 outer passes costs one
                    // inner cycle instead of 65536 - collapsing ~4.19M wait cycles to ~a few
                    // hundred, matching ND-110's fast boot.
                    //
                    // GATING: strictly cpuType == ND120 AND the exact MACL3 CSAR (0x403). MACL3
                    // is only ever reached inside the master-clear delay loop, never during
                    // normal macro-program execution or the self-test, so this cannot affect
                    // architectural correctness - it only collapses a pure busy-wait, exactly
                    // like the ND-110 source patch it mirrors.
                    if (cpuType == CPUType.ND120 && lastAddress == 0x403)
                    {
                        hw.alu.Q = 0xFFFF; // seed near wrap, same as RASK "B,177777"
                    }

                }
                else
                {
                    Logger.Log($"LoadMicroCode() failed", Logger.LogLevel.Opcodes);
                }
            }

            #region Counters
            _ucInstructionCount++;

            #endregion Counters
        }


        bool _InterruptDetected;
        internal void ClearIntrqnFlipFlop()
        {
            _InterruptDetected = false;
        }

        /// <summary>
        /// Pre-increment P before latching interrupt vectors 0x0E / 0x0F.
        ///
        /// The microcode entered from these vectors unconditionally executes P := P - 1:
        ///   - vector 0x0E: CS 000016 -> PANEL stub -> PANVC (CS 003760/003761/003762, all
        ///     three entries are "B,P ALUF,B-1 ALUD,B")
        ///   - vector 0x0F: CS 000017 -> MACRI -> ITSRV (CS 003740+) -> XINT (CS 001101,
        ///     "B,P ALUF,B-1 ALUD,B")
        /// That decrement was written for the real hardware, where the interrupt is
        /// recognized while fetching the NEXT macro instruction - i.e. AFTER the fetch has
        /// already incremented P - so P-1 backs P up to the correct resume address.
        /// (COND,PXM in the PANEL/MACRI stubs adds a second decrement only in prefix mode,
        /// which never applies at this emulator's instruction-boundary dispatch.)
        ///
        /// This emulator, however, dispatches at the FETCH boundary BEFORE FetchFromRAM()
        /// has incremented P - P already IS the resume address. Without compensation the
        /// microcode's P-1 made every panel (MS20/20ms) and macro interrupt resume one
        /// instruction EARLY, silently re-executing the last completed instruction. That is
        /// usually invisible (idempotent poll instructions) but corrupts control flow when
        /// the re-executed instruction is a taken conditional jump or a skip-return:
        /// INSTRUCTION-VERIFY's console input poll (macro 052321 JAZ around the accept path
        /// at 052322-052325) then "accepted" a character that was never received, and the
        /// program echoed the phantom NUL as an ampersand and an at sign forever (traced 2026-07-11 in
        /// Diag_InstructionVerifyHelp's micro ring: P walked 052326 -> 052323 across one
        /// RTC episode of PANVC/PANVC/XINT decrements).
        /// </summary>
        private void CompensateVectorPDecrement()
        {
            ushort p = hw.alu.ReadRegisterE(RegisterEnum.P);
            hw.alu.WriteRegisterE(RegisterEnum.P, (ushort)(p + 1));
        }


        /// <summary>
        /// Fetch MacroCode
        /// Loads registers based on the operand
        /// Calculates the mic.map based on the operand entry-point
        /// </summary>
        /// <param name="operand"></param>
        /// <returns></returns>
        public bool Fetch(ushort operand)
        {

            #region Initialize CPU register via MacroOperand
            hw.cpu_regs.Operand = operand;

            /* When a new macroinstruction is executed, some CPU registers have a defined value */
            hw.cpu_regs.IR = operand & 0x7ff; // Bit 0-10 of the instruction will be loaded into the instruction register
            hw.alu.GPR = operand; // GPR is loaded with the machine instruction every time we decode a new Macro Instruction
                                  //alu.Latch(alu.ReadRegisterE(RegisterEnum.P)); // latch the P register into the ALU ?? dont believe. removed 07.02.23


            // False sequence is JMP and POP.
            hw.cpu_regs.COND_AddressSelect = NextAddressSelect.Jump;
            hw.cpu_regs.COND_StackControl = StackControl.Pop;


            #endregion Initialize CPU register via MacroOperand

            hw.mic.CalculateMacEntryPoint(operand);// Sets mic.map            
            if (LoadMicroCode())
            {
                Utils.Log($"Loaded operand {Utils.Int2Oct(operand)} from uc-address {Utils.Int2Oct(hw.mic.map)}", true);
                return true;
            }

            return false;

        }


        /// <summary>
        /// Loads MicroCode from Control Store.
        /// mic.map MUST be set before calling this function using hw.mic.LatchMap(ucAddress);
        /// </summary>        
        /// <returns>TRUE if load was successful</returns>
        public bool LoadMicroCode()
        {
            try
            {
                // Log what address we latched
                Utils.Log("", true);
                Utils.Log($"[MAP:{Utils.Int2Oct(hw.mic.map, true)}]");

                // Load MicroCode from Control Store            
                ulong uc = hw.cs.Read(hw.mic.map);

#if _DUMP_MICROCODE_FOR_TEST_
                if (hw.mic.map!=1023)
                    Debug.WriteLine($"{{ 0x{hw.mic.map}, 0x{uc:X8} }},");
#endif // _DUMP_MICROCODE_FOR_TEST_

                if (uc == 0) return false; // HALT

                // Decode the microinstruction
                microCode.DecodeMicroCode(uc);

                // Set register ARG to be the 16 lower bits of the microinstruction.
                hw.cpu_regs.ARG = (ushort)uc;

                // Set FETCH to OFF
                hw.mac.FETCH = false;

                return true;
            }
            catch (Exception ex)
            {
                Utils.Log($"LoadMicroCodeFromAddress - Exception: {ex}");
            }

            return false;

        }

        #endregion PUBLIC API FOR CPU

        /// <summary>
        /// Execute instruction at given memory instruction.
        /// </summary>
        void FetchFromRAM()
        {
            // The P register points to the next machine instruction.
            //hw.mac.mac_address = hw.alu.ReadRegister((ushort)RegisterEnum.P);

            if (memory.ValidAddress(hw.mac.mac_address))
            {
                ushort opcode = memory.ReadPhysicalMemory(hw.mac.mac_address);
                hw.mac.fetch_address = hw.mac.mac_address; // Store the FETCH address for later calculations

                // Hot path (every instruction fetch): guard the interpolation so it costs nothing
                // unless CPU logging is actually enabled - otherwise this string is built per fetch.
                if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
                    Logger.Log($"FETCH[{hw.alu.PIL}]:  address[{Numeric.Num2Str(hw.mac.mac_address)}] opcode[{Numeric.Num2Str(opcode)}]  A[{Numeric.Num2Str(hw.alu.ReadRegisterE(RegisterEnum.A, false))}] T[{Numeric.Num2Str(hw.alu.ReadRegisterE(RegisterEnum.T, false))}] X[{Numeric.Num2Str(hw.alu.ReadRegisterE(RegisterEnum.X, false))}] B[{Numeric.Num2Str(hw.alu.ReadRegisterE(RegisterEnum.B, false))}]", Logger.LogLevel.CPU);

#if _DEBUG_
                string regs = "";

                regs += $"({hw.alu.PIL}) ";
                regs += $"P:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.P))} ";
                regs += $"A:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.A))} ";
                regs += $"D:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.D))} ";
                regs += $"T:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.T))} ";
                regs += $"L:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.L))} ";
                regs += $"X:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.X))} ";
                regs += $"B:{Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.B))} ";

                ushort sts = (ushort)(hw.alu.STS | 1 << 12 | ReadALURegister(NDMicroCpu.Core.Enums.RegisterEnum.STS)); // bit 12 is ND100 flag, force it!

                regs += $"STS:{Utils.Int2Oct(sts)} ";


                Console.WriteLine($"[{Utils.Int2Oct(hw.mac.mac_address)}] {Utils.Int2Oct(opcode)} {regs}");
#endif // #if _DEBUG_

                hw.mac.CalculateNextMacAddress();
                hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);
                Fetch(opcode);
            }
            else
            {
                // TODO: Set Out of memory TRAP
            }
        }


        /// <summary>
        /// Execute all steps in the loaded microcode
        /// Before calling this function you need to load and decode the microcode
        /// </summary>
        /// <returns></returns>
        private void InternalExecuteMicroCode()
        {
            hw.cpu_regs.P_was_updated = false;

            start_IDB_value = hw.idb.idb_data;

            // Capture the loop counter BEFORE this instruction can modify it (LDLC in
            // phase 3, LCOUNT in phase 4). The next instruction's A,LC / B,LC operand
            // must see this pre-modification value - see MIC.LC_operand for the ground
            // truth (ND-06.031.1EN LDLC: "The modified loop counter will not be available
            // as an A-operand until the next microinstruction has been completed").
            int lcAtInstructionStart = hw.mic.loopCounter;

            Execute_1_LatchInternalBus();
            Execute_2_LatchALU();
            Execute_3_Commands();
            Execute_4_ConditionControl();

            hw.mic.LatchLCOperand(lcAtInstructionStart);

            // Update NWP info
            hw.cpu_regs.P_was_updated_last_cycle = hw.cpu_regs.P_was_updated;

            // Next ucode adress should now be updated in hw.mic.map!
        }

        private void Execute_4_ConditionControl()
        {
            #region Execute Test

            bool test_result = hw.cpu_regs.COND;



            // Microinstruction branching uses the standard format, whereby the condition and false sequence must be set up
            // at least one microinstruction before CONDENABL (condition enable) is executed. The test is on the result of the set condition
            // in the previous microinstruction

            // Bit 23 (ECOND) enables testing (against the previously set test condition) of the result of the previous microinstruction.
            if (microCode.ECOND)
            {

                // Don't execute test here.
                // According to ND-docs:
                // If a test object changes state, one microinstruction will be executed before the change is detected.
                // This is caused by the pipelining of ALU, and sequencing calculations.
                test_result = hw.cpu_regs.COND; // Load previous test result
                //test_result = ExecuteTestCondition(hw.cpu_regs.COND_TestObjects);

                //hw.cpu_regs.COND = ExecuteTestCondition(hw.cpu_regs.COND_TestObjects);


            }

            // LCZ0 is re-sampled from the loop counter on every microinstruction, BEFORE a
            // potential count - which gives the documented one-instruction detection delay
            // ("The new value of the loop counter will not be available as ... test object
            // until after the next microinstruction", ND-110 microcode doc for bit 22).
            // CLFF GATING (ground truth, ND-120 CGA_MIC.v GATES_3: LCZ_n =
            // NAND(NOR(LC4, LC5, CLFF), LC[3:0]==LCC)): a microinstruction executing
            // COMM,CLFF forces LCZ FALSE for that cycle. FAD's alignment loop depends on
            // this - its arming instruction (CS 003204: COND,LC=0 + COMM,CLFF) suppresses
            // LCZ so a freshly loaded loop counter of 0 (|exp diff| = 1, loaded at
            // CS 003156/003160) is not seen until the NORM1 loop has run its extra pass,
            // giving the SRD net |exp diff| shifts. Without this, FAD 1.0+3.0 returned 5.0.
            hw.mic.ClockLZ0(microCode.command == Commands.CLFF);

            if (microCode.LOOP)
            {
                // Bit 22 (LOOP) increments or decrements the loop counter (always towards zero).
                //
                // LCOUNT COUNTS UNCONDITIONALLY (corrected 2026-07-27 - see FPP BIAS below).
                // The token doc is "COUNT LOOP-COUNTER : IF FALSE 'RETURN'&'HOLD'"
                // (ND110Tokens.cs / ND120Tokens.cs): those are two INDEPENDENT effects - the
                // counter counts on every pass, and *separately* the condition picks the
                // next-address path. There is no condition gate on the count itself.
                //
                // FPP BIAS (2026-07-27): the previous "suppress the count on the
                // condition-true exit pass" rule made NLZ emit every result ONE BINADE HIGH
                // (NLZ 20 of 1 -> 040200 instead of 040100), because NLZ's normalize loop
                // derives its exponent FROM the final loop counter. ND-110-RASK.uc:
                //   CS 003462 (NLZ2)  "B,R5 ALUF,D-1 ... COMM,LDLC 21"  -> R5 = 20 octal = 16,
                //                     LC = 21 octal = 17  (= max shifts + 1, see below)
                //   CS 003463         the SLB shift loop, LCOUNT, exits on the armed COND,F15
                //   CS 003464         one SRB to undo the loop's documented overshoot pass
                //   CS 003465         "A,R5 B,LC ALUF,A-D-1 ALUD,Q"  -> Q = R5 - LC - 1
                //                     which must equal the NET shift count
                //   CS 003466         "A,R6 B,R6 ALUF,A-Q"           -> exponent -= Q
                // For A=1: 15 net shifts + 1 overshoot pass + 1 exit pass = 17 passes, so LC
                // must reach 0 and Q = 16-0-1 = 15. Suppressing the exit pass left LC=1,
                // Q=14, and the exponent one too high. For A=3: 16 passes -> LC=1, Q=14. Both
                // land exactly right ONLY if every pass counts. Confirmed four ways: the
                // NLZ->DNZ round trip returns N (it returned 2N before), 040100 is this
                // microcode's exact FMU multiplicative identity, the bias constant NLZ itself
                // loads is 0400 = 256 (CS 003451 "A,10 ... IDBS,BMG"), and RetroCore's
                // offline suite independently asserts 040100/040240/040544/041013.
                //
                // WHY THIS DOES NOT BREAK THE TWO CASES THE OLD RULE WAS BUILT FOR - both
                // read the counter through the LC-OPERAND PIPELINE LATCH (MIC.LatchLCOperand:
                // "the modified loop counter will not be available as an A-operand until the
                // next microinstruction has been completed"), and that latch is what decides
                // whether the exit pass's count is even visible:
                //   * CHKIT level scan (ND-110-RASK.LISTING.TXT CS 001066-001071) loads
                //     LC=13 octal, shifts PID&PIE left testing COND,F11, and reads "B,LC"
                //     at CS 001071 - the VERY NEXT microinstruction after the loop. The
                //     operand latch therefore serves the value from the START of the exit
                //     pass, i.e. BEFORE that pass's count. The level number is unchanged
                //     whether the exit pass counts or not, so the old suppression was never
                //     what made CHKIT work. (This was the 2026-07-11 fix for "WRONG VALUE OF
                //     P-REG AFTER TEST"; INSTRUCTION-VERIFY still passes without it.)
                //   * UPACK/UWRTS (CS 012464-012465) needed the count to happen; the old rule
                //     needed a "bare SCOND arm" exception (2026-07-19, fixes UPACK 140125 -
                //     otherwise the loop counter stuck at 15 instead of 15->14 and the top
                //     ASCII word was recovered from the wrong RF-0 slot). Counting always
                //     gives that case what it wanted directly, so the exception is gone.
                //   * LCZ-terminated loops (COND,LC=0 - LVSWP register copy at CS 001121, FAD
                //     alignment at CS 003204) are unaffected either way: they exit when LC is
                //     already 0 and IncrementLoopCounter is a no-op at 0.
                // Verified: the FULL TestND110 suite is green with this change, including
                // SelfTestPasses, ND110_CanRunInstructionVerify and every INSTRUCTION-VERIFY
                // area (BYTE-STRING/UPACK, SHIFT-INSTRUCTIONS, 32-BITS-FLOATING, PRIVILEGED).
                //
                // ---- historical note, kept because the hardware citations are still the
                //      best derivation of the condition PIPELINE (they are what ruled out a
                //      static microword bit as the discriminator) ----
                // Previous rule (2026-07-11, superseded): the LCOUNT token means
                // "count the loop counter : if FALSE 'RETURN'&'HOLD'" (ND110Tokens.cs) - the
                // count belongs to the repeat (condition-false) passes, NOT to the final
                // (condition-true) exit pass. This matters for condition-terminated scans
                // where LC itself is the RESULT: the CHKIT level scan (ND-110-RASK.LISTING.TXT
                // CS 001066-001071) loads LC=13 octal, shifts PID&PIE left once per pass
                // testing COND,F11, and expects LC to hold the found LEVEL NUMBER on exit
                // ("NEW LEVEL -> Q", CS 001071). Because of the documented one-instruction
                // condition-detection delay, the exit pass runs one cycle after F11 went
                // true - and if that pass also counted, every level came out one too low:
                // MST PID for level 1 entered "level 0" (= no switch), which made every
                // INSTRUCTION-VERIFY RUN test fail with "WRONG VALUE OF P-REG AFTER TEST".
                // LCZ-terminated loops (COND,LC=0 - e.g. LVSWP register copy at CS 001121,
                // FAD alignment) are unaffected: they exit when LC is already 0 and
                // IncrementLoopCounter is a no-op at 0.
                //
                // BARE-SCOND-ARM EXCEPTION (fixes UPACK 140125, 2026-07-19): the suppress-on-
                // -true above is correct ONLY when the condition now in COND is a *settled,
                // tested* condition. Two microwords with the IDENTICAL byte pattern
                // "...T,NEXT T,POP LCOUNT" need OPPOSITE count behaviour:
                //   * CS 001070 (CHKIT level scan): its condition F11 was armed at CS 001066
                //     (COND,F11) and re-evaluated every loop pass via the persistent TSEL
                //     select; on the exit pass test_result reflects a genuinely tested F11,
                //     so the count MUST be suppressed (commit b2761c7 - "WRONG VALUE OF
                //     P-REG" if reintroduced).
                //   * CS 012465 (UWRTS word-store loop): the F=0 it would gate on was
                //     *freshly SCOND-armed* one microword earlier at CS 012464
                //     ("A-B-1  COND,F=0") which carried NO CONDENABL and NO LOOP - a bare
                //     arm. That F=0 has NOT been through the condition TEST pipeline yet, so
                //     it must NOT suppress this count. Suppressing it (the pre-fix bug) left
                //     the loop counter at 15 instead of advancing 15->14, so the top ASCII
                //     word saved to RF-0 slot 14 at CS 012351 ("% OBS! LAST LC (B)") was
                //     recovered as slot 15 (the trailing word) at CS 012434
                //     ("A,0 B,LC ... IDBS,REG XRF") and stored by WRTOP at CS 012453 -
                //     destroying the top two characters of a multiword ASCII destination.
                //
                // No STATIC microword bit tells 001070 and 012465 apart. The distinction is
                // the ND-120 ALU->condition PIPELINE TIMING, derived from the hardware:
                //   - CGA_MIC_CONDREG.v (SCAN_FF_EN CSBIT4..CSBIT7): the TSEL/FS/LCC
                //     condition-select register is (re)loaded from the microword ONLY on the
                //     MCLK edge where CSSCOND(=SCOND)=1; otherwise it holds the last-armed
                //     select.
                //   - CGA_MIC.v GATES_5 (line 414) / GATES_6 (423) / GATES_8 (442): the
                //     selected condition s_cond_n only reaches the TRUE/FALSE sequencing
                //     decision (s_etrue/s_efalse -> SC5/SC6 via GATES_23/24) when CSECOND or
                //     CSLOOP is asserted; on a bare SCOND word (CSECOND=0, CSLOOP=0)
                //     s_etrue = 1 unconditionally - the just-armed condition produces NO
                //     tested result that cycle.
                // Hence: a condition that entered COND from a bare SCOND-arm (SCOND set, but
                // ECOND and LOOP both clear) in the previous microinstruction is treated as
                // "not yet tested" and does not suppress the count. COND_FromBareArm carries
                // that one-bit pipeline state (set at the end of this method for the NEXT
                // instruction to read).
                // ---- end historical note ----
                hw.mic.IncrementLoopCounter();

                //test_result = hw.cpu_regs.COND; // Load previous test result
                //test_result = ExecuteTestCondition(hw.cpu_regs.COND_TestObjects);
            }

            // Save test result for next uc instruction to test on
            //hw.cpu_regs.COND = test_result;

            #endregion Execute Test


            // Bits TS3-6 (bits 28-31 control the AddressSelect and StackControl if the condition result is true;
            // They will only be in control as long as LOOP (bit 22) and ECOND (bit 23) are true (Note: ND-110 TRUE, bit=0)
            // Ronny.. wtf does that mean.. 


            //if (test_result && LOOP && ECOND)

            if ((microCode.ECOND == false) && (microCode.LOOP == false))
            {
                //Utils.Log($"ConditionControl: Use microcode TRUE");
                // We dont care about the test result, use TRUE from microcode
                hw.mic.StackControl = microCode.TRUE_StackControl;
                hw.mic.AddressSelect = microCode.TRUE_AddressSelect;

            }
            else
                if (microCode.LOOP)
                {
                    //Utils.Log($"CC=LOOP[{test_result}]");
                    if (test_result)
                    {
                        //Utils.Log($"TRUE {hw.cpu_regs.COND_TestObjects}");
                        hw.mic.AddressSelect = microCode.TRUE_AddressSelect;
                        hw.mic.StackControl = microCode.TRUE_StackControl;
                    }
                    else
                    {
                        //Utils.Log("FALSE, RETURN and HOLD");
                        //If false, return 'RETURN' & 'HOLD' <= This is according to ucode token specs(in ND110 microcode manual and HW schematics)

                        hw.mic.AddressSelect = NextAddressSelect.Return;
                        hw.mic.StackControl = StackControl.Hold;
                    }
                }
                else // ECOND ==TRUE
                {
                    //Utils.Log($"CC=ECOND[{test_result}]");
                    if (test_result)
                    {
                        //Utils.Log($"Use microcode TRUE");
                        hw.mic.StackControl = microCode.TRUE_StackControl;
                        hw.mic.AddressSelect = microCode.TRUE_AddressSelect;
                    }
                    else
                    {
                        //Utils.Log($"Use SET condition TST={hw.cpu_regs.COND_TestObjects}");
                        hw.mic.StackControl = hw.cpu_regs.COND_StackControl;
                        hw.mic.AddressSelect = hw.cpu_regs.COND_AddressSelect;
                    }
                }

            SetConditionCodeAndTestCurrent();

            // Save current condition test to previous;
            hw.cpu_regs.COND = hw.cpu_regs.CurrentCond;

            // Pipeline the "was this a BARE SCOND-arm?" bit alongside COND: SCOND set while
            // ECOND and LOOP are both clear means the just-armed condition produced no tested
            // result this cycle (CGA_MIC.v GATES_5/6/8 - s_cond_n only reaches the sequencing
            // decision when CSECOND or CSLOOP is asserted; CGA_MIC_CONDREG.v SCAN_FF_EN shows
            // the TSEL/FS/LCC select is reloaded only on an SCOND edge and otherwise holds).
            // NOTE (2026-07-27): this bit no longer gates the loop counter - LCOUNT now counts
            // unconditionally (see the FPP BIAS derivation in Execute_4_ConditionControl).
            // It is kept because it records a real one-cycle pipeline state of the condition
            // logic that any future condition-timing work will need.
            hw.cpu_regs.COND_FromBareArm = microCode.SCOND && !microCode.ECOND && !microCode.LOOP;

            // Latch new MAP address into MIC ?
            if (hw.mic.AddressSelect == NextAddressSelect.Jump)
            {
                ushort newAddress = microCode.jmpAddress;

                //  If VECTOR = true, the vector address is the microinstruction branch address (Bits 0—11 plus Bit 20).
                //  If false, the lower 4 bits of the vector address are determined by MISO (bit 42) to be either IR(O-3) or the A—operand.
                if (microCode.Vectored)
                {
                    // use pre-calculated vector
                    newAddress = microCode.jmpAddress;
                }

                else
                {
                    // Now calculate the new JMP address
                    switch (microCode.MIS & 0x01)
                    {
                        case 0:
                            newAddress = (ushort)((microCode.jmpAddress & 0x1FF0) | (hw.cpu_regs.IR & 0x0F)); // low 4 bits from IR
                            break;
                        case 1:
                            newAddress = (ushort)((microCode.jmpAddress & 0x1FF0) | (hw.alu.A_Operand & 0x0F)); // low 4 bits from RA (RA is bit 12-15 => Register)
                            break;
                    }
                }
                hw.mic.LatchJumpAddress(newAddress);

            }


            hw.mic.ExecuteMAPUpdate();

        }

        void SetConditionCodeAndTestCurrent()
        {
            if (microCode.SCOND)
            {
                // Set a condition to be tested later.
                // 4—bit code indicating the test condition and a 4—bit code indicating the sequence instructions in case of a false test, are output to the Hold register.

                //Utils.Log("SCOND");

                // Bits 0-3
                // This selects the false sequence to be latched by SCOND (bit 24). It specifies the sequencer instruction (jump, return, next, repeat)
                hw.cpu_regs.COND_StackControl = microCode.SCOND_False_StackControl;
                hw.cpu_regs.COND_AddressSelect = microCode.SCOND_False_AddressSelect;

                // Bits 4-7 (=> SCOND_test)
                // This selects the _test object_ to be latched by SCOND (bit 24). If a test object changes state, one microinstruction will be executed
                // before the change is detected. This is caused by the pipelining of ALU, and sequencing calculations.             
                hw.cpu_regs.COND_TestObjects = microCode.SCOND_test;

                // Unsure if this is to be run here, or when Clock goes HIGH
                //hw.cpu_regs.COND = ExecuteTestCondition(hw.cpu_regs.COND_TestObjects);
            }


            // Pipelining the test condition - step 2
            // Bit 23 (ECOND) enables testing (against the previously set test condition) of the result of the previous microinstruction.
            //if (microCode.ECOND)
            {

                hw.cpu_regs.CurrentCond = ExecuteTestCondition(hw.cpu_regs.COND_TestObjects);

                //hw.mic.ClockLZ0();
                //TestObjects testObject = hw.cpu_regs.COND_TestObjects;
                //hw.cpu_regs.COND = ExecuteTestCondition(testObject);
            }
        }


        private bool ExecuteTestCondition(TestObjects testObject)
        {
            bool test_result = false;

            switch (testObject)
            {
                // COND FOR TESTING IS THE 'DOUBLE-ZERO-DETECT' FLIP-FLOP (floating point)
                case TestObjects.DZD:
                    // ND-100 microcode doc:
                    // (DZD moved to test code value=0 in ND-110)
                    // the double zero detect flip-flop is used to set TG correctly in FDV instructions.

                    break;

                // CONDITION FOR TESTING IS 'LOOP-COUNTER' CONTENT =0
                case TestObjects.LCZ:
                    //Utils.Log($"LCZ[Flag:{hw.mic.LCZ0}, Counter={hw.mic.loopCounter}");
                    test_result = hw.mic.LCZ0;
                    break;

                // COND FOR TEST IS 'IRQ' . CHECKS LEV 10-15 IF 'COMM, CLIRO'
                case TestObjects.IRQ:

                    // ND-100 microcode doc:
                    // used to test the interrupt request flip-flop, to see if interrupts are pending.
                    // If the test is preceded by the CLIRQ command, only interrupts from AM 2914 are tested and not panel interrupts such as STOP, RTC, etc.

                    test_result = hw.pic.ReadInterruptRequestPin == false; // active low

                    Utils.Log($"Test[{testObject}= {test_result}");
                    break;

                // COND FOR TESTING IS 'RESTRICTED-MODE'. TRUE IF RING 0-1"
                case TestObjects.RESTR:
                    // Return true if RING = 0 or 1
                    // RING is stored at PGS[PIL] bit 0-1, or cpu_regs.PGS

                    // ND-100 microcode doc:
                    // used to test if the current program is running in restricted mode.
                    // Instructions that are privileged should not execute when this condition is true.
                    // Privileged WCS instructions must test this condition, other privileged instructions are mapped different whether RESTR is on or off.

                    // If Memory Management enabled,check ring level
                    if (hw.alu.STS_PONI)
                    {
                        // PGS register is in RF3 via PIL
                        ushort current_pgs = hw.RF3.Read((ushort)hw.alu.PIL, (ushort)0);

                        byte ring = (byte)(current_pgs & 0x03);
                        test_result = (ring == 0) || (ring == 1);
                    }
                    break;

                // CONDITION FOR TESTING IS LAST MEMORY REQUEST WAS FETCH
                case TestObjects.FETCH:  // In the ND-120 schematics this signal is "CFETCH from CGA_DCD"

                    // ND-100 microcode doc:
                    // indicates whether the last memory access was a fetch of new instruction.
                    // Used to generate correct P in case of page fault.

                    test_result = false;

                    // Checked CPU schematics on DELILHA CGA_DCD sheet 5 of 10 (page 69 in PDF) 
                    // COMM.MIS
                    // 22.2 (MAP)
                    // 23 CJMP
                    // 24 CNEXT
                    // 25 CNEXT
                    // 26 JMP
                    // 27 JMP, SPARE, CONTINUE
                    if (
                        ((microCode.command == Commands.Command22) && (microCode.MIS == 2)) ||
                        (microCode.command == Commands.CJMP) ||
                        (microCode.command == Commands.CNEXT_24) ||
                        (microCode.command == Commands.CNEXT_25) ||
                        (microCode.command == Commands.JMP_26) ||
                        (microCode.command == Commands.JMP_27)
                        ) test_result = true;

                    Utils.Log($"!!!Test[{testObject}= {test_result}");
                    break;

                // CONDITION FOR TESTING IS THE 'ONE-OUT-DETECT' FLIP-FLOP
                case TestObjects.OOD:
                    // TODO:Floating point 

                    // ND-100 microcode doc:
                    // the one out detect flip-flop is used to set TG correctly in FAD, FSB and FMU instructions.

                    test_result = false;
                    Utils.Log($"!!!Test[{testObject}= {test_result}");
                    break;

                // Spare
                case TestObjects.Spare:
                    break;

                // COND FOR TEST IS OUTCOME OF LATEST TEST (DELAYS TESTS)
                case TestObjects.COND:
                    // Latch the true/false result of the LAST microinstruction. // TODO: Research & Implement
                    test_result = hw.cpu_regs.COND;
                    break;

                // Reserved
                case TestObjects.Reserved1:
                    test_result = false;
                    break;
                // Reserved
                case TestObjects.Reserved2:
                    test_result = false;
                    break;

                // CONDITION FOR TESTING IS 'OVERFLOW' FROM ALU
                case TestObjects.OVF:
                    test_result = hw.alu.DYNAMIC_OVERFLOW_OUT;
                    break;

                // CONDITION FOR TESTING IS 'CARRY' FROM ALU
                case TestObjects.CRY:
                    test_result = hw.alu.CARRY_OUT;
                    break;

                // CONDITION FOR TESTING IS BIT 11 FROM ALU
                case TestObjects.F11:
                    test_result = hw.alu.F11;
                    break;
                // CONDITION FOR TESTING IS BIT 15 FROM ALU
                case TestObjects.F15:
                    test_result = hw.alu.F15;
                    break;

                // CONDITION FOR TESTING IS 'ALL ZEROS' FROM ALU
                case TestObjects.ZF:
                    test_result = hw.alu.ZF;
                    break;

                // CONDITION FOR TESTING IS 'STOP'
                case TestObjects.STP:
                    test_result = hw.cpu_regs.STOP;
                    break;
                default:
                    break;
            }

            return test_result;
        }

        private void Execute_3_Commands()
        {
            #region Command Decode

            hw.mac.FETCH = false; // Default NOT fetch. Will be set in the commands that are FETCH

            // Helper variables for command decode


            // Precalc "default" memory displacement.
            sbyte mriDisplacement = (sbyte)(hw.cpu_regs.Operand & 0xFF); //TODO: Remove this and update all code using this
            bool b_adressing = (hw.cpu_regs.Operand & 1 << 8) != 0; // IF ",B" is set in the opcode we will use B regiser instead of P for address calculation
            sbyte displacement;


            if (microCode.command != Commands.NONE)
                Utils.Log($"Command:{microCode.command} (MIS:{microCode.MIS} A:{hw.alu.A_Operand})");
            // This generates control signals from the COMM and MIS fields of the microinstruction word.
            // These define the CPU operation that is to be performed          


            switch (microCode.command)
            {
                case Commands.NONE:
                    break;
                case Commands.LDPIL: // Load PIL from IDB
                    // VALIDATED against ND-120 Verilog CGA_ALU_STS.v: STS flip-flops 15..8
                    // each have TE = LDPILN and D = FIDBO (the IDB), so a real LDPIL loads
                    // the ENTIRE STS high byte (IONI/PONI/SEXI/N100/PIL) from the IDB.
                    // Preserving the global flags across a level switch is the MICROCODE's
                    // job, not LDPIL's - see LVSWP at CS 001120-001123 in
                    // ND-110-RASK.LISTING.TXT: reads live STS, ANDs with 170000 (keeps bits
                    // 15-12), ORs in the new level bits, then LDPIL.
                    Logger.Log($"LDPIL CSAR={Utils.Int2Oct(hw.mic.map)} IDB={Utils.Int2Oct(hw.idb.ReadWord())} STS(before)={Utils.Int2Oct(hw.alu.STS)}", Logger.LogLevel.Debug);
                    hw.alu.LoadSTS_Hi(hw.idb.ReadHI());
                    break;
                case Commands.LDGPR:  // Load GPR from IDB
                    hw.alu.GPR = hw.idb.ReadWord();
                    break;
                case Commands.EWRF: // Enable Write to Register File
                    switch (microCode.RASEL_RegisterFile)
                    {
                        case 0:
                            hw.RF.Write(hw.alu.A_Operand, hw.alu.B_Operand, hw.idb.ReadWord());
                            break;
                        case 1:
                            hw.RF1.Write(hw.alu.A_Operand, hw.alu.B_Operand, hw.idb.ReadWord());
                            break;
                        case 2:
                            hw.RF2.Write(hw.alu.A_Operand, hw.alu.B_Operand, hw.idb.ReadWord());
                            break;
                        case 3:
                            hw.RF3.Write(hw.alu.A_Operand, hw.alu.B_Operand, hw.idb.ReadWord());
                            break;

                    }
                    break;
                case Commands.CLIRQ:
                    //  Doc says: "Force interrupt request to the cleared condition.This is used by the interrupt handler."                                        
                    //  Token says: "PREVENT JUMP TO INTR VECT. REMOVE 'PANEL'-EFF ON IRQ-TEST"
                    // DELILAH Scematics: "TEMPORARY INTERRUPT INHIBIT"
                    // ND-100 microcode doc:
                    // * Inhibits the ability of interrupts to modify the microinstruction sequence at the time of a mapped jump.
                    // * Used to avoid interrupts when the MAP bit is used at other times than FETCH. CLIRQ will also block PANEL interrupts from being included in the IRQ condition.

                    //hw.pic.ClearPendingIRQ(); // clears the PENDING IRQ flip-flop ONLY                    


                    ushort tmpbits = (ushort)hw.idb.ReadWord();

                    Utils.Log($"COMM,CLIRQ: IDB=0x{tmpbits:X4}, F=0x{hw.alu.F:X4}"); // IDB={hw.idb.ReadWord():X8}");
                    ClearIntrqnFlipFlop(); // clears the INTRQN flip-flop ONLY

                    //hw.pic.ClearInterruptDetectbits((ushort)hw.alu.F); //hw.idb.ReadWord());

                    // TODO: Validate what's correct!
                    break;

                case Commands.RSDA:
                    if (cpuType == CPUType.ND110)
                    {
                        hw.uart.ResetDataAvailable();
                    }
                    else
                    {
                        if (microCode.IDBsource != IDBSource.UART)
                            hw.uart2661.Write(microCode.MIS, hw.idb.ReadLO());
                    }
                    break;
                case Commands.TBSTR:
                    switch (microCode.MIS)
                    {

                        case 0:
                            // 06,0 = TBSTR
                            if (this.cpuType == CPUType.ND110)
                            {
                                Logger.Log($"TBSTR CSAR={Utils.Int2Oct(hw.mic.map)} data={Utils.Int2Oct(hw.idb.ReadLO())} P={Utils.Int2Oct(hw.alu.ReadRegisterE(RegisterEnum.P))} PIL={(hw.alu.STS >> 8) & 0x0F}", Logger.LogLevel.Debug);
                                hw.uart.TransmitDataStrobe(hw.idb.ReadLO());
                            }
                            // 06,0 = in ND-120, spare
                            break;

                        case 1:
                            // 06,1 = spare
                            break;

                        case 2:
                            // 06,2 = LDPANC
                            // load lower byte of IDB into the panel processor FIFO.
                            hw.panel.LoadFifo(hw.idb.ReadLO());
                            Logger.Log($"LDPANC: PIL = {hw.alu.PIL}, STS= {hw.alu.STS:X8}", Logger.LogLevel.Opcodes);
                            break;
                        case 3:
                            // 06,3 = LDPCR
                            // 
                            hw.cpu_regs.PCR = hw.idb.ReadWord();
                            Logger.Log($"LDPCR: PIL= {hw.alu.PIL}, IDB={hw.cpu_regs.PCR:X8}", Logger.LogLevel.Opcodes);

                            hw.RF3.Write(hw.alu.PIL, 0, hw.cpu_regs.PCR);


                            break;
                    }


                    break;
                case Commands.SIOC:

                    ushort oldIOControl = hw.cpu_regs.IOControl; // keep a copy of the old settings
                    hw.cpu_regs.IOControl = hw.idb.ReadWord();
                    ushort changedBits = (ushort)(hw.cpu_regs.IOControl ^ oldIOControl);


                    // Bits:
                    // 7 => RESET (real time clock)
                    // 6 => ~SCONS => ~CONSOLE
                    // 5 => Turn on RED led (LED2) on LOW
                    // 4 => Turn on GREEN led (LED3) on LOW
                    // 3 => IOC3 (When HIGH together with IOC0 => BINT~13 goes low)
                    // 2 => IOC2 (when HIGH, ENABLE signal ~TBMT to go to BINT~10) // Enable output interrupt on level 10, from terminal #1 - UART transmit buffer empty
                    // 1 => IOC1 (When HIGH AND ~SCONS, bit 6 is HIGH), ENABLE signal ~DA to go to  // Enable output interrupt on level 12, from terminal #1 - UART data available
                    // 0 => IOC0 (When HIGH together with IOC3 => BINT~13 goes low) // Enable clock interrupt on level 13


                    if ((changedBits & 1 << 7) != 0)
                    {
                        if ((hw.cpu_regs.IOControl & 1 << 7) != 0)
                        {
                            Utils.LogAlways($"Reset real time clock", true); // Clear interrupt flag ?
                            hw.panel.ClearRTC();
                        }
                    }


                    if (cpuType == CPUType.ND110)
                    {
                        hw.uart.CONSOLE_n = (hw.cpu_regs.IOControl & 1 << 6) != 0;

                        // Configure UART word length and parity from IOControl bits 11-14.
                        // These bits are set by microcode TRM3 handler (IOX 303 write) via WSIOC/COMM,SIOC.
                        // TRM3 inverts the input, so the encoding is inverted relative to IOX 303 spec:
                        //   bit 12: 0=7-bit, 1=8-bit word length
                        //   bit 14: 0=parity enabled, 1=parity disabled
                        hw.uart.ConfigureFromSIOC(hw.cpu_regs.IOControl);
                    }
                    else
                    {
                        // Same polarity as the ND-110 board above: CONSOLE_n mirrors IOC
                        // bit 6 directly - TRUE (IOR bit 11 = 1) when the console is in
                        // NORMAL mode, FALSE when assigned to OPCOM. This was inverted
                        // ("== 0") until 2026-07-13, which made the DELILAH TRM2 console
                        // status handler (IOX 302, CS 000531-000536: DA test then
                        // "CONSOLE_n" test - "%CC SAME AS RASK FROM HERE, EXCEPT DA
                        // INVERTED") conclude the console was at OPCOM while the program
                        // owned it, so the DA bit was never delivered to the macro status
                        // word and INSTRUCTION-B's prompt ignored all typed input.
                        hw.uart2661.CONSOLE_n = (hw.cpu_regs.IOControl & 1 << 6) != 0;
                    }

                    if ((changedBits & 1 << 6) != 0)
                    {
                        if ((hw.cpu_regs.IOControl & 1 << 6) == 0)
                        {
                            Utils.LogAlways($"Set terminal #1 in OPCOM (Console) as opposed to Normal", true);
                        }
                        else
                        {
                            Utils.LogAlways($"Set terminal #1 to Normal", true);
                        }
                    }
                    // The panel's MS20 service needs the console assignment to decide
                    // whether to activate MOPC while the CPU is running (see
                    // Panel.SetConsoleAssignedToOpcom / TriggerRTC).
                    hw.panel.SetConsoleAssignedToOpcom((hw.cpu_regs.IOControl & 1 << 6) == 0);

                    if ((changedBits & 1 << 5) != 0)
                    {
                        // CPU BOARD RED LED
                        if ((hw.cpu_regs.IOControl & 1 << 5) == 0)
                        {
                            Utils.LogAlways($"Enable Master Clear, red LED ON", true);
                        }
                        else
                        {
                            Utils.LogAlways($"MACL: red LED OFF", true);
                        }
                    }

                    if ((changedBits & 1 << 4) != 0)
                    {
                        // CPU BOARD GREEN LED

                        if ((hw.cpu_regs.IOControl & 1 << 4) == 0)
                        {
                            Utils.LogAlways($"Initialization completed, green LED ON", true);
                        }
                        else
                        {
                            Utils.LogAlways($"Initialization started, green LED OFF", true);
                        }
                    }

                    if ((changedBits & 1 << 3) != 0)
                    {
                        //if ((hw.cpu_regs.IOControl & 1 << 3) != 0)
                        //    Utils.LogAlways($"Clock interrupt generated from RTC trap handler", true);
                    }

                    if ((changedBits & 1 << 2) != 0)
                    {
                        bool int10enabled = (hw.cpu_regs.IOControl & 1 << 2) != 0;
                        hw.SetUartInt10Enabled(int10enabled);

                        if (int10enabled)
                        {
                            Utils.LogAlways($" Enable output interrupt on level 10, from terminal #1 - UART transmit buffer empty", true);
                        }
                    }

                    if ((changedBits & 1 << 1) != 0)
                    {
                        bool int12enabled = (hw.cpu_regs.IOControl & 1 << 1) != 0;
                        hw.SetUartInt12Enabled(int12enabled);

                        if (int12enabled)
                            Utils.LogAlways($"Enable output interrupt on level 12, from terminal #1 - UART data available", true);
                    }

                    if ((changedBits & 1 << 0) != 0)
                    {
                        if ((hw.cpu_regs.IOControl & 1 << 0) != 0)
                        {
                            //Utils.LogAlways($" Enable clock interrupt on level 13", true);
                            hw.EnableRTCInterrupt();
                        }
                        else
                        {
                            hw.DisableRTCInterrupt();
                        }
                    }


                    // NOTE: When CLEAR is held, both red and green LEDs are illuminated. When CLEAR is released, red LED only stays illuminated while
                    // control store is loaded from EPRDM, hardware is initialised, and self—test runs.
                    // After successful completion, the red LED is extinguished and the green LED is illuminated.

                    // ND100: The latest IDB value outputted by a SIOC command will always be saved in a word named STATUS in the register file.

                    break;

                case Commands.SLOW:
                    // TODO: Command a "slow" microcycle
                    // In ND-120, a ”slow” microcycle is a minimum (no wait states) of 204 8ns. In ND—llO, it is 256ns

                    break;
                case Commands.EPIC:
                    // Enable priority interrupt controller. The A—operand then specifies a command to the PIC. 
                    if (microCode.IDBsource != IDBSource.none) // Implementation detail in this emulator: Later, if source = none, the Epic command has already been executed. A little workaround for getting details correct into the bus/alu
                    {
                        hw.pic.ExecuteInstruction(hw);
                    }
                    break;
                case Commands.SMPID:
                    // Set bits in the micro—PID (Priority Interrupt Detect) register in the PIC. (In the ND-100 the chip's 24C + 21C, 74S240 on page 5A05 - is inverting and tri-stating the interrupt signals)
                    // Bits can only be set to 1 by this command. (The PIC command CLRMPID resets bits to 0.) // ND.CLRMPID = Am2914.CLRIN (Clear all interrupts)                    

                    hw.pic.SetInterruptDetectbits(hw.idb.ReadWord());
                    break;
                case Commands.START:
                    hw.cpu_regs.STOP = false;
                    break;
                case Commands.SSTOP:
                    hw.cpu_regs.STOP = true;
                    break;
                case Commands.CLRTC:  // Clear real—time interrupt. Every 20ms the real time clock will generate a panel interrupt, thereby entering the clock routine. This routine sends out the CLRTC command.                    
                    hw.panel.ClearRTC();
                    break;
                case Commands.CLFF: // Clear all flip—flops having special functions regarding the floating point rounding indicator(T6).
                                    // The DZD (double zero detect used in FDV) and the OOD (one out detect used in FAD, FSB and FMU) flip-flops are cleared by CLFF.

                    Utils.Log($"@@ CLFF");
                    // TODO: Implement
                    break;
                case Commands.LDLC:
                    //  Load the loop counter with the 6 lower bits of the IDB. Bit 6 is the sign bit. 
                    //  The modified loop counter will not be available as an A—operand until the next microinstruction has been completed.

                    int tmplc = (ushort)(hw.idb.ReadLO() & 0b111111);
                    if ((tmplc & (1 << 5)) != 0)
                    {
                        // Calculate 2's complement - https://en.wikipedia.org/wiki/Two%27s_complement                        
                        tmplc--;        // remove one
                        tmplc = ~tmplc; // inverse the bits						
                        tmplc &= 0x1F;  // keep only 5 bit.		
                        tmplc *= -1;    //make it negative.		
                    }
                    hw.mic.LatchLC(tmplc);
                    Utils.Log($"LC={tmplc}");

                    break;
                case Commands.Command20:
                    switch (microCode.MIS)
                    {
                        case 0:
                            Utils.Log($"LDSEG");
                            // Load segment register. This selects a physical 64K memory address area (address bits 16—23).
                            // The segment register is located in the MAC. (Note: In this emulator the segment register is stored in "Memory" class)
                            memory.LoadSegmentRegister(hw.idb.ReadLO()); // Uses only bits 0-7
                            break;

                        case 1:
                            Utils.Log($"spare");
                            break;

                        case 2:
                            Utils.Log($"spare");
                            break;

                        case 3:
                            Utils.Log($"@@LDIRV"); // Load Instruction register (IR) in Mic (Or-logistics)
                            hw.cpu_regs.IR = hw.idb.ReadWord();
                            break;
                    }
                    break;
                case Commands.Command21:
                    switch (microCode.MIS)
                    {
                        case 0:
                            Utils.Log($"@@WCHIM"); // Write cache inhibit map (bit 15 of IDB, see TRR CILP macroinstruction)
                            break;

                        case 1:
                            Utils.Log($"SSEMA"); // Set semaphore flag (prepare semaphore request).
                            break;

                        case 2:
                            Utils.Log($"@@CCLR"); // Cache clear.
                            //hw.cpu_regs.CSR |= 1 << 3;
                            break;

                        case 3:
                            Utils.Log($"LDEXM"); // Load examine mode in MAC function.
                            hw.mac.SetEXM(hw.idb.ReadWord());
                            break;
                    }
                    break;
                case Commands.Command22:
                    switch (microCode.MIS)
                    {
                        case 0:
                            Utils.Log($"IREAD,PT"); // Indirect Read as PT-MODE
                            memory.IndirectReadType = IndirectRead.PT;

                            //hw.cpu_regs.DBR = memory.ReadIndirect(hw.idb.ReadWord() + mriDisplacement); // Memory access stores result in DBR
                            break;

                        case 1:
                            Utils.Log($"IREAD,APT"); // Indirect read as APT-MODE
                            memory.IndirectReadType = IndirectRead.APT;
                            //hw.cpu_regs.DBR = memory.ReadIndirect(hw.idb.ReadWord()); // Memory access stores result in DBR
                            break;

                        case 2: // MAP


                            // Address control-store mapped as when FETCH                                                           
                            // Used in Execute Register instruction (IDB contains instruction).                                
                            //TODO: This MUST be validated

                            Utils.Log($"MAP");



                            ushort exopcode = (ushort)hw.idb.ReadWord();

                            // Fetch(exopcode)
                            #region Initialize CPU register via MacroOperand (COPY FROM FETC()
                            hw.cpu_regs.Operand = exopcode;


                            /* When a new macroinstruction is executed, some CPU registers have a defined value */
                            hw.cpu_regs.IR = exopcode & 0x7ff; // Bit 0-10 of the instruction will be loaded into the instruction register
                            hw.alu.GPR = exopcode; // GPR is loaded with the machine instruction every time we decode a new Macro Instruction

                            // False sequence is JMP and POP.
                            hw.cpu_regs.COND_AddressSelect = NextAddressSelect.Jump;
                            hw.cpu_regs.COND_StackControl = StackControl.Pop;


                            #endregion Initialize CPU register via MacroOperand


                            // Load uc-address for the instruction
                            microCode.jmpAddress = hw.mic.CalculateMacEntryPoint(exopcode);


                            // Return calculated address for next ucode ??
                            //return mic.map;
                            break;

                        case 3: // CNEXT,NWP
                            // EXECUTE NEXT INSTRUCTION IF P NOT CHANGED IN LAST CYCLE (Text from from token description)                            
                            // Translated "DONT execute next instruction if P is changed in the last cycle"

                            // Conditional "next", if P is not changed in the last cycle
                            Utils.Log($"CNEXT,NWP");

                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.idb.ReadWord());
                            //TODO: Unclear what "NEXT INSTRUCTION" mean. Maybe its "continue as normal", and if P is changed, RETURN ??? I have no fucking clue

                            if (hw.cpu_regs.P_was_updated_last_cycle)
                                MacroJump(hw.alu.ReadRegisterE(RegisterEnum.P));

                            break;
                    }
                    break;
                case Commands.CJMP:
                    switch (microCode.MIS)
                    {
                        case 0: // MACRO JUMP IF F15=1 (NEGATIVE)
                            Utils.Log($"CJMP,F15");
                            if (hw.alu.F15)
                            {
                                MacroJump(hw.idb.ReadWord());
                            }

                            break;

                        case 1: // MACRO JUMP IF F15=0 (POSITIVE)
                            Utils.Log($"CJMP,NF15");
                            if (!hw.alu.F15)
                            {
                                MacroJump(hw.idb.ReadWord());
                            }

                            break;

                        case 2: // MACRO JUMP IF F=0 (ZERO)
                            Utils.Log($"CJMP,F=0");
                            if (hw.alu.ZF)
                            {
                                MacroJump(hw.idb.ReadWord());
                            }
                            break;

                        case 3: // MACRO JUMP IF F><0 (NOT ZERO)
                            Utils.Log($"CJMP,NF=0");
                            if (!hw.alu.ZF)
                            {
                                MacroJump(hw.idb.ReadWord());
                            }
                            break;
                    }
                    break;

                /*

                  NOTES ABOUT THE CNEXT COMMANDS

                  AFTER TESTING "A LOT" AND COMPARING THE RESULT TO THE MICROCODE, IT SEEMS THE DESCRIPTION FOR THE CNEXT, COMMANDS ARE INVERTED
                  UNIT TESTS SHOWED THAT ALL HAD AN INVERTED RESULT.

                  AFTER MODIFYING THE CNEXT,xxx COMMANDS AND COMPARING THE RESULT TO EXPECTED RESULTS IT WAS CLEAR THAT *SOMETHING" HAD GONE WRONG IN THE DOC OR THE PHYSICAL 110-CPU, WHICH WAS FIXED IN MICROCOE

                  RONNY 06.04.2023 (17 hours I never will get back!)

                 */
                case Commands.CNEXT_24:
                    switch (microCode.MIS)
                    {
                        case 0: // MACRO NEXT IF SGR=1 (GREATER OR EQUAL)
                            Utils.Log($"CNEXT,SGR");
                            if (hw.alu.SGR)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();
                            break;

                        case 1: // MACRO NEXT IF SGR=0 (LESS THAN)
                            Utils.Log($"CNEXT,NSGR");
                            if (!hw.alu.SGR) // TODO: Validate SGR calculation
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();

                            break;

                        case 2: // MACRO NEXT IF CRY=1 (MAGNITUDE)
                            Utils.Log($"CNEXT,CRY");
                            if (hw.alu.CARRY_OUT)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();

                            break;

                        case 3: // MACRO NEXT IF CRY=0
                            Utils.Log($"CNEXT,NCRY");
                            if (!hw.alu.CARRY_OUT)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();


                            break;
                    }
                    break;
                case Commands.CNEXT_25:
                    switch (microCode.MIS)
                    {
                        case 0: // MACRO NEXT IF F15=1 (NEGATIVE)
                            Utils.Log($"CNEXT,F15");
                            if (hw.alu.F15)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();
                            break;

                        case 1: // MACRO NEXT IF F15=0 (POSITIVE)
                            Utils.Log($"CNEXT,NF15");
                            if (!hw.alu.F15)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();


                            break;

                        case 2: // MACRO NEXT IF F=0 (ZERO)
                            Utils.Log($"CNEXT,F=0");

                            if (hw.alu.ZF)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();


                            //if (microCode.jmpAddress != 0)
                            //{



                            //}
                            //else
                            //{
                            //    if ((hw.alu.ZF) || (hw.mic.AddressSelect == NextAddressSelect.Next))
                            //    {
                            //        MacroSkip();
                            //    }
                            //}
                            //// CNEXT,F=0 seems to be the last microkode before a fetch.

                            //hw.mac.FETCH_IF_JMP_0 = true; // Hackety'hack
                            break;

                        case 3: // MACRO NEXT IF F><0 (NOT ZERO)
                            Utils.Log($"CNEXT,NF=0");
                            if (!hw.alu.ZF) // || (!hw.alu.ZF) && hw.mic.AddressSelect == NextAddressSelect.Next)
                            {
                                hw.mac.FETCH = true;
                            }
                            else
                                MacroSkip();

                            break;
                    }
                    break;
                case Commands.JMP_26:
                    displacement = (sbyte)hw.idb.ReadWord();
                    switch (microCode.MIS)
                    {

                        case 0: // UNCONDITIONAL MACRO JUMP	(P-RELATIVE)
                                // JMP,* 
                            Utils.Log($"JMP,*");

                            // Adressingmode 0 - EA = (P) + displacement
                            hw.mac.mac_address = CalcEA(AdressingMode.RelativeAdressing, mriDisplacement);
                            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);

                            hw.mac.FETCH = true;
                            break;

                        case 1: // UNCONDITIONAL MACRO JUMP	(B-RELATIVE)
                                // JMP,B 
                            Utils.Log($"JMP,B");

                            // AddressingMode  1 => EA = (B) + displacement 
                            hw.mac.mac_address = CalcEA(AdressingMode.BrelativeAddressing, mriDisplacement);
                            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);

                            hw.mac.FETCH = true;
                            break;

                        case 2: // UNCONDITIONAL MACRO JUMP	(INDIRECT)
                                // JMP,I 
                            Utils.Log($"@@ JMP,I");



                            if (b_adressing)
                            {
                                Utils.Log($"JMP,I (,B)"); // Adressingmode 3 =>  EA = ((B) + disp)
                                hw.mac.mac_address = CalcEA(AdressingMode.IndirectBrelativeAddressing, mriDisplacement);
                            }

                            else
                            {
                                Utils.Log($"JMP,I"); // Adressingmode 2 =>  EA = ((P) + displacement)   
                                hw.mac.mac_address = CalcEA(AdressingMode.IndirectPrelativeAddressing, mriDisplacement);
                            }

                            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);
                            hw.mac.FETCH = true;

                            break;

                        case 3:  // UNCONDITIONAL MACRO JUMP (X-RELATIVE)
                                 // JMP,X 
                            Utils.Log($"JMP,X");

                            // AddressingMode  4 => EA = (X) + displacement 
                            hw.mac.mac_address = CalcEA(AdressingMode.XrelativeAddressing, mriDisplacement);

                            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);
                            hw.mac.FETCH = true;
                            break;
                    }
                    break;
                case Commands.JMP_27:
                    displacement = (sbyte)hw.idb.ReadWord();
                    switch (microCode.MIS)
                    {
                        case 0: // UNCONDITIONAL MACRO JUMP	(POST-INDEXED.	B-REL.)
                                // JMP,XB
                            Utils.Log($"JMP,XB");

                            hw.mac.mac_address = CalcEA(AdressingMode.BrelativeIndexedAddressing, mriDisplacement);

                            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);
                            hw.mac.FETCH = true;


                            break;

                        case 1: // UNCONDITIONAL MACRO JUMP	(POST-INDEXED. INDIR.)
                                // JMP,XI(,B)

                            Utils.Log($"JMP,XI(,B)"); // Jump, post—indexed, indirect.

                            if (b_adressing)
                            {
                                Utils.Log($"JMP,X I ,B"); // Adressingmode 3 =>  EA = ((B) + disp)
                                hw.mac.mac_address = CalcEA(AdressingMode.IndirectBrelativeIndexedAddressing, mriDisplacement);
                            }

                            else
                            {
                                Utils.Log($"JMP,X I"); // Adressingmode 2 =>  EA = ((P) + displacement)   
                                hw.mac.mac_address = CalcEA(AdressingMode.IndirectPrelativeIndexedAddressing, mriDisplacement);
                            }

                            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);
                            hw.mac.FETCH = true;

                            break;

                        case 2:
                            // spare                            
                            break;

                        case 3: // FETCH MACRO-INSTR AS POINTED BY P-REG
                                // (FETCH NEW INSTRUCTION RELATIVE TO P)                                                

                            Utils.Log($"CONTINUE");
                            hw.mac.FETCH = true;


                            // TODODODODODO
                            // TODO: Recalculate P = P +1 if running in macro code.
                            // TODO: Detect when to update P. Running from uc first time. NO UPDATE. Normal, yes!... maybe more rules                            
                            // If coming from microcode, like OPCOM or cpu boot code, adding +1 will be wrong



                            break;
                    }
                    break;
                case Commands.AREAD_30:


                    switch (microCode.MIS)
                    {
                        case 0: // READ P-RELATIVE
                            // COMM,AREAD,*  
                            Utils.Log($"AREAD,*"); // Adressingmode 0 - EA = (P) + displacement

                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.RelativeAdressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);


                            break;

                        case 1: // READ B-RELATIVE
                            // COMM,AREAD,B
                            Utils.Log($"AREAD,B"); // AddressingMode  1 => EA = (B) + displacement 

                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.BrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);


                            break;

                        case 2: // READ INDIRECT
                            // COMM,AREAD,I (,B)

                            if (b_adressing)
                            {
                                Utils.Log($"AREAD,I (,B)"); // Adressingmode 3 =>  EA = ((B) + disp)
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectBrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            }

                            else
                            {
                                Utils.Log($"AREAD,I"); // Adressingmode 2 =>  EA = ((P) + displacement)   
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectPrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            }


                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);

                            break;

                        case 3: // READ X-RELATIVE
                            // AREAD,X
                            Utils.Log($"AREAD,X"); // Adressingmode 4 => EA =  (X) + displacement    
                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.XrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);

                            break;
                    }
                    break;
                case Commands.AREAD_31:
                    switch (microCode.MIS)
                    {
                        case 0: // READ POST-INDEXED AFTER B-RELATIVE
                            // COMM,AREAD,XB  
                            Utils.Log($"AREAD,XB");

                            // Adressingmode 5 => EA =  (B) + (X) + displacement    // B indexed addressing
                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.BrelativeIndexedAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);

                            break;

                        case 1: // READ POST-INDEXED AFTER INDIRECT
                            // COMM,AREAD,XI (,B)

                            Utils.Log($"AREAD,XI(,B)");
                            // Adressingmode 7 => EA =  ((B)+ displacement) + (X)     // B indirect indexed addressing

                            if (b_adressing)
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectBrelativeIndexedAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            else
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectPrelativeIndexedAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT

                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);

                            break;

                        case 2:
                            // ND-120 READ2 INDIRECT ADDRESS (PREFIXED)
                            Utils.Log($"spare");
                            break;

                        case 3: // READ NEXT LOCATION (LCA + 1)
                            Utils.Log($"AREAD,NEXT");
                            // TODO: Validate 
                            hw.cpu_regs.lastEA = (ushort)(hw.cpu_regs.lastEA + 1);
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);

                            break;
                    }
                    break;
                case Commands.AWRITE_32:
                    switch (microCode.MIS)
                    {
                        case 0: // WRITE P-RELATIVE
                            Utils.Log($"AWRITE,*");  // Adressingmode 0 - EA = (P) + displacement

                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.RelativeAdressing, mriDisplacement); // also sets hw.cpu_regs.lastPT

                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 1: // WRITE B-RELATIVE
                            Utils.Log($"AWRITE,B");// AddressingMode  1 => EA = (B) + displacement 

                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.BrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT

                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);

                            break;

                        case 2: // WRITE INDIRECT
                            Utils.Log($"AWRITE,I (,B)");// Adressingmode 2 or 3 

                            if (b_adressing)
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectBrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            else
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectPrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT

                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 3: // WRITE X-RELATIVE
                            Utils.Log($"AWRITE,X");// Adressingmode 4 => EA =  (X) + displacement    
                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.XrelativeAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;
                    }
                    break;
                case Commands.AWRITE_33:
                    switch (microCode.MIS)
                    {
                        case 0: // WRITE POST-INDEXED AFTER B-RELATIVE
                            Utils.Log($"AWRITE,XB");
                            // Adressingmode 5 => EA =  (B) + (X) + displacement    // B indexed addressing
                            hw.cpu_regs.lastEA = CalcEA(AdressingMode.BrelativeIndexedAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 1: // WRITE POST-INDEXED AFTER INDIRECT
                            Utils.Log($"AWRITE,XI(,B)");
                            // Adressingmode 7 => EA =  ((B)+ displacement) + (X)     // B indirect indexed addressing
                            if (b_adressing)
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectBrelativeIndexedAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT
                            else
                                hw.cpu_regs.lastEA = CalcEA(AdressingMode.IndirectPrelativeIndexedAddressing, mriDisplacement); // also sets hw.cpu_regs.lastPT

                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 2: // WRITE IN LAST USED ADDRESS (USED BY "MIN") LCA + 0
                            Utils.Log($"AWRITE,HOLD"); // Write in last-used address (used by MIN)
                                                       // TODO: Validate

                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 3: // WRITE IN NEXT LOCATION (LCA + 1)
                            Utils.Log($"AWRITE,NEXT"); // Write in next-location
                            hw.cpu_regs.lastEA = (ushort)(hw.cpu_regs.lastEA + 1); // TODO: Validate
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);

                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;
                    }
                    break;
                case Commands.READ_34:
                    switch (microCode.MIS)
                    {
                        case 0: // READ PT-MODE AND USE ADDRESS FROM IDB
                            Utils.Log($"READ,PT {Utils.Int2Oct(start_IDB_value)}"); // Read, page-table relative, address from IDB                            
                            hw.cpu_regs.lastEA = start_IDB_value;
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, PageTable.StandardPageTable);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);
                            break;

                        case 1: // READ APT-MODE AND USE ADDRESS FROM IDB
                            Utils.Log($"READ,APT {Utils.Int2Oct(start_IDB_value)}"); // Read, alternative page-table relative, address from IDB
                            hw.cpu_regs.lastEA = start_IDB_value;
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, PageTable.AlternativePageTable);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);
                            break;

                        case 2:
                            Utils.Log($"READ,HOLD {Utils.Int2Oct(start_IDB_value)}");  // READ AS PREVIOUS AND USE ADDRESS FROM IDB 
                                                                                       // Read from last-used PT, address from IDB
                                                                                       // TODO: Validate 
                            hw.cpu_regs.lastEA = start_IDB_value;
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.READ, hw.cpu_regs.lastPT);
                            hw.cpu_regs.DBR = memory.ReadPhysicalMemory(hw.cpu_regs.lastPhysicalAddress);
                            break;

                        case 3: // COMM,EXRQ  -  EXAMINE PHYSICALLY , USE ADDRESS FROM IDB AND SEG 
                            // ND-100: EXRQ, examine memory request. The physical address must be on IDB. The segment number must already be loaded into the MMM segment register.
                            Utils.Log($"EXAMINE");
                            hw.cpu_regs.DBR = memory.ExamineRead(start_IDB_value);
                            break;
                    }

                    break;
                case Commands.WRITE_35:
                    switch (microCode.MIS)
                    {
                        case 0: // WRITE PT-MODE AND USE ADDRESS FROM IDB

                            Utils.Log($"WRITE,PT {Utils.Int2Oct(start_IDB_value)}"); // Write, page table-relative, address from IDB.
                            hw.cpu_regs.lastEA = start_IDB_value;
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, PageTable.StandardPageTable);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 1: // WRITE APT-MODE AND USE ADDRESS FROM IDB
                            Utils.Log($"WRITE,APT {Utils.Int2Oct(start_IDB_value)}"); // Write, ATP PT—reiative, addr from IDB.
                            hw.cpu_regs.lastEA = start_IDB_value;
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, PageTable.AlternativePageTable);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 2: // WRITE AS PREVIOUS AND USE ADDRESS FROM IDB 
                            Utils.Log($"WRITE,HOLD {Utils.Int2Oct(start_IDB_value)}"); // Write to Test—used PT, address from IDB
                            hw.cpu_regs.lastEA = start_IDB_value;
                            hw.cpu_regs.lastPhysicalAddress = memory.MapVirtualToPhysical(hw.cpu_regs.lastEA, AccessMode.WRITE, hw.cpu_regs.lastPT);
                            memory.WritePhysicalMemory(hw.cpu_regs.lastPhysicalAddress, hw.idb.idb_data);
                            break;

                        case 3: // DEPOSIT PHYSICALLY , USE ADDRESS FROM IDB AND SEG
                            Utils.Log($"DEPOSIT {Utils.Int2Oct(start_IDB_value)}");

                            memory.DepositWrite(start_IDB_value, hw.idb.idb_data);
                            break;
                    }
                    break;
                case Commands.Command_36:
                    switch (microCode.MIS)
                    {
                        case 0: // ADDRESS CONTROL-STORE FROM IDB
                            Utils.Log($"ADCS (LWCS) {Utils.Int2Oct(start_IDB_value)}"); // Load control store address from IDB.
                            hw.cpu_regs.ControlStoreAddress = hw.idb.ReadWord();
                            break;

                        case 1: // ACCESS CONTROL-STORE AS ADDRESSED BY ADCS
                            Utils.Log($"RWCS"); // Read/write control store. //TODO: what value when write, and when do you know its read or write ?                            

                            //TODO: This should probably be executed before ALU calculations ??
                            if (hw.alu.aluDestination == ALUDestination.ALUD_B)
                                hw.alu.WriteRegister(hw.alu.B_Operand, hw.cs.ReadCS(hw.cpu_regs.ControlStoreAddress));
                            break;

                        case 2:
                            if (cpuType == CPUType.ND110)
                            {
                                // In ND-110 reserved.
                                Utils.Log($"@reserved");
                            }
                            else
                            {
                                // LCS
                                Utils.Log($"MCL");  // In ND-120, load control store from PROM and reset devices (MASTER CLEAR). //TODO: 
                                hw.MasterClear();
                            }
                            break;

                        case 3:
                            if (cpuType == CPUType.ND110)
                            {
                                Utils.Log($"reserved");
                            }
                            else
                            {
                                Utils.Log($"XSLOW");  // In ND-120, XSLOW - Force current microcycle to the maximum length of time (435.2ns). This is used for very slow I/O devices e.g. the UART
                            }

                            break;
                    }
                    break;
                case Commands.Command_37:
                    switch (microCode.MIS)
                    {
                        case 0:
                            // spare
                            Utils.Log($"@spare");
                            break;

                        case 1:
                            // spare
                            Utils.Log($"@spare");
                            break;

                        case 2:
                            //IDENT
                            // ND-100: IDENT bus request.
                            // As with write requests, the write data must be enabled onto IDB in the next microinstruction,
                            // accompanied by a WAIT cycle.The following instruction must also be a WAIT cycle, with DBR as source to IDB.

                            // This handler is only reached via the microcode command COMM,IDENT,
                            // which real IDENT-instruction microcode only issues from IDX
                            // ("DEVICE IS NOT ON CPU-BOARD, SEND OUT GENERAL IDENT" -
                            // ND-110-RASK.LISTING.TXT CS 004414) - i.e. after the on-board fast
                            // paths (ID13/ID10, CS 004410/004404) already checked RTC/terminal
                            // for a genuine pending interrupt and found none. Level mapping
                            // below is read directly off those two R1 comparisons
                            // (CS 004370/004371); levels 11/12 are not reached via a direct R1
                            // compare in the traced microcode, so are left unmapped rather than
                            // guessed.
                            int level = -1;
                            switch (hw.cpu_regs.IR & 0x3F)
                            {
                                case 0x04: // R1==4 (octal) -> ID13
                                    level = 13; // RTC/clock
                                    break;
                                case 0x09: // R1==11 (octal) -> ID10
                                    level = 10; // Terminal (UART), TX-empty/output-ready
                                    break;
                            }

                            Logger.Log($"@@IDENT level {level}", Logger.LogLevel.Device);

                            // Safety net: if this is genuinely the on-board UART's TX-empty
                            // condition (level 10) and it's actually pending and enabled to
                            // interrupt (IOC bit 2 - see the Commands.SIOC handler's IOControl
                            // bit comments), raise INT10 for real rather than silently dropping
                            // it. Real ID10 microcode should already have caught this and
                            // returned via IDRET before ever reaching here, but our microcode
                            // engine reaching this fallback for a level-10 IDENT while the
                            // condition is genuinely true would mean it didn't - so honor it here
                            // instead of masking the bug.
                            bool onboardUartInt10Pending = level == 10
                                && cpuType == CPUType.ND110
                                && hw.uart.Int10Enabled
                                && hw.uart.TRANSMIT_BUFFER_EMPTY;

                            if (onboardUartInt10Pending)
                            {
                                hw.SetInternalInterrupt(InterruptPin.INT10);
                            }

                            // No external devices are implemented, so any other IDENT (no
                            // on-board device claimed it) completes as "no device responded" -
                            // real hardware does NOT raise a new error interrupt merely because
                            // nothing answered an IDENT; do not manufacture one here either.

                            break;

                        case 3:
                        // IOX(T) - general I/O to an off-CPU-board device (RASK/DELILAH
                        // microcode "DEVICE IS NOT ON CPU-BOARD, ISSUE GENERAL IOX",
                        // listing 000506-000510). The device address is in R1 (loaded a
                        // microinstruction earlier as "ADDRESS => IOX" and unchanged since);
                        // its low bit is the ND read/write select (even = read, odd = write).
                        // The output data (for a write) is the A register ("OUTPUT DATA").
                        // NB: this runs in Command Decode, BEFORE this microinstruction's
                        // IDB latch, so the IDB still holds the previous cycle's value - read
                        // A/R1 from the register file, not the IDB. A read returns its result
                        // via DBR, which the next microinstruction picks up with IDBS,DBR.
                        {
                            int ioxAddr = hw.alu.ReadRegister((ushort)RegisterEnum.R1);
                            bool ioxWrite = (ioxAddr & 1) != 0;
                            ushort ioxData = hw.alu.ReadRegisterE(RegisterEnum.A);
                            Logger.Log($"IOX: {Utils.Int2Oct(ioxAddr)} {(ioxWrite ? "W" : "R")} data={Utils.Int2Oct(ioxData)}", Logger.LogLevel.Device);

                            if (iobus.Iox(ioxAddr, ioxWrite, ioxData, out ushort ioxRead))
                            {
                                // A device claimed it. For a read, hand the value back through
                                // DBR (the microcode's IDBS,DBR at 000510 reads it into A/B).
                                if (!ioxWrite)
                                    hw.cpu_regs.DBR = ioxRead;
                            }
                            else
                            {
                                // No device owns this address -> IOX error (unchanged behaviour).
                                hw.pic.SetInterruptDetectbits(1 << 10);
                            }
                        }
                        break;
                    }

                    break;
                default:
                    break;
            }

            #endregion Command Decode         
        }

        private void MacroSkip()
        {
            //hw.mac.FETCH = true;            

            //hw.mac.mac_address = (ushort)(hw.alu.ReadRegisterE(RegisterEnum.P) + 1);
            hw.mac.CalculateNextMacAddress();
            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);

            //hw.mac.FETCH_IF_JMP_0 = true; // Hackety'hack
        }

        private void MacroJump(ushort address)
        {
            hw.mac.mac_address = address;
            hw.alu.WriteRegister((ushort)RegisterEnum.P, hw.mac.mac_address);

            // This command is a FETCH
            hw.mac.FETCH = true;
        }


        /// <summary>
        /// Latch input to ALU and execute ALU calculation
        /// </summary>
        private void Execute_2_LatchALU()
        {

            #region Latch ALU modes from microcode
            // Bits 44-45
            hw.alu.ALUM = microCode.aluMode;

            // Bits 55-57 ALU Source
            hw.alu.aluSource = microCode.aluSource;

            // Bits 58-60 ALU Function
            hw.alu.aluFunc = microCode.aluFunction;

            // Bits 61-63 ALU Destintation            
            hw.alu.aluDestination = microCode.aluDestination;
            #endregion Latch ALU modes from microcode


            #region ALU calculation

            switch (microCode.cin)
            {
                case CIN.CarryIs0:
                    // Carry is 0
                    hw.alu.CARRY_IN = false;
                    break;
                case CIN.CarryIs1:
                    // Carry is 1
                    hw.alu.CARRY_IN = true;
                    break;
                case CIN.CarryIsCFlipFlop: //CRY,C - (STATUS C -> CARRY IN)
                    // Carry is the C flipflop of the STS register
                    hw.alu.CARRY_IN = hw.alu.GetSTSBit(BUFALU.STS_C_bit);
                    break;
                case CIN.CarryIsGPRBit0: //CRY,GPR (GPR BIT 0 -> CARRY IN)
                    // Carry is the GPR bit 0 (used in division)
                    hw.alu.CARRY_IN = hw.alu.GPR0 != 0;
                    break;
                default:
                    break;
            }

            // Default shift mode, used by all ALUM except ALUM = IR_SHIFT
            hw.alu.ShiftMode = (ShiftMode)microCode.MIS;  // Shift mode is defined din the MIS0+MIS1 bits


            // Set the shift mode in the ALU depending on other bits
            // See page 17 in ND-06.031.1 EN "ND-110 microprogrammers guide"
            switch (hw.alu.ALUM)
            {
                // Bits 44-45: These modify bits 55-65, before they reach the ALU. 
                // ALUM 0
                // ALUM 1

                case ND_ALUMode.uSHIFT:
                    // The ALU behaviour is controlled entirely from the microprogram.


                    break;
                case ND_ALUMode.FMU:
                    // FMU updateds bit 1 in ALUI/Source with GPR0 (or ALUi1 to be specific)


                    ALUSource orgS = hw.alu.aluSource;

                    // Clear bit 1 in ALU Source
                    int tmpAlus = (int)orgS & 0b101;

                    // Set bit 1 in ALU Source based on GPR0
                    // GPR0=1 => 001 => ALU SOURCE will be A+B,
                    // GPR0=0 => 011 => ALU SOURCE will be 0+B
                    if (hw.alu.GPR0 == 0)
                        tmpAlus |= 0b010;

                    // Update ALU SOurce
                    hw.alu.aluSource = (ALUSource)tmpAlus;



                    // DEBUG!
                    //if (hw.alu.aluSource != orgS)
                    //{
                    //    Logger.Log($"FMU: Changing ALUS from {orgS} to {hw.alu.aluSource} [GPR0={hw.alu.GPR0}]", Logger.LogLevel.Debug);
                    //}

                    break;

                case ND_ALUMode.FDV:
                    // FDV updates bit 0 in ALUI/Func with GPR0 (or ALIU3 to be specific)

                    ALUFunc org = hw.alu.aluFunc;

                    // Clear bit 0 in ALU Func
                    int tmpAluf = (int)org & 0b110;

                    // Set bit 0 in ALU Function based on GPR0
                    tmpAluf |= hw.alu.GPR0;

                    // Update ALU Func
                    hw.alu.aluFunc = (ALUFunc)tmpAluf;

                    // DEBUG!
                    //if (hw.alu.aluFunc != org)
                    //{
                    //    Logger.Log($"FDV_1: Changing ALUF from {org} to {hw.alu.aluFunc}", Logger.LogLevel.Debug);
                    //}


                    break;


                case ND_ALUMode.IR_SHIFT:
                    // SHIFT INSTR. MODE. SH MODE FROM IR-BITS. M IS SET AUTOM.

                    // The shift direction and shift mode is controlled from the loop counter together with bits 9—10 of the macroinstruction.
                    // The extra microinstruction executed after the loop counter reaches 0 does not shift the ALU result.


                    // According to ND-110 uc bit-Diagram, page 17 ND-06.031.1EN

                    // Clear bit 2 and 1 in ALU Destination
                    int tmpAlud = (int)hw.alu.aluDestination;
                    tmpAlud &= 0b001; // Clear the bits ALUi8 and ALUi7 (and set them according to /LCZ and /UP) - See ND-06.031.1.EN Figure 3 when ALUM=IR SHIFT for details.

                    //// "/UP" updates ALIU/Destination bit 1  (or ALUI7)  // "Right shift" has loopcounter with minus value.                    
                    if (!hw.mic.UP0)
                        tmpAlud |= 0b010;



                    // "/LCZ" updates ALUI/Destination bit 2 (or ALUI8) LCZ=TRUE when Loop Counter (LC) is 0
                    if (!hw.mic.LCZ0)
                        tmpAlud |= 0b100;

                    hw.alu.aluDestination = (ALUDestination)tmpAlud;
                    hw.alu.ShiftMode = (ShiftMode)((hw.cpu_regs.IR >> 9) & 0x03); // Shift mode is defined in the IR9+IR10 bits

                    //alu.QR1 = alu.QL1 = alu.RR1 =  alu.RL1 = (byte)(alu.STS_M ? 1 : 0);


                    // Note: The value for the input pins to the ALU for Q and RAM (rotating right or left) is dependend on bits
                    break;
                default:
                    break;
            }




            int alud = (int)hw.alu.aluDestination;
            int i6 = alud & 0x01;
            int i7 = ((alud >> 1) & 0x01); // i7=> 0=SHIFT RIGHT, 1=SHIFT LEFT
            int i8 = ((alud >> 2) & 0x01); // i8=> 1=DO _SHIFT_ (not logical operation. Also shift GPR register)




            // Latch data into ALU
            hw.alu.DataBus = hw.idb.ReadWord();
            hw.alu.ExecuteALUOperation();

            //Logger.Log($"ALU.EXEC result: Q={Utils.Int2Oct(hw.alu.Q)} F={Utils.Int2Oct(hw.alu.F)} Y={Utils.Int2Oct(hw.alu.Y)}", Logger.LogLevel.Debug);


            #region Catch output values in case of shift
            // Chip 15F Data 2 -> Calculate signal SM1
            int SIGNAL_SM1 = 0;
            int sm1_Selector = i6 << 1;
            if (i7 == 0) sm1_Selector |= 0x01;
            switch (sm1_Selector)
            {
                case 0:
                    SIGNAL_SM1 = hw.alu.RLI;
                    break;
                case 1:
                    SIGNAL_SM1 = hw.alu.QRI;
                    break;
                case 2:
                    SIGNAL_SM1 = hw.alu.RLI;
                    break;
                case 3:
                    SIGNAL_SM1 = hw.alu.RRI;
                    break;
            }
            #endregion

            // Set data input for RAM0/Q0/RAM15/Q15 based on MIS (SEL1+SEL0) and ALUD (I7 and I6)


            #region Set ALU shift input bits
            // STOPGAP (2026-07-10): the CGA_CPU_ALU_CONTR.v-derived gate rewrite that lived
            // here computed RLI/RRI/QLI correctly (verified bit-exact against GATES_16-40 in
            // CGA_CPU_ALU_CONTR.v) but then wrote that QLI value into hw.alu.QRI as well.
            // QRI does NOT exist as a signal anywhere in the real hardware: it is not an
            // output of CGA_CPU_ALU_CONTR.v, and CGA_ALU_QREG.v (the real Q register) only
            // takes QLI + a 2-bit QSEL_1_0 mux select as inputs - there is no "QRI" pin on
            // real hardware. hw.alu.QRI is read elsewhere in this method (SIGNAL_SM1, feeding
            // the STS M flag) by pre-existing ND-110-schematic-derived logic, so overwriting
            // it with a fabricated value corrupted the M flag on every ALU cycle landing in
            // that selector case, which fed back into STS7 above and cascaded into the ND-110
            // self-test failure (confirmed via A/B test: self-test passes with this block
            // reverted, fails with the gate rewrite).
            // RESOLVED (2026-07-13): Q's real QSEL_1_0 mux is now modeled in
            // BUFALU.ExecuteAluDestination (per CGA_ALU_QREG.v + GATES_26/27 in
            // CGA_CPU_ALU_CONTR.v): QRI/QLI are OUTPUT-only Q-bit reads, the double-right
            // Q15 := F[0] link is hardwired, and the only external Q data input is
            // QLeftSerialInput (the CONTR's QLI signal) written by the left-shift cases
            // below. SIGNAL_SM1 keeps reading the true Q0/Q15/F bits.
            // Chip 14H 74S251
            // RL1 (RAM-15) (only if ALU I7 ==1) // Stro
            if (i7 == 0) // RIGHT/DOWN
            {
                if (i6 == 0)
                {
                    // Double shift RIGHT (RAMQD - e.g. SAD SHR, RMPY/RDIV steps): RAM holds
                    // the high word (A), Q holds the low word (D). The INTERNAL link is
                    // fixed: RAM bit 0 falls into Q bit 15. The END input (RAM bit 15) is
                    // selected by the shift mode, same table as the single-register case
                    // below (ND-100 shift semantics: plain shift = arithmetic/sign, ROT =
                    // 32-bit rotate so Q bit 0 comes back in at the top, ZIN = zero,
                    // LIN = the M/link flag).
                    // BUG FIX (2026-07-11): this used to be "RLI = QRI unless ZIN", i.e. a
                    // hardwired 32-bit rotate - wrong for plain arithmetic double shifts.
                    // (The RAM0 -> Q15 internal link itself is HARDWIRED inside the Q mux
                    // now - CGA_ALU_QREG.v MUXQ15 D3 = F[0] - so nothing to set up here.)

                    switch (hw.alu.ShiftMode)
                    {
                        case ShiftMode.ARITHM:
                            hw.alu.RLI = hw.alu.F15 ? 1 : 0; // sign extend the 32-bit value
                            break;
                        case ShiftMode.ROT:
                            hw.alu.RLI = hw.alu.QRI; // Bit 0 of Q into Bit 15 of RAM (32-bit rotate)
                            break;
                        case ShiftMode.ZIN:
                            hw.alu.RLI = 0;
                            break;
                        case ShiftMode.LIN:
                            hw.alu.RLI = hw.alu.STS_M ? 1 : 0;
                            break;
                    }
                }
                else
                {
                    switch (hw.alu.ShiftMode)
                    {
                        case ShiftMode.ARITHM:
                            hw.alu.RLI = hw.alu.F15 ? 1 : 0;
                            break;
                        case ShiftMode.ROT:
                            hw.alu.RLI = hw.alu.RRI; // RAM0
                            break;
                        case ShiftMode.ZIN:
                            hw.alu.RLI = 0;
                            break;
                        case ShiftMode.LIN:
                            hw.alu.RLI = hw.alu.STS_M ? 1 : 0;
                            break;
                    }
                }
            }
            else // i7==1 => LEFT/UP
            {
                // Chip 15H 74S251
                // RR1 (RAM0) (only if ALU I7 ==1)

                if (i6 == 0)
                {
                    // Double shift LEFT (RAMQU - e.g. SAD n): RAM holds the high word (A),
                    // Q holds the low word (D). The INTERNAL link is fixed: Q bit 15 climbs
                    // into RAM bit 0. The END input (Q bit 0) is selected by the shift mode
                    // (plain shift / ZIN = zeros in at the bottom, ROT = RAM bit 15 wraps
                    // around = 32-bit rotate, LIN = the M/link flag).
                    // BUG FIX (2026-07-11): this used to be unconditionally "QRI = RLI",
                    // i.e. always a 32-bit ROTATE. INSTRUCTION-VERIFY's SAD2OP caught it:
                    // A=100000 D=0, "SAD 1" must give A=0 D=0 (bit 15 leaves through M),
                    // but the rotate fed A's bit 15 back into D bit 0, giving D=1.
                    hw.alu.RRI = hw.alu.QLI; // Bit 15 of Q into Bit 0 of RAM (internal link)

                    // The Q register's bit-0 input on a left double shift - the real
                    // hardware QLI control signal (CGA_CPU_ALU_CONTR.v -> CGA_ALU_QREG.v
                    // MUXQ0 D2). Selected by shift mode:
                    switch (hw.alu.ShiftMode)
                    {
                        case ShiftMode.ARITHM:
                            hw.alu.QLeftSerialInput = 0; // plain shift: zeros in at the bottom
                            break;
                        case ShiftMode.ROT:
                            hw.alu.QLeftSerialInput = hw.alu.RLI; // Bit 15 of RAM into Bit 0 of Q (32-bit rotate)
                            break;
                        case ShiftMode.ZIN:
                            hw.alu.QLeftSerialInput = 0;
                            break;
                        case ShiftMode.LIN:
                            hw.alu.QLeftSerialInput = hw.alu.STS_M ? 1 : 0;
                            break;
                    }
                }
                else
                {
                    // Single left shift (RAMU, i6=1): Q holds (QSEL = 00), so only the
                    // RAM serial input matters here.
                    switch (hw.alu.ShiftMode)
                    {
                        case ShiftMode.ARITHM:
                            hw.alu.RRI = 0; // GND
                            break;
                        case ShiftMode.ROT:
                            hw.alu.RRI = hw.alu.RLI; // RAM15
                            break;
                        case ShiftMode.ZIN:
                            hw.alu.RRI = 0; // GND
                            break;
                        case ShiftMode.LIN:
                            hw.alu.RRI = hw.alu.STS_M ? 1 : 0;
                            break;
                    }
                }
            }




            #endregion Set ALU shift input bits


            #region Shift GPR
            if (i8 == 1) // Shifting happening
            {
                if (i7 == 0) // RIGHT/DOWN
                {
                    //ushort gprOrg = hw.alu.GPR;
                    hw.alu.GPR = (ushort)((hw.alu.GPR >> 1) & 0x7FFF);
                    // No feed in from top bit as pin 11, Serial Input Shift Right is tied to GND for chip 10B (8 bit upper part of GPR)

                    //if (hw.alu.GPR != gprOrg)
                    //{
                    //    Logger.Log($"I8=1, SHIFT GPR RIGHT: Org:{Utils.Int2Oct(gprOrg)} => {Utils.Int2Oct(hw.alu.GPR)}]", Logger.LogLevel.Debug);
                    //}
                }
                else
                {   // LEFT/UP

                    ushort gprOrg = hw.alu.GPR;


                    int GPRLI = 0;

                    int cry = hw.alu.CARRY_OUT ? 1 : 0;
                    int M = hw.alu.STS_M ? 1 : 0;

                    int GPR0 = hw.alu.GPR0;

                    // shift left
                    hw.alu.GPR = (ushort)((hw.alu.GPR << 1) & 0xFFFE);

                    // input signal for bit 0 may come from:
                    // M, CRY or GPR0 (chip 7F, 74H52 => 4 input and with OR)
                    // In ND-120 bit 0 is set in CGA_ALU_GPR with
                    //      input GPRLI coming from CGA_ALU_CONTR bits STS.7 (M), CRY, GPR0. GPR0 is before the shift
                    //      and input GPRC_2_0 coming from CGA_ALU_CONTR  (meaning: this bit is only set when ALU i8 is 1, and ALU i7 =1)
                    //      (but not during XFETCH or LDGPR) XFETCHN = output from DCD
                    //              COmmand = 22.2, 23,24,25,26,27
                    if (microCode.command != Commands.LDGPR)
                    {
                        GPRLI |= (ushort)(cry & GPR0);  // CRY and GPRI
                        GPRLI |= (ushort)(M & GPR0); // M and GPRI
                        GPRLI |= (ushort)(cry & M); // CRY and M

                        hw.alu.GPR |= (ushort)(GPRLI & 0x01);
                    }

                    //if (hw.alu.GPR != gprOrg)
                    //{
                    //    Logger.Log($"I8=1, SHIFT GPR LEFT: Org:{Utils.Int2Oct(gprOrg)} => {Utils.Int2Oct(hw.alu.GPR)}]", Logger.LogLevel.Debug);
                    //}
                }
            }


            #endregion Shift GPR

            // Execute ALU operation
            hw.alu.ExecuteAluDestination();

            //Logger.Log($"ALU.DEST result: Q={Utils.Int2Oct(hw.alu.Q)} F={Utils.Int2Oct(hw.alu.F)} Y={Utils.Int2Oct(hw.alu.Y)}", Logger.LogLevel.Debug);

            // Re-latch output from ALU to be used by Commands (maybe commands should read ALU.Y direct?)
            if (microCode.IDBsource == IDBSource.ALU)
            {
                //if (microCode.aluDestination != ALUDestination.ALUD_NONE)
                {
                    //hw.alu.DataBus = hw.alu.Y;
                    hw.idb.Latch(hw.alu.Y, "ALU.Y");
                }
            }


            // These control the behaviour of the 8 lower bits of the STS register
            // Bits 53-54
            // STSOperation
            switch (microCode.stsOperation)
            {
                case STSOperation.HOLD_STS: // (bits 0-7)
                                            // does not affect the STS register
                    break;
                case STSOperation.ENABLE_SET_COQ: //STS,EA (CRY -> STS C : OVF -> STS Q : OVF \/ STS O -> STS O)
                                                  // Updates the C, O and Q flip-flops to the result of the current ALU instruction
                    hw.alu.SetSTS(BUFALU.STS_C_bit, hw.alu.CARRY_OUT); // CRY as input signal in schematics (output carry from last ALU chip)
                    // O is the STATIC overflow: per the token spec quoted above it is
                    // "OVF \/ STS O -> STS O" - set on overflow, STICKY until cleared
                    // explicitly (TRR/MCL STS). It must never be overwritten with a plain
                    // 0 by a non-overflowing ALU op. BUG FIX (2026-07-11): this used to
                    // assign OVERFLOW_OUT directly, so any ROP (e.g. the "COPY SZ DA"
                    // between two checks in INSTRUCTION-VERIFY's PRIVILEGED
                    // MCL/MST/TRA/TRR test, image 036351-036360) silently cleared O and
                    // "TRA STS & 0257" read 0217 instead of 0257.
                    hw.alu.SetSTS(BUFALU.STS_O_bit, hw.alu.OVERFLOW_OUT || hw.alu.STS_O);
                    hw.alu.SetSTS(BUFALU.STS_Q_bit, hw.alu.DYNAMIC_OVERFLOW_OUT); //OVF as input signal in schematics
                    break;
                case STSOperation.ENABLE_SET_M://STS,ES
                                               // Updates the M flip-flop to the disappearing bit in a shift microinstruction.
                                               // If there is ALUM IRSHIFT command, M will be updated, regardless of the settings of bit 53-54
                    hw.alu.SetSTS(BUFALU.STS_M_bit, SIGNAL_SM1 != 0);
                    break;
                case STSOperation.LOAD_ST: // STS,LO (IDB-BITS 0-7 -> STATUS-BITS 0-7)
                                           // Loads the lower bits of the STS register from the IDB 
                                           // At least thats what the doc says. SINTRAN uses an undocumented HI byte update. So doc and reality doesnt align
                                           // TODO: Document more the HI byte update

                    hw.alu.LoadSTS_Low(hw.idb.ReadLO());
                    //hw.alu.LoadSTS(hw.idb.ReadWord());
                    break;
            }

            if (hw.alu.ALUM == ND_ALUMode.IR_SHIFT)
            {
                // If there is ALUM IRSHIFT command, M will be updated, regardless of the settings of bit 53-54

                // IF logic below determined by comparing results from ND-100 emulator, and when diff - comparing with ND-110/CX HW
                //
                // BUG FIX (2026-07-11): the old condition also fired on
                // "aluDestination == ALUD_B_YA". In IR_SHIFT mode the ALUD i8/i7 bits are
                // recomputed from /LCZ and /UP (see the IR_SHIFT case above), so the
                // documented non-shifting EXTRA microinstruction after the loop counter
                // reaches 0 ("The extra microinstruction ... does not shift the ALU
                // result", ND-06.031.1EN) decays a double shift's ALUD_SLD (110) to
                // 010 = ALUD_B_YA. Updating M there re-clocked the M flip-flop from the
                // UNSHIFTED result's edge bit and destroyed the real disappearing bit -
                // INSTRUCTION-VERIFY's SAD2OP ("SAD 1" with A=100000: BSKP on M) caught it.
                // M must only clock while an actual shift happens (i8 == 1).
                if (i8 == 1)
                {
                    hw.alu.SetSTS(BUFALU.STS_M_bit, SIGNAL_SM1 != 0);
                    Utils.Log($"SET M = {SIGNAL_SM1}");
                }
            }

            #endregion ALU calculation
        }

        private void Execute_1_LatchInternalBus()
        {

            //********************************************************************
            //                    latch data into Internal Bus
            //********************************************************************


            // Bits 48-49
            // These contol the source of the B-operand in to the CPU
            switch (microCode.RBSEL)
            {
                case 0:
                    // The B-operand is bits 16-19 of the microinstruction. It is used to address a register file or ALU WRF (WorkRegisterFile)
                    hw.alu.B_Operand = (ushort)microCode.bits16_19;
                    break;
                case 1:
                    // The B-operand is bits 0-2 of the current macroinstruction. This is used to address the destination register, in macroinstructions.
                    hw.alu.B_Operand = (ushort)(hw.cpu_regs.Operand & 0x07);
                    break;

                case 2:
                    // The B-operand is bits 3-5 of the current macroinstruction. This is used to address the source register, in macroinstructions.
                    hw.alu.B_Operand = (ushort)((hw.cpu_regs.Operand >> 3) & 0x07);
                    break;
                case 3:
                    // The B-operand is bits 0-3 of the loop counter. This is used when performing a level change, to provide fast scanning of
                    // addresses in the register file and working register file. It is aso used to read the loop counter from MIC onto BA0-3, using BARG in the ALU
                    // to steer ut onto the IDB
                    // NOTE: the OPERAND path sees the loop counter through a one-instruction
                    // pipeline latch (MIC.LC_operand) - ND-06.031.1EN LDLC: "The modified
                    // loop counter will not be available as an A-operand until the next
                    // microinstruction has been completed".
                    hw.alu.B_Operand = (ushort)(hw.mic.LC_operand & 0x0F);
                    break;
            }
            RegisterEnum rb = (RegisterEnum)(hw.alu.B_Operand);
            Logger.Log($"B operand={rb} [{Utils.Int2Oct(hw.alu.B_Operand)}]  RBSEL:[{microCode.RBSEL}] ", Logger.LogLevel.Opcodes);

            // Bits 50-52: These control the source of the A-Operand to the ALU (RA)
            // A-Operand is used to select Register (Zero,D,...R7), Bit Level (when accessing register file) or Pic Instruction)
            microCode.RASEL_RegisterFile = 0;

            // 52 = RASEL_1
            // 51 = RASEL_0
            // 50 = XREF3

            // NOTE: The selection of RF(1:0) bits may *NOT* be correct here, as the schematics on the CGA_MIC shows a bit more complex setting.

            switch (microCode.RASEL)
            {

                case 0b000:  // 0
                             // The A-Operand is bits 12-15 of the microinstruction (Logisim agrees, rf=0)
                    hw.alu.A_Operand = (ushort)microCode.bits12_15;
                    break;

                case 0b010: // 2 
                            // The A-Operand is the PIL register (used to address the current level register block in the register file) (Logisim agrees, rf=0)
                    hw.alu.A_Operand = (ushort)hw.alu.PIL;
                    break;

                case 0b100: // 4 "A,REG"
                            // The A-Operand is bits 3-6 of the current macroinstruction. This is used in bit operation instructions,
                            // and in instructions specifying a level in the register file (Logisim agrees, rf=0)
                    hw.alu.A_Operand = (ushort)(hw.cpu_regs.Operand >> 3 & 0x0F);
                    break;

                case 0b101: // 5
                            // The A-Operand is bits 3-5 of the current macroinstruction. This is used to address the source register, in macroinstructions (Logisim agrees, rf=0)
                    hw.alu.A_Operand = (ushort)(hw.cpu_regs.Operand >> 3 & 0x07);
                    break;

                case 0b110: // 6 "A,LC"
                            // The A-Operand is bits 0-3 of the loop counter (Logisim agrees, rf=0)
                            // Uses the pipelined operand latch, NOT the live counter - see MIC.LC_operand.
                    hw.alu.A_Operand = (ushort)(hw.mic.LC_operand & 0x0F);
                    break;

                case 0b001: // 1 "A,? XRF"
                            // Extended register file RF2 addessed by *bit12-15 on the A-Operand. This is used to store globally scratch registers, accessible by microcode only
                    hw.alu.A_Operand = (ushort)microCode.bits12_15;
                    microCode.RASEL_RegisterFile = 2; //(Logisim agrees, rf=2)
                    break;
                case 0b011: // 3 "A,PIL XRF"
                            // Extended register file RF3 addessed by program level bits PIL0-3 on the A-Operand.
                            // This is used to store the PCR's and other level-specific scratch registers that must be retained.
                    hw.alu.A_Operand = hw.alu.PIL; //hw.RF3.Read(hw.alu.PIL & 0x0F, 0);  
                    microCode.RASEL_RegisterFile = 3; // PIL ON RF3 (Logisim agrees, rf=3)
                    break;

                case 0b111: // 7  "A,LC XRF"
                            // ND doc says: Extended register file RF1 addressed by loop counter bits LC12-15 on the A-operand. This is used for temporary storage in loop-context operations)
                            // Uses the pipelined operand latch, NOT the live counter - see MIC.LC_operand.
                    hw.alu.A_Operand = (ushort)(hw.mic.LC_operand & 0x0F);
                    microCode.RASEL_RegisterFile = 1; //  //(Logisim agrees, rf=1)
                    break;
            }

            RegisterEnum ra = (RegisterEnum)(hw.alu.A_Operand);
            Logger.Log($"A operand={ra} [{Utils.Int2Oct(hw.alu.A_Operand)}] RASEL:[{microCode.RASEL}]", Logger.LogLevel.Opcodes);

            LatchDataToIDB(microCode);

        }

        private void LatchDataToIDB(MicroCode microCode)
        {
            Utils.Log($"IDBS={microCode.IDBsource}");

            IDBSource idbs = microCode.IDBsource;
            switch (idbs)
            {
                case IDBSource.ALU:
                    // ALU Result OR register pointed to by the A-operand. (dependent on bits 61-63/ALUDestination)

                    /*
                    switch (hw.alu.aluDestination)
                    {
                        case ALUDestination.ALUD_Q:
                            break;
                        case ALUDestination.ALUD_NONE:
                            // No source - disconnected ?
                            break;
                        case ALUDestination.ALUD_B_YA:
                            break;
                        case ALUDestination.ALUD_B:
                            break;
                        case ALUDestination.ALUD_SRD:
                            break;
                        case ALUDestination.ALUD_SRB:
                            break;
                        case ALUDestination.ALUD_SLD:
                            break;
                        case ALUDestination.ALUD_SLB:
                            break;
                        default:
                            break;
                    }
                    */

                    switch (hw.alu.aluDestination)
                    {

                        case ALUDestination.ALUD_B_YA:
                            hw.idb.Latch(hw.alu.ReadRegister(hw.alu.A_Operand), $"WRF[ALU.A_OPERAND({hw.alu.A_Operand})]");
                            break;

                        case ALUDestination.ALUD_Q:
                        case ALUDestination.ALUD_NONE:
                        case ALUDestination.ALUD_B:
                        case ALUDestination.ALUD_SRD:
                        case ALUDestination.ALUD_SRB:
                        case ALUDestination.ALUD_SLD:
                        case ALUDestination.ALUD_SLB:
                        default:
                            hw.idb.Latch(hw.alu.Y, $"ALU.Y ({hw.alu.aluDestination})");
                            break;
                    }
                    //if (hw.alu.aluDestination == ALUDestination.ALUD_NONE)
                    //{
                    //    hw.idb.Latch(hw.alu.A_Operand);
                    //}
                    //else
                    //{
                    //    hw.idb.Latch(hw.alu.Y);
                    //}

                    // TODO: Implement logic for bits 60-63 which may result in a different latch()


                    break;
                case IDBSource.BMG:
                    hw.idb.Latch(hw.alu.BMG(), "BMG");
                    break;
                case IDBSource.GPR:
                    hw.idb.Latch(hw.alu.GPR, "GPR");
                    break;
                case IDBSource.DBR:
                    hw.idb.Latch(hw.cpu_regs.DBR, "DBR");
                    break;
                case IDBSource.ARG:
                    hw.idb.Latch(hw.cpu_regs.ARG, "ARG");
                    break;
                case IDBSource.REG:


                    switch (microCode.RASEL_RegisterFile)
                    {
                        case 0:
                            hw.idb.Latch(hw.RF.Read(hw.alu.A_Operand, hw.alu.B_Operand));
                            break;
                        case 1:
                            hw.idb.Latch(hw.RF1.Read(hw.alu.A_Operand, hw.alu.B_Operand));
                            break;
                        case 2:
                            hw.idb.Latch(hw.RF2.Read(hw.alu.A_Operand, hw.alu.B_Operand));
                            break;
                        case 3:
                            hw.idb.Latch(hw.RF3.Read(hw.alu.A_Operand, hw.alu.B_Operand));
                            break;

                    }

                    break;
                case IDBSource.STS:
                    hw.idb.Latch(hw.alu.STS);
                    break;
                case IDBSource.BARG:
                    /*

                      CORRECTED: BARG Places 6+ Bits on IDB

                      Complete BARG to IDB Mapping:

                      When IDBS=BARG and EBARG is active, multiple microcode fields are routed to IDB:

                      // IDB Bit Sources when EBARG is active (multiplexer input 7):
                      IDB[0] = s_lba_0    // LBA[0] - B register select bit 0
                      IDB[1] = s_lba_1    // LBA[1] - B register select bit 1
                      IDB[2] = s_lba_2    // LBA[2] - B register select bit 2
                      IDB[3] = AARG0      // A argument bit 0
                      IDB[4] = s_laa_1    // LAA[1] - A register select bit 1
                      IDB[5] = s_laa_2    // LAA[2] - A register select bit 2
                      IDB[6] = s_laa_3    // LAA[3] - A register select bit 3
                      IDB[7] = ?          // Need to check what's connected
                      IDB[8+] = s_gpr_7 or other sources (sign extended or other data)

                      This Explains "Values 0-17":

                      With 6 bits (IDB[5:0]) you can represent values 0-63, which easily covers 0-17.

                      For "B,17 IDBS,BARG":
                      - Binary 17 = 010001₂
                      - IDB[0] = 1 (LBA[0])
                      - IDB[1] = 0 (LBA[1])
                      - IDB[2] = 0 (LBA[2])
                      - IDB[3] = 0 (AARG0)
                      - IDB[4] = 1 (LAA[1])
                      - IDB[5] = 0 (LAA[2])

                      Revised Answer:

                      BARG places at least 6 significant bits on IDB (bits 0-5), combining:
                      - 3 bits from LBA (B register selection)
                      - 1 bit from AARG0 (A argument)
                      - 3 bits from LAA (A register selection, bits 1-3)

                      This creates a 6-bit argument value that can range from 0-63, which includes the documented range of 0-17 for B
                      operand specifications.

                      You were absolutely right to question my earlier answer!what 

                     1. Compiler Decision Logic

  The compiler analyzes the value range and context to determine encoding:

  Case 1: B,17 (Small Immediate)

  Compiler Logic: "Value 17 is ≤ 17, use BARG encoding"
  IDBS Field: CSIDBS_4_0 = 8 (IDBS_BARG)
  LBA Field:  LBA_3_0 = 17 (binary: 10001)
  LAA Field:  LAA_3_0 = 0 (unused for this case)

  Case 2: B,177777 (Large Immediate)

  Compiler Logic: "Value 177777₈ (65535₁₀) is > 17, use ARG encoding"
  IDBS Field: CSIDBS_4_0 = 4 (IDBS_ARG)
  ARG Field:  CSBIT_15_0 = 177777₈ (full 16-bit immediate)
  LBA Field:  Not used for ARG

  2. Detailed Field Assignments

  For B,17 IDBS,BARG:

  Microcode Bit Layout:
  CSIDBS_4_0[4:0] = 01000₂ (8 = BARG)
  LBA_3_0[3:0]    = 17₁₀ = 10001₂

  Hardware Execution:
  - EBARG = 1 (enabled by CSIDBS=8 decode)
  - IDB[0] = LBA[0] = 1
  - IDB[1] = LBA[1] = 0
  - IDB[2] = LBA[2] = 0
  - IDB[3] = LBA[3] = 0
  - IDB[4] = LAA[1] = 1 (if LAA contains part of the value)
  - Result: IDB = 17₁₀

  For B,177777 IDBS,ARG:

  Microcode Bit Layout:
  CSIDBS_4_0[4:0] = 00100₂ (4 = ARG)
  CSBIT_15_0[15:0] = 177777₈ = 1111111111111111₂

  Hardware Execution:
  - EARG = 1 (enabled by CSIDBS=4 decode)
  - IDB[15:0] = CSBIT_15_0[15:0] = 177777₈
  - Result: IDB = 65535₁₀ (full 16-bit value)

  3. Compiler Value Range Logic

  // Pseudocode for compiler B operand encoding
  if (B_operand_value <= 17) {
      CSIDBS_4_0 = IDBS_BARG;      // Use 8
      encode_in_LBA_LAA_fields(B_operand_value);
  }
  else if (B_operand_value <= 170 && (B_operand_value % 8 == 0)) {
      CSIDBS_4_0 = IDBS_AARG;      // Use 12 (A-arg shifted left 3)
      encode_in_LAA_field(B_operand_value >> 3);
  }
  else if (B_operand_value <= 65535) {
      CSIDBS_4_0 = IDBS_ARG;       // Use 4 (full 16-bit immediate)
      CSBIT_15_0 = B_operand_value;
  }

  4. Hardware Control Answer

  Yes, EBARG is controlled by IDBS=BARG:

  // In CGA_ALU_OUTMUX_IDBS.v
  // When CSIDBS_4_0 = 8 (BARG), the decoder generates EBARG=1
  .QGN(s_ebarg_out),  // Output from register when BARG is decoded

  EBARG activation chain:
  1. Microcode: IDBS,BARG → Compiler sets CSIDBS_4_0 = 8
  2. Hardware: CSIDBS=8 decoded by ND38GLP + R81 registers
  3. Output: EBARG=1 enables B argument path to IDB
  4. Result: LBA/LAA fields → IDB[5:0] for small immediate values

  This elegant design allows the same B,value syntax to automatically select the most efficient encoding based on
  the immediate value size.

h, for your emulator! You need to know exactly which microcode bits to read to reconstruct the 16-bit value when
  IDBS=BARG. Let me give you the precise mapping:

  Emulator: Reconstructing 16-bit Value from BARG

  Microcode Bits to Read:

  When CSIDBS_4_0 = 8 (BARG), you need to read these specific microcode fields:

  // Microcode bit extraction for BARG
  uint16_t reconstruct_barg_value(uint64_t microcode_word) {
      // Extract the relevant fields from microcode
      uint8_t lba_2_0 = (microcode_word >> LBA_BIT_POSITION) & 0x7;     // 3 bits
      uint8_t laa_3_1 = (microcode_word >> LAA_BIT_POSITION) & 0x7;     // 3 bits
      uint8_t aarg0   = (microcode_word >> AARG0_BIT_POSITION) & 0x1;   // 1 bit

      // Reconstruct the 16-bit value as it appears on IDB
      uint16_t idb_value = 0;

      idb_value |= (lba_2_0 & 0x7);           // IDB[2:0] = LBA[2:0]
      idb_value |= (aarg0 & 0x1) << 3;        // IDB[3] = AARG0
      idb_value |= (laa_3_1 & 0x7) << 4;      // IDB[6:4] = LAA[3:1]

      // Higher bits (IDB[15:7]) appear to be zero or sign-extended
      // Based on the "Values 0-17" comment, only lower 7 bits are significant

      return idb_value & 0x7F;  // Mask to 7 bits for BARG range 0-17
  }

  Key Point for Your Emulator:

  BARG only uses 7 bits effectively (IDB[6:0]), giving values 0-127, but the documentation says "Values 0-17" - this
   suggests your emulator should:

  1. Extract the 7 bits as shown above
  2. Validate the range - if > 17, it may be an invalid microcode or different interpretation
  3. Use the reconstructed value as the B operand immediate

  Microcode Field Positions:

  You'll need to check your microcode format documentation for exact bit positions, but based on the ND-120 layout:

  // Approximate bit positions (verify against your microcode format)
  #define LBA_BIT_POSITION    52   // LBA field in microcode word
  #define LAA_BIT_POSITION    48   // LAA field in microcode word
  #define AARG0_BIT_POSITION  47   // AARG0 bit in microcode word

  Simple Extraction for 0-17 Range:

  If you only need to handle the documented 0-17 range:

  uint16_t get_barg_immediate(uint64_t microcode_word) {
      // For values 0-17, you might only need LBA + AARG0
      uint8_t lba = (microcode_word >> LBA_BIT_POSITION) & 0x7;
      uint8_t aarg0 = (microcode_word >> AARG0_BIT_POSITION) & 0x1;

      return lba | (aarg0 << 3);  // Gives 0-15 range
  }

  Bottom Line: Extract LBA[2:0], AARG0, and LAA[3:1] from the microcode word and combine them as shown above to get
  your 16-bit immediate value for the B operand.
 
                     */

                    hw.idb.Latch(hw.alu.B_Operand & 0x0F);
                    break;
                case IDBSource.SWAP:
                    hw.idb.Latch(hw.idb.ReadSwap());
                    break;
                case IDBSource.PEA:
                    hw.idb.Latch(hw.cpu_regs.PEA);
                    break;
                case IDBSource.PES:
                    hw.idb.Latch(hw.cpu_regs.PES);
                    break;
                case IDBSource.AARG:
                    hw.idb.Latch((hw.alu.A_Operand & 0x0F) << 3);
                    break;
                case IDBSource.PICS:
                    hw.idb.Latch(hw.pic.ExecuteReadStatus());
                    break;
                case IDBSource.IOR:
                    //if (hw.cpu_regs.IO == 0)
                    hw.cpu_regs.IO = hw.ReadIORegister(); //TODO: Find a better solution

                    hw.idb.Latch(hw.cpu_regs.IO);
                    break;
                case IDBSource.none: // Nothing enabled onto the IDB by the IDBS field
                                     //hw.idb.Disconnect(); ?
                                     //hw.idb.Latch(0);
                    if ((microCode.command == Commands.EPIC) && (hw.alu.A_Operand == (int)Am2914.Am2914Instruction.RDM))
                    {
                        // Read the mask register from PIC and latch to IDB (for later retrieval
                        hw.idb.Latch(hw.pic.ExecuteReadMask());
                    }
                    break;

                case IDBSource.MIPANS:
                    hw.idb.Latch(hw.panel.EPANS);
                    break;
                case IDBSource.MAPANS:
                    hw.idb.Latch(hw.panel.EPANS);
                    hw.panel.ResetPanelStatusReadBit();
                    break;
                case IDBSource.GPR_SEXT:
                    // GPR BITS 0-7 (WITH BITS 8-15 EQUAL TO BIT 7) -> IDB
                    hw.idb.Latch(hw.alu.GPR_SEXT());
                    break;
                case IDBSource.PGS: // PGS in schematic (bit 15=FETCHN, Bit 14=PVIOL, Bit 11->0 = LA_21->10
                    ushort pgs = hw.RF.Read(10, 13);
                    //ushort pgs = hw.cpu_regs.PGS;
                    hw.idb.Latch(pgs); // TODO: Validate what "Paging Status *Extended*" means in this contexts (maybe its reading the MM variable?)

                    break;
                case IDBSource.CSR:
                    hw.idb.Latch(hw.cpu_regs.CSR);
                    break;
                case IDBSource.PCR:
                    hw.idb.Latch(hw.RF3.Read((ushort)hw.alu.PIL, (ushort)0));
                    hw.idb.Latch(hw.cpu_regs.PCR);
                    break;
                case IDBSource.ALD:
                    ushort ald_val = hw.cpu_regs.ALD;
                    if (cpuType == CPUType.ND120)
                    {
                        // ND-120/CX - See details on sheet 41 of 50 on ND-120 3202D schematics

                        // INVERT BIT 0 - 3(ALD),BIT 4 - 6(PRINT RELEASE),BIT 7(CX),BIT14(PRINT NO)
                        // BIT7 = 1 = > HIGH SPEED,BIT 15 - 13 = 101 = > 3202

                        ald_val |= 0b011 << 4; // 011 for version D
                        ald_val |= 1 << 7; // CX signal  1=HIGH SPEED (Controlled by PAL44601B, set to VCC=1)                    
                        ald_val |= 0b101 << 13; // 101 for board 3202

                        // Invert signals as they go through an 74244 which negates the signals
                        ald_val = (ushort)(ald_val ^ 0xFFFF);

                    }
                    else
                    {
                        // ND-110/CX

                        ald_val |= 0b011 << 4; // 011 for version D
                        ald_val |= 1 << 7; // CX signal  1=HIGH SPEED (Controlled by PAL44601B, set to VCC=1)                    
                        ald_val |= 0b111 << 13; // 111 .. what is correct ?
                    }
                    hw.idb.Latch(ald_val);
                    break;
                case IDBSource.PANEL:
                    hw.idb.Latch((ushort)hw.panel.InterruptVector); //TODO: Read vector from panel
                    break;
                case IDBSource.RCS:
                    hw.idb.Latch(hw.cpu_regs.ControlStoreAddress); // TODO, Need offset int control store ?? See Command 36,1 RWCS
                    break;
                case IDBSource.PICVC: // See command EPIC
                    hw.idb.Latch(hw.pic.ExecuteReadVector());
                    //ExecuteEpicCommands(PriorityInterruptController.Am2914Instruction.RDVC);
                    break;
                case IDBSource.spare1:
                    hw.idb.Latch(0);
                    //hw.idb.Disconnect(); ?
                    break;
                case IDBSource.spare2:
                    hw.idb.Latch(0);
                    //hw.idb.Disconnect(); ?
                    break;

                case IDBSource.JMPA:
                // ND-120: this is IDBS,LA (source 34 octal) - the ND-120-DELILAH-L
                // token table describes it with the exact same guide text as below
                // ("LOGICAL ADDRESS USED BY 'ALU' WHEN JUMP", TOKENS.TXT) and the L
                // microcode uses it 59 times, so the ND-110 body applies to both
                // CPU types. This was previously guarded ND-110-only, which left
                // the IDB unlatched for every ND-120 jump - the biggest functional
                // gap found in the 2026-07-13 ND-120 phase-0 audit.
                {

                    //ND-06.031.1 EN, Page 27
                    // LOGICAL ADDRESS USED BY 'ALU' WHEN JUMP
                    // In ND-110, LA (10-15), CA (0-9) Update P in ALU (ie all 24 bits)


                    // The three addressing mode bits called "', X", 1" and ”',B” provide eight different addressing modes.
                    // Bit 10-8
                    //var adressingmode = (AdressingMode)((hw.cpu_regs.Operand >> 8) & 0x07);

                    // Displacement
                    // Bit 7-0
                    var mriDisplacement = (sbyte)(hw.cpu_regs.Operand & 0xFF);
                    //int tmpP = hw.alu.ReadRegisterE(RegisterEnum.P) + mriDisplacement;
                    int tmpP = hw.mac.fetch_address + mriDisplacement;
                    hw.idb.Latch((ushort)tmpP);

                    // Update P in ALU  ?
                    //hw.alu.WriteRegisterE(RegisterEnum.P, (ushort)tmpP);

                    //Debug.Assert(false); // This needs to be validated

                    // Fix indirect read
                    //var newAddress = CalcEA(adressingmode, mriDisplacement);                    
                    //hw.alu.WriteRegisterE(RegisterEnum.P, newAddress);  
                    //Debug.Assert(false); // What is this ?? LA(10-15) + CA (0-9) - update P in ALU
                }
                break;
                case IDBSource.RINR: // Read installation number
                    hw.idb.Latch(INSTALLATION_NUMBER);
                    break;
                case IDBSource.PICMASK:
                    // In ND-120 read PIC mask register
                    if (cpuType == CPUType.ND120)
                        hw.idb.Latch(hw.pic.ExecuteReadMask());
                    break;
                case IDBSource.UART:
                    // In ND-120 read UART
                    if (cpuType == CPUType.ND120)
                    {
                        // See also decode 5,x UART
                        hw.idb.Latch(hw.uart2661.Read(microCode.MIS));
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// See Manual ND.06.014, Page 34
        /// </summary>
        /// <param name="mode">
        /// The addressing mode from the instruction word.
        /// </param>
        /// <param name="mriDisplacement">
        /// The displacement field of the memory reference instruction.
        /// </param>
        internal ushort CalcEA(AdressingMode mode, int mriDisplacement)
        {


            PageTable calc_pageTable = PageTable.StandardPageTable;

            //int PC = hw.alu.ReadRegisterE(RegisterEnum.P);

            uint PC = hw.mac.fetch_address;
            uint EffectiveAddress = PC;


            switch (mode)
            {
                case AdressingMode.RelativeAdressing: /* (P) + disp */
                    EffectiveAddress = (uint)((PC + mriDisplacement) & 0xFFFF);
                    calc_pageTable = PageTable.StandardPageTable;
                    break;
                case AdressingMode.BrelativeAddressing:  /* (B) + disp */
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.B) + mriDisplacement) & 0xFFFF);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.IndirectPrelativeAddressing:  /* ((P) + disp) */
                    EffectiveAddress = (uint)((PC + mriDisplacement) & 0xFFFF);
                    EffectiveAddress = (uint)ReadIndirectVirtualMemory(EffectiveAddress, PageTable.StandardPageTable);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.IndirectBrelativeAddressing:  /* ((B) + disp) */
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.B) + mriDisplacement) & 0xFFFF);
                    EffectiveAddress = (uint)ReadIndirectVirtualMemory(EffectiveAddress, PageTable.AlternativePageTable);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.XrelativeAddressing: /* (X) + disp */
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.X) + mriDisplacement) & 0xFFFF);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.BrelativeIndexedAddressing: // (B) + disp + (X)  => Example: LDA ,B,X
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.B) + hw.alu.ReadRegisterE(RegisterEnum.X) + mriDisplacement) & 0xFFFF);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.IndirectPrelativeIndexedAddressing: /* ((P) + disp) + (X) */
                    EffectiveAddress = (uint)((PC + mriDisplacement) & 0xFFFF);
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.X) + ReadIndirectVirtualMemory(EffectiveAddress, PageTable.StandardPageTable)) & 0xFFFF);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.IndirectBrelativeIndexedAddressing:
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.B) + mriDisplacement) & 0xFFFF);
                    EffectiveAddress = (uint)((hw.alu.ReadRegisterE(RegisterEnum.X) + ReadIndirectVirtualMemory(EffectiveAddress, PageTable.AlternativePageTable)) & 0xFFFF);
                    calc_pageTable = PageTable.AlternativePageTable;
                    break;
                case AdressingMode.Undefined:
                    EffectiveAddress = 0;
                    break;
            }
            hw.cpu_regs.lastPT = calc_pageTable;
            return (ushort)EffectiveAddress;
        }

        /// <summary>
        /// Read via page table.. maybe need to implement this ?
        /// </summary>
        /// <param name="virtualAddress">
        /// The virtual address to read.
        /// </param>
        /// <param name="pt">
        /// The page table to translate through.
        /// </param>
        /// <returns>
        /// The word read, or -1 when the translation failed.
        /// </returns>
        private int ReadIndirectVirtualMemory(uint virtualAddress, PageTable pt)
        {
            uint pm = memory.MapVirtualToPhysical(virtualAddress, AccessMode.READ, pt);
            return memory.ReadPhysicalMemory(pm);
        }




        #region Public API for reading and writing registers (for debugging)

        /// <summary>
        /// Reads a register of the current program level, for the debugger and the tests.
        /// </summary>
        /// <param name="reg">
        /// The register.
        /// </param>
        /// <param name="doLog">
        /// False suppresses the debug log entry.
        /// </param>
        /// <returns>
        /// The register value.
        /// </returns>
        public ushort ReadALURegister(RegisterEnum reg, bool doLog = true)
        {
            return hw.alu.ReadRegister((ushort)reg, doLog);
        }

        /// <summary>
        /// Writes a register of the current program level, for the debugger and the tests.
        /// </summary>
        /// <param name="reg">
        /// The register.
        /// </param>
        /// <param name="value">
        /// The new value.
        /// </param>
        public void WriteALURegister(RegisterEnum reg, ushort value)
        {
            hw.alu.WriteRegister((ushort)reg, value);

            if (reg == RegisterEnum.P)
                hw.cpu_regs.P_was_updated = true;
        }




        #endregion Public API for reading registers (for debugging)


#if _ND100_EMU_
        /// <summary>
        /// Map a 16 bit virtual address to a 16, 19 or 24 bit physical address depending on configuration.
        /// Also does memory protection
        /// </summary>
        /// <param name="virtualAddress">A 6 bit virtual address</param>
        /// <param name="pt">Selects adress mapping</param>
        /// <returns>Up to 24-bit phyical address</returns>
        public int MapVirtualToPhysical(int virtualAddress, AccessMode am = AccessMode.READ, PageTable pt = PageTable.StandardPageTable)
        {

            virtualAddress = virtualAddress & 0xFFFF; // Make sure its no more than 16-bits
            byte pageTable = 0;

            // Read PCR for the current level and calculate the ring we are executing the code under.
            ushort pcr = regs.PCR[regs.PIL]; // Read out the Page Control Register for the current level
            byte ring = (byte)(pcr & 0x03);  // 2 lower bits of the PCR is the Ring the current level is using


            // Ring 3 is the most powerfull, and for Ring 3 RAM will always be in the shadow of PageTable RAM
            if ((ring == 3) && (IsAddressShadowMemory(virtualAddress, false)))
            {
                return virtualAddress; // Read/WritePhysical will handle the actual access to shadow memory
            }

            /*

			The paging system maps the 16-bit virtual address from the address arithmetic into a physical address.
			The number of bits in the physical address depends on whether the "normal" (19-bit NORD10/S compatible) mode is used.
			If paging isnt enabed, virtual address maps directly to physical address - a 16 bit address


			16-bit Virtual Address
			+----------+-------+
			| 15 -  10 | 9  - 0|
			+----------+-------+
			|   VPN    | DIP   |
			+----------+-------+

			To address any location within a 1K address space, 10 address bits are required.
			These bits are refered as the displacement within a page (DIP).
			The most significant part of the virtual address (bits 10-15) us used as an address slection on the 64 locations in the page table.
			These six bits are referred to as the Virtual Page Number (VPN)


			*/

            // If memory management is not enabled, don't use mapping (physical = virtual)
            if (regs.PONI == false)
                return (virtualAddress & 0xFFFF);


            // Calculate VPN and DIP
            int DIP = virtualAddress & 0x3FF; // lower 10 bites - Displacement
            int VPN = (virtualAddress >> 10) & 0x3f; // upper 6 bits - Virtual Page number

            PageTableMode ptm = PageTableMode.Four; // Default to four pagetables



            // Find PageTable Number and identify if we have the optional 16 page-table mode
            if ((regs.currentRegisters.STS.PTM) && (pt == PageTable.AlternativePageTable))
            {
                if ((pcr & 1 << 2) != 0 && (mmsType == MMSType.MMS2))
                {
                    // Sixteen page table mode
                    pageTable = (byte)((pcr >> 7) & 0xF); // AlternativePageTable - 16x
                    ptm = PageTableMode.Sixteen;
                }
                else
                {
                    // Four page table mode
                    pageTable = (byte)((pcr >> 7) & 0x03); // AlternativePageTable - 4x
                    ptm = PageTableMode.Four;
                }
            }
            else
            {
                if ((pcr & 1 << 2) != 0 && (mmsType == MMSType.MMS2))
                {
                    // Sixteen page table mode
                    pageTable = (byte)((pcr >> 11) & 0xF); // PageTable - 16x
                    ptm = PageTableMode.Sixteen;
                }
                else
                {
                    // Four page table mode
                    pageTable = (byte)((pcr >> 9) & 0x03); // PageTable - 4x
                    ptm = PageTableMode.Four;
                }
            }


            // Find the PageTableEntry, PTe
            uint pageTableEntry = regs.pagingTables.GetPageTableEntry(pageTable, VPN, regs.SEXI, ptm);


            // Check for page protection
            if (!checkPageProtection(VPN, pageTable, pageTableEntry, pt, am, virtualAddress))
            {
                // We should never get here, but added a return statement anyway! (Will end up here if interrupts are disabled?)
                return (virtualAddress & 0xFFFF);
            }


            byte pageTableRing = (byte)((pageTableEntry >> 25) & 0x03);
            // Degrade ?
            // If a program in ring 3 executes instructions assigned to rings 0,1 or 2, its ring number is reduced accordingly.
            // Such accessess are detected by hardware which automatically changes the ring number in the PCR register for the current program level.
            // NB! This degrading only occurs when lower ring instruction codes are executed, but not when data is accessed
#if _DEGRADE_
			if ((am == AccessMode.FETCH) && (pageTableRing < ring) && (ring == 3)) // ?? => && (regs.PIL!=0))
			{

				if (Logger.IsLogEnabled(Logger.LogLevel.CPU))
					Logger.Log($"[{regs.PIL}] Ring degrading. Ring={ring} PTRing={pageTableRing} Accessmode ={am} PGS={Numeric.Num2Str(regs.PGS)} PageTable={pageTable} VPN={VPN} PageTableEntry=0x{pageTableEntry:X8}", Logger.LogLevel.CPU);

				// Degrade!
				ring = pageTableRing;

				regs.PCR[regs.PIL] &= 0xFFFC; // Clear ring bits in PCR
				regs.PCR[regs.PIL] |= ring & 0x03; // Set bit 0-1 in PCR to the current ring level
			}
#endif



            #region Ring Protection Check
            // Check for Ring Protection
            // INFO: For the ND CPU Ring 3 is most powerfull, ring 0 least powerfull.
            // If the current level has a "ring level" that is smaller than the ring level on the page, generate a fault
            //
            // The ring bits of the appropriate PCR are compared with the ring bits of the appropriate page table entry.
            // The PCR ring bits should always be greater than or equal to the PT ring bits. If not, an internal interrupt (MPV) will be generated.
            if (!(ring >= pageTableRing))
            {
                // Update PGS
                UpdatePGS(pageTable, VPN, am);

                if (Logger.IsLogEnabled(Logger.LogLevel.Device))
                    Logger.Log($"[{regs.PIL}] Ring Protection Violation. Ring={ring} PTRing={pageTableRing} Accessmode={am} PGS={Numeric.Num2Str(regs.PGS)} PT={pageTable} VPN={VPN} PTe=0x{pageTableEntry:X8}", Logger.LogLevel.Device);

                InternalInterruptLvl14(1 << 2, pageTable, regs.PC); // Ring Protection Violation, will throw CPUException

                // We should never get here, but added a return statement anyway! (Will end up here if interrupts are disabled?)
                return (virtualAddress & 0xFFFF);
            }
            #endregion Ring Protection Check



            // Map to physical page			
            ushort PPN = 0;
            if (regs.SEXI)
            {
                // Use lower 14-bit
                PPN = (ushort)(pageTableEntry & 0x3FFF);
            }
            else
            {
                // "normal" mode, use only the lower 9-bits
                PPN = (ushort)(pageTableEntry & 0x1FF);
            }

            // Calculate physical address
            int physicalAddress = (PPN << 10) | DIP;

            bool outOfRange = false;


            if (physicalAddress > MAX_MEMORY)
            {
                Debug.Assert(false); // Should be impossible, there must be something wrong with the content in the pageing tables
                outOfRange = true;
            }

            if (!outOfRange)
            {
                // Check if memory is "out of range"
                try
                {
                    int tmp = Memory.ReadMemoryW(physicalAddress << 1); // Underlying memory object uses bytes. ND Memory is 16-bits wide so we need to compensate for that with *2 or <<1
                }
                catch (Exception)
                {

                    outOfRange = true;
                }
            }

            if (outOfRange)
            {
                UpdatePGS(pageTable, VPN, am);

                if (Logger.IsLogEnabled(Logger.LogLevel.Device))
                    Logger.Log($"[{regs.PIL}] Memory out of range. Ring={ring} PTRing={pageTableRing} Accessmode ={am} PGS={Numeric.Num2Str(regs.PGS)} PageTable={pageTable} PageTableAddress={Numeric.Num2Str(VPN)} PageTableEntry=0x{pageTableEntry:X8}", Logger.LogLevel.Device);

                int PES = (ushort)(physicalAddress & 0xFFFF); // Lower 16 bits of physical memory
                int PEA = (ushort)(physicalAddress >> 16 & 0xFF); // Upper 8 bits of physical memory

                // Set and LOCK regs
                regs.SetPES((ushort)PES);
                regs.SetPEA((ushort)PEA);

                InternalInterruptLvl14(1 << 9, regs.PC, physicalAddress); //Interrupt Level 9=MOR (Memory out of range)
                return -1; // out of range!
            }
            else
            {
                // Mark page used
                pageTableEntry = regs.pagingTables.SetPageUsed(pageTable, VPN, regs.SEXI, ptm, pageTableEntry);
                if (am == AccessMode.WRITE)
                {
                    // Mark page as "written to" (so SINTRAN knows it need to store it to disk before removing page from memory)
                    pageTableEntry = regs.pagingTables.SetPageWritten(pageTable, VPN, regs.SEXI, ptm, pageTableEntry);
                }
            }

            // ECC Memory Parity Interrupt enabled?
            if ((regs.ECCR & 1 << 3) != 1) // If 1, error correction and parity error interrupt is disabled.
            {
                int eccBits = 0;

                if ((regs.ECCR & 1 << 0) != 0) // Simulate memory error in bit 0
                    eccBits++;

                if ((regs.ECCR & 1 << 1) != 0) // Simulate memory error in bit 15
                    eccBits++;

                if ((regs.ECCR & 1 << 4) != 0) // Simulate memory error in bit 6
                    eccBits++;

                if (eccBits > 0)
                {
                    //if (
                    //		(eccBits > 1) || // more than 1 bit error .. or 
                    //		((eccBits == 1) && ((regs.ECCR & 1 << 2) == 1)) // ECCR allows interrupt on only 1-bit error
                    //		)
                    {

                        int PES = (ushort)(physicalAddress & 0xFFFF); // Lower 16 bits of physical memory
                        int PEA = (ushort)(physicalAddress >> 16 & 0xFF); // Upper 8 bits of physical memory
                        int errorCode = 0;


                        if (eccBits == 1)
                        {
                            // Single bit, error table  Figure 2.18, page 2-51 in ND-06.014.02
                            if ((regs.ECCR & 1 << 0) != 0) // Simulate memory error in bit 0
                                errorCode = 3;

                            if ((regs.ECCR & 1 << 1) != 0) // Simulate memory error in bit 15
                                errorCode = 0x1C; // o34

                            if ((regs.ECCR & 1 << 4) != 0) // Simulate memory error in bit 6
                                errorCode = 0x0D; // o15

                            // Set error code
                            PES |= errorCode << 8;
                        }
                        else
                        {
                            // Multiple bits
                            PES |= 1 << 13; // FATAL ERROR
                                            // no error code as its multiple bits							
                        }



                        if (am == AccessMode.FETCH)
                        {
                            PES |= 1 << 15; // Error during fetch (or DEPOSIT ot EXAM)
                        }

                        // When the error condition occurs, the content of PES and PEA is locked and not released until TRA PEA is executed.
                        // These registers do not contain correct information unless
                        // an internal interrupt with code 10 or 11(parity error and memory out of range) is detected


                        // Set and LOCK regs
                        regs.SetPES((ushort)PES);
                        regs.SetPEA((ushort)PEA);

                        Logger.Log($"Memory Parity Fault! Bits={eccBits} ErrorCode={errorCode} VirtualAddress={Numeric.Num2Str(virtualAddress)} PT={pageTable} VPN={VPN} PTe={pageTableEntry:X8} Accessmode={am} PGS={Numeric.Num2Str(regs.PGS)}", Logger.LogLevel.Device);
                        InternalInterruptLvl14(1 << 8, pageTable, regs.PC); // PTY - Memory parity Fault										
                    }
                }
            }


            // Return physical address
            return physicalAddress;
        }

#endif  // _ND100_EMU_

    }



}

