---
id: TASK-301
title: Trace every contour of the loading dragon
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 01:46'
updated_date: '2026-09-16 01:49'
labels: []
dependencies: []
ordinal: 343000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The loading glow selects only the first contour of the head path, leaving detached pieces and inner contours unlit. Each separate contour needs a complete circuit on both loading surfaces.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every nonempty dragon contour has a glow trail that travels its full closed circumference, including wraparound.
- [x] #2 Desktop and fullscreen retain theme colors, reduced motion and animation cleanup.
- [x] #3 Full-cycle geometry and rendered captures verify all parts, with documentation updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Split the bundled vector into all closed figures and trace each independently. 2. Verify contour coverage and seam continuity over a full cycle, inspect dark/light render frames, and run relevant loading tests. 3. Update the visual spec and commit only owned files, preserving the user preview delay.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the hard-coded head-only figure with all 15 closed SVG figures. Each receives an independent 1.8-second full-perimeter trail with seam wraparound. Geometry tested at 101 positions per contour; 18 focused tests passed. Nine rendered phases at desktop88px and fullscreen100px in dark/light colors captured and visually checked. User five-second desktop preview override preserved; desktop timing suite intentionally not rerun against it. Evidence: docs/spikes/loading-dragon-contours.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Every dragon contour now receives a full glow circuit on both loading screens. Verified geometry coverage, seam continuity, theme rendering, motion cleanup and fullscreen readiness with 18 tests and visual captures.
<!-- SECTION:FINAL_SUMMARY:END -->
