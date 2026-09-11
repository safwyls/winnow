---
id: TASK-194
title: Read a coherent GOG Galaxy snapshot across WAL checkpoints
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Ingest.Gog/GalaxyDatabaseSnapshot.cs:140'
  - 'src/Winnow.Ingest.Gog/GalaxyDatabaseSnapshot.cs:169'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 225000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R06. Evidence: Executed synthetic copy-order reproduction. GalaxyDatabaseSnapshot copies the main database and then WAL/SHM files independently. A checkpoint between those copies can combine generations. A synthetic two-table database produced snapshot (newest, old) while live data was (newest, committed); that tuple never existed in the source. PRAGMA quick_check still returned ok because structural validity does not establish a consistent snapshot. A valid-looking copy can invent a mixture of ownership or play observations. The current comment and validation imply stronger consistency than the copy algorithm provides.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Galaxy ingest reads one consistent committed source state while honoring the rule against writing any launcher files.
- [ ] #2 A deterministic test interleaves main-file copy, checkpoint/reset and WAL writes and proves that impossible mixed-generation rows are never ingested.
- [ ] #3 Unreadable/busy or unverifiable snapshots fail conservatively, and comments/specification accurately describe the consistency guarantee and any platform limits.
<!-- AC:END -->
