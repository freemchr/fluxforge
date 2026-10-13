# FluxForge — Product Requirements Document

**Working title:** FluxForge  
**Tagline:** Preserve the past. One disk at a time.  
**Product:** Windows desktop floppy imaging, preservation, conversion and diagnostics application  
**Document:** PRD v1.0 · 14 October 2026  
**Status:** Proposed, for design and engineering handoff  
**Platform:** Windows 10/11 x64 · offline-first · local hardware via Greaseweazle

> Naming is provisional. Check product name, domain and trademark availability before launch. FluxForge is an independent frontend, not an official Greaseweazle product.

## 1. Executive summary

Develop a professional, modern, highly usable Windows desktop application built on the official Greaseweazle host tools, with convenient disk imaging and restoration, rich diagnostics and a preservation workflow. The goal is to combine the everyday functions of existing GUI wrappers with a more polished UX and an extensible advanced toolkit. A person should be able to install FluxForge, connect supported hardware, back up an Amiga or Atari ST disk and inspect its result without opening a terminal or manually installing Greaseweazle.

The software must distinguish between **Greaseweazle host tools** (`gw.exe`, installable automatically) and **physical Greaseweazle hardware/firmware** (requires actual USB device, firmware updates must be explicit). Offline file conversion and library operations can work without a connected device.

**Product pillars:** effortless first run; trustworthy preservation; transparent feedback; broad formats; advanced controls when needed; maintainability across upstream releases; local-first collection management.

## 2. Problem and opportunity

Greaseweazle offers powerful commands but Windows users often juggle command-line tools, wrappers, separate image viewers and spreadsheets. Existing frontends include FluxMyFluffyFloppy (actively developed, reported v5.2.8 on 6 September 2026) and GreaseweazleGUI-dotNET. Compete on overall end-to-end experience, not a claim that existing projects are unmaintained. The app must add practical value beyond command execution, especially preservation-first defaults, accurate track reporting, saved profiles, collection records, file conversion and guided diagnostics.

## 3. Users and jobs to be done

1. **Hobbyist:** Read Amiga/Atari/C64 disks to images quickly with safe defaults.
2. **Collector:** Capture and catalog multiple original disks, retain provenance and log bad sectors.
3. **Archivist:** Preserve raw flux, multiple revolutions, hashes, capture notes and retriable problem tracks.
4. **Repairer/power user:** Diagnose USB board, drive, cabling and track geometry; adjust format, drive selection, retries and timings.
5. **Emulation user:** Convert suitable images for emulators and Gotek/FlashFloppy using supported transformations.

## 4. Goals and non-goals

### Goals
- First successful common disk backup with minimal setup and zero terminal use.
- Reliable host-tools installation and updates from the official `keirf/greaseweazle` releases.
- Guided Read, Write, Verify and Convert flows with explicit destructive-action confirmations.
- Live progress and track outcome display based on verified tool output (never manufacture a success state).
- Native supported profile discovery and import of user disk definitions.
- Raw-preservation safeguards: truly raw flux capture must not be silently regenerated or replaced by sector data.
- Full logs and reproducible run settings.
- A genuinely modern, responsive, accessible desktop UX.

### Non-goals for v1
- Reimplementing the full Greaseweazle USB protocol or codecs from scratch.
- Guaranteeing recovery of damaged or copy-protected disks.
- Claiming all image extensions are bidirectionally readable/writable.
- Fully accurate sector/file browsing across every vintage filesystem.
- Firmware updates without explicit selection and user consent.
- Cloud accounts, subscription dependency, AI features or telemetry required to operate.

## 5. Baseline ecosystem: verified 14 October 2026

