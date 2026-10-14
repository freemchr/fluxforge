# Next steps (handoff)

Status as of 14 Oct 2026 (Verify and Library finished). Read this first when resuming work.

## Agreed direction

v1.0.0 (Verify, Library, per-feature README screenshots) is the current release, chosen by the user on
14 Oct 2026. v0.1.0 stays up because a Reddit post links to it; 0.1.0 installs update in-app.

## Done and tested on real hardware

Host tools install/update/older versions, device detection with USB auto re-detect, Read (raw SCP and
sector images), verified Write, Image Studio decode with format suggestion, error handling (write
protect, cancel, USB unplug, wrong drive), status bar, installer and CI, and **Drive Tools** (RPM,
USB bandwidth, delays, seek, reset tested; clean and erase built but untested: they need a cleaning
disk and a disposable disk), and **Verify** (CF0 disk vs its backup image: all 1,760 sectors match;
vs an altered copy: exactly the 3 changed sectors flagged; vs the raw `.scp` capture: decoded, all
match), and **Library** (SQLite catalogue in `Data\library.db`; auto-adds reads/decodes via
`CataloguingJobStore`, imports Activity history once, edit/search/tags/condition, integrity check,
verify results recorded per image; tested in the app with real history and a real verify).

## To build next, in order

1. **Firmware assistant** (new card on Drive Tools or its own page): show current firmware (`gw info`)
   and what `gw update` would install. `gw update` with no options downloads the latest firmware from
   GitHub itself (see `fixtures/gw-1.23/tools/update-help`). Require idle device, explicit typed
   confirmation, power/cable warning, link to official recovery docs. **Never expose `--bootloader`.**
   Ask the user before flashing their only board (V4.1, firmware 1.6; firmware 1.7 is mentioned upstream).
2. Then: batch imaging, more formats (Apple II, Acorn, MSX, Amiga HD), PC/C64 real-disk tests, light
   theme visual pass, Activity export click-through, run `Setup.exe` and test self-update for real.
   Library ideas not built yet: move an image to another disk / merge disks, scans/photos of labels.
3. Release: bump `<Version>` in `Directory.Build.props`, then tag `vX.Y.Z` (the release workflow
   builds, packages and publishes). Code signing is still to do.

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
- Screenshots come from the demo profile `C:\Users\Public\FluxForge-demo` (run with
  `FF_HOME=C:\Users\Public\FluxForge-demo` and `snap.ps1 -Start`) with sample images in
  `C:\Users\Public\Documents\FluxForge`, so no personal paths appear. Accent colour is the logo blue `#0A84FF`.
- **Every new feature gets a screenshot in the README** (`docs/screenshots/`). Take screenshots from a
  profile with neutral paths so the user's Windows username never appears.
- Verify relies on gw filling undecodable sectors with `-=[BAD SECTOR]=-` (pinned in
  `fixtures/gw-1.23/amiga-worn/bad-sector`). That is how "unreadable" is told apart from "differs".
- No em dashes in README, release notes or posts.
- Commits use the GitHub noreply address (already configured in this repo).
