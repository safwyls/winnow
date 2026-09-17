---
id: TASK-311
title: Mark selected Patched games as read from library actions
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 15:32'
updated_date: '2026-09-17 00:48'
labels: []
dependencies: []
ordinal: 353000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reading patches currently requires opening game details. Add a context-menu action for selected games in Patched, including list multi-selection and the matching fullscreen action.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The desktop Patched selection menu marks selected games read, including grouped releases and multi-selection, without affecting unselected games.
- [x] #2 Fullscreen exposes the same action and persistence failures remain visible.
- [x] #3 Focused tests verify acknowledgement, library refresh and later patches returning.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reuse the update flag service in a shared selection command; expose it in desktop and fullscreen Patched actions; cover persistence and refresh with focused tests and update the visual spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared selection acknowledgement bounded by the tile update timestamp, desktop context-menu entry, fullscreen Library options entry and visible error messages on both surfaces. App build passed with no warnings. Initial composition runs passed behavior assertions but exposed asynchronous feed teardown in new tests; fixing test cleanup before final verification.

Verification complete: app build passed with 0 warnings/errors. All 34 tests across UpdateAcknowledgementCompositionTests, RailListControlsTests and FullscreenActionOverlayTests passed. Desktop test checks the actual MainWindow menu binding and action; fullscreen test opens Library options and activates Mark as read via keyboard. Database-backed tests verify grouped releases, multi-selection, unselected preservation, newer-patch snapshot bounds and persistence failures. Tests run on throwaway databases; no production library used.

Full branch validation exposed a teardown race in the existing later-push composition case: background feed refresh outlives its temporary database. Extend the existing pending-feed cleanup helper to cover the desktop-only case, then rerun the suite.

Final full-branch verification passed: Release build with warnings as errors had zero warnings/errors; all 6,126 applicable tests passed, including 791 UI tests and the previously racing later-push test. Two Linux-only tests skipped on Windows and are covered by PR CI. Site build/type/asset checks and migration/CI-script checks also passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Mark as read for selected Patched games in the desktop context menu and fullscreen Library options. Reuses release acknowledgement rules, refreshes counts, preserves later patches and displays failures. Verified with clean app build and 34 passing focused UI/composition tests.
<!-- SECTION:FINAL_SUMMARY:END -->
