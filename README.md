<p align="center"><img src="assets/brand/fluxforge-logo.png" alt="FluxForge — Preserve the Past One Disk at a Time" width="720"></p>

<p align="center"><a href="https://buymeacoffee.com/cjf077"><img src="https://img.shields.io/badge/Buy%20me%20a%20coffee-support%20FluxForge-FFDD00?logo=buymeacoffee&amp;logoColor=black" alt="Buy me a coffee"></a></p>

# FluxForge

Windows desktop app for imaging, writing and documenting floppy disks with a
[Greaseweazle](https://github.com/keirf/greaseweazle). It drives the official `gw.exe` host tools;
it is an independent frontend, not an official Greaseweazle product.

Product spec: [`docs/FluxForge_PRD_v1.0.md`](docs/FluxForge_PRD_v1.0.md) ·
Spike checklist: [`docs/phase0-spike.md`](docs/phase0-spike.md) ·
Open decisions: [`docs/open-decisions.md`](docs/open-decisions.md)

## Status

Phase 1 vertical slice, **not yet validated on real hardware** (see the spike checklist):

- Host-tools discovery (custom path → managed → PATH), user-approved install from official GitHub
  releases, safe ZIP extraction, self-test, atomic activation, rollback, offline ZIP import.
- Device detection via `gw info`, with tools / device / firmware shown separately.
- Read Disk: preservation (raw SCP, always `--raw` with a format) or sector image; live log,
  track map, truthful status, `(INCOMPLETE)` labelling on cancel/failure, SHA-256, job record.
- Write Disk: format confirmation for ambiguous images, explicit acknowledgement + confirmation
  dialog, verify by default, "interrupted = unknown state".
- Host-tools versions: daily release check that always asks before updating, "skip this version",
  install/switch to any older stable release, remove unused versions.
- Activity: searchable job history with exportable text report (optional path redaction).
- Verify, Image Studio, Library and Drive Tools are placeholders for Phase 2.

## Layout

```
src/FluxForge.Application     domain, gw command builder, output parser, job runner (no UI, no I/O policy)
src/FluxForge.Infrastructure  process runner, GitHub releases, host-tools manager, JSON storage
src/FluxForge.Desktop         WPF + WPF-UI shell, MVVM (CommunityToolkit.Mvvm)
tools/FluxForge.FakeGw        development stand-in for gw.exe (builds as gw.exe) — never ship
tests/FluxForge.Tests         xUnit tests
scripts/spike                 Phase 0 fixture capture against real hardware
```

## Build and test

Requires the .NET 10 SDK on Windows. Build from a Windows path if you can — building on `\\wsl$`
works but is slow.

```powershell
dotnet build FluxForge.slnx
dotnet test tests/FluxForge.Tests
dotnet run --project src/FluxForge.Desktop
```

## Developing without hardware

1. `dotnet build tools/FluxForge.FakeGw -o C:\dev\fakegw`
2. In FluxForge: **Settings → Use my own gw.exe…** and pick `C:\dev\fakegw\gw.exe`.
3. Optional environment variables before launching the app:
   - `FLUXFORGE_FAKEGW_SCENARIO` = `ok` (default) | `nodevice` | `badtracks` | `fail`
   - `FLUXFORGE_FAKEGW_DELAY_MS` = per-track delay (default 25)
   - `FLUXFORGE_HOME` = alternative data folder (default `%LOCALAPPDATA%\FluxForge`)

The fake prints a `FLUXFORGE-SIMULATED` marker; the app then shows **SIMULATED HARDWARE** and
labels every job and report as simulated. Its "images" are placeholder bytes.

## Support

Found a bug or have an idea? [Open an issue](https://github.com/freemchr/fluxforge/issues).
If FluxForge saved one of your disks, you can [buy me a coffee](https://buymeacoffee.com/cjf077) ☕

## Brand assets

`assets/brand/`: `fluxforge-logo-original.png` is the master artwork. `fluxforge-logo.png`,
`fluxforge-icon.png` and `fluxforge-wordmark.png` are transparent cut-outs of it, and
`fluxforge.ico` is the multi-size app icon (16–256 px).
