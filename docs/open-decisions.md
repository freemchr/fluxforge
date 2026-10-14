# Open decisions

Decisions the PRD leaves open. Each needs an owner and an answer before the stage noted.

| # | Decision | Needed by | Current default in code |
|---|---|---|---|
| D1 | ~~License and business model~~ **Decided (14 Oct 2026): open source, MIT License.** Free app, optional Buy Me a Coffee. | Done | `LICENSE` |
| D2 | **Code-signing certificate** (OV/EV or Azure Trusted Signing) and budget. Unsigned builds trigger SmartScreen. | Beta | Unsigned |
| D3 | **Installer technology**: Velopack (recommended: per-user, delta updates, portable zip) vs MSIX vs WiX. | Phase 1 packaging | Not started |
| D4 | **Firmware compatibility source** for FR-18: where does "compatible" come from? Hand-maintained table, upstream data, or show versions only? | Phase 2 | Versions shown only, no compatibility claim |
| D5 | **Hardware lab**: which boards (V4/V4.1/F7), drives and reference disks are available for the regression checklist? | End of Phase 0 | Fake gw only |
| D6 | **Raw-capture decode check**: is `--format X --raw` (decode while capturing raw) acceptable as the track map source, or should a separate `gw convert` pass be used? | After spike A5 | `--format X --raw` |
| D7 | **Job history storage**: JSON files per job now; move index to SQLite with the Library (FR-13/14)? | Phase 2 | JSON per job (source of truth) |
| D8 | **Product name / trademark check** for "FluxForge". | Before public launch | Working title |
