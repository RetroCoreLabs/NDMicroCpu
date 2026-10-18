# Contributing to NDMicroCpu

Thanks for looking. This file says how to get a build running, what the code is expected to look
like, and what will get a change sent back.

---

## Getting a build

| # | Requirement | How to check |
|---|---|---|
| 1 | The .NET SDK version pinned in `src/global.json` | `dotnet --info` |
| 2 | PowerShell 7 or newer | `$PSVersionTable.PSVersion` |
| 3 | The `wasm-tools` workload, only to publish the web emulator | `dotnet workload list` |

```powershell
# from the repository root
.\src\build.ps1
```

`build.ps1` builds, runs the tests, and publishes every command-line program as a single
self-contained executable with no loose DLLs beside it and no .NET install needed on the target
machine. It **stops before publishing if any test fails**. Useful switches: `-Configuration Debug`,
`-SkipTests` when you know why, `-Clean`.

Everything the build needs is on nuget.org; there is no local package feed.

### The test suite

`dotnet test` runs 276 tests. The `Boot` fixtures boot the emulated machine through its micro code
self-test and run Norsk Data's own INSTRUCTION-B verification program area by area; that is the
proof the CPU is right, and it takes a few minutes. The `[Explicit]` tests are tools, not checks:
they write the reference traces in `src/docs/traces` for the nd-120 Verilog project to diff
against, and they are run by name when those traces need regenerating.

> [!IMPORTANT]
> Pass **`-nodeReuse:false`** on every `dotnet build` and `dotnet test` you run by hand.
> `build.ps1` already does. Node reuse is on by default and leaves MSBuild worker processes
> behind that hold a lock on the output DLL; the next build then quietly keeps the old binary and
> **the tests report green against stale code.**

### The micro code the CPU runs

`src/NDMicroCpu.Core/microcode` holds the control store images (`.CODE`), symbol files and listings
for ND-110 RASK and ND-120 DELILAH-L. They are the output of the NDMicroAsm assembler, copied here,
never edited. The web emulator serves its own copies from `wwwroot/Assets`; a test fails when they
differ, and `src/eng/sync-web-assets.ps1` refreshes them.

---

## What the code is expected to look like

 - **No LINQ, and no `foreach`.** Use index-based `for` loops. The micro code loop runs millions of
   times a second; an enumerator allocation per cycle is a real cost.
 - **Every public member has an XML documentation comment** that says what the member does and,
   where the code follows the hardware, which signal, register or manual it follows. The build fails
   on a missing one (`GenerateDocumentationFile` with warnings as errors) and the API site is
   generated from them. No `<list>` tags and no entity escapes such as `&lt;` in a comment; write
   the sentence so the character is not needed.
 - **Emulate the mechanism, not the answer.** The CPU runs the real micro code; a shortcut that
   computes the same value another way is not accepted, because the steps, the flags and the
   intermediate states are the thing being emulated.
 - **Naming follows the RetroCore Labs C# standard**: PascalCase types, methods and public
   members, camelCase locals and parameters. One adjustment for this code: a name that comes from
   the hardware or the micro code keeps its spelling from the schematics and the listings
   (`STS_SEXI`, `TICK_20MS`, `COND_StackControl`, the Am2914 instruction names), because that is
   the name a reader finds in the manuals.
 - **Unit tests only**, in the existing test projects. Never add a standalone program that prints
   something and exits - it cannot fail a build.
 - **No FluentAssertions.** Use NUnit's own asserts. Never sleep and then assert; wait on the
   signal, or run the cycles that make it happen.
 - **Keep the comments, and add more.** A comment that records a manual reference, a schematic
   sheet, a bit position, or the reason a thing is NOT done the obvious way is the most valuable
   line in the file. Never delete one because a style guide prefers less; replace one only when it
   has become factually wrong.
 - **Plain words.** In code, comments, commit messages and documentation alike. No jargon where a
   normal word works.
 - **Run `dotnet format`** from `src` if whitespace drifts; the build enforces the `.editorconfig`
   rules, including the file header on every `.cs` file.
 - **The gRPC contract is a wire format.** `NDMicroCpu.Debugger.Contracts` is mirrored in the
   RetroCore repository; a changed `[DataMember(Order = n)]` breaks the other side silently.

### Naming

Root namespace = project name. The standard is `NAMING.md`, kept beside the repositories. The
organisation is **RetroCoreLabs**; the old name `HackerCorpLabs` is retired and must not appear in
anything new.

---

## Documentation changes

```powershell
# from the repository root
python src\eng\check-docs.py
```

It must exit 0. It fails on a relative link that does not resolve, a relative link that escapes
the repository root, a machine-specific absolute path, and a Mermaid diagram that is malformed or
breaks `MERMAID_COLOR_STANDARDS.md`.

> [!WARNING]
> **Never put an absolute path in anything committed here.** A drive letter or a home directory is
> correct on exactly one machine, and this repository is public. A path inside the repository goes
> repo-relative; a sibling repository is named, not located; anything outside is described. A
> variable such as `%USERPROFILE%` or `~` is fine - that is the portable way to name a per-user
> location.

The documents in `src/docs` are also the Guides section of the DocFX site. Its navigation is
written from the tracked files, never by hand:

```powershell
# from src
.\eng\gen-docs-toc.ps1       # rewrites docs\toc.yml; commit it with the new document
dotnet tool restore
dotnet docfx docfx\docfx.json --warningsAsErrors   # the same strict build the workflow runs
```

A README follows `README-TEMPLATE.md`, beside the repositories: one sentence at the top that a
stranger to the subject understands, four badges that can each be wrong, every application in a
table linking its own README, install before build-from-source, licence last.

---

## Submitting a change

 1. Branch off `main`. Do not commit to `main` directly.
 2. One logical change per commit. A commit message says **what changed and why**, in plain
    words - the subject line in the imperative, then a blank line, then the reasoning. If you
    measured something, put the number in the message.
 3. `.\src\build.ps1` passes, including the tests, before you open the pull request.
 4. Say in the pull request what you verified and how. "Tests pass" on its own is not useful;
    "INSTRUCTION-B's SHIFT area still passes on both CPU types, 276 tests, all green" is.
 5. If something is still broken or unfinished, say so plainly. A known gap that is written down
    is fine. One that is hidden is not.

---

## Licence

By contributing you agree your work is licensed under the MIT licence in [LICENSE](../LICENSE).
