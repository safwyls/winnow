---
id: TASK-29
title: Complete reduced-motion coverage across animated surfaces
status: Done
assignee:
  - '@beta_ui'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 22:41'
labels:
  - accessibility
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 2600
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reduced-motion support is partial, not absent. Remaining animated surfaces must respect the OS reduced-motion preference. Finding F47. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every animated surface respects `prefers-reduced-motion` or the OS accessibility setting
- [x] #2 A test or audit confirms no animated surface is uncovered
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inventory every Avalonia transition and animation in app views and controls. 2. Propagate the existing OS reduced-motion state to each animated surface and use styles to snap or remove motion. 3. Add focused coverage that exercises the reduced state and audits the inventory, then run it in the shared Release scratch-output slot.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audited the four AXAML motion surfaces. Reduced-motion styles now clear every transition; the only indeterminate pip animation is gated behind the normal-motion class. Added a closed source inventory audit and a real headless tile test for snapped transition collections.

Validation passed in C:\Temp\winnow-beta-final: ReducedMotionCoverageTests 6/6 and CardDetailsInteractionTests.Reduced_motion_snaps_every_animated_tile_state 1/1. The audit finds exactly FeedCard, GameTile, MainWindow, and MergeQueue; git diff --check is clean.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed reduced-motion coverage for every animated Avalonia surface. Verified by the closed inventory audit and a headless rendered GameTile test in the Release scratch-output build.
<!-- SECTION:FINAL_SUMMARY:END -->
