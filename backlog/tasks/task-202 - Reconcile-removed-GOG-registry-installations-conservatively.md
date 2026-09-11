---
id: TASK-202
title: Reconcile removed GOG registry installations conservatively
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:30'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Ingest.Gog/GogLibrarySource.cs:147'
  - 'src/Winnow.Ingest.Gog/GogInstalledGameRegistry.cs:51'
  - 'src/Winnow.App/Services/LibrarySyncService.cs:202'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 233000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R14. Evidence: Source verified. GOG registry-only installations emit candidates while present. Removing a registry entry emits no candidate, and LibrarySyncService has no corresponding complete-inventory absence reconciliation for that source. The ownership can remain installed indefinitely. The registry interface also cannot distinguish a complete empty scan from an unreadable registry. Launch actions and Ready to play eligibility can remain stale after uninstall on machines without usable Galaxy data.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A complete registry inventory reconciles missing installations by clearing only install-specific facts while preserving ownership and history.
- [x] #2 Unreadable or partial registry scans cannot produce false uninstalls.
- [x] #3 Tests cover registry-only install/uninstall, restart, absent Galaxy data and inaccessible registry state; desktop/fullscreen actions refresh consistently.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add explicit GOG registry scan completeness, preserving readable positives and distinguishing missing keys from failed enumeration. 2. Persist identifiers proven by registry observations in migration0036; clear only known registry install flags/paths after a complete current scan, preserving ownership/history and unknown legacy provenance. 3. Refresh cheap registry evidence under the sync gate while Galaxy copies remain outside, and prevent old remote Galaxy snapshots from restoring install state. 4. Add installed/uninstalled/restart/partial and stale-remote regression tests, coordinate shared library refresh behavior, document limits and verify focused GOG tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Backend verified: Windows Release focused GOG/Galaxy/identity-inventory/lifecycle regression run passed72/72; GOG coverage includes registry-only uninstall, recreated factory/repository after restart, unreadable/partial positive inventories, unknown legacy provenance, stale remote Galaxy replay, and ambient transaction/savepoint rollback after a caught batch failure. Migration0036 persists positive registry identifiers; new IGogInstallStateRepository clears install flag/path only. Current registry refresh runs under gate; remote false flag discards old Galaxy install facts. Program registration wired by coordinator. Spec4.8 documents availability and legacy limitation. Native Windows registry access remains read-only; tests use captured registry fixture values and injected enumeration failures, never machine registry writes. AC3 remains open for desktop/fullscreen action refresh verification owned by UI agent.

Additional surface evidence: StoresAccountContextTests verifies real persisted GOG ownership reloads in desktop GameDetailsView and FullscreenDetailsPage. Unknown preserves Play and path; authoritative uninstall changes the same open context to Install and removes path. Combined surface suite passed17/17 in tests/Winnow.Ui.Tests/TestResults/ui-account-context.trx.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Persisted positive GOG registry provenance and reconcile complete missing inventories without removing ownership/history. Partial reads preserve installs and remote backfill cannot restore stale Galaxy facts. Verified72/72 backend checks and17/17 shared surface checks, including GOG action refresh on desktop/fullscreen Details; migration0036 integrity verified.
<!-- SECTION:FINAL_SUMMARY:END -->
