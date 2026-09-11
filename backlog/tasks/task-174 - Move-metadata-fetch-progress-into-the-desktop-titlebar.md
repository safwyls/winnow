---
id: TASK-174
title: Move metadata fetch progress into the desktop titlebar
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 15:49'
updated_date: '2026-09-10 15:52'
labels: []
dependencies: []
ordinal: 205000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Move the fetching-details label and remaining-title count from the rail to the titlebar as requested.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop titlebar shows the current fetch label and count only while active; rail no longer shows the field.
- [x] #2 Caption dragging and accessibility remain intact; fullscreen presentation is assessed and recorded.
- [x] #3 Visual documentation matches the placement and relevant checks pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Move the existing bindings into a compact passive caption field, remove the rail row, update the visual spec and historical decisions, and verify build plus existing fetch and fullscreen checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: compact text field before window controls; passive hit testing retains the caption drag strip, with accessible Name and ItemStatus bindings. Rail status and its row removed. Fullscreen: existing separate interface continues hiding desktop chrome; headless round-trip test verifies the shared count updates while hidden and reappears correctly. Build: zero warnings/errors. Full solution tests: 4,368 passed, 2 Linux-only tests skipped on Windows. Includes 235 UI tests and a new minimum-width caption lifecycle/accessibility/fullscreen test. Native Windows dragging was not manually exercised; its existing handler is unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Moved metadata progress from the rail to a compact desktop titlebar line, visible only during fetching. Updated visual spec and decision history. Verified clean build, 4,368 passing tests and 2 expected Linux-only skips; fullscreen round trip preserves current progress.
<!-- SECTION:FINAL_SUMMARY:END -->
