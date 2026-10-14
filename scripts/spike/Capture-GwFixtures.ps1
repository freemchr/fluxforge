<#
.SYNOPSIS
  Phase 0 spike: capture real gw.exe output as test fixtures (PRD §15, Phase 0).

.DESCRIPTION
  Runs a fixed set of benign and read-only gw commands and stores stdout, stderr and exit code for
  each under fixtures\gw-<version>\<scenario>\. Run it once per scenario with the right hardware
  state (board unplugged, board + no disk, good disk, damaged disk...). Nothing here writes to a floppy.

.EXAMPLE
  .\Capture-GwFixtures.ps1 -Gw C:\tools\greaseweazle-1.23\gw.exe -Scenario no-device
  .\Capture-GwFixtures.ps1 -Gw C:\tools\greaseweazle-1.23\gw.exe -Scenario amiga-good -Format amiga.amigados
#>
param(
    [Parameter(Mandatory)] [string] $Gw,
    [Parameter(Mandatory)] [ValidateSet('no-device', 'no-disk', 'amiga-good', 'amiga-worn', 'amiga-bad', 'atarist-good', 'pc-good', 'c64-good', 'help-only')] [string] $Scenario,
    [string] $Format,
    [string] $Drive = 'A',
    [string] $OutRoot = (Join-Path $PSScriptRoot '..\..\fixtures')
)
$ErrorActionPreference = 'Stop'
$env:PYTHONUNBUFFERED = '1'

# Fixtures are shared in the repo: strip the board serial number and local paths.
function Protect-Fixture([string] $Path) {
    if (-not (Test-Path $Path)) { return }
    $text = Get-Content -Raw $Path
    if (-not $text) { return }
    $text = $text -replace '(Serial:\s*)\S+', '$1REDACTED'
    $text = $text.Replace($Gw, 'gw.exe').Replace($script:target, '<fixture>')
    Set-Content -NoNewline -Path $Path -Value $text
}

function Invoke-Gw([string] $Name, [string[]] $Arguments) {
    $dir = Join-Path $script:target $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Gw -ArgumentList $Arguments -NoNewWindow -PassThru -Wait `
        -RedirectStandardOutput (Join-Path $dir 'stdout.txt') -RedirectStandardError (Join-Path $dir 'stderr.txt')
    foreach ($f in 'stdout.txt', 'stderr.txt') { Protect-Fixture (Join-Path $dir $f) }
    @{ args = $Arguments; exitCode = $p.ExitCode; seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2) } |
        ConvertTo-Json | Set-Content (Join-Path $dir 'meta.json')
    "{0,-28} exit {1}" -f $Name, $p.ExitCode
}

# Version first so fixtures are filed by host-tools version. gw prints this line on stderr, which
# Windows PowerShell turns into an error record, so relax error handling for this one call.
$ErrorActionPreference = 'Continue'
$info = & $Gw info 2>&1 | Out-String
$ErrorActionPreference = 'Stop'
$version = if ($info -match 'Host Tools:\s*v?([\d.]+)') { $Matches[1] } else { 'unknown' }
$script:target = Join-Path $OutRoot "gw-$version\$Scenario"
New-Item -ItemType Directory -Force -Path $script:target | Out-Null
"Capturing gw $version / $Scenario -> $script:target"

Invoke-Gw 'help'            @('--help')
Invoke-Gw 'version-flag'    @('--version')       # does a version flag exist? (assumption A2)
Invoke-Gw 'info'            @('info')
Invoke-Gw 'read-help'       @('read', '--help')
Invoke-Gw 'write-help'      @('write', '--help')
Invoke-Gw 'convert-help'    @('convert', '--help')
Invoke-Gw 'reset'           @('reset')
if ($Scenario -eq 'help-only' -or $Scenario -eq 'no-device') { return }

$tmp = Join-Path $script:target 'images'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
Invoke-Gw 'read-raw'        @('read', "--drive=$Drive", (Join-Path $tmp 'raw.scp'))
if ($Format) {
    Invoke-Gw 'read-raw-format' @('read', "--format=$Format", '--raw', "--drive=$Drive", (Join-Path $tmp 'raw-format.scp'))
    $ext = switch -Wildcard ($Format) { 'amiga.*' { '.adf' } 'atarist.*' { '.st' } 'ibm.*' { '.img' } 'commodore.*' { '.d64' } default { '.img' } }
    Invoke-Gw 'read-sector'     @('read', "--format=$Format", "--drive=$Drive", (Join-Path $tmp "sector$ext"))
    Invoke-Gw 'convert-scp'     @('convert', "--format=$Format", (Join-Path $tmp 'raw-format.scp'), (Join-Path $tmp "converted$ext"))
}
"Done. Commit stdout/stderr/meta.json; keep images out of git unless they are tiny and freely licensed."
