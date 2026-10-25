---
id: TASK-276
title: Redesign desktop feed as cover shelves with quick details
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 23:05'
updated_date: '2026-09-13 23:19'
labels: []
dependencies: []
ordinal: 318000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The approved mock presents curated cover rows with quieter recommendation text, hover actions and a compact details flyout.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop shelves show six portrait covers at wide sizes and readable horizontal overflow at narrow sizes, matching the approved visual direction.
- [x] #2 Hover and keyboard focus reveal feedback/list actions; clicking opens quick details with primary action and full details navigation.
- [x] #3 Existing feedback, undo, impression and fullscreen behavior remain covered; rendered desktop layouts are inspected.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Replace desktop feed card composition and add an anchored quick-details flyout. Adapt shelf layout and keyboard navigation for six-column horizontal rows. Preserve shared view model behavior and test desktop interactions plus fullscreen regressions. Update design documentation and inspect captures.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Prior branch history through 91db9a8 pushed to origin before implementation. Desktop cards now use portrait shelves with explicit quick details and focus/hover action strip; shared scoring and fullscreen presentation unchanged. Focused checks: 130 feed/store layout unit tests, 22 feedback/impression/supplemental UI tests, and 12 card interaction tests pass. Inspected 1600px and 900px captures with local cached artwork in C:\Temp\winnow-feed-shelf-captures; no production host or library writes used. Final full UI run pending.

Final verification: 678/678 UI tests passed, including fullscreen and cover presentation regressions. 130/130 focused feed/store layout unit tests passed. Capture checks covered six-slot wide layout, narrow horizontal overflow and open themed quick details. Popup tests cover explicit open, feedback isolation, Undo, Escape focus restoration, held countdown, non-stacking and rebind/detach cleanup.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented approved desktop feed direction with six portrait slots per shelf, responsive horizontal overflow, hover/focus actions and click-open quick details. Preserved feedback, Undo, impression accounting, shared artwork settings and fullscreen behavior. Updated design specification; inspected wide/narrow renders and passed 678 UI plus 130 focused unit tests.
<!-- SECTION:FINAL_SUMMARY:END -->
