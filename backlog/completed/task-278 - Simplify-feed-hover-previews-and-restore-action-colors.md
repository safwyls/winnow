---
id: TASK-278
title: Simplify feed hover previews and restore action colors
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 01:35'
updated_date: '2026-09-14 01:39'
labels: []
dependencies: []
ordinal: 320000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make the preview informational and keep game navigation directly on the tile.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Preview opens immediately on hover and closes immediately on exit, including entry into the preview, and contains no actions.
- [x] #2 Tile click and keyboard activation open full details; independent feed actions retain behavior and original colors.
- [x] #3 Interaction and rendering checks pass and design guidance matches; fullscreen assessed separately.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Remove timers and controls, use attached flyout for hover only, route activation to details, restore Azure/Amber/TextDim strokes, update tests and documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed hover timers and all popup buttons. Tile click/Enter/Space opens details directly through the existing command. Popup closes on tile exit and explicit native-popup entry. Restored Azure, Amber and TextDim strokes from the original feed. Desktop guidance updated; fullscreen uses its separate presentation and was not changed. Validation: 23 focused UI tests passed covering interaction, lifecycle, colors, rendering and supplemental desktop/fullscreen feeds. Inspected actual hover captures at 900 and 1600 pixels in C:\Temp\winnow-feed-immediate-captures.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Simplified feed previews to immediate informational hover bubbles, restored direct tile details navigation and original action colors. Verified 23 focused UI tests and wide/narrow captures.
<!-- SECTION:FINAL_SUMMARY:END -->
