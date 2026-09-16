---
id: TASK-305
title: Enlarge the dragon on both loading screens
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 04:36'
updated_date: '2026-09-16 04:38'
labels: []
dependencies: []
type: enhancement
ordinal: 347000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants to try a larger loading dragon on desktop and fullscreen.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen show larger centered dragons without clipping or changing animation timing.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Increase the desktop mark from 88 to 144 and fullscreen from 100 to 160; run existing startup checks and inspect captures.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop dragon increased to 144px and fullscreen reference-canvas dragon to 160px. Animation and reveal timing are unchanged. All 22 desktop/fullscreen startup tests passed with captures enabled. Inspected desktop and fullscreen large-text screenshots in C:/Temp/winnow-305-captures; mark, glow, text and exit action remain centered and unclipped. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Enlarged both loading dragons by about 60%, verified with 22 startup tests and rendered captures.
<!-- SECTION:FINAL_SUMMARY:END -->
