---
id: TASK-12
title: Validate bucket threshold invariants
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - data
  - recommend
dependencies: []
priority: medium
ordinal: 12000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Bucket and staleness query thresholds are not validated at construction time. A misconfigured threshold can silently produce wrong bucket membership. Finding F21. Source: stabilization-2026-08-28.md Group 2. Trigger: next bucket or staleness query change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Invalid threshold values (zero, negative, inverted ranges) are rejected at construction
- [ ] #2 A test demonstrates rejection of each invalid case
<!-- AC:END -->
