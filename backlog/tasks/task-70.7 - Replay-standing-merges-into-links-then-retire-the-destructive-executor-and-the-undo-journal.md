---
id: TASK-70.7
title: >-
  Replay standing merges into links, then retire the destructive executor and
  the undo journal
status: To Do
assignee: []
created_date: '2026-09-02 00:15'
updated_date: '2026-09-02 00:15'
labels: []
dependencies:
  - TASK-70.3
parent_task_id: TASK-70
ordinal: 94000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 6 of TASK-70. Removes the destructive merge once linking has replaced it, and migrates any merge that is still standing. Runs last because until it does, an install can still hold a merge applied under the old model.

**Replay before retire, in migration 0019.** Three cases, and only the second does any work.

1. Applications with `undone_at` set. The rows are already restored; TASK-70.2 already reset their candidate status. Nothing to do.
2. Applications still standing with `undo_journal_version` set. The absorbed rows are gone but recoverable. Run the existing undo for each, then write one `identity_links` row from the restored child work to the survivor under one act stamped as a migration. This is the only path that keeps the decision and recovers the rows, and it reuses code that already exists and is already tested. Perform it at the C# layer as a one-shot, not in SQL; the undo repository and journal must stay alive until this has run and are deleted immediately after.
3. Applications standing with `undo_journal_version` NULL, from before 0017. Not replayable. Record as an unrecoverable prior unification, leave the destroyed rows destroyed, and say so in the migration file. The 0017 header states no live install has ever applied one, so this case is currently empty; it exists so that an install that upgrades late fails loudly rather than silently.

**Then drop and delete.** `merge_applications`, `merge_undo_rows`, `undone_at` and `undo_journal_version` go. `MergeExecutionRepository`, `MergeUndoRepository`, `MergeUndoJournal`, `MergeUndoJournalWriter`, `MergeExecutor` (the merging half), `MergeMode`, `MergeBlocker`, `MergeUndoBlocker`, `MergeUndoPlan`, `MergeRepointCounts`, the fifteen-table repoint inventory and the cascade tripwire go with them, along with `MergeExecutionTests`, `MergeUndoTests` and the parts of `MergeQueueViewModelTests` that assert merge outcomes. `ChooseWork` is kept and moved: it becomes the default suggestion in the primary picker, not a write-path decider. The 0016 canonical-pair CHECK and UNIQUE stay, so F20 remains closed.

The migration file must carry the whole path in its own header, because an install that upgrades in a year will have nothing else to read.

**Tests.** A database holding a standing merge with a journal, migrated, ends with the absorbed work restored, a live link from it to the survivor, and no `merge_undo_rows` table. A database holding an undone application migrates to nothing but a dropped table. A database holding a standing merge without a journal fails the migration with a message naming the application, rather than proceeding. `DatabaseBackupTests.Rewind` covers 0019. A build-level test asserts no type in the solution references the deleted merge namespace. Every acceptance criterion on TASK-70 still passes after the deletion.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A merge still standing with an undo journal is replayed into a restored work plus a live identity link, preserving the decision and recovering every row
- [ ] #2 A merge already undone needs no migration work beyond dropping the log
- [ ] #3 A standing merge with no undo journal fails the migration loudly, naming the application, rather than silently proceeding
- [ ] #4 merge_applications, merge_undo_rows and the undone columns are dropped, and the destructive executor, undo repository, journal and their tests are deleted
- [ ] #5 The 0016 canonical-pair CHECK and UNIQUE survive, so F20 stays closed
- [ ] #6 The migration file documents the whole path in its own header
- [ ] #7 Every acceptance criterion on the parent task still passes after the deletion
<!-- AC:END -->
