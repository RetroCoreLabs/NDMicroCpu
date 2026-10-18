<#
.SYNOPSIS
  Writes src\docs\toc.yml, the Guides navigation of the DocFX site, from the documents git tracks.

.DESCRIPTION
  A hand-kept navigation list is stale the day a document is added, so this script builds it
  from the tree: every tracked Markdown file in src\docs becomes an entry, titled by its first
  heading, with the reference traces grouped under their README. Run it after adding, renaming
  or removing a document, then commit the result; the docs workflow builds the site from it.

.EXAMPLE
  .\eng\gen-docs-toc.ps1
#>
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $src 'docs'

function Title($path) {
    foreach ($line in Get-Content $path -Encoding utf8) {
        if ($line -match '^#\s+(.+)$') { return $Matches[1].Trim() }
    }
    return [IO.Path]::GetFileNameWithoutExtension($path)
}

# only what git tracks: the clean checkout on the runner has nothing else
$tracked = git -C $src ls-files docs | Where-Object { $_ -like '*.md' } | ForEach-Object { $_ -replace '^docs/', '' }
$guides = $tracked | Where-Object { $_ -notlike 'traces/*' } | Sort-Object
$traces = $tracked | Where-Object { $_ -like 'traces/*' -and $_ -ne 'traces/README.md' } | Sort-Object

$out = New-Object System.Collections.Generic.List[string]
$out.Add('# The Guides section of the DocFX site (src\docfx). Written by eng\gen-docs-toc.ps1 from the')
$out.Add('# documents git tracks in src\docs; run it after adding or renaming one, never edit by hand.')
foreach ($g in $guides) {
    $out.Add("- name: $(Title (Join-Path $docs $g))")
    $out.Add("  href: $g")
}
if (Test-Path (Join-Path $docs 'traces\README.md')) {
    $out.Add("- name: $(Title (Join-Path $docs 'traces\README.md'))")
    $out.Add('  href: traces/README.md')
    $out.Add('  items:')
    foreach ($t in $traces) {
        $name = [IO.Path]::GetFileNameWithoutExtension($t) -replace '^TRACE-INSTRUCTION-VERIFY-', ''
        $out.Add("  - name: $name")
        $out.Add("    href: $t")
    }
}
$target = Join-Path $docs 'toc.yml'
[IO.File]::WriteAllText($target, (($out -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding $false))
Write-Host "wrote $target : $($guides.Count) guides, $($traces.Count) traces"
