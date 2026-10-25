---
id: TASK-371
title: Grow Rift cover previews into full game details
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 21:03'
updated_date: '2026-09-28 21:16'
labels: []
dependencies: []
type: spike
ordinal: 407000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The fixed Library details column takes space from browsing and feels disconnected from the covers. The user wants the earlier adjacent cover popover, with its portal expanding into the full details view when opening a game.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Library uses its full browsing width and shows an adjacent portal preview on hover or keyboard focus, with viewport-aware placement and a reachable View game action.
- [x] #2 Opening details expands the originating portal over the content view into a complete game page; Back restores the browsing position and cover focus.
- [x] #3 Desktop and fullscreen support dismissal, reduced motion, long titles and artwork fallback; verify the mock and save evidence without changing Afterglow or production.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Replace the Library inspector with a reusable cover preview adapter using the existing portal material. 2. Build a full details surface and a portal expansion transition that reveals a fixed layout. 3. Verify both presentation modes, keyboard flow, edge placement and motion/artwork fallbacks; document and capture.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed the fixed Library inspector and restored cover-adjacent portals through a dedicated library-preview adapter. Pointer hover, a 240 ms crossing allowance, keyboard focus, Tab into actions, Escape dismissal, left/right placement and narrow docking share the existing portal renderer and activity controls. A separate details controller reveals a fixed full-page layout by expanding the source portal for 680 ms, then stops the renderer. Back preserves the existing gallery DOM, scroll and originating focus. Browser checks passed in desktop and fullscreen, including real pointer hover/crossing/click, keyboard actions, longest title, 390 px docked preview and missing-art details. The narrow return restored exactly 210 px scroll and original cover focus. Still mode skipped the transition. Discover used its persistent portal for the same entrance. No browser warnings/errors or horizontal overflow were observed. Controlled-clock checks passed source bounds, intermediate growth, completion event, loop termination and viewport corner coverage at shape extremes. All JavaScript syntax, HTML unique IDs/local references and git whitespace checks passed. Captures 08 and 09 and README document the mock. Production and Afterglow remain unchanged; physical controller/touch hardware and GPU performance were not measured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Library now uses the full width with adjacent portal previews. View game expands the portal into an artwork-led details page, with Back/Escape restoring browsing position and focus. Verified desktop/fullscreen, pointer and keyboard flow, long titles, narrow missing-art fallback, reduced motion and expansion lifecycle.
<!-- SECTION:FINAL_SUMMARY:END -->
