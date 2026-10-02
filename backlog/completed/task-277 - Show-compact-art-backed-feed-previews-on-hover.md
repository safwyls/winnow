---
id: TASK-277
title: Show compact art-backed feed previews on hover
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 01:18'
updated_date: '2026-09-14 01:28'
labels: []
dependencies: []
ordinal: 319000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Move feed quick details beside the cover and remove repeated recommendation text, using a dim hero background and connected pointer shape.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hover opens a compact side preview and crossing into it keeps actions reachable; keyboard access and dismissal remain available.
- [x] #2 Preview omits repeated reason, uses leased hero artwork and a pointer toward its tile, with viewport edge fallback and no stacked previews.
- [x] #3 Pointer interaction, lifecycle and rendering are verified; fullscreen behavior remains unchanged.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use transient hover previews with short opening and closing grace periods. Anchor to the cover right side, flip at edges and orient a shared bubble backdrop. Request/release hero art with preview lifetime. Update interaction tests and inspect renders.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: implemented 300ms hover opening and 220ms crossing grace, right placement with left fallback, compact title/metadata/summary/actions, and a connected pointer with dim leased hero art. Keyboard opening and dismissal remain available. Fullscreen presentation is unchanged; the full UI suite includes its existing regressions. Validation: all 683 UI tests passed, including 17 focused feed interaction and bubble rendering cases. Inspected 1600px and 900px captures in C:\Temp\winnow-feed-hover-captures confirming right placement, left fallback, artwork and compact content. Updated design-system.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Desktop feed previews now open on hover beside the cover, with compact content, dim hero art and a connected speech-bubble pointer. Removed the repeated recommendation heading and reason. Verified 683 UI tests and wide/narrow rendered captures.
<!-- SECTION:FINAL_SUMMARY:END -->
