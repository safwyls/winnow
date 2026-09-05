---
id: TASK-4
title: Build full-screen gamepad mode
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
updated_date: '2026-09-04 18:15'
labels:
  - ui
  - accessibility
milestone: m-3
dependencies:
  - TASK-3
priority: low
ordinal: 60000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the M10 deliverable: a 10-foot UI navigable entirely by gamepad. This is a second complete UI surface with its own focus management, controller input, navigation model, and layouts. Deliberately last because it serves the narrowest user segment. Source: ROADMAP.md section 4, M10 row.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every screen and interaction is reachable via gamepad
- [ ] #2 Focus is always visible
- [ ] #3 No mouse or keyboard is required for any operation
- [ ] #4 The full-screen surface shows a clock
- [ ] #5 Connected controller battery level is shown when the platform reports it, and the absence of that reading is not treated as an error
<!-- AC:END -->
