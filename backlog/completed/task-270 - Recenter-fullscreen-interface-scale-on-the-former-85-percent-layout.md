---
id: TASK-270
title: Recenter fullscreen interface scale on the former 85 percent layout
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 21:42'
updated_date: '2026-09-13 21:48'
labels: []
dependencies: []
ordinal: 312000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Make new 100 percent match the former 85 percent fullscreen layout. User explicitly requests existing installations switch to the new baseline, not preserve old visual size. Retain proportional scale controls and other appearance preferences.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 New 100 percent uses former 85 percent canvas geometry across standard and ultrawide fullscreen.
- [x] #2 Legacy saved interface scales reset once to new 100 percent; new adjustments persist and reset returns to new baseline.
- [x] #3 Scale, layout and navigation tests pass; design system and desktop assessment updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Introduce effective scale baseline; version the interface-scale setting so legacy values reset; verify proportional geometry, preference loading/reset and representative fullscreen layouts.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented an effective 0.85 fullscreen baseline with a separate displayed scale. Legacy fullscreen.ui-scale is ignored in favor of ui-scale-v2, so existing installs start at 100 percent and later adjustments persist. Desktop has no corresponding overall scale setting; its presentation is unchanged. Targeted scale/Home tests pass (18). Inspected the populated Library details capture at the new baseline; full UI regression verification is running.

Verification: Release build and all 654 UI tests pass, including desktop interaction regressions, fullscreen navigation, standard/ultrawide geometry, overlays, keyboard placement, and preference reset/persistence. Rendering assertions allow up to one physical pixel for Avalonia layout rounding; configured canvas dimensions remain exact. Updated the scrolling fixture to overflow the roomier layout. Visual inspection used a headless details capture; no live-library UI actions.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Recentered fullscreen 100 percent on the former 85 percent layout. Existing scale choices reset to the new baseline; subsequent settings persist. Documented the scale and verified all 654 UI tests.
<!-- SECTION:FINAL_SUMMARY:END -->
