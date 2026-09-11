---
id: TASK-218
title: Make list commands atomic and publish only committed state
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 07:59'
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
- [x] #1 Create-with-members and bulk membership commands commit atomically; rename/reorder/filter/delete publish committed state or restore it on failure.
- [x] #2 Pending membership changes either prevent conflicting input or reliably persist the latest desired checkbox state, with visible busy/error behavior.
- [x] #3 Fault-injection and delayed-repository tests cover partial batches and failed/overlapping edits through desktop/fullscreen entry points.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add atomic repository create-with-members and bulk append/remove operations using the existing transaction/savepoint batch helper, returning committed membership. Publish list names, ordering, rules and deletion only after successful writes; serialize local list commands and retain current row identity across snapshots. Persist the latest requested detail membership state with shared busy/error/rollback behavior and render it on desktop/fullscreen. Add SQL-trigger fault injection for partial batches and delayed callback/repository tests for rapid checkbox changes and failed edits, then verify both entry paths and update narrow list contract documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Validation: 78/78 model/repository tests passed in tests/Winnow.Tests/TestResults/ui218-list-model.trx, including ten new SQL-trigger batch and committed-model fault cases; 32/32 UI tests passed in tests/Winnow.Ui.Tests/TestResults/ui218-list-parity.trx, including ten new actual desktop/fullscreen cases for delayed latest intent, refresh during save, failure rollback/retry, failed compensation, and visible list-action errors. Existing details and refresh ordering regressions also passed. Repository batches use savepoints under ambient transactions; order changes return committed membership without a separate failing post-commit read. Runtime mouse/keyboard and gamepad dispatch were headless; no physical-controller or pixel QA claimed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
List creation and bulk membership now commit atomically; shared list commands serialize writes and publish only committed state. Membership saves preserve the latest desired state with visible progress, rollback and retry on both surfaces. Refreshes preserve row identity and cannot publish stale list snapshots. Documented in architecture 5.1 and visual spec 12.3. Verified 78 model/repository and 32 desktop/fullscreen headless tests.
<!-- SECTION:FINAL_SUMMARY:END -->
