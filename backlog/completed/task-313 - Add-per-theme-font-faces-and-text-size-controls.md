---
id: TASK-313
title: Add per-theme font faces and text size controls
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 23:00'
updated_date: '2026-09-16 23:21'
labels: []
dependencies: []
ordinal: 355000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Allow typography customization alongside theme appearance controls. User confirmed font choices must be stored per theme and included in exported themes. Preserve existing heading, interface and data roles, apply changes live on desktop and fullscreen, and keep existing fullscreen accessibility scaling separate.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Appearance provides font face choices and text size controls for the current theme with a reset to authored defaults.
- [x] #2 Typography persists by theme, switches correctly, survives restart and round-trips through theme export/import; old themes keep current typography.
- [x] #3 Desktop and fullscreen reflect typography changes live with readable controls and retained keyboard/controller navigation.
- [x] #4 Validation covers malformed typography, unavailable fonts, persistence, export and representative UI layouts; relevant documentation is updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit typography application paths; add optional theme typography schema and per-theme preference persistence; implement shared live typography resources and desktop/fullscreen controls; verify round trips, theme switching and rendered layout.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented three per-theme font roles and 80-120 percent size in desktop and fullscreen, including authored reset, persisted overrides and exported typography. Added role fallback, live resources across AXAML/C# text, native popout scaling, and independent fullscreen accessibility scaling. Verified nine new headless UI tests and 238 theme/visual-discipline checks; inspected desktop and real fullscreen captures at 120 percent. Full-suite verification in progress. Browser-owned HTML keeps provider typography.

Final domain suite: 4788/4788 passed. Final targeted theme and visual-discipline checks: 238/238 passed. Initial full UI run used screenshot capture globally, exposing six optional capture-only failures; normal full-suite rerun omits that capture flag. New typography capture tests run separately and all nine pass. One normal-suite grid scroll-restore test failed, then the complete ListBrowsingPositionTests group passed 10/10 on rerun.

Final normal UI suite: 787/788 passed, including all typography, fullscreen accessibility, settings and scaling checks. The sole failure was ListBrowsingPositionTests.Closing_details_restores_the_scrolled_library_position(grid: True); all 10 tests in that group passed on isolated rerun. No product change was made for that intermittent failure. Full domain suite passed 4788/4788. Test artifacts: C:\Temp\winnow-typography-results; inspected 120-percent captures in C:\Temp\winnow-typography-captures.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added live heading, interface and data font choices plus 80-120 percent text size per theme on desktop and fullscreen. Preferences survive theme switching/restart and export/import, with authored reset and bundled missing-font fallback. Updated theme documentation and dynamic typography throughout native UI. Verified 4788 domain tests, nine new typography UI tests and the full 788-test UI run (one intermittent scroll test passed with its ten-test group on rerun).
<!-- SECTION:FINAL_SUMMARY:END -->
