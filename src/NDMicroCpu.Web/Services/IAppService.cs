//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MudBlazor;
using MudBlazor.Charts;
using NDMicroCpu.Core;
using NDMicroCpu.Web.Objects;

namespace NDMicroCpu.Web.Services
{
    /// <summary>
    /// The state the web emulator shares between its pages: the CPU, its memory, the console text, the log and the breakpoints, and the machine loop that drives them.
    /// </summary>
    public class IAppService
    {
        internal CPU _cpu = null!;   // BuildCpu in the constructor always sets it
        /// <summary>
        /// The memory of the emulated machine.
        /// </summary>
        public Memory memory;


        /// <summary>
        /// Creates the service with a fresh memory and a CPU of the default type.
        /// </summary>
        public IAppService()
        {
            memory = new Memory();

            // Build the initial CPU as the default type (ND-110). LoadCpuAsync() can later rebuild
            // it for a different type when the operator switches in Preferences.
            BuildCpu(_selectedCpuType);
        }

        // --- CPU type selection (drives which microcode ROM + listing the whole app uses) ---

        private NDMicroCpu.Core.Enums.CPUType _selectedCpuType = NDMicroCpu.Core.Enums.CPUType.ND110;
        /// <summary>Which CPU the emulator is currently running (ND-110 or ND-120).</summary>
        public NDMicroCpu.Core.Enums.CPUType SelectedCpuType => _selectedCpuType;

        /// <summary>Raised after a CPU switch has (re)loaded the microcode + listing, so pages that
        /// show CPU-dependent state (the microcode editor/debugger) can refresh to the new machine.</summary>
        public event Action? OnCpuChanged;

        /// <summary>The wwwroot/Assets file names (compiled microcode + listing) for a CPU type.
        /// Single source of truth so nothing is hardcoded per call site.</summary>
        private static (string code, string listing) AssetsFor(NDMicroCpu.Core.Enums.CPUType t)
            => t == NDMicroCpu.Core.Enums.CPUType.ND120
                ? ("ND-120-DELILAH-L-CODE.TXT", "ND-120-DELILAH-L-LISTING.TXT")
                : ("ND-110-RASK-CODE.TXT", "ND-110-RASK-LISTING.TXT");

        /// <summary>(Re)create the CPU for the given type and wire it to memory + console output.
        /// The type is baked into the CPU/CpuInternals at construction, so a switch is a rebuild.</summary>
        private void BuildCpu(NDMicroCpu.Core.Enums.CPUType type)
        {
            _selectedCpuType = type;
            _cpu = new NDMicroCpu.Core.CPU(memory, type);
            memory.AttachCPU(_cpu);

            // ND-110 console is hw.uart; ND-120 console is the SCN2661 (hw.uart2661). Wire whichever
            // this CPU type actually has so terminal output reaches the console text.
            if (type == NDMicroCpu.Core.Enums.CPUType.ND110)
            {
                if (_cpu.hw.uart != null) _cpu.hw.uart.OnTextOutput += Uart_OnTextOutput;
            }
            else
            {
                if (_cpu.hw.uart2661 != null) _cpu.hw.uart2661.OnTextOutput += Uart_OnTextOutput;
            }
        }
        #region Properties for keeping state when navigating pages

        /// <summary>
        /// Current MicroCode memory address
        /// </summary>
        public int memAddress { get; set; } = 0;

        #endregion

        /// <summary>
        /// Is set to TRUE after source code and microcode has been downloaded  - and the appService object is initialized
        /// </summary>
        bool _isInitalized = false;
        /// <summary>
        /// True once the micro code and its listing have been fetched and the CPU is ready.
        /// </summary>
        public bool IsInitialized { get => _isInitalized; }

        /// <summary>
        /// Semaphore to ensure InitializeAsync is only executed once even if called from multiple pages simultaneously
        /// </summary>
        readonly SemaphoreSlim _initializationSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// HTTP server address
        /// </summary>
        public string _baseAddress = "";

        #region CPU objects


        /// <summary>
        /// The emulated CPU.
        /// </summary>
        public CPU Cpu { get => _cpu; }

        string _consoleText = "";
        /// <summary>
        /// The text the console has printed, trimmed to the last lines.
        /// </summary>
        public string ConsoleText
        {
            get => _consoleText;
            set
            {
                _consoleText = TruncateConsoleText(value);
            }
        }

