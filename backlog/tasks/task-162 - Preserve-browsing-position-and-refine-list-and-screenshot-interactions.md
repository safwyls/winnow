---
id: TASK-162
title: Preserve browsing position and refine list and screenshot interactions
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 16:28'
updated_date: '2026-09-08 16:35'
labels: []
dependencies: []
ordinal: 194000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fix six follow-up UI issues: list additions reset browsing position, modal alignment and visual hierarchy, rail glyphs, feed bookmark alignment, list removal context action, and screenshot wheel navigation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Adding games to existing or new lists preserves the current browsing context and scroll position
- [x] #2 List modal aligns row and field edges and uses clear graphical indicators
- [x] #3 Rail controls use vector icons and feed feedback icons are centered
- [x] #4 Static list context menu removes selected games from the list
- [x] #5 Screenshot wheel input scrolls horizontally without Shift
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Trace collection refresh and focus scroll behavior; implement independent visual and input fixes; add focused regression tests; render modal and run build and affected suites.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Preserved visible source and selection for unchanged membership recounts; new-list additions stay in the browsing context. Verified actual grid/list offsets, new/existing destination persistence, modal row/field alignment with 2 and 40 lists, static-only context menu, and wheel/Shift/trackpad screenshot behavior. Reviewed rendered short and long modal captures. Build: zero warnings/errors. Tests: 3749 core and 78 UI passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed all six reported issues: browsing position preservation, aligned illustrated list modal, vector rail controls, centered feed bookmark, static-list context removal, and horizontal screenshot wheel scrolling. Verified with 3827 passing tests and rendered modal inspection.
<!-- SECTION:FINAL_SUMMARY:END -->
