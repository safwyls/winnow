---
id: TASK-368
title: Recompose Rift as an independent browsing workspace
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 19:59'
updated_date: '2026-09-28 20:20'
labels: []
dependencies: []
type: spike
ordinal: 404000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The first Rift study retains Afterglow structure. The user approves the colors but wants a distinct layout around floating covers and portals.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Replace hero and shelves with a distinct selection and details composition.
- [x] #2 Verify desktop, fullscreen, keyboard, constrained layouts, reduced motion and sample states.
- [x] #3 Document and capture the proposal without changing Afterglow or production.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Replace the page hierarchy with a collection index, spatial cover deck and persistent artwork portal. 2. Adapt selection and portal lifecycle while preserving finish and depth effects. 3. Inspect desktop and fullscreen, keyboard and constrained layouts; update study evidence and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the banner/shelves/grid with a searchable index, a three-cover deck and a persistent selected-game portal. Desktop keeps the index; fullscreen exposes it on demand; narrow and short windows scroll the workspace with fixed chrome. Verified search/empty/single-result flows, return and unplayed lenses, full long titles, keyboard selection, index dismissal, mixed/missing art, palette plus foil/tilt, portal roundness and Still mode. Browser widths covered 2025, 1280, 760 and 390 px. All JS syntax, 53 unique HTML IDs and 10 local references passed; no Rift console warnings/errors. Captures and current rationale saved in the study README. Production and Afterglow files unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Recomposed the Rift mock around selecting and inspecting one game in a floating deck and persistent artwork portal. Desktop/fullscreen and constrained browser layouts verified; existing material controls retained. Ready for design review, with production integration still deferred.
<!-- SECTION:FINAL_SUMMARY:END -->
