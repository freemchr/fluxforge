# Contributing to FluxForge

Product spec: [`docs/FluxForge_PRD_v1.0.md`](docs/FluxForge_PRD_v1.0.md) ·
Hardware validation: [`docs/phase0-spike.md`](docs/phase0-spike.md) ·
Open decisions: [`docs/open-decisions.md`](docs/open-decisions.md)

## Layout

```
src/FluxForge.Application     domain, gw command builder, output parser, job runner (no UI)
src/FluxForge.Infrastructure  process runner, GitHub releases, host-tools manager, JSON storage
src/FluxForge.Desktop         WPF + WPF-UI shell, MVVM (CommunityToolkit.Mvvm)
tools/FluxForge.FakeGw        development stand-in for gw.exe (builds as gw.exe) — never ship
tests/FluxForge.Tests         xUnit tests, including replays of real Greaseweazle output
fixtures/gw-1.23              captured gw output from real hardware (images stay local)
scripts/spike                 fixture capture against real hardware
```

## Build and test

Requires the .NET 10 SDK on Windows.

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

The fake prints a `FLUXFORGE-SIMULATED` marker. The app then shows **SIMULATED HARDWARE** and labels
every job and report as simulated.

## Brand assets

`assets/brand/`: `fluxforge-logo-original.png` is the master artwork. `fluxforge-logo.png`,
`fluxforge-icon.png` and `fluxforge-wordmark.png` are transparent cut-outs of it, and
`fluxforge.ico` is the multi-size app icon (16–256 px).


## Releasing

Push a tag such as `v0.2.0`. The **Release** workflow tests, publishes a self-contained build, packs it
with [Velopack](https://velopack.io) and publishes a GitHub release with `Setup.exe`, a portable zip
and update packages. Build an installer locally with `scripts\package.ps1 -Version 0.2.0`.

The Velopack pack id (`FluxForge.Desktop`) deliberately differs from the data folder
(`%LOCALAPPDATA%\FluxForge`): Velopack removes its install folder on uninstall, and that must never
touch users' images, tools or history.

## Third-party components

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
