---
id: TASK-283
title: Allow a preferred default library sort
status: Done
assignee:
  - '--plan'
created_date: '2026-09-14 03:13'
updated_date: '2026-09-14 03:17'
labels: []
dependencies: []
ordinal: 325000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Choose the library order at startup instead of always starting at Dormant longest.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Default sort persists and applies to desktop and fullscreen.
- [x] #2 Temporary sorts and manual list ordering preserve the preference; invalid settings fall back safely.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Persist a Library settings selector and apply it to both surfaces on startup and preference changes. Preserve manual list order and temporary sorts. Verify persistence and controls.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added Settings Library default sort selector on desktop and controller adjustment in fullscreen. Stored enum names in library.default-sort with safe fallback, applied at startup and on explicit preference changes. Browsing sorts remain temporary; manual lists preserve their order and use a changed default after exit. Verified 26 LibrarySettingsViewModel tests, 35 ListsViewModel tests, and 12 FullscreenSettings tests including desktop selector and controller interaction. Build completed without warnings; git diff check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Users can choose a persistent default library sort in both presentations. Persistence, invalid values, temporary sorts, manual list order and both controls are covered by 73 passing targeted tests.
<!-- SECTION:FINAL_SUMMARY:END -->
