---
id: TASK-342
title: Allow persistent removal from Derelict
status: Done
assignee:
  - codex
created_date: '2026-09-19 16:38'
updated_date: '2026-09-19 16:43'
labels: []
dependencies: []
type: feature
ordinal: 375000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Delisted games can remain playable and enjoyable. A user decision must take precedence over lifecycle evidence, including future metadata refreshes.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Derelict collection offers Remove from Derelict for selected games on desktop and fullscreen.
- [x] #2 The override persists across reloads and overrides every automatic derelict signal without deleting source evidence.
- [x] #3 Tests verify persistence, classification, and both presentation paths; domain documentation reflects the behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Persist a user exemption separately from lifecycle observations and apply it before release and group bucket classification. Add a shared library command acting on selected release IDs, expose it in desktop context menus and fullscreen actions, and refresh after saving. Verify persistence and precedence with database tests and both action paths with headless UI tests; update domain documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented migration 0045 and atomic per-release user exemptions. Source lifecycle status and evidence remain intact; IsDerelict respects the saved exemption before grouping, feed eligibility and launch selection. Desktop right-click menu and fullscreen Library options share the command; fullscreen saves refresh the desktop library too. Errors remain visible without removing games on failed persistence. Verification: solution build passed with zero warnings/errors; 66 focused LifecycleTests and LibraryViewModelTests passed; 21 DerelictOverrideCompositionTests and UpdateAcknowledgementCompositionTests passed; all 45 migration hashes verified; diff check clean; independent review found no actionable issues. Used scratch build output because production Winnow is running. Full suite and physical-controller testing not run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Remove from Derelict on desktop and fullscreen. The persistent user decision overrides all automatic lifecycle signals, including future evidence, and applies to selected grouped copies. Verified persistence, atomic rollback, both action paths and cross-surface refresh with 87 focused tests, a clean solution build and migration integrity checks.
<!-- SECTION:FINAL_SUMMARY:END -->
