---
id: TASK-29
title: Complete reduced-motion coverage across animated surfaces
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - accessibility
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 29000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reduced-motion support is partial, not absent. Remaining animated surfaces must respect the OS reduced-motion preference. Finding F47. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every animated surface respects `prefers-reduced-motion` or the OS accessibility setting
- [ ] #2 A test or audit confirms no animated surface is uncovered
<!-- AC:END -->
