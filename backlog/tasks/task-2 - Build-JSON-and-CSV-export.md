---
id: TASK-2
title: Define and deliver portable library export beyond acquisition CSV
status: To Do
assignee: []
created_date: '2026-08-29 21:51'
updated_date: '2026-09-11 13:59'
labels:
  - data
  - infra
milestone: m-1
dependencies: []
documentation:
  - game-library-design.md
  - ROADMAP.md
priority: low
ordinal: 16500
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Provide portable Winnow library data beyond the existing schema-v2 acquisition CSV. Define a versioned JSON transfer format and useful CSV views for external analysis. Resolve the general importer contract before implementation; no general Winnow JSON importer currently exists. Preserve the delivered acquisition export, including account provenance and unknown-value semantics.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Define included domain records, identity relations, account/source provenance, settings and exclusions. Explicitly decide whether general import is part of this deliverable before implementation.
- [ ] #2 Versioned JSON preserves the approved records and distinctions, including unknown values and account-specific facts, with a documented compatibility policy.
- [ ] #3 If import is included, importing into an empty database preserves the approved query results and relationships; otherwise documentation explicitly states that export has no round-trip importer.
- [ ] #4 Additional CSV views have a documented analysis scope and do not promise lossless round-trip or identical coverage to JSON.
- [ ] #5 Desktop and fullscreen expose consistent export behavior, with cancellation and write-failure coverage; the existing acquisition CSV remains supported.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: src/Winnow.App/Services/AcquisitionExport.cs and AcquisitionExportTests provide acquisition CSV schema v2, provenance, quoting and output handling. LibrarySettingsView and FullscreenLibraryToolsPage share the export command. game-library-design.md section 7 defers general JSON transfer. Export is useful for portability and external analysis; it is no longer the first consumer of acquisition columns.
<!-- SECTION:NOTES:END -->