- Official host tools: https://github.com/keirf/greaseweazle ; latest listed release v1.23 (17 Dec 2025). Windows host distribution contains `gw.exe`, installable by unzipping. The tools have permissive redistribution terms (verify bundled license before packaging).
- Official firmware is a **separate** repository: https://github.com/keirf/greaseweazle-firmware ; version/capability compatibility cannot simply be inferred from host-tools version numbers.
- Existing frontends: https://github.com/FrankieTheFluff/FluxMyFluffyFloppy (GPL-2.0; v5.2.8 Sep 2026 per repository); https://github.com/Foosie/GreaseweazleGUI-dotNET (MIT).
- Official disk-image support: https://github.com/keirf/greaseweazle/wiki/Supported-Image-Types . Suffix alone is insufficient; formats vary in geometry, metadata and read/write direction.
- Read/write tutorial: https://github.com/keirf/greaseweazle/wiki/Getting-Started/2aba37f69d58526e7e32813ab29982b4d952cfa4 .

## 6. Product design and navigation

### Visual direction
- Dark-first, optional light theme, restrained charcoal/slate base, electric teal/blue accent; readable high-contrast statuses. No nostalgic faux terminal overload.
- Fluent-style Windows look, rounded surfaces, subtle depth, clear iconography, large action targets and fast keyboard navigation.
- Status colors always paired with text/icons: success, retries/weak, error, pending, unknown. Do not turn uncertain data green.
- User-selected compact vs spacious density; scalable at 125–200% DPI.

### Shell
Left navigation: **Home | Read Disk | Write Disk | Verify | Image Studio | Library | Drive Tools | Activity | Settings**.  Top bar: board indicator, model/COM port, installed GW version, firmware compatibility, selected drive, contextual Help.

### Home dashboard
- **Connected hardware** card (or clear 'No Greaseweazle detected').
- **Quick actions:** Back Up Disk, Write Image, Open Image, Convert Image.
- **Recent activity:** disk, date, outcome, affected tracks.
- **Collection** summary and latest tools release information.
- Setup checklist for first-time users.

### Read Disk wizard
1. Hardware and drive selection (or detected issue).
2. System/disk format profile: Amiga, Atari ST, PC, C64, Apple II, Acorn, etc.; advanced exact diskdef selection.
3. Capture choice: **Preservation (raw SCP)** or **Convenient sector image**; optional dual-capture session if supported, with explicit separate reads and caveat that the captures may differ.
4. Output location, disk label, metadata, source condition, write-protect reminder.
5. Advanced: disk geometry, heads/tracks, drive RPM/density, revolutions if supported, retries, double-step, flippy/reverse, custom diskdefs, advanced parameters subject to GW capability version.
6. Review command preview, start, live track matrix and console detail.
7. Summary: success/partial/failure, verified status, bad tracks, files created, checksums and report; options 'Retry selected tracks' only when safe/technically implementable without losing original capture.

### Write Disk wizard
Select image -> inspect type/geometry/size -> select device/drive -> validate compatibility and target capacity -> warn about write protect and overwrite -> checkbox **I understand this will overwrite data on the floppy** -> explicit Write button -> progress -> verify where supported -> detailed outcome. Warn that raw flux formats do not get the same automatic verification as sector formats; offer a separate readback comparison workflow when meaningful and label limitations clearly.

### Main graphical track map
- Grid indexed by cylinder (rows) and side/head (columns) with track detail popover: observed status, retries, sectors recovered, checksum errors, time, log excerpt, confidence.
- Legend: Not read / Reading / Decoded OK / Recovered after retry / Unreadable / Flux captured, decode unknown / Not applicable.
- No fabricated 'health score' without transparent methodology.
- Later phases: track-level visualization of flux timing, revolutions, density and irregularity; graph is generated by a dedicated image parser/analysis library, **not** by assuming `gw.exe` emits ready-to-chart flux data.

## 7. Functional requirements and acceptance criteria

Priorities: **P0** essential for MVP, **P1** for v1.0, **P2** advanced subsequent release.

