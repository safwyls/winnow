---
id: TASK-269
title: Open Library options from anywhere in fullscreen grid
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 21:36'
updated_date: '2026-09-13 21:40'
labels: []
dependencies: []
ordinal: 311000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Y opens the existing side action panel with My lists, Filter and sort, and Library actions. B restores the selected game and row without scrolling to the header. Preserve pointer header access and global Start menu.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Y opens Library options with lists, filters and existing contextual actions from deep in the grid.
- [x] #2 Closing options or backing out of lists and filters preserves selection and viewport; repeated Y does not stack panels.
- [x] #3 Footer guidance and design system updated; fullscreen interaction checks pass and desktop behavior assessed.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Route Library Y to shared action menu; add My lists and order common actions first; verify shell overlays and deep-grid return, list/filter routes and global menu; update docs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Library Y now opens the same Library options side panel as header More, with My lists and Filter and sort first, followed by existing search/list/contextual actions. Footer and design system updated. Validation: 64 focused Release UI tests passed across new Library options, browse, row navigation, overlays, Quick menu and interaction suites. Three new cases use a 900-game temporary library at row60/column2 and verify selection and viewport restoration after closing options or returning from lists/filters, repeated Y suppression, and Start Quick menu return. Screenshot inspected with reduced motion after the first capture caught the panel entrance in progress; all three new cases passed again. Desktop source unchanged; existing interaction test verifies desktop search/sort/bucket remain untouched. No live-library or physical-controller run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Y Library options from anywhere in the fullscreen Library grid, preserving the selected game and viewport through menu/list/filter return. Updated footer and design guidance. Verified 64 focused UI tests and rendered side panel with a 900-game fixture.
<!-- SECTION:FINAL_SUMMARY:END -->
