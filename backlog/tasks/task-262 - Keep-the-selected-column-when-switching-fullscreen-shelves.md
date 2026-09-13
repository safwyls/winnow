---
id: TASK-262
title: Keep the selected column when switching fullscreen shelves
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 19:15'
updated_date: '2026-09-13 19:17'
labels: []
dependencies: []
ordinal: 304000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Carry the current visible column across For you shelves instead of restoring a separate selected column for each shelf. Verify Library and Search retain their existing column navigation; desktop navigation is outside this fullscreen request.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Shelf changes carry the current visible column and clamp on shorter rows, including revisits and trigger navigation.
- [x] #2 Retained row controls and sliding transitions remain intact; Library and Search column behavior is verified.
- [x] #3 Document fullscreen behavior and record desktop scope and validation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Update shelf selection before moving the viewport, preserving each shelf overflow page. Add focused navigation regression tests and run fullscreen tests with a scratch Release build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
For you now carries the visible column through the common shelf-switch path (arrows, triggers, wheel and indicator), clamping to the last available card while keeping the destination overflow page. No row invalidation or transition changes. Added a headless UI regression covering changed columns on revisits, trigger navigation, short shelves and retained row identity. Library/Search continue using shared FullscreenGridState column movement; existing fullscreen grid/navigation coverage passed. Desktop is unchanged because this request concerns fullscreen shelf navigation. Validation: Release UI build and all 229 Fullscreen-filtered UI tests passed with scratch output C:\Temp\winnow-column-verify; git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Carried visible selection columns between fullscreen shelves instead of restoring independent selections. Documented behavior and verified all 229 fullscreen UI tests, including the new navigation regression.
<!-- SECTION:FINAL_SUMMARY:END -->
