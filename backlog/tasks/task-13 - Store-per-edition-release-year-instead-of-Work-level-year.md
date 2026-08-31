---
id: TASK-13
title: Store per-edition release year instead of Work-level year
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - data
  - resolve
dependencies: []
priority: medium
ordinal: 40000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Release year is currently a Work-level field, but different editions (base game, GOTY, remaster) have different release years. The year must live on the Release to support correct matching. Finding F22. Source: stabilization-2026-08-28.md Group 2. Trigger: next release-matching change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Release year is stored per Release, not per Work
- [ ] #2 Existing Work-level years migrate to their associated Releases
- [ ] #3 Queries that depend on release year read the Release column
<!-- AC:END -->
