# Phase 0 spike — verify gw.exe assumptions

FluxForge's code is written against **assumptions** about Greaseweazle host tools v1.23 output and
options. Every assumption below must be confirmed with real hardware before the MVP is called done.
Capture evidence with `scripts/spike/Capture-GwFixtures.ps1`; the resulting `fixtures/gw-1.23/**`
files become parser regression tests.

| # | Assumption | Where it lives | How to verify | Result (V4.1, fw 1.6, gw 1.23) |
|---|---|---|---|---|
| A1 | `gw info` prints `Host Tools: X.Y` **before** looking for hardware, so it works as a no-device smoke test | `HostToolsManager.ProbeAsync`, `GwOutputParser.ParseInfo` | `no-device` scenario → `info/stdout.txt` | ✅ Confirmed — but printed on **stderr** |
| A2 | No dedicated `--version` flag is needed | same | `version-flag` capture | ✅ No `--version` flag exists (prints usage, exit 1) |
| A3 | `gw info` prints `Port:`, `Model:`, `Firmware:`, `Serial:` lines when a board is connected | `ParseInfo` | `amiga-good` → `info` | ✅ Confirmed (`Port: COM6`, `Model: Greaseweazle V4.1`, `Firmware: 1.6`) |
| A4 | Option syntax `--format=`, `--raw`, `--drive=`, `--device=`, `--revs=`, `--retries=`, `--no-verify` | `GwCommands` | `read-help`, `write-help` | ✅ Confirmed for read and write help; drive `3` also valid |
| A5 | `--format X --raw` writes genuinely raw flux to SCP **and** reports per-track decode results | `GwCommands.Read`, `JobRunner.EvaluateRead` | compare `read-raw-format` stdout with `read-raw`; inspect SCP | 🟡 Help says `--raw`: "output raw stream (--format verifies only)" — needs a real read |
| A6 | Per-track lines look like `T<cyl>.<head>: <codec> (<found>/<total> sectors) from Raw Flux (...)` | `TrackProgressTracker` | `read-sector` stdout | ⏳ Needs a disk read |
| A7 | Retries appear as additional `T<c>.<h>:` lines (e.g. `Retry #n`) for the same track | `TrackProgressTracker` | `amiga-bad` scenario | ⏳ Needs a damaged disk |
| A8 | A final `Found N sectors of M` summary line is printed | `TrackProgressTracker.Summary` | `read-sector` stdout | ⏳ Needs a disk read |
| A9 | `PYTHONUNBUFFERED=1` makes the frozen gw.exe stream output live when redirected | `ProcessRunner` | watch timestamps in the app's job log during a real read | ⏳ Needs a disk read (and gw writes progress to stderr) |
| A10 | `gw reset` exists and is safe after killing gw mid-operation | `JobRunner.RunTrackedAsync` | `reset` capture; cancel a real read | ✅ `gw reset` exists, exit 0 |
| A11 | The release ZIP is named `*win*.zip` and contains exactly one `gw.exe` | `GitHubReleaseClient.SelectWindowsAsset` | inspect the v1.23 release assets | ✅ `greaseweazle-1.23-win64.zip` (10.3 MB), one `gw.exe`; real install + self-test passed |
| A12 | diskdef names `amiga.amigados`, `atarist.720`, `atarist.360`, `ibm.1440`, `ibm.720`, `commodore.1541` exist | `FormatRegistry.BuiltIn` | `read-help` / diskdefs list | ✅ All six diskdefs present |
| A13 | `gw write` verifies by default for sector formats | `GwCommands.Write`, `EvaluateWrite` | `write-help`; one real write to a scratch disk | 🟡 `--no-verify` exists (verify is default) — needs a real write |

Anything that turns out wrong is fixed in the parser/builder **and** pinned by a fixture test, so a
future gw release that changes its output fails CI instead of producing a false green result.

## Findings so far (14 Oct 2026)

- **gw writes all output to stderr**, including `info`, help and (very likely) per-track progress.
  FluxForge reads both streams, so nothing is lost; the console no longer flags stderr as errors.
- Captured fixtures live in `fixtures/gw-1.23/help-only/` (serial number and local paths redacted)
  and are exercised by `RealGwFixtureTests`.

## Reference disks to collect

Known-good: Amiga ADF, Atari ST ST, C64 D64, IBM PC 720K/1.44M. Known-bad: one disk with a few
unreadable sectors, one blank/unformatted disk, one write-protected scratch disk (for the write test).