        /// <summary>
        /// True while the micro code log is collected into <see cref="cpuLog"/>.
        /// </summary>
        public bool LoggingEnabled { get; set; }
        /// <summary>
        /// The most recent log lines from the CPU, at most 100.
        /// </summary>
        public List<string> cpuLog = new();

        /// <summary>
        /// The breakpoints the debugger pages set.
        /// </summary>
        public BreakPoints BreakPoints { get; private set; } = new BreakPoints();
        #endregion

        /// <summary>
        /// The MicroCode source code
        /// Will be loaded during initialisation
        /// </summary>
        public string? microCodeSource { get; set; }

        /// <summary>
        /// Dictionary that keep information on microcode addresses and its corresponding lines in the sourcecode
        /// </summary>
        public Dictionary<int, MicroCodeLineInfo>? microCodeLines { get; set; }

        /// <summary>
        /// The compiled microcode that is loaded during initialisation
        /// </summary>
        public byte[]? microCode { get; set; }


        /// <summary>
        /// Initialize the appService object - called from any page
        /// This method is thread-safe and will only execute once even if called from multiple pages simultaneously
        /// </summary>
        /// <returns></returns>
        public async Task InitializeAsync()
        {
            // Fast path: if already initialized, return immediately without taking the lock
            if (_isInitalized) return;

            // Acquire semaphore to ensure only one thread initializes
            await _initializationSemaphore.WaitAsync();
            try
            {
                // Double-check after acquiring lock (another thread might have initialized while we waited)
                if (_isInitalized) return;

                // Load the default CPU's microcode + listing. The Preferences selector may switch to
                // a saved CPU type afterwards via LoadCpuAsync (see the component that reads localStorage).
                await LoadCpuAsync(_selectedCpuType);
                _isInitalized = true;
            }
            finally
            {
                _initializationSemaphore.Release();
            }
        }

        /// <summary>
        /// Switch the whole emulator to the given CPU type: rebuild the CPU, then fetch and load THAT
        /// type's compiled microcode (ROM) and its listing (for the debugger source view). Replaces the
        /// old hardcoded double-load: the CPU constructor's LoadCS() reads a local FILE which does not
        /// exist under WASM, so the real load has always been this byte-array path - now it is the ONLY
        /// path and it is CPU-type driven. Powers the machine off first (a running ticker must not tick
        /// a half-swapped CPU). Safe to call at startup and on every later switch.
        /// </summary>
        public async Task LoadCpuAsync(NDMicroCpu.Core.Enums.CPUType type)
        {
            if (string.IsNullOrEmpty(_baseAddress)) return;

            // A switch while running would tick a half-built CPU - stop the machine first.
            SetPower(false);

            // Rebuild the CPU for this type. At first init the constructor already built the default
            // type, so skip the rebuild then (avoids a redundant CPU + double UART wiring).
            bool alreadyBuilt = _selectedCpuType == type && !_isInitalized;   // the constructor always builds _cpu
            if (!alreadyBuilt)
                BuildCpu(type);

            (string codeAsset, string listingAsset) = AssetsFor(type);
            HttpClient http = new HttpClient { BaseAddress = new Uri(_baseAddress) };

            // Listing (text) -> debugger source view + address->line map.
            var listingResp = await http.GetAsync($"Assets/{listingAsset}");
            listingResp.EnsureSuccessStatusCode();
            microCodeSource = await listingResp.Content.ReadAsStringAsync();
            BuildMicroCodeLines();

            // Compiled microcode (bytes) -> control store. The bool is the "is ND-110" flag the loader
            // uses for the (slightly different) ND-110 vs ND-120 word packing.
            var codeResp = await http.GetAsync($"Assets/{codeAsset}");
            codeResp.EnsureSuccessStatusCode();
            microCode = await codeResp.Content.ReadAsByteArrayAsync();
            _cpu.hw.cs.LoadFromByteArray(microCode, type == NDMicroCpu.Core.Enums.CPUType.ND110);

            // Notify CPU-dependent views (microcode editor/debugger) that the machine changed so
            // they re-render against the newly loaded listing + control store.
            OnCpuChanged?.Invoke();
        }

