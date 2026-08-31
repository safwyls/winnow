---
id: TASK-5
title: Build merge execution and enforce canonicality in the repository
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - resolve
  - data
dependencies: []
priority: high
ordinal: 5000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The soft-match queue proposes merges and stores confirmations, but nothing applies them. 23 cross-store pairs are pending on the user's library. The `ON DELETE CASCADE` hazard on collapsing two releases is documented and unresolved. Canonicality must be enforced in the repository layer, not by callers. Findings F09 (P1) and F20. Sources: stabilization-2026-08-28.md Group 2; ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A confirmed merge pair collapses to one canonical release with the other's external ids preserved
- [ ] #2 The repository enforces canonicality; callers cannot bypass it
- [ ] #3 The CASCADE hazard is resolved, with a test proving a merge does not orphan dependent rows
- [ ] #4 Re-running a merge on an already-merged pair is a no-op
<!-- AC:END -->
