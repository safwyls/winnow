---
id: TASK-315
title: Contain setup dialog surfaces inside rounded frame
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 00:04'
updated_date: '2026-09-17 00:06'
labels: []
dependencies: []
ordinal: 357000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The setup header and footer backgrounds overlap the frame corners, matching the earlier rounded containment issue.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Setup surfaces respect the outer border and corner radius on desktop.
- [x] #2 Rendered checks and desktop/fullscreen setup navigation pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add inset rounded containment to the setup frame, inspect dark/light corner rendering and run setup interaction checks for both surfaces.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a rounded inner Border at RadiusPaneInner=7 inside the existing 1px border and 8px outer frame. Header, embedded step content and footer are clipped together. Fullscreen setup uses an unframed page and needs no containment change. All 13 FirstRunSetup UI checks passed, including dark/light rendered corner tests with a negative control: removing inner clipping reproduces overpainting at every corner. Inspected captures in C:\Temp\winnow-setup-corner-captures. Build passed with warnings treated as errors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed setup frame corner overlap with inset rounded clipping. Verified dark/light rendering, all four corners, keyboard focus and desktop/fullscreen setup navigation with 13 passing UI tests.
<!-- SECTION:FINAL_SUMMARY:END -->
