---
id: TASK-110
title: 'Merges rows: a Details button, and clicking the row sets the header'
status: To Do
assignee: []
created_date: '2026-09-05 02:49'
updated_date: '2026-09-11 14:04'
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
- [ ] #1 A dedicated Details action opens the selected row's game details.
- [ ] #2 Selecting a promotable pending row body makes that entry the header instead of opening details.
- [ ] #3 The current header is evident before and after promotion.
- [ ] #4 Keyboard users can reach and activate Details and header selection independently.
- [ ] #5 Resolved or ineligible relation rows cannot invoke invalid promotion. Fullscreen retains equivalent eligible actions through its controller interaction model.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: MergeQueueView.axaml.cs dispatches row press to OpenDetailsCommand; the radio in MergeQueueView.axaml performs promotion. FullscreenLibraryToolsPage already separates Open game and eligible Make header. This task changes the desktop gesture and verifies parity, without inventing a resolved-group mutation.
<!-- SECTION:NOTES:END -->
