---
id: TASK-373
title: Make Rift cover previews compact and informational
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 00:40'
updated_date: '2026-09-29 00:47'
labels: []
dependencies: []
type: enhancement
ordinal: 409000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The hover preview should explain the selected game without making users move into a second interaction surface. Cover activation remains the way to open details.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Rift cover previews contain no buttons, do not accept pointer interaction, and close when the cover loses hover or focus.
- [x] #2 Clicking or activating the cover opens details with the portal transition; keyboard dismissal and ordinary Tab navigation remain usable in desktop and fullscreen.
- [x] #3 The fixed reading plane is shorter without internal scrolling; the production design and currently shared mock match, with tests and visual verification recorded.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Remove preview actions and hover retention; simplify cover-owned visibility and keyboard handling; shorten the fixed plane in app and mock; verify both modes, update documentation and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed Library preview actions and all panel hover/focus retention in the app and shared mock. The cover owns preview visibility and activation; preview content is a pointer-transparent tooltip associated through aria-describedby. Reading planes are fixed at up to 420 px desktop / 440 px fullscreen. Verified production-renderer pointer dismissal, cover-click details, fullscreen ordinary Tab/Enter, fixed non-scrolling reading planes, and matching mock behavior in both modes. Four focused suites: 12 tests passed. Production build and mock JavaScript syntax passed. Evidence: docs/spikes/2026-09-28-rift-integration/compact-preview.png and README.md. No packaged binary rebuilt.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Made Rift cover previews compact and informational, with cover-only interaction and preserved portal expansion into details. Updated app, mock, documentation and tests; verified desktop/fullscreen interaction and production build.
<!-- SECTION:FINAL_SUMMARY:END -->
