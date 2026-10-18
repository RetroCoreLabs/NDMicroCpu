//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/NDMicroCpu
// NDMicroCpu — Running yesterday's micro code on today's machines
//

using NUnit.Framework;

namespace NDMicroCpu.Core.Tests
{
    /// <summary>
    /// Where the tests find the files that live in the source tree: the micro code listings and
    /// symbol files, the BPUN test programs, and the folder the trace generators write to.
    /// </summary>
    /// <remarks>
    /// Everything is found by walking up from the test binary to the <c>src</c> folder, the one
    /// that holds <c>NDMicroCpu.slnx</c>. No path depends on where the repository was cloned.
    /// </remarks>
    public static class TestPaths
    {
        const string SolutionFile = "NDMicroCpu.slnx";

        /// <summary>
        /// The <c>src</c> folder of the repository.
        /// </summary>
        public static string SrcDir()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, SolutionFile)))
            {
                dir = Path.GetDirectoryName(dir);
            }
            Assume.That(dir, Is.Not.Null, $"{SolutionFile} not found above the test directory");
            return dir!;
        }

        /// <summary>
        /// <c>src\NDMicroCpu.Core\microcode</c>: the .CODE images with their .SYMBOLS.TXT and .LISTING.TXT files.
        /// </summary>
        public static string MicrocodeDir() => Path.Combine(SrcDir(), "NDMicroCpu.Core", "microcode");

        /// <summary>
        /// <c>src\NDMicroCpu.Machine\BPUN</c>: the BPUN test programs.
        /// </summary>
        public static string BpunDir() => Path.Combine(SrcDir(), "NDMicroCpu.Machine", "BPUN");

        /// <summary>
        /// <c>src\docs\traces</c>: where the [Explicit] trace generators write their reference traces.
        /// </summary>
        public static string TracesDir()
        {
            string dir = Path.Combine(SrcDir(), "docs", "traces");
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// A folder of this test project in the source tree, for golden files and captures kept beside the tests.
        /// </summary>
        /// <param name="subFolder">
        /// The folder under <c>src\tests\NDMicroCpu.Core.Tests</c>, for example <c>Boot</c>.
        /// </param>
        public static string TestsDir(string subFolder) => Path.Combine(SrcDir(), "tests", "NDMicroCpu.Core.Tests", subFolder);
    }
}
