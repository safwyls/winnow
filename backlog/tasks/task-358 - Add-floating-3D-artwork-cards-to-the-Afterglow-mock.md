---
id: TASK-358
title: Add floating 3D artwork cards to the Afterglow mock
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 17:23'
updated_date: '2026-09-27 17:33'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 394000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user approved Satin and Foil and wants artwork to feel like a floating card that tilts with the pointer. Preserve the finish/intensity controls and readable hover captions in the review mock.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Poster artwork, finish lighting and captions move together with a smooth cursor-driven perspective tilt, lift and shadow without shifting the grid or destabilizing hover.
- [x] #2 Card depth and tilt strength can be adjusted independently of finish intensity; keyboard focus and reduced motion use a stable raised state.
- [x] #3 Desktop and fullscreen pointer/keyboard interactions, controls, compact records, cleanup and narrow layout are verified and documented with review evidence.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Wrap poster content in a visual surface while keeping the button hit area fixed; add configurable CSS perspective transforms driven by local pointer coordinates; make Pixi use the same untransformed dimensions; verify both mock surfaces and record screenshots.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a visual card surface with CSS perspective, lift and cursor-directed shadows. Artwork, Pixi finish and caption transform together; the stable outer button supplies coordinates to both effects. Card depth is independent of finish intensity and offers a toggle plus 0-12 degree tilt (default 7). Pointer updates coalesce into one frame; no idle depth loop. Desktop and fullscreen browser checks at 2052x1272 and 760x560 verified pointer tilts, unchanged hit bounds, keyboard level lift, zero tilt, disabled floating with Satin still active, stationary Foil, reset, compact records, dialog cleanup, eight returning games and no horizontal overflow at maximum tilt. One shader canvas reached its settled state. No new browser warnings or errors; node --check for depth.js/finish.js/mock.js and git diff --check passed. Screenshot 11-floating-artwork.png and README record evidence. OS reduced-motion switching and physical touch were not exercised; the shared stationary branch was verified through keyboard and disabled pointer following. This remains a review mock; production frontends are unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added adjustable floating artwork cards to the Afterglow mock, with integrated Satin/Foil lighting and readable captions. Verified desktop/fullscreen pointer and keyboard behavior, controls, cleanup and narrow layouts; saved review evidence and passed JavaScript syntax/whitespace checks.
<!-- SECTION:FINAL_SUMMARY:END -->
