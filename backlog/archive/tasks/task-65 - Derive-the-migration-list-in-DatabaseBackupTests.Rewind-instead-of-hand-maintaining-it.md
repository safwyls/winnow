---
id: TASK-65
title: >-
  Derive the migration list in DatabaseBackupTests.Rewind instead of
  hand-maintaining it
status: To Do
assignee: []
created_date: '2026-09-01 04:07'
updated_date: '2026-09-11 13:59'
labels:
  - infra
  - data
dependencies: []
priority: low
ordinal: 103000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
This request is already covered by the migration-baseline rewind implementation delivered with TASK-70.7. DatabaseBackupTests derives schema and journal boundaries from embedded migrations; there is no hand-maintained migration list left to replace.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Adding a new migration does not require editing Rewind for the SchemaVersions half
- [ ] #2 The test still fails loudly if a migration rebuilds a table Rewind cannot restore, rather than passing silently
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Archive reason: duplicate of completed TASK-70.7 work. DatabaseBackupTests derives the pre-0012 schema and RewindBoundary, recreates differing objects from baseline DDL and removes later journal entries. SQL/DDL failures propagate. The targeted backup/account/provenance run passed 40 tests on September 11. A future unsupported rewind case should be reported as a concrete defect rather than retain this obsolete request.
<!-- SECTION:NOTES:END -->
