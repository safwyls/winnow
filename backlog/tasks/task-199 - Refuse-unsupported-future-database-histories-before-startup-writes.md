---
id: TASK-199
title: Refuse unsupported future database histories before startup writes
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Startup validates supported schema history before migration or service writes and refuses an unsupported future/incompatible history with an actionable diagnostic.
- [ ] #2 Legacy Hoard journal renaming, supported old/current histories and valid interrupted upgrades continue to work.
- [ ] #3 Isolated startup tests verify refusal without modifying the incompatible database and consistent failure behavior for desktop/fullscreen startup.
<!-- AC:END -->
