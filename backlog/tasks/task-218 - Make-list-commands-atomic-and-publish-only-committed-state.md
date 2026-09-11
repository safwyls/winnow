---
id: TASK-218
title: Make list commands atomic and publish only committed state
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/ViewModels/Lists/ListsViewModel.cs:159'
  - 'src/Winnow.App/ViewModels/Lists/ListsViewModel.cs:205'
  - 'src/Winnow.App/ViewModels/Lists/ListsViewModel.cs:263'
  - src/Winnow.App/ViewModels/Lists/GameListsViewModel.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 249000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R30. Evidence: Source verified. ListsViewModel creates a list and adds selected releases through separate writes; bulk membership changes can partially commit. Rename, move, rule update and delete mutate UI state before persistence without rollback in this layer. GameListsViewModel ignores checkbox changes while a write is pending, allowing delayed checked→unchecked input to leave unchecked UI with checked membership. The checkbox race requires an asynchronously delayed write and was not reproduced; current synchronous SQLite behavior reduces its ordinary trigger. Failures and overlapping input produce partial lists or phantom UI changes. Shared application commands should own persistence and latest user intent for both surfaces.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Create-with-members and bulk membership commands commit atomically; rename/reorder/filter/delete publish committed state or restore it on failure.
- [ ] #2 Pending membership changes either prevent conflicting input or reliably persist the latest desired checkbox state, with visible busy/error behavior.
- [ ] #3 Fault-injection and delayed-repository tests cover partial batches and failed/overlapping edits through desktop/fullscreen entry points.
<!-- AC:END -->