        /// <summary>Parse microCodeSource into the address-&gt;line map used by the debugger. Same column
        /// layout for the RASK and DELILAH-L listings (line number in cols 0-4, octal CS address 6-12).</summary>
        private void BuildMicroCodeLines()
        {
            microCodeLines = new Dictionary<int, MicroCodeLineInfo>();
            if (string.IsNullOrEmpty(microCodeSource)) return;

            string[] lines = microCodeSource.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                // Drop any trailing comment before column parsing.
                int idxComment = line.IndexOf("%");
                if (idxComment >= 0) line = line.Substring(0, idxComment);

                if (line.Length < 13) continue; // too short to hold line-number + address columns

                int microCodeAddress = SafeParse(line.Substring(6, 7), true);
                int lineNumber = SafeParse(line.Substring(0, 5));
                if (microCodeAddress < 0 || lineNumber < 0) continue;

                if (microCodeLines.TryGetValue(microCodeAddress, out MicroCodeLineInfo? info))
                    info.LastLine = lineNumber;
                else
                    microCodeLines[microCodeAddress] = new MicroCodeLineInfo(lineNumber, lineNumber);
            }
        }

        /// <summary>
        /// Fetch a .BPUN image from wwwroot/Assets and plug it onto the bus at the given device
        /// number (0x370 = 1560 floppy, 0x100 = 400 paper tape), WITHOUT booting it - the "insert
        /// the disk" step. Booting happens from the Terminal page: the CPU only ticks while the
        /// machine is powered on there, so the operator powers on, waits for the OPCOM "#" prompt,
        /// and types the load command ("1560$" / "400$") to stream the image in and run it.
        /// Returns false if the asset could not be fetched.
        /// </summary>
        public async Task<bool> AttachBpunAssetAsync(string assetName, int deviceBase)
        {
            if (string.IsNullOrEmpty(_baseAddress)) return false;

            HttpClient http = new HttpClient { BaseAddress = new Uri(_baseAddress) };
            var resp = await http.GetAsync($"Assets/{assetName}");
            if (!resp.IsSuccessStatusCode) return false;
            byte[] image = await resp.Content.ReadAsByteArrayAsync();

            AttachBinaryLoadDevice(image, deviceBase);
            return true;
        }

        /// <summary>
        /// Fetch a .BPUN from wwwroot/Assets and boot it in one shot from any page: attach it,
        /// power on, and auto-type the load command once OPCOM is ready (see PowerOnAndBoot).
        /// Returns false if the asset could not be fetched.
        /// </summary>
        public async Task<bool> PowerOnAndBootAssetAsync(string assetName, int deviceBase)
        {
            if (string.IsNullOrEmpty(_baseAddress)) return false;

            HttpClient http = new HttpClient { BaseAddress = new Uri(_baseAddress) };
            var resp = await http.GetAsync($"Assets/{assetName}");
            if (!resp.IsSuccessStatusCode) return false;
            byte[] image = await resp.Content.ReadAsByteArrayAsync();

            PowerOnAndBoot(image, deviceBase);
            return true;
        }

        /// <summary>
        /// Plug a binary-load image onto the bus at the given device number, WITHOUT booting it -
        /// the "insert the disk / mount the tape" step. The operator then powers on / boots and
        /// types the load command at the terminal themselves ("1560$" for a floppy at 1560, "400$"
        /// for the paper-tape reader at 400). Use <see cref="PowerOnAndBootAssetAsync"/> for the one-shot
        /// insert-and-boot instead. deviceBase is the octal device number as an int
        /// (0x370 = 1560 floppy, 0x100 = 400 paper tape).
        /// </summary>
        public void AttachBinaryLoadDevice(byte[] image, int deviceBase)
        {
            Cpu.iobus.Attach(new NDMicroCpu.Core.NDBus.NdBinaryLoadDevice(image, deviceBase));
        }

        #region Machine power + global CPU ticker (so boot works from ANY page)

        // The CPU tick loop lived in the Terminal page, so the machine only ran while that page
        // was open. It lives here now (this service is a DI singleton), so power + ticking are
        // global: a "boot" button on any page can power the machine on and it keeps running as
        // you navigate. The Terminal page just observes this via OnMachineBatch.
        private System.Timers.Timer? _cpuLoop;
        // Ticks executed per timer fire. This is the emulator's throughput lever: browsers throttle
        // timer callbacks (down to ~1/s for background/hidden tabs), so a small batch starves the
        // CPU - e.g. the ND-120 BYTE-STRING (BFILL/MOVB) tests run tens/hundreds of millions of
        // silent cycles and appeared to HANG for minutes. A big batch does real work per fire
        // regardless of timer frequency; the UI stays responsive because we only re-render on
        // console change (see CpuLoop_Elapsed). ~100k/fire keeps each batch short (~10-30ms) so the
        // UI stays responsive, while doing far more work per timer fire than the old 30k.
        /// <summary>
        /// How many CPU ticks one timer fire runs before the page gets a chance to update.
        /// </summary>
        public const int CPU_TICKS_PER_BATCH = 100000;

