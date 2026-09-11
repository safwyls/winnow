---
id: TASK-194
title: Read a coherent GOG Galaxy snapshot across WAL checkpoints
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 06:40'
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
- [x] #1 Galaxy ingest reads one consistent committed source state while honoring the rule against writing any launcher files.
- [x] #2 A deterministic test interleaves main-file copy, checkpoint/reset and WAL writes and proves that impossible mixed-generation rows are never ingested.
- [x] #3 Unreadable/busy or unverifiable snapshots fail conservatively, and comments/specification accurately describe the consistency guarantee and any platform limits.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Replace independent copies with read-only Windows handles denying write/delete for the database and existing WAL throughout copy; reject journal recovery and unsupported live platforms. 2. Keep quick_check as structural validation only, rebuild SHM solely in the private directory and defer busy source reads until a later scan. 3. Provide an explicit caller-owned immutable snapshot path for fixtures and non-Windows use without claiming live safety. 4. Add deterministic native SQLite writer/checkpoint, WAL-generation, held-writer, no-WAL and unchanged-source tests; update section 4.8 and decisions, then run focused GOG tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced sequential unguarded copies with read-only Windows main/WAL handles denying write/delete throughout copying. Existing native SQLite writer handles decline the read; missing/unreadable inputs and any rollback journal fail conservatively. No source SQLite connection or SHM copy; private SHM is rebuilt only beside the copied DB. quick_check is structural validation only. Non-Windows live input is explicitly unsupported; CopyImmutable permits caller-owned immutable pairs without claiming live safety. Native SQLite interleave test rejects writes/checkpoint after main copy, then confirms the next snapshot after actual checkpoint/rollover returns latest|next rather than an impossible tuple. Added held-writer, WAL pair, no-WAL, rollback journal and unchanged-source tests. Windows Release GOG/Galaxy suite passed 50/50. Non-Windows branch is source-verified, not runtime-tested here. Both desktop/fullscreen consume the same persisted library; deferred reads publish no replacement Galaxy observations. Updated section 4.8 and decisions.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Galaxy copying now establishes coherence with Windows writer-excluding file handles, validates structure separately, and defers busy or unverifiable reads. An explicit immutable-input API supports private fixtures on other platforms. Native SQLite regression tests passed 50/50.
<!-- SECTION:FINAL_SUMMARY:END -->
