<#
.SYNOPSIS
  Build a local FluxForge installer (Setup.exe + portable zip) with Velopack.

.EXAMPLE
  .\scripts\package.ps1 -Version 0.1.0
  Output lands in .\artifacts\releases.
#>
param(
    [string] $Version = '0.1.0',
    [string] $Vpk = 'vpk'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$releases = Join-Path $root 'artifacts\releases'

dotnet publish (Join-Path $root 'src\FluxForge.Desktop') -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# Pack id differs from the data folder name (%LOCALAPPDATA%\FluxForge) on purpose: Velopack installs to
# %LOCALAPPDATA%\<packId> and removes that folder on uninstall, which must never touch user data.
& $Vpk pack --packId FluxForge.Desktop --packVersion $Version --packDir $publish --mainExe FluxForge.exe `
    --packTitle FluxForge --packAuthors 'Chris Freeman' --icon (Join-Path $root 'src\FluxForge.Desktop\Assets\fluxforge.ico') `
    --outputDir $releases
if ($LASTEXITCODE) { throw 'vpk pack failed' }
Get-ChildItem $releases | Format-Table Name, Length
