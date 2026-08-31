---
id: TASK-31
title: Add immutable hash verification for shipped migrations
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - data
  - infra
milestone: m-4
dependencies: []
priority: medium
ordinal: 31000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Shipped SQL migrations are append-only by convention, but nothing enforces immutability. A silent edit to a shipped migration can corrupt an existing database on upgrade. Finding F46. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each shipped migration has a recorded hash
- [ ] #2 Startup or CI verifies that no shipped migration's content has changed
- [ ] #3 A test demonstrates that altering a shipped migration triggers a failure
<!-- AC:END -->
