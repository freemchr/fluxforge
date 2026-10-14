# Next steps (handoff)

Status as of 14 Oct 2026. Read this first when resuming work.

## Agreed direction

Build out the missing features properly before the next release. No v0.1.1 yet. v0.1.0 is public
(linked from a Reddit post), so leave that release in place.

## Done and tested on real hardware

Host tools install/update/older versions, device detection with USB auto re-detect, Read (raw SCP and
sector images), verified Write, Image Studio decode with format suggestion, error handling (write
protect, cancel, USB unplug, wrong drive), status bar, installer and CI, and **Drive Tools** (RPM,
USB bandwidth, delays, seek, reset tested; clean and erase built but untested: they need a cleaning
disk and a disposable disk).

## To build next, in order

1. **Verify page**: read a disk (sector image, via a temp file) and compare it sector by sector with a
   chosen image; for `.scp` images decode first with `gw convert`. Show mismatched sectors on the track
   map. Reuse `JobRunner` read/decode paths. Replace the Verify placeholder in `ShellViewModel`.
2. **Firmware assistant** (new card on Drive Tools or its own page): show current firmware (`gw info`)
   and what `gw update` would install. `gw update` with no options downloads the latest firmware from
   GitHub itself (see `fixtures/gw-1.23/tools/update-help`). Require idle device, explicit typed
   confirmation, power/cable warning, link to official recovery docs. **Never expose `--bootloader`.**
   Ask the user before flashing their only board (V4.1, firmware 1.6; firmware 1.7 is mentioned upstream).
3. **Library**: SQLite catalogue (Microsoft.Data.Sqlite) of disks and images: title, system, condition,
   notes, tags, SHA-256, provenance from jobs. Auto-add successful reads and decodes. Search. Images stay
   on disk; only paths and hashes in the database. Replace the Library placeholder.
4. Then: batch imaging, more formats (Apple II, Acorn, MSX, Amiga HD), PC/C64 real-disk tests, light
   theme visual pass, Activity export click-through, run `Setup.exe` and test self-update for real.
5. Release: tag `vX.Y.Z` (release workflow does the rest). Stay on 0.x until the features above are
   done, signed and community-tested.

## Conventions that matter

- Real gw output first: capture with `gw` into `fixtures/gw-1.23/...`, then write parsers and tests
  against it. gw writes everything to **stderr** and can **exit 0 on failure** ("Command Failed").
- Hardware rig: Greaseweazle V4.1 on COM6, 3.5" drive on **drive B** (straight cable). Never write to
  the user's original disks. The CF0 Amiga disk is the write-test disk (backup in
  Documents\FluxForge\Backups).
- Dev app: build with `dotnet build src/FluxForge.Desktop -o %TEMP%\fluxforge-dev\app`, run with
  `FLUXFORGE_HOME=%TEMP%\fluxforge-dev\home` (scripts `snap.ps1`, `click.ps1`, `setvalue.ps1`,
  `selectcombo.ps1` live in that temp folder for UI automation and screenshots).
- `IconNameTests` validates every WPF-UI icon name; XAML icon typos otherwise crash at runtime.
- **Every new feature gets a screenshot in the README** (`docs/screenshots/`). Take screenshots from a
  profile with neutral paths so the user's Windows username never appears.
- No em dashes in README, release notes or posts.
- Commits use the GitHub noreply address (already configured in this repo).
