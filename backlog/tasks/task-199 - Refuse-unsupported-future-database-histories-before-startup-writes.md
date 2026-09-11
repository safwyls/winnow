---
id: TASK-199
title: Refuse unsupported future database histories before startup writes
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 06:18'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Data/DatabaseInitializer.cs:80'
  - 'src/Winnow.Data/DatabaseInitializer.cs:108'
  - 'docs/releases.md:85'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 230000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R11. Evidence: Reproduced. DatabaseInitializer considers pending embedded migrations but does not reject unknown applied journal entries. A current32-migration database with an added Winnow.Data.Migrations.9999_future_schema.sql entry is accepted. The release guide says an older binary must not reopen a database a newer build may have migrated. Downgrade safety depends on the user. This is a missing compatibility guard, not evidence of current schema corruption.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Startup validates supported schema history before migration or service writes and refuses an unsupported future/incompatible history with an actionable diagnostic.
- [x] #2 Legacy Hoard journal renaming, supported old/current histories and valid interrupted upgrades continue to work.
- [x] #3 Isolated startup tests verify refusal without modifying the incompatible database and consistent failure behavior for desktop/fullscreen startup.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect journal initialization and legacy rename handling. 2. Reject unknown applied Winnow migration identities before any startup mutation while permitting supported history and interrupted upgrades. 3. Add current/old/future and unchanged-on-refusal tests, update the owning schema/release contract and verify focused initialization tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only compatibility preflight precedes WAL configuration and legacy journal repair. Known old/current histories and interrupted upgrades retain existing behavior. Verified 65 focused Release tests including database compatibility/backup/migration/legacy cases; seven process-level startup tests additionally passed after using actual persisted fullscreen preferences. Unsupported current and Hoard future names leave database bytes and journal mode unchanged; no backup is created on refusal.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Refuse unsupported migration histories before writes, preserving legacy rename compatibility and interrupted upgrades. Verified isolated database and normal/fullscreen-preference process regressions.
<!-- SECTION:FINAL_SUMMARY:END -->
