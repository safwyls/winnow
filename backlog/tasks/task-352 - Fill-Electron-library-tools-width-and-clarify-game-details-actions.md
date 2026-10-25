---
id: TASK-352
title: Fill Electron library tools width and clarify game details actions
status: Done
assignee:
  - codex
created_date: '2026-09-27 03:22'
updated_date: '2026-09-27 03:24'
labels: []
dependencies: []
type: bug
ordinal: 388000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Manage library currently shrinks horizontally to its active tab content. The Open game label also suggests launching even though it navigates to details.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Library tools fill the available content width without changing width between tabs on desktop and fullscreen.
- [x] #2 Game details actions say View game or View followed by the title; related help uses consistent wording.
- [x] #3 Verify desktop and fullscreen rendering and run relevant frontend checks.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Override the library tools inherited feature-page width and margins; update details action copy and accessibility names; inspect both presentation modes with isolated data and run frontend checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed inherited centered margins and 1300px limit from LibraryTools, setting full width with bounded internal scrolling. Updated Discover button, shared card accessibility names and list-creation help. Native packaged inspection with C:\Temp\winnow-electron-20260926 verified desktop Hidden games and Identity review at 1440x980, fullscreen Lists and Manual games at 3440x1440, stable panel width and visible header/footer. Fullscreen Discover and card accessibility tree show View labels. npm run build and six existing app/draft tests passed; electron-builder --dir succeeded. Shared styles also serve Catalogue. No new tests added for this CSS/copy-only change.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Library management fills the available content width across tabs, and game details actions use View game wording. Verified native desktop/fullscreen layouts, production build, packaged launch and six focused frontend tests.
<!-- SECTION:FINAL_SUMMARY:END -->
