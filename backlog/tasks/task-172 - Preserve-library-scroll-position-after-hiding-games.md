---
id: TASK-172
title: Preserve library scroll position after hiding games
status: Done
assignee:
  - '@codex'
created_date: '2026-09-09 15:35'
updated_date: '2026-09-09 15:40'
labels: []
dependencies: []
type: bug
ordinal: 204000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Hiding a game resets the library to the top.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hide preserves grid and list scroll positions
- [x] #2 Filter changes still reset scrolling
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Preserve viewport during hide reloads and add headless regression coverage.
<!-- SECTION:PLAN:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserved grid and list offsets during hide reloads, with clamping after layout and source guards. Six headless regression cases pass for single and multi-game hides, bottom offsets and subsequent search resets. Build passed with zero warnings or errors. Full suite identified three failures; corrected the test selection setup and renamed identity inventory entry, then all six scroll tests and five inventory tests passed. Other suites passed (108 covers, 160 recommendation, 3800 other core and 110 other UI cases); two Linux-only tests skipped on Windows.
<!-- SECTION:FINAL_SUMMARY:END -->