| ID | Priority | Requirement | Acceptance criteria |
|---|---|---|---|
| FR-01 | P0 | Host tools discovery | Detect valid configured path, previously managed install and reasonable PATH candidates; report tools version without requiring hardware. |
| FR-02 | P0 | First-run auto-install | With approval, download official latest stable Windows x64 host-tools release, validate, extract into user-local versioned directory and run smoke test. |
| FR-03 | P0 | Device discovery | Enumerate serial candidates, invoke `gw info` with actual supported syntax, distinguish tools-found/device-missing/driver issue/incompatible firmware. |
| FR-04 | P0 | Read disk | Read supported common disk types into appropriate sector images and raw SCP; preserve exact tool output in logs. |
| FR-05 | P0 | Write image | Safe confirmation and single-job ownership of device; verify when backend supports it; report partial failures clearly. |
| FR-06 | P0 | Format/profile picker | Include verified presets for common machines; map each preset to exact upstream diskdef, format direction and expected geometry. |
| FR-07 | P0 | Progress and cancellation | Display live step/track information where available, elapsed time, state and raw log; safe cancel with cleanup and controlled process termination. |
| FR-08 | P0 | Offline operations | Library browsing and supported conversions run when no board is attached, provided tools are installed. |
| FR-09 | P0 | Reliable logging | Log command arguments redacted where needed, GW version, device, timestamps, exit code, stdout/stderr and generated files. |
| FR-10 | P0 | Safe output handling | No silent overwrite, path confirmation, free-space check, temporary files where supported, retain partial images with accurate labels. |
| FR-11 | P1 | Convert image | List allowable **directional** source-to-target pairs based on installed tools; refuse unsupported conversions with explanation. |
| FR-12 | P1 | Verify and compare | Use backend-supported verification plus optional independently captured readback comparisons; never call flux byte comparison 'physical verification'. |
| FR-13 | P1 | Job history | Searchable history with parameters, diagnostics and exportable JSON/CSV/TXT report. |
| FR-14 | P1 | Disk library | Store file path, hashes, disk identity, platform, image type, condition, tags, notes, scans/photos as references. |
| FR-15 | P1 | Profile manager | Save drive type, system, preferred output, custom diskdefs and advanced flags; import/export profiles. |
| FR-16 | P1 | Device utilities | Supported info, seek, clean, bandwidth, erase, reset, pin/delay controls with permission gates and version checks. |
| FR-17 | P1 | Host-tools update | Manual/check on launch, release notes, confirm installation, preserve old versions and support rollback. |
| FR-18 | P1 | Firmware assistant | Display current/compatible status when detectable; manual update with hardware-specific warnings, checks and recovery guidance. Never auto-flash. |
| FR-19 | P1 | Localization/accessibility | Accessible labels, keyboard access, screen-reader-friendly controls, light/dark themes. |
| FR-20 | P2 | Raw flux inspector | Parse SCP initially; chart revolution timing, flux intervals and anomalies; read-only by default. |
| FR-21 | P2 | Recovery workshop | Per-track recapture, multiple attempts/revolutions, compare captures, non-destructive merge proposals with uncertainty. |
| FR-22 | P2 | Sector/filesystem browser | Read-only, explicitly supported parsers (e.g., AmigaDOS, FAT12/Atari ST, C64 directory) with provenance and graceful unknown sectors. |
| FR-23 | P2 | Batch imaging | Safe queue and auto-numbered outputs; require operator to confirm disk changes; no concurrent jobs on a shared board. |
| FR-24 | P2 | Integrity manifests | SHA-256 hashes, JSON provenance records, optional archival package export and later audit. |
| FR-25 | P2 | Ecosystem integrations | Optional launch/export to HxC tools, emulator-friendly targets, FlashFloppy/Gotek profiles where technically and legally supported. |

### Required first-class format coverage

- **Sector images** (direction depends on official support): Amiga `.adf`, Atari ST `.st` / `.msa`, C64 `.d64` / `.d71` / `.d81`, IBM PC `.img` / `.ima` / `.dsk`, Amstrad `.dsk` / `.edsk`, Acorn `.ssd` / `.dsd` / `.adf` variants, Apple II `.do` / `.po` where mapped and supported.
- **Raw/bitcell/flux**: `.scp`, `.hfe`, KryoFlux `.raw` sets, with container-specific options.
- **Import-only**: `.ipf`, `.td0`, PC-98 `.d88` / `.dcp` / `.dim` / `.fdi` / `.nfd`, `.a2r` as allowed by GW build. Do not describe these as universally exportable.
- **Caveats:** generic `.img`, `.dsk`, `.adf` can be ambiguous; some formats require `--format`; IPF handling can require optional CAPS/SPS components; not all image types support built-in verification. Validate via installed tools rather than hard-code a forever-static matrix.

