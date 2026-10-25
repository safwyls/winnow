---
id: TASK-279
title: Constrain feed previews to the app window
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 01:51'
updated_date: '2026-09-14 01:53'
labels: []
dependencies: []
ordinal: 321000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Native popups can extend below the window when hovering a partially visible feed tile.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Preview stays within client bounds at bottom and side edges, with its pointer aimed toward the tile.
- [x] #2 Hover and direct-details interactions remain working; window-edge regression tests pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use custom placement clamped to client bounds using measured popup size, cap preview dimensions, and verify edge placement and existing interactions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Custom placement uses measured popup size and an 8px client-area inset; preview width and height are capped to available client dimensions. The existing pointer tracks final placement. Verified 24 UI tests including bottom-left, bottom-right and partially scrolled top-right tiles, hover exit, direct details activation, feedback actions, bubble rendering and supplemental desktop/fullscreen feeds. Fullscreen presentation is separate and unchanged. Updated design-system.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Feed previews now shift inside the Winnow client area rather than relying on monitor constraints. Verified 24 focused UI tests including partially visible edge tiles.
<!-- SECTION:FINAL_SUMMARY:END -->
