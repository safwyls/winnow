---
id: TASK-303
title: Use a Ground overlay for fullscreen home backdrop fades
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 04:00'
updated_date: '2026-09-16 04:02'
labels: []
dependencies: []
type: bug
ordinal: 345000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fullscreen home shows dark bands on ultrawide displays. Adapt the details overlay approach as requested.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Home and library use full-opacity artwork with a theme-colored horizontal overlay instead of a whole-backdrop opacity mask.
- [x] #2 Details and desktop artwork remain unchanged; rendering and backdrop checks pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Replace the browsing mask and dimming with a Ground overlay, retaining reveal positions and vertical fade. Update the visual spec, inspect rendered captures, and run focused backdrop tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the non-cinematic horizontal opacity mask and internal 80% artwork dimming with a Ground-to-transparent overlay at 30%-85%. Theme refresh recolors it through the existing veil update. Existing library page-level dimming, vertical hero fades, transitions, details composition, and desktop artwork are retained. Validation: 67 focused backdrop, artwork preference, desktop feed backdrop, fullscreen browse and details tests passed; 4 existing hero geometry cases rerun with capture output passed. Inspected home/details ultrawide gradient captures in C:/Temp/winnow-303-captures. Captures use the existing white diagnostic artwork to expose fade shape; actual No Mans Sky art on the physical display has not been rechecked. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Adapted the details Ground-overlay technique to fullscreen browsing. Verified focused behavior tests and standard/ultrawide rendered captures; desktop path remains separate.
<!-- SECTION:FINAL_SUMMARY:END -->
