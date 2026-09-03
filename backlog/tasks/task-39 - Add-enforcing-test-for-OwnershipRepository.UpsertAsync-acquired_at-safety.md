---
id: TASK-39
title: Add enforcing test for OwnershipRepository.UpsertAsync acquired_at safety
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-08-29 21:55'
labels:
  - data
dependencies: []
priority: low
ordinal: 66000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`OwnershipRepository.UpsertAsync` could overwrite an imported `acquired_at` if a Steam candidate source ever starts supplying `AcquiredAt`. Today both sources hard-code null, so the safety is incidental. An enforcing test should lock this invariant. Source: ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A test proves that upserting a candidate with a non-null `AcquiredAt` does not overwrite an existing imported `acquired_at`
- [ ] #2 The test fails if the guard is removed
<!-- AC:END -->
