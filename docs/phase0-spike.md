# Phase 0 spike — verify gw.exe assumptions

FluxForge's code is written against **assumptions** about Greaseweazle host tools v1.23 output and
options. Every assumption below must be confirmed with real hardware before the MVP is called done.
Capture evidence with `scripts/spike/Capture-GwFixtures.ps1`; the resulting `fixtures/gw-1.23/**`
files become parser regression tests.

| # | Assumption | Where it lives | How to verify |
|---|---|---|---|
| A1 | `gw info` prints `Host Tools: X.Y` **before** looking for hardware, so it works as a no-device smoke test | `HostToolsManager.ProbeAsync`, `GwOutputParser.ParseInfo` | `no-device` scenario → `info/stdout.txt` |
| A2 | No dedicated `--version` flag is needed | same | `version-flag` capture |
| A3 | `gw info` prints `Port:`, `Model:`, `Firmware:`, `Serial:` lines when a board is connected | `ParseInfo` | `amiga-good` → `info` |
| A4 | Option syntax `--format=`, `--raw`, `--drive=`, `--device=`, `--revs=`, `--retries=`, `--no-verify` | `GwCommands` | `read-help`, `write-help` |
| A5 | `--format X --raw` writes genuinely raw flux to SCP **and** reports per-track decode results | `GwCommands.Read`, `JobRunner.EvaluateRead` | compare `read-raw-format` stdout with `read-raw`; inspect SCP |
| A6 | Per-track lines look like `T<cyl>.<head>: <codec> (<found>/<total> sectors) from Raw Flux (...)` | `TrackProgressTracker` | `read-sector` stdout |
| A7 | Retries appear as additional `T<c>.<h>:` lines (e.g. `Retry #n`) for the same track | `TrackProgressTracker` | `amiga-bad` scenario |
| A8 | A final `Found N sectors of M` summary line is printed | `TrackProgressTracker.Summary` | `read-sector` stdout |
| A9 | `PYTHONUNBUFFERED=1` makes the frozen gw.exe stream output live when redirected | `ProcessRunner` | watch timestamps in the app's job log during a real read |
| A10 | `gw reset` exists and is safe after killing gw mid-operation | `JobRunner.RunTrackedAsync` | `reset` capture; cancel a real read |
| A11 | The release ZIP is named `*win*.zip` and contains exactly one `gw.exe` | `GitHubReleaseClient.SelectWindowsAsset` | inspect the v1.23 release assets |
| A12 | diskdef names `amiga.amigados`, `atarist.720`, `atarist.360`, `ibm.1440`, `ibm.720`, `commodore.1541` exist | `FormatRegistry.BuiltIn` | `read-help` / diskdefs list |
| A13 | `gw write` verifies by default for sector formats | `GwCommands.Write`, `EvaluateWrite` | `write-help`; one real write to a scratch disk |

Anything that turns out wrong is fixed in the parser/builder **and** pinned by a fixture test, so a
future gw release that changes its output fails CI instead of producing a false green result.

## Reference disks to collect

Known-good: Amiga ADF, Atari ST ST, C64 D64, IBM PC 720K/1.44M. Known-bad: one disk with a few
unreadable sectors, one blank/unformatted disk, one write-protected scratch disk (for the write test).
