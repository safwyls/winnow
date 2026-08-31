---
id: TASK-14
title: Implement Dead bucket raw facts and query branch
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - data
  - recommend
dependencies: []
priority: low
ordinal: 14000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Dead bucket (games whose servers have been shut down or that are permanently unplayable) has no raw-fact storage or query branch. Finding F23. Source: stabilization-2026-08-28.md Group 2. Trigger: when the Dead bucket is implemented.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A raw fact marks a release as dead with a source and date
- [ ] #2 The bucket query surfaces dead releases in a dedicated bucket
- [ ] #3 A dead release does not appear in the active recommendation pool
<!-- AC:END -->
