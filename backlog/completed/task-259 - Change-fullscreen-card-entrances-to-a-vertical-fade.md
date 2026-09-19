---
id: TASK-259
title: Change fullscreen card entrances to a vertical fade
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 18:32'
updated_date: '2026-09-13 18:34'
labels: []
dependencies: []
ordinal: 301000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User found the horizontal stagger distracting and requested a top-to-bottom fade. Replace it with simultaneous vertical fades within cards across fullscreen Home, Library and Search. Desktop remains outside this requested change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Cards fade from top to bottom together without horizontal staggering; focus stays immediate.
- [x] #2 Reduced motion and detach restore complete visibility and clear animation masks.
- [x] #3 Update visual documentation and pass focused UI checks.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Replace column delays with a soft vertical opacity mask over each cover, fade labels with the lower edge, and retain the existing lifecycle cleanup. Update tests and design spec; build and run fullscreen entrance/layout checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced column staggering with a simultaneous 240ms vertical gradient fade inside covers; labels follow the lower edge. Shared helper serves fullscreen Home, Library and Search. Desktop presentation remains unchanged per scope. Focus border is outside the opacity mask. Reduce motion and detach clear the mask and restore opacity. App and UI test project rebuilt successfully; all 16 entrance and fullscreen Home layout tests passed. Tests verify vertical mask orientation, cleanup, reduced motion, completion and focus. Inspected rendered 1920x1080 completion frame in C:/Temp/winnow-card-vertical-qa; fixture uses placeholder art, so real-art motion feel remains for user evaluation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Changed fullscreen cards to a soft top-to-bottom fade with no horizontal stagger. Updated design spec and lifecycle checks. Build and 16 focused UI tests pass; rendered completion layout inspected.
<!-- SECTION:FINAL_SUMMARY:END -->
