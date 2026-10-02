---
id: TASK-263
title: Hide captions beneath fullscreen game cards
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 19:25'
updated_date: '2026-09-13 19:28'
labels: []
dependencies: []
ordinal: 305000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remove below-cover title captions in fullscreen For you, Library and Search. Keep accessible game names, missing-art title placeholders and the selected-game hero title. Desktop cards retain their captions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fullscreen cards have no below-cover title captions across For you, Library and Search.
- [x] #2 Fullscreen layout and navigation checks pass; desktop presentation remains unchanged.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Remove caption controls and their reserved layout space, update the visual specification, and verify existing fullscreen UI tests and rendered layout.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed below-cover title controls from shared For you/Library cards and Search cards. Removed caption height from cover sizing. Preserved accessible button names, missing-art placeholders and Home hero title; desktop code is unchanged as requested. Release UI build and all 229 fullscreen tests passed with screenshot capture enabled. Inspected rendered Home, Library and Search shell captures in C:\Temp\winnow-no-captions. Updated existing geometry assertion to allow independent one-pixel layout rounding and settled layout before asserting final row transforms when screenshots arrange the viewport mid-transition. git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed fullscreen card captions and reserved caption space across For you, Library and Search. Verified rendered captures and all 229 fullscreen UI tests.
<!-- SECTION:FINAL_SUMMARY:END -->
