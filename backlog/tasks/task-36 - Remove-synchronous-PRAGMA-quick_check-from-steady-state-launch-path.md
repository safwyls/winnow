---
id: TASK-36
title: Remove synchronous PRAGMA quick_check from steady-state launch path
status: Done
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 22:41'
labels:
  - infra
  - data
milestone: m-4
dependencies: []
priority: low
ordinal: 3200
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`PRAGMA quick_check` runs synchronously on every launch while the legacy data directory exists. It is new pre-window I/O that persists until the user deletes the old directory. Small today, but the cost grows with database size. No finding ID; listed in stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 `PRAGMA quick_check` does not run on the synchronous startup path
- [x] #2 Integrity verification either runs asynchronously after first paint or only on migration
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Trace data-location adoption and integrity-check calls. 2. Restrict quick_check to legacy migration/adoption rather than ordinary startup. 3. Add focused regression coverage and record validation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced data-location adoption checks with a lightweight SQLite table probe. Full quick_check remains at migration and staged-copy integrity boundaries.

Validation: parent ran the Release focused suite (268 passing tests), including WinnowDataLocationTests. Steady-state directory choice now uses the lightweight SQLite identity probe; full quick_check remains only at migration and staged-copy integrity boundaries.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed quick_check from steady-state data-location selection by using a lightweight database identity probe. Release focused data-location tests passed; full integrity checks remain on migration and staged-copy paths.
<!-- SECTION:FINAL_SUMMARY:END -->
