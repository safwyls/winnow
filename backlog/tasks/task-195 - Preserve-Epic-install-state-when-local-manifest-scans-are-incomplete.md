---
id: TASK-195
title: Preserve Epic install state when local manifest scans are incomplete
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 07:30'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Ingest.Epic/EpicLibrarySource.cs:91'
  - 'src/Winnow.Ingest.Epic/EpicLibrarySource.cs:315'
  - 'src/Winnow.Ingest.Epic/EpicManifestReader.cs:63'
  - 'src/Winnow.App/Services/LibrarySyncService.cs:178'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 226000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R07. Evidence: Source verified. EpicLibrarySource treats Directory.Exists as evidence that install scanning is complete. EpicManifestReader skips locked, malformed and oversized manifests; the source then emits known catalog entries as not installed. Startup/scheduled/direct sync can therefore clear a valid install after a partial scan. Lazy file enumeration exceptions are not wholly covered by the surrounding enumeration setup catch. Transient launcher writes or unreadable files become durable false uninstall observations. Steam already has a more explicit scan-completeness boundary.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Manifest enumeration/parsing returns explicit completeness and absence clears install facts only after a complete authoritative scan.
- [x] #2 Locked, malformed, oversized, disappearing and enumeration-failing inputs preserve prior install state while reporting the incomplete scan.
- [x] #3 Startup, watcher, scheduled and remote reconciliation paths consume the same completeness contract; actions remain consistent on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Give Epic manifest directory reads an explicit completeness result, covering lazy enumeration and every unreadable or malformed file. 2. Preserve positive install observations but emit unknown for absent entries during incomplete scans and for malformed completion flags. 3. Carry scan completeness through the composed source used by startup, watcher, scheduled and remote refresh. 4. Add deterministic locked, malformed, oversized, missing/enumeration-failing and direct-sync regressions; run serialized Epic tests and update section 4.8 and decisions.

UI verification: use persisted Epic ownership and shared library reload with actual desktop/fullscreen Details; unknown observation preserves Play and complete absence switches both surfaces to Install.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented explicit EpicManifestScan completeness and a shared parser/fingerprint path for watcher and local source. Missing directories, lazy enumeration failure, locked, malformed, oversized, disappearing inputs and invalid completion flags emit unknown for absent entries while retaining readable positive facts. EpicScanResult exposes ManifestScanComplete. Deterministic integration tests show local and remote reconciliation preserve prior installed/path values until a complete scan proves absence. Release Epic suite passed 269/269. Desktop/fullscreen both consume persisted ownership through shared application sync; coordinator is supplying surface action regressions for AC3. Updated build spec section 4.8 and decisions.

UI AC3 evidence: StoresAccountContextTests reads persisted Epic ownership through LibraryViewModel into real GameDetailsView and FullscreenDetailsPage. A null install observation preserves Play and stored path; authoritative false switches the same open details to Install and clears the path. Combined surface suite passed17/17 in tests/Winnow.Ui.Tests/TestResults/ui-account-context.trx; scan completeness authority is covered by backend fixtures.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Shared Epic manifest completeness across scan and watcher paths; incomplete observations preserve install facts until complete absence. Verified269/269 backend checks and17/17 shared surface checks including persisted unknown/uninstalled action refresh on desktop/fullscreen Details.
<!-- SECTION:FINAL_SUMMARY:END -->