        private bool _powerOn;
        /// <summary>Is the machine powered on (CPU ticking)?</summary>
        public bool PowerOn => _powerOn;

        /// <summary>Raised after each CPU tick batch so any view (e.g. the Terminal) can refresh.</summary>
        public event Action? OnMachineBatch;

        // Pending auto-boot: once the machine reaches its OPCOM wait state after power-on, the
        // queued load command ("1560$" / "400$") is typed for the operator.
        private bool _bootPending;
        private string? _bootCommand;
        private bool _reachedOpcomSincePower;

        private void EnsureTicker()
        {
            if (_cpuLoop == null)
            {
                _cpuLoop = new System.Timers.Timer(2); // 2 ms between batches once running
                _cpuLoop.AutoReset = true;
                _cpuLoop.Elapsed += CpuLoop_Elapsed;
            }
        }

        // Micro-cycle wall-clock budget per timer fire. We run the CPU for up to this many
        // milliseconds, then yield to the browser (render/input). TIME-BOUNDED (not a fixed tick
        // count) so throughput self-tunes to the actual WASM speed: a small fixed batch gets
        // throttled by the browser's timer rate; a big fixed batch blocks input. ~12 ms keeps the
        // UI responsive (well under a frame budget) while running as many ticks as the machine can.
        private const int BATCH_BUDGET_MS = 12;
        private const int TICKS_BETWEEN_CLOCK_CHECKS = 4000;

