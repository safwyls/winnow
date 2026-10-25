---
id: TASK-304
title: Match fullscreen Home artwork to Details dimming
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 04:10'
updated_date: '2026-09-16 04:11'
labels: []
dependencies: []
type: enhancement
ordinal: 346000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants artwork visible across fullscreen Home behind the title and description, matching the Details composition rather than leaving a solid left region.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fullscreen Home uses the same cinematic backdrop treatment as Details; Library and desktop keep their existing presentation.
- [x] #2 Focused Home and backdrop checks pass and rendered cinematic fades are inspected.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Select the existing cinematic backdrop mode for feed pages, update the visual specification, and verify Home behavior and standard/ultrawide cinematic rendering.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Home now selects the same cinematic FullscreenBackdrop mode as Details, sharing the horizontal, top and lower veils rather than maintaining separate fade values. Library retains the non-cinematic mode and existing page dimming. Desktop is a separate presentation and is unchanged. Validation: 81 focused Home layout, browse, backdrop and Details tests passed; inspected the existing standard and ultrawide cinematic diagnostic captures from C:/Temp/winnow-303-captures (renderer is unchanged). Physical-display Hades comparison remains for user validation. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Matched fullscreen Home to the existing Details artwork composition and text-protection veils. Verified 81 focused tests and cinematic render captures.
<!-- SECTION:FINAL_SUMMARY:END -->