## 8. Greaseweazle host-tools bootstrapper

**Answer to the key question: Yes, the application can fetch and install the host tools automatically if absent, and retain them in a clean managed folder.** This is a core MVP requirement.

**Recommended directories** (per-user, no admin rights required):

```
%LOCALAPPDATA%\FluxForge\
  Tools\Greaseweazle\1.23\... including gw.exe
  Tools\Greaseweazle\<other-version>\...
  Cache\downloads\
  Logs\
  Jobs\
  Config\
  Data\fluxforge.db
```

**Boot sequence:**
1. Load configured custom executable path, if any; then check managed active tools; then optionally detect `gw.exe` on PATH and documented common locations. Validate by running a benign version/help command. `gw info` is not an installation test because it requires connected hardware.
2. If nothing valid is found, show **Install Greaseweazle Tools** with official publisher, version, source link, target path, size and a user approval action. If offline, offer **Browse for existing gw.exe / Import downloaded ZIP**.
3. Query `https://api.github.com/repos/keirf/greaseweazle/releases/latest` over HTTPS. Reject drafts/prereleases in stable channel. Identify the proper Windows x64 binary ZIP from release asset names/metadata; do not guess a permanent filename; if missing, show manual-install fallback. Support GitHub rate limits and proxy/network errors.
4. Download into a temporary location with progress, retries and size limits. Verify trusted source, archive structure and hash when a trustworthy pinned/signed digest is available; don't misrepresent a hash supplied alongside a malicious archive as proof of authenticity. Never execute scripts from downloaded archives.
5. Prevent archive path traversal, symlinks/out-of-root writes and zip bombs; extract only inside staging, verify expected `gw.exe`, run a benign smoke test and compare detected version to release metadata.
6. Atomically activate the new version using a config pointer, preserving the last known-good version. Clean up failed staging; make rollback one click.
7. Add a **Use my own installation** override. Never overwrite a user's external Greaseweazle directory.
8. On later app launches, check releases at most once per day unless explicitly requested, notify but do not silently replace a known-good executable during active disk jobs.

**Separate concepts in UI:**
- *Greaseweazle Host Tools*: Found / Missing / Update available / Failed.
- *Greaseweazle Device*: Connected / Disconnected / Driver required / Busy.
- *Device Firmware*: Detected version / Compatibility warning / Update offered.

**Firmware:** `gw update` is a supported path for board firmware updating, but specific boards have special recovery procedures. Confirm exact board model, display official guidance, require idle device, power/cable warning, and explicit confirmation. Never invoke `gw update --bootloader` in the normal UI; advanced users need to follow official recovery documentation separately.

## 9. Application architecture

**Recommended implementation:** C# on .NET 10 LTS, WPF desktop UI with MVVM and a modern Fluent-inspired component/theme library, SQLite for catalog/history, SkiaSharp for high-performance track/flux rendering, `System.Diagnostics.Process` for `gw.exe` subprocess execution, and a signed installer + portable ZIP distribution. Choose WPF for maturity and predictable Windows desktop/hardware integration; isolate UI from backend so a future Avalonia/other UI remains possible. Confirm framework/package versions at implementation time.

```
FluxForge.Desktop (WPF views, accessibility, navigation)
    |
FluxForge.Application (use-cases, commands, state, job queue, validation)
    |-- ToolManager (discover/download/install/update/rollback)
    |-- DeviceManager (serial discovery, info, capability negotiation)
    |-- JobRunner (per-device lock, progress, cancellation, audit)
    |-- FormatRegistry (profiles + directional format capabilities)
    |-- ImageService (inspect, convert, checksum; read-only preview)
    |-- ArchiveLibrary (SQLite metadata, filesystem references)
    |-- DiagnosticService (troubleshooting/drive utilities)
    |
FluxForge.Infrastructure
    |-- GwCliAdapter (typed safe command arguments; stdout/stderr parser)
    |-- ReleaseClient (GitHub official stable releases)
    |-- FileSystem / Archive / SQLite
    |-- ImageParsers (SCP in later phase)
    |
External: official gw.exe + physical Greaseweazle USB board
```

