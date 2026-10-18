//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

namespace NDMicroCpu.Cli
{
    /// <summary>
    /// Minimal microcode symbol table: maps a control-store address to a human label, used to
    /// annotate pseudo-C jump targets. Loadable from a DELILAH-L/RASK `.SYMBOLS.TXT` file (lines
    /// of the form `LABEL=oooooo` or `oooooo=LABEL`, octal), or left empty (addresses shown raw).
    /// </summary>
    public class SymbolTable
    {
        private readonly Dictionary<ushort, string> _byAddress = new();

        /// <summary>
        /// Number of symbols.
        /// </summary>
        public int Count => _byAddress.Count;

        /// <summary>
        /// The label at a micro address.
        /// </summary>
        /// <param name="address">
        /// The micro address.
        /// </param>
        /// <returns>
        /// The label, or null when none is known.
        /// </returns>
        public string? GetSymbol(ushort address)
            => _byAddress.TryGetValue(address, out string? s) ? s : null;

        /// <summary>
        /// Adds or replaces the label at a micro address.
        /// </summary>
        /// <param name="address">
        /// The micro address.
        /// </param>
        /// <param name="label">
        /// The label.
        /// </param>
        public void Add(ushort address, string label) => _byAddress[address] = label;

        /// <summary>
        /// Load labels from a symbols file. Accepts `LABEL=oooooo` and `oooooo=LABEL` (octal
        /// address). Non-matching lines are ignored. Returns the number of symbols loaded.
        /// </summary>
        public int LoadFromFile(string path)
        {
            if (!File.Exists(path)) return 0;
            int loaded = 0;
            string[] rawLines = File.ReadAllLines(path);
            for (int lineIdx = 0; lineIdx < rawLines.Length; lineIdx++)
            {
                string line = rawLines[lineIdx].Trim();
                if (line.Length == 0 || line[0] == '%' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1) continue;

                string left = line.Substring(0, eq).Trim();
                string right = line.Substring(eq + 1).Trim();

                // Try octal on each side; the other side is the label.
                if (TryOctal(right, out ushort addrR)) { _byAddress[addrR] = left; loaded++; }
                else if (TryOctal(left, out ushort addrL)) { _byAddress[addrL] = right; loaded++; }
            }
            return loaded;
        }

        private static bool TryOctal(string s, out ushort value)
        {
            value = 0;
            if (s.Length == 0) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '7') return false;
            try { value = Convert.ToUInt16(s, 8); return true; }
            catch { return false; }
        }
    }
}
