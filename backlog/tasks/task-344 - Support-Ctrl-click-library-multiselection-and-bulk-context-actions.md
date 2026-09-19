---
id: TASK-344
title: Support Ctrl-click library multiselection and bulk context actions
status: Done
assignee:
  - codex
created_date: '2026-09-19 16:57'
updated_date: '2026-09-19 17:04'
labels: []
dependencies: []
type: feature
ordinal: 377000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users need to select several library games with Ctrl-click and apply context actions such as Mark as read and Remove from Derelict to the whole selection.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Ctrl-click toggles games in desktop grid and list without opening details.
- [x] #2 Right-click on a selected desktop game preserves the whole selection; right-clicking an unselected game targets it, and bulk actions act on all selected games.
- [x] #3 Selection visuals and counts stay consistent across filtering and refresh; pointer tests cover desktop bulk actions.
- [x] #4 Fullscreen retains its existing single-game interaction; user explicitly restricted Ctrl-click multiselection to desktop.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Centralize multiselection state and toggle/context-target helpers in LibraryViewModel and preserve visible selection through filtering/reload. Desktop grid intercepts Ctrl-click without opening details; list uses native Ctrl multiselection and preserves right-click targets. Existing bulk commands act on the whole selected set. Verify pointer gestures, both bulk actions, selection state, and fullscreen regression coverage. Fullscreen pointer behavior stays unchanged per explicit user instruction.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
User clarified desktop-only scope; fullscreen pointer edits were reverted before completion and existing fullscreen single-game behavior retained. Added Ctrl-click grid selection and native list synchronization, right-click preservation/replacement, shared selected flags/counts, and selection restoration by ownership across reloads. Bulk commands already iterate selected games. Validation: 8 new real-pointer LibraryMultiSelectionTests passed across grid/list, both bulk actions and persistence, selection reload/sort/view switch/filter; 46 LibraryViewModelTests; 38 fullscreen options, Derelict/Patched composition and library refresh tests; 26 CardDetailsInteractionTests. Total 118 focused tests passed. Builds and diff check clean. No production app clicks or database writes; scratch test outputs. Full suite not run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added desktop-only Ctrl-click multiselection in library grid and list. Right-click preserves selected games, so Mark as read, Remove from Derelict and other bulk commands target the whole set. Selection survives sorting, view switches and reloads and prunes filtered-out games. Fullscreen remains single-game. All 118 focused tests passed; independent review found no actionable issues.
<!-- SECTION:FINAL_SUMMARY:END -->
