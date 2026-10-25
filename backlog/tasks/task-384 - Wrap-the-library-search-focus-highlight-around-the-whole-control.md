---
id: TASK-384
title: Wrap the library search focus highlight around the whole control
status: Done
assignee:
  - '@codex'
created_date: '2026-10-03 19:08'
updated_date: '2026-10-03 19:16'
labels: []
dependencies: []
type: bug
ordinal: 462000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Avalon library search focus ring surrounds only the text input, leaving the search icon outside the highlighted field.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop search focus encloses the icon, input and clear action without changing layout or losing clear-button keyboard focus.
- [x] #2 The same control is verified in fullscreen More, with rendered evidence and a successful frontend build.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Apply the existing accent outline to the search wrapper when it contains focus; suppress the redundant input outline, retain clear-button focus, and inspect the production control on both surfaces.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Moved the existing accent outline onto the complete search wrapper with focus-within, suppressing only the redundant input outline. Desktop clear-button keyboard focus stays visible. Fullscreen More retains its accent underline; its clear action now uses intrinsic width rather than the panel-wide button width, preserving input space. Fullscreen outline offset fits the scroll gutter. Native Electron verification used the production AvalonLibrary in isolated data directories: desktop at1440/880px, fullscreen More at1920px, pointer focus, Tab to Clear search, clearing text, no focus-induced layout shift, input width over200px and clear width under100px. Rendered captures inspected in .tmp/search-focus-QFI9Fi. Production typecheck/build passed with existing dependency annotation warnings; final CSS also compiled in the native probe. Updated design-system.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Search focus now surrounds the icon, input and clear action. Verified wide/narrow desktop and fullscreen More in native Electron; keyboard clear behavior preserved, layout stable, frontend build passed.
<!-- SECTION:FINAL_SUMMARY:END -->
