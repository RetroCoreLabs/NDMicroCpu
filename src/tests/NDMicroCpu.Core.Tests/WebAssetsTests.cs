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
    /// The web emulator serves its own copies of the control store images and listings from
    /// wwwroot; they must be byte for byte the files in NDMicroCpu.Core\microcode, or the browser
    /// runs different micro code than the console and the tests. eng\sync-web-assets.ps1 refreshes them.
    /// </summary>
    [TestFixture]
    public class WebAssetsTests
    {
        static string WebAssets() => Path.Combine(TestPaths.SrcDir(), "NDMicroCpu.Web", "wwwroot", "Assets");

        [TestCase("ND-110-RASK.CODE", "ND-110-RASK-CODE.TXT")]
        [TestCase("ND-110-RASK.LISTING.TXT", "ND-110-RASK-LISTING.TXT")]
        [TestCase("ND-120-DELILAH-L.CODE", "ND-120-DELILAH-L-CODE.TXT")]
        [TestCase("ND-120-DELILAH-L.LISTING.TXT", "ND-120-DELILAH-L-LISTING.TXT")]
        public void WebAssetMatchesCoreMicrocode(string coreName, string webName)
        {
            string core = Path.Combine(TestPaths.MicrocodeDir(), coreName);
            string web = Path.Combine(WebAssets(), webName);
            Assert.That(File.Exists(core), Is.True, $"missing {core}");
            Assert.That(File.Exists(web), Is.True, $"missing {web} - run eng\\sync-web-assets.ps1");
            byte[] a = File.ReadAllBytes(core);
            byte[] b = File.ReadAllBytes(web);
            Assert.That(b.Length, Is.EqualTo(a.Length), $"{webName} differs in size from {coreName} - run eng\\sync-web-assets.ps1");
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    Assert.Fail($"{webName} differs from {coreName} at byte {i} - run eng\\sync-web-assets.ps1");
                }
            }
        }
    }
}