**Engineering principles:**
- No shell string concatenation: `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, redirected output, strict file validation.
- Commands formed from typed profiles and whitelisted capabilities, not arbitrary text interpolation. Optional Expert Console clearly segregated.
- `gw.exe` output is presentation-oriented, not a stable machine-readable API; implement per-version parser adapters, log preservation and unknown-status fallback. Do not report track success solely from regex guesses.
- Use semver and runtime probes to enable options that exist in the installed version, plus test fixtures across known releases.
- Use a single active hardware job per device. No two concurrent processes talking to the same board.
- Provide cancellation and timeout escalation with best-effort motor reset and explicit 'state unknown' if interrupted mid-write.
- Do not install kernel drivers automatically; explain supported Windows driver paths and link official guidance.
- Keep image parsing optional/isolated from core device operations; malformed images are untrusted inputs.
- Background integrity checks don't change original images; preserve originals immutable when feasible.

## 10. Operations and backend mapping

| UI action | Typical Greaseweazle operation | Notes |
|---|---|---|
| Detect board | `gw info` | Hardware required; handle no device cleanly. |
| Read an Amiga disk | `gw read --format=amiga.amigados <output.adf>` | Confirm CLI syntax against installed version. |
| Preserve flux | `gw read <output.scp>` | No `--format` means raw; if a profile is also specified, include `--raw`. |
| Write an Amiga disk | `gw write --format=amiga.amigados <input.adf>` | Destructive. Verify only as supported. |
| Convert | `gw convert <input> <output>` with exact required options | Hardware not required; source/target capabilities checked. |
| Erase | `gw erase` with safety-bounded track selection | Requires explicit 'Erase' confirmation. |
| Firmware update | `gw update` | Separate firmware assistant; never unattended. |
| Diagnostics | `gw info`, and supported `seek`, `clean`, `bandwidth`, `reset`, `pin`, `delays` actions | Capabilities verified per release. |

**Critical preservation invariant:** `gw read --format=<format> file.scp` can produce regenerated, 'perfected' flux rather than original measurements unless `--raw` is specified. Preservation Mode must build a genuinely raw capture command and explain the distinction to users. Never silently transform questionable tracks into pristine-looking archives.

## 11. User-facing jobs and edge cases

**First launch, no tools, no device:** App opens -> 'Greaseweazle host tools missing' -> one-click official installation -> shows 'Tools Ready, Connect Device' -> guided USB/driver connection -> `gw info` confirms model -> Quick Read unlocked.

**Tools available, device disconnected:** Read/Write disabled with actionable explanation; Convert and Library remain usable.

**Board connected, disk absent:** Explain index/no disk possibilities without asserting absent floppy from a single ambiguous error; offer drive selection and cable/power checklist.

**Read quality degraded:** Preserve original capture; show observed bad sectors/retries; classify as partial; suggest cleaning/drive troubleshooting and optional second capture; avoid invented recovery guarantees.

**Target overwritten:** Always require explicit confirmation and offer 'read original first'. Do not infer write-protect state unless detectable.

**Unrecognized format:** Default to preservation-first raw capture rather than selecting an unsafe geometry. Ask for profile or accept 'Unknown/Raw'.

**Firmware/tools mismatch:** Surface version info, explain likely mismatch without asserting unsupported capabilities, offer upgrade path only after review.

**Unsupported conversion:** Show why input cannot be exported into requested format and suggest an appropriate supported alternative, without corrupting original.

## 12. Operational safety and preservation ethics

- Always preserve source floppies as originals. Recommend hardware write-protect before imaging where possible.
- Require explicit confirmation for Write, Erase and Firmware Update. No 'one click' destructive starts.
- Never update tools mid-job or flash hardware silently.
- Raw captures and derived/decoded images must be stored and named as distinct artifacts with provenance links.
- Distinguish 'read completed' from 'all sectors decoded' and 'write finished' from 'verified'.
- Don't call unsupported flux verification successful; warn about hardware/head variations and copy-protection.
- Put user data only in user-selected image/library folders, not hidden inside app binaries.
- Privacy: all work local by default; optional release-check network traffic only; diagnostics export with path-redaction option.
- Confirm licenses for any third-party libraries/converters. GPL-2.0 code from FluxMyFluffyFloppy must not be copied into proprietary/noncompatible code without satisfying obligations.

## 13. Non-functional requirements

- **OS:** Windows 10 22H2 and Windows 11 x64; test high-DPI and multi-monitor use.
- **Packaging:** Self-contained signed installer preferred, plus portable edition. No administrator permission for basic app or tool install.
- **Startup:** Under 3 seconds on reference modern SSD after first initialization, excluding external hardware discovery delays (target, measure).
- **Responsiveness:** No UI-thread blocking for device commands, network I/O or image parsing; target 60 FPS for dashboards and 30+ FPS flux graph panning on representative images.
- **Reliability:** No job can terminate in a false green success; all actions produce a durable audit record.
- **Supportability:** Text/JSON export, app version and GW version included in diagnostics.
- **Security:** HTTPS allowlist for host-tool releases, archive safety checks, strict argument/path handling, dependency scanning, signed releases, optionally portable integrity manifest.
- **Accessibility:** Full keyboard flows, programmatic controls, non-color-only health statuses, readable contrast.
- **Offline-first:** All imaging, writing, browsing and conversions possible without network after tools are installed.
- **Testing:** Simulated process/fixture mode and full integration tests on real board/drive/disk combinations before release.

## 14. Data model sketch

`DiskRecord(id, title, system, formatProfile, notes, labels, tags, origin, physicalCondition, createdAt)`  
`ImageArtifact(id, diskId, path, sha256, byteSize, containerType, captureKind[raw|derived|sector], sourceJobId, createdAt)`  
`DeviceRecord(id, usbSerial, model, firmwareVersion, driveConfig, lastSeenAt)`  
`JobRecord(id, type, deviceId, parametersJson, toolsVersion, startAt, endAt, status, exitCode, logPath)`  
`TrackRecord(jobId, cylinder, head, status, retries, detailJson, parseConfidence)`  
`Preset(id, displayName, formatId, driveId, trackSelection, advancedParamsJson, userDefined)`

Never put large flux payloads directly in SQLite. Images remain on disk; store paths, hashes and non-lossy source relationships in catalog.

## 15. Release roadmap

### Phase 0 — technical spike and interaction design
- Validate CLI behavior for v1.23, command options, sample stdout/stderr, Windows path edge cases and unsupported firmware.
- Collect reference artifacts: known-good Amiga ADF, Atari ST ST, C64 D64, IBM PC IMG and raw SCP; include known-bad images for error states.
- Produce clickable app design for Home, first-run setup, Read wizard, Write wizard, track map and Image Studio.

### Phase 1 — MVP: useful every day
- App shell, device detection, tools bootstrapper, board status.
- Read: standard sector images and genuine raw SCP, profile picker and safe defaults.
- Write: confirmation, progress, supported auto-verify, cancellation rules.
- Live progress, terminal log pane, exportable job report.
- Managed tools updates; packaging and unsigned beta distribution for testing.

### Phase 2 — v1.0: better than a wrapper
- Format conversion with directional compatibility matrix.
- Persistent library, SHA-256, track outcome map, saved profiles, history and report export.
- Drive utilities, firmware update assistant, accessible theme/settings, installer signing and broader regression suite.

### Phase 3 — v1.5: preservation suite
- Read-only raw SCP inspector, revolutions/flux analysis, recovery workshop.
- Sector-level inspection for selected filesystems, batch workflows, provenance package export, optional emulator/Gotek integrations.
- Advanced drive geometry, hard-sector and flippy workflows with tested device capabilities.

## 16. MVP definition of done

- A new Windows user can install FluxForge and approve official Greaseweazle host tools download without using a terminal or installing Python.
- No device attached clearly shows tools ready/device missing, while file conversion and library remain usable.
- Known working Amiga and Atari ST source disks can be captured to expected images; raw SCP preservation creates actual raw capture.
- A selected compatible image can be written only after clear overwrite confirmation, with verifiable outcome reporting.
- Live log and status accurately reflect an intentionally corrupted test disk and a disconnected device mid-job.
- A cancellation cannot leave an image incorrectly marked 'complete' or 'verified'.
- An update failure leaves last known-good `gw.exe` active.
- Filename spaces, Unicode paths, non-admin Windows user, no Internet, high-DPI display and COM port changes are tested.
- Package has documented third-party notices and passes a hardware regression checklist.

## 17. Test matrix

| Scenario | Hardware/image | Expected result |
|---|---|---|
| First-run network install | No GW tools, Internet | User-approved download + safe activation |
| First-run offline | No GW tools, offline | Manual import available; meaningful error |
| Missing device | GW installed, unplugged | Convert enabled; Read/Write disabled |
| COM changes | Plug to different USB port | Re-detect or select without stale config |
| Standard preservation | Amiga disk, SCP | Raw preservation metadata confirms correct invocation |
| Sector decode | Amiga ADF, Atari ST ST | Valid image and truthful sector statistics |
| Existing disk target | Output file exists | Safe overwrite prevention/confirmation |
| Write-protected medium | Known write-protected disk | Clear failure, no false success |
| Unreadable sector | Bad disk fixture | Partial status, detailed affected tracks |
| Input format mismatch | Ambiguous IMG/DSK | Require format confirmation |
| Raw image write | SCP/HFE | Clearly label verification limitations |
| Incompatible update | Different board/firmware | No unattended firmware flash |
| Update rollback | Download or smoke test fails | Prior tool version still usable |
| Long filename | Unicode and spaces | Proper argument handling |
| Abort in progress | Read and write jobs | Safe cancellation and truthful state |

## 18. Product principles for implementation team

1. **Preservation first.** Never silently change raw captured data into cleaned-up representations.
2. **Accuracy over appearance.** Unknown is better than a false green checkmark.
3. **Simple until asked.** First-time users get plain-language presets; experts see the actual format ID and command preview.
4. **Upstream compatibility.** Adapt to versions and capabilities instead of forking Greaseweazle internals unnecessarily.
5. **No surprise writes.** All destructive operations are guarded, visible and auditable.
6. **Local ownership.** User files, metadata and history are exportable without a cloud account.
7. **Build vertical slices.** Complete first-run -> Read -> report before implementing exotic format inspectors.

## 19. Engineering handoff prompt

> Build **FluxForge**, a polished Windows 10/11 x64 desktop application as defined in this PRD. Use C#/.NET 10 WPF MVVM, SQLite, safe subprocess integration to official `gw.exe`, and a separate application/service/data architecture. Prioritize a shippable end-to-end vertical slice covering first-run discovery, user-approved official tools download with rollback, device detection, an Amiga/Atari/Raw SCP Read workflow, a guarded Write workflow, live logging/progress and a basic job summary. Create an attractive high-DPI Fluent-inspired interface that is fully usable with a keyboard and clearly distinguishes tools availability from physical-device availability. Implement mock device and CLI fixtures for development without hardware. Never simulate successful device operations in production, never silently modify raw flux data, and never run firmware updates without explicit user confirmation. Add automated tests, a Windows packaging plan and development instructions. Defer flux waveform analytics, exotic filesystem browsers and recovery merging until the core workflows are robust. Validate exact GW CLI options against the installed version instead of assuming forward compatibility.

## 20. Reference links

- Official host tools and releases: https://github.com/keirf/greaseweazle and https://github.com/keirf/greaseweazle/releases
- Official Greaseweazle wiki: https://github.com/keirf/greaseweazle/wiki
- Supported image types and raw-flux caveat: https://github.com/keirf/greaseweazle/wiki/Supported-Image-Types
- Official firmware and releases: https://github.com/keirf/greaseweazle-firmware/releases
- Existing GUI: https://github.com/FrankieTheFluff/FluxMyFluffyFloppy
- Existing GUI: https://github.com/Foosie/GreaseweazleGUI-dotNET
