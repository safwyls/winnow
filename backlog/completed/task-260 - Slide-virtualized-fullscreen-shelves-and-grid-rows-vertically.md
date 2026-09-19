---
id: TASK-260
title: Slide virtualized fullscreen shelves and grid rows vertically
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 18:38'
updated_date: '2026-09-13 18:49'
labels: []
dependencies: []
ordinal: 302000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remove the distracting fullscreen card fades. For you shelves and Library/Search grid rows should retain nearby rows and slide vertically as users navigate, with bounded realized covers and no opacity flash. User explicitly included all three fullscreen surfaces. Desktop presentation remains unchanged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fade entrances are removed from Home, Library and Search.
- [x] #2 Vertical navigation slides persistent outgoing and incoming rows in a clipped viewport; only nearby rows are realized.
- [x] #3 Rapid navigation, reversal, reduced motion, resizing, filtering and leaving a page preserve focus and release unused cover rows.
- [x] #4 All three fullscreen surfaces have focused UI coverage; desktop scope and current visual behavior are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Introduce a reusable bounded virtual-row viewport and row-based fullscreen grid navigation state. Integrate retained shelf rows into Home and grid rows into Library/Search, keeping shared library behavior unchanged. Remove the previous fade helper/tests. Verify bounded realization, navigation and lifecycle with headless UI tests and inspect rendered transition frames; update specs and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed both fullscreen fade iterations and obsolete page navigation state/tests. Added a shared virtual row viewport: one Home shelf or two Library/Search rows, neighboring rows retained, 220ms vertical translation, target-only input, retargeted reversal, bounded long-jump snapping, reduced-motion/resize snapping and detach cleanup. Added row-based grid identity state, mouse-wheel navigation, Search row-range hints, and synchronized pending rebuilds before navigation with immutable row factory snapshots. Desktop presentation remains unchanged; desktop/fullscreen recommendation and supplemental-feed parity checks included. Domain-agent review findings (pending-data navigation and Search header return) fixed and regression-tested. Verification: solution build succeeded with zero warnings/errors; broad fullscreen plus recommendation/supplemental UI run passed 220 tests. Subsequent focused run passed 55 checks with one incorrect fixture-position expectation, corrected by anchoring the expanding filter at the start of the library; final 13 row-navigation tests all pass. Seven viewport and 13 state checks cover bounded realization, reversal, reduced motion, resize, empty data and cleanup. Inspected Home/Library/Search full-shell 1920x1080 transition frames under C:/Temp/winnow-rows-qa; outgoing covers remain opaque, focus rings and viewport clipping are correct. Frames use placeholder art; real-art/device feel has not been manually assessed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced card fades with retained virtualized rows sliding vertically in fullscreen Home, Library and Search. Shared domain behavior and desktop presentation remain unchanged. Build clean; broad UI and final navigation regressions pass; actual fullscreen transition frames inspected.
<!-- SECTION:FINAL_SUMMARY:END -->