        private void CpuLoop_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            // Disable while we run a batch so a long batch can't re-enter (same guard the old
            // Terminal timer used).
            _cpuLoop!.Enabled = false;
            if (_hdSw == null) _hdSw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                if (_cpu != null && _powerOn)
                {
                    long endMs = _hdSw.ElapsedMilliseconds + BATCH_BUDGET_MS;
                    do
                    {
                        for (int i = 0; i < TICKS_BETWEEN_CLOCK_CHECKS; i++) _cpu.TickCpu();
                        _totalTicks += TICKS_BETWEEN_CLOCK_CHECKS;

                        // At OPCOM once self-test completed and reached a macro FETCH.
                        if (!_reachedOpcomSincePower && _cpu.hw.mac.FETCH)
                            _reachedOpcomSincePower = true;

                        // Fire a queued boot once OPCOM is ready (type the load command).
                        if (_bootPending && _reachedOpcomSincePower)
                        {
                            _bootPending = false;
                            string cmd = _bootCommand ?? "1560$";
                            for (int c = 0; c < cmd.Length; c++) _cpu.hw.AddConsoleChar(cmd[c]);
                        }
                    }
                    while (_hdSw.ElapsedMilliseconds < endMs && _powerOn);
                }
            }
            catch (Exception ex)
            {
                // Never let a TickCpu exception escape the timer callback: if it did, the timer
                // would stay disabled and the machine would silently FREEZE. Surface it and carry on.
                Console.Error.WriteLine($"[cpu] TickCpu exception (recovered): {ex.GetType().FullName}: {ex.Message}");
            }
            finally
            {
                // ALWAYS re-arm the timer, even after an exception (the freeze bug).
                _cpuLoop!.Enabled = _powerOn;
            }

            // COALESCE renders to a fixed ~10/sec, DECOUPLED from CPU speed and output volume.
            // Re-rendering per timer fire (or per console change) let the Terminal re-render ~70x/sec
            // - the renderer (a textarea bound to the whole growing console string) can't keep up, it
            // backs up, and the whole machine crawls (this is what made ND-120 BYTE-STRING look HUNG:
            // the banner alone took ~40s). 10 renders/sec is smooth to the eye and near-zero overhead.
            long nowMs = _hdSw.ElapsedMilliseconds;
            if ((nowMs - _lastRenderMs) >= 100)
            {
                _lastRenderMs = nowMs;
                OnMachineBatch?.Invoke();
            }

            // Update the rolling CPU throughput (~1/sec) that the Terminal shows as a speed readout,
            // so a long silent computation (e.g. the ND-120 BYTE-STRING tests, ~34M cycles / minutes)
            // is visibly RUNNING rather than looking hung.
            if (_cpu != null && nowMs - _lastHbMs >= 1000)
            {
                long dTicks = _totalTicks - _lastHbTicks;
                long dMs = nowMs - _lastHbMs; if (dMs < 1) dMs = 1;
                CpuTicksPerSecond = dTicks * 1000 / dMs;
                _lastHbMs = nowMs; _lastHbTicks = _totalTicks;
            }
        }

        private System.Diagnostics.Stopwatch? _hdSw;
        private long _totalTicks = 0;
        private long _lastRenderMs = 0;
        private long _lastHbMs = 0;
        private long _lastHbTicks = 0;

        /// <summary>Rolling CPU throughput (micro-cycles/sec), updated ~once per second while running.
        /// Shown in the Terminal so a long silent run is visibly progressing, not hung.</summary>
        public long CpuTicksPerSecond { get; private set; }

        /// <summary>Turn machine power on/off. On = master clear + start the global ticker.</summary>
        public void SetPower(bool on)
        {
            _powerOn = on;
            if (on)
            {
                _reachedOpcomSincePower = false;
                _cpu.ResetCpu();
                _consoleText += "POWER ON\r\n";
                EnsureTicker();
                _cpuLoop!.Start();
            }
            else
            {
                _consoleText += "POWER OFF\r\n";
                _cpuLoop?.Stop();
            }
        }

        /// <summary>Master-clear the running machine back to OPCOM (the panel Reset button).</summary>
        public void ResetMachine()
        {
            _reachedOpcomSincePower = false;
            _cpu.ResetCpu();
            _consoleText += "Reset\r\n";
        }

        /// <summary>
        /// Boot an image from ANY page: attach it onto the bus, power the machine on (master clear
        /// -> self-test -> OPCOM), and once OPCOM is ready automatically type the load command
        /// ("1560$" for a floppy at 1560, "400$" for the paper-tape reader at 400). The operator
        /// can watch it on the Terminal page. If the machine is already on, this re-master-clears
        /// it so the load starts from a clean OPCOM prompt.
        /// </summary>
        public void PowerOnAndBoot(byte[] image, int deviceBase)
        {
            AttachBinaryLoadDevice(image, deviceBase);
            _bootCommand = deviceBase == 0x100 ? "400$" : "1560$";
            _bootPending = true;
            SetPower(true); // resets to a fresh OPCOM and starts (or restarts) the ticker
        }

        #endregion Machine power + global CPU ticker

        private void Uart_OnTextOutput(char out_char)
        {
            byte b = (byte)out_char;
            //if ((b >= 31) || (b == 13) || (b == 10)) // filter none printable
            {
                _consoleText += out_char;
                _consoleText = TruncateConsoleText(_consoleText);
            }
        }

        /// <summary>
        /// Keeps only the last lines of the console text so the page stays fast.
        /// </summary>
        /// <param name="consoleText">
        /// The whole console text.
        /// </param>
        /// <returns>
        /// The text with the oldest lines dropped.
        /// </returns>
        public string TruncateConsoleText(string consoleText)
        {
            // Define the maximum number of "\r\n" occurrences allowed
            int maxNewLines = 24;

            // Split the string into an array of lines using "\r\n" as the delimiter
            string[] lines = consoleText.Split(new[] { "\r\n" }, StringSplitOptions.None);

            // Check if the number of lines exceeds the maximum allowed
            if (lines.Length > maxNewLines)
            {
                // Remove excess lines from the beginning of the array
                int linesToRemove = lines.Length - maxNewLines;
                string[] kept = new string[maxNewLines];
                Array.Copy(lines, linesToRemove, kept, 0, maxNewLines);
                lines = kept;
            }

            // Join the remaining lines back together with "\r\n" as the separator
            string truncatedText = string.Join("\r\n", lines);

            return truncatedText;
        }

        /// <summary>
        /// Helper function to parse octal-string into an int
        /// </summary>
        /// <param name="strInt"></param>
        /// <param name="isOct"></param>
        /// <returns></returns>
        private int SafeParse(string strInt, bool isOct = false)
        {
            strInt = strInt.Trim();
            try
            {

                if (isOct)
                {
                    return Utils.Oct2Int(strInt);
                }
                else
                {
                    if (int.TryParse(strInt, out int value))
                        return value;
                }
            }
            catch (Exception)
            {
                // just return -1 below
            }

            return -1;
        }

        /// <summary>
        /// Add log to the MicroCode debugger log
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="eol"></param>
        public void LogMsg(string msg, bool eol)
        {
            if (!LoggingEnabled) return;

            // Just keep the last 100 log lines
            while (cpuLog.Count > 100)
                cpuLog.RemoveAt(0);

            cpuLog.Add(msg);
        }
    }


}
