---
id: TASK-110
title: 'Merges rows: a Details button, and clicking the row sets the header'
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-09-05 02:49'
updated_date: '2026-09-11 18:44'
labels:
  - ui
dependencies: []
documentation:
  - design-system.md
priority: medium
type: enhancement
ordinal: 137000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
On promotable pending Merges rows, separate opening Details from choosing the group header: a dedicated Details action opens the game, and selecting the row body promotes the eligible entry. Existing desktop row presses open details while a radio selects the header. Preserve fullscreen's separate Open game/Make header actions and respect rows that cannot be promoted. Persisted header preferences for already linked groups remain TASK-109.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A dedicated Details action opens the selected row's game details.
- [x] #2 Selecting a promotable pending row body makes that entry the header instead of opening details.
- [x] #3 The current header is evident before and after promotion.
- [x] #4 Keyboard users can reach and activate Details and header selection independently.
- [x] #5 Resolved or ineligible relation rows cannot invoke invalid promotion. Fullscreen retains equivalent eligible actions through its controller interaction model.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Give desktop rows an explicit Details button and use eligible row-body presses for header promotion. Preserve guarded commands and fullscreen actions. 2. Exercise independent pointer and keyboard actions plus ineligible rows in headless UI tests. 3. Update the visual specification and commit the verified task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: MergeQueueView.axaml.cs dispatches row press to OpenDetailsCommand; the radio in MergeQueueView.axaml performs promotion. FullscreenLibraryToolsPage already separates Open game and eligible Make header. This task changes the desktop gesture and verifies parity, without inventing a resolved-group mutation.

Desktop headless tests exercise row-body promotion, Details pointer/Enter activation, independent radio Space activation, accessible header status and hidden resolved rows. Fullscreen action-sheet tests exercise separate Open game/Make header actions and removal of promotion from the current header. MergeQueueViewModelTests: 83 passed, including ineligible expansion promotion. Five focused merge UI tests passed; updated resolved-row assertion also passed on rerun.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a separate desktop Details button and eligible row-body header selection. Preserved fullscreen actions and promotion guards; verified with headless interaction tests and 83 merge model tests.
<!-- SECTION:FINAL_SUMMARY:END -->
