---
id: TASK-195
title: Preserve Epic install state when local manifest scans are incomplete
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Manifest enumeration/parsing returns explicit completeness and absence clears install facts only after a complete authoritative scan.
- [ ] #2 Locked, malformed, oversized, disappearing and enumeration-failing inputs preserve prior install state while reporting the incomplete scan.
- [ ] #3 Startup, watcher, scheduled and remote reconciliation paths consume the same completeness contract; actions remain consistent on desktop and fullscreen.
<!-- AC:END -->
