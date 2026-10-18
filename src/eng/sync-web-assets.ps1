<#
.SYNOPSIS
  Copies the control store images and listings the web emulator serves from NDMicroCpu.Core\microcode.

.DESCRIPTION
  The browser fetches the micro code over HTTP from wwwroot\Assets (IAppService.AssetsFor), so the
  web project needs its own copies of ND-110-RASK and ND-120-DELILAH-L: the .CODE image as
  NAME-CODE.TXT and the listing as NAME-LISTING.TXT. They are tracked in git because the Blazor
  static-asset pipeline collects wwwroot before any build target runs, so a build-time copy would
  not be served. This script refreshes them; the test WebAssetsMatchCoreMicrocode fails when they
  drift from the Core copies.

.EXAMPLE
  .\eng\sync-web-assets.ps1
#>
$ErrorActionPreference = 'Stop'
$src = Split-Path -Parent $PSScriptRoot
$from = Join-Path $src 'NDMicroCpu.Core\microcode'
$to = Join-Path $src 'NDMicroCpu.Web\wwwroot\Assets'

foreach ($name in 'ND-110-RASK', 'ND-120-DELILAH-L') {
    Copy-Item (Join-Path $from "$name.CODE") (Join-Path $to "$name-CODE.TXT") -Force
    Copy-Item (Join-Path $from "$name.LISTING.TXT") (Join-Path $to "$name-LISTING.TXT") -Force
    Write-Host "  $name  -> $name-CODE.TXT, $name-LISTING.TXT"
}
Write-Host "Done. Review the changes with: git -C `"$src`" status"
