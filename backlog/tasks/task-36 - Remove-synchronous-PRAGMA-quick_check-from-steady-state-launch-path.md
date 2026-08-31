---
id: TASK-36
title: Remove synchronous PRAGMA quick_check from steady-state launch path
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - infra
  - data
milestone: m-4
dependencies: []
priority: low
ordinal: 36000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`PRAGMA quick_check` runs synchronously on every launch while the legacy data directory exists. It is new pre-window I/O that persists until the user deletes the old directory. Small today, but the cost grows with database size. No finding ID; listed in stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 `PRAGMA quick_check` does not run on the synchronous startup path
- [ ] #2 Integrity verification either runs asynchronously after first paint or only on migration
<!-- AC:END -->
