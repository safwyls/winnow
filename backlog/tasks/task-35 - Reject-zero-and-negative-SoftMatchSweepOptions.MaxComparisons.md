---
id: TASK-35
title: Reject zero and negative SoftMatchSweepOptions.MaxComparisons
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - resolve
milestone: m-4
dependencies: []
priority: medium
ordinal: 53000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`SoftMatchSweepOptions.MaxComparisons` accepts zero and negative values. A zero truncates the sweep forever without progress, the same class of validation gap as F21. No finding ID; listed in stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Construction rejects zero and negative values with an `ArgumentOutOfRangeException`
- [ ] #2 A test demonstrates rejection of zero and negative inputs
<!-- AC:END -->
