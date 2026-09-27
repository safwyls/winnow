---
id: TASK-351
title: 'Polish Electron viewport, artwork, Journal bounds and Discover rotation'
status: Done
assignee:
  - codex
created_date: '2026-09-27 02:40'
updated_date: '2026-09-27 03:04'
labels: []
dependencies: []
type: bug
ordinal: 387000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User testing of Afterglow exposed scrolling and artwork layout problems, a rejected Journal statistics request, and a static Discover hero. Correct these together while preserving the approved design and theme controls.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Header and footer remain visible while bounded content panes scroll on desktop and fullscreen, including narrow windows and interface scaling.
- [x] #2 Library grid cards have consistent artwork and title geometry; details heroes have stable sizing without a grey strip and show a polished loading state.
- [x] #3 Journal sends valid whole-second UTC statistics bounds and loads without the reported validation error.
- [x] #4 Dropdown indicators are centered in their trailing space and Discover rotates among recommendations with accessible manual and pause controls that respect reduced motion.
- [x] #5 Automated frontend checks and native desktop/fullscreen inspection are recorded with updated theme guidance.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Correct shell scroll ownership, shared card/artwork sizing and dropdown geometry. 2. Fix UTC Journal bounds with focused regression coverage. 3. Add an accessible timed Discover carousel. 4. Run frontend checks, inspect desktop/fullscreen/narrow rendering using throwaway data, update documentation and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Viewport, cards, artwork and Journal corrections implemented. Native checks pass for standard desktop and ultrawide fullscreen; Discover autoplay advances through multiple recommendations. Minimum 760x560 window at 130% scale exposed insufficient result space, so the library now uses compact controls and thumbnail cards when vertical space is limited. Catalogue wrapper and duplicate hero sibling keys found in review were corrected.

Validation completed: typecheck, production build and 118 frontend tests pass (2 existing opt-in skips). Native Windows checks cover desktop, ultrawide fullscreen, minimum 760x560 at 130% scaling, both bundled themes, Journal, card alignment, details artwork and carousel controls. Rebuilt unpacked Windows app and verified packaged startup against isolated data. Evidence and screenshots: docs/spikes/2026-09-26-electron-polish/README.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Pinned shell chrome and bounded content scrolling; aligned grid cards and details artwork with a stable loading state; corrected whole-second Journal bounds and select indicators; added accessible nine-second Discover rotation. Verified 118 passing frontend tests, production build, packaged launch and native desktop/fullscreen/scaled-window checks. Updated theme guidance and example layout. No .NET source changed; Linux/macOS and physical controllers were not exercised.
<!-- SECTION:FINAL_SUMMARY:END -->
