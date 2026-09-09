---
id: TASK-4
title: Build full-screen gamepad mode
status: In Progress
assignee:
  - codex
created_date: '2026-08-29 21:52'
updated_date: '2026-09-09 17:43'
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
- [x] #4 The full-screen surface shows a clock
- [x] #5 Connected controller battery level is shown when the platform reports it, and the absence of that reading is not treated as an error
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect navigation and modal focus; add platform controller polling and deterministic input handling; implement fullscreen presentation with clock and optional battery; add controller navigation and text entry; verify headless and input tests and document hardware limits.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented fullscreen shell scaling, clock, optional XInput battery, Windows XInput and Linux joydev polling, D-pad/left-stick spatial and existing grid navigation, shoulder focus cycling, right-stick scrolling, and on-screen text entry with masked password preview. Shared shell commands and dialogs are reused. All 4224 tests passed across the five suites; two Linux-only process tests skipped on Windows. Full UI suite: 133 passed. Headless rendering verified minimum-size fullscreen keyboard and visible exit. Review found and fixed ordinary flyout D-pad navigation, covered by a regression test. Physical controller and complete controller-only operation remain unverified; native file dialogs and embedded sign-in retain their own input requirements. Keep TASK-4 In Progress because acceptance criteria 1-3 are broader than the verified shell behavior.

Release solution build passed with zero warnings and zero errors. Changes are on codex/gamepad-fullscreen.
<!-- SECTION:NOTES:END -->
