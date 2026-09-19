---
id: TASK-341
title: Restore vivid desktop tile art on keyboard and controller focus
status: Done
assignee:
  - '@codex'
created_date: '2026-09-18 00:37'
updated_date: '2026-09-18 00:43'
labels: []
dependencies: []
type: bug
ordinal: 383000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Feed and Library tiles show focus chrome but remain dormant-looking when reached by keyboard or controller, unlike pointer hover.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Pointer focus does not latch the highlight after pointer exit; recycling and detach clear interaction state.
- [x] #2 Focused regression tests cover both desktop surfaces and fullscreen cover behavior remains intact.
- [x] #3 Desktop Feed action focus and Library selection restore vivid artwork; clearing focus or selection restores dormancy unless still hovered.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Include keyboard action focus in shared tile vivid state and stop Feed from overwriting it with pointer-only state. 2. Add focus/hover interaction regressions and update design wording. 3. Run focused desktop and fullscreen UI tests and review rendered output before pushing a PR.

Library directional navigation holds focus on the window and sets IsSelected. Include selection in DisplayAlpha (list rows still use DormancyAlpha), alongside the existing focus reveal state.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop shared tile now includes Tab/directional action focus in vivid state. Feed no longer overwrites that state with pointer-only hover. Library arrow/controller navigation selects with window focus, so IsSelected also restores DisplayAlpha and notifies the artwork bindings. List rows retain DormancyAlpha; fullscreen already uses its independent selected state. Five new rendered-opacity regressions cover both desktop surfaces, Tab/directional input, focus loss, pointer overlap/exit, pointer focus, and selection notifications. 74 Release UI tests passed including existing recycle/detach and fullscreen Home/cover checks; 46 LibraryViewModel tests passed. Reviewed headless captures at C:\Temp\winnow-tile-focus-captures. No physical controller session was run; tests exercise its directional focus path. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored vivid artwork for keyboard/controller focus in desktop Feed and Library and for Library tile selection. Verified 74 focused UI and 46 LibraryViewModel tests; reviewed rendered focus captures. Fullscreen selection remains independent and its focused regression checks pass.
<!-- SECTION:FINAL_SUMMARY:END -->
