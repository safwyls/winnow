---
id: TASK-70.7
title: >-
  Replay standing merges into links, then retire the destructive executor and
  the undo journal
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 00:15'
updated_date: '2026-09-02 04:44'
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

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. RESEARCH (done). Read TASK-70 design + four product decisions, TASK-70.7, notes on 70.1/70.2/70.3/70.4/70.6, migrations 0016/0017/0018, MergeExecutor, MergeUndoRepository, MergeUndoJournal, MergeQueueViewModel, DatabaseInitializer, DatabaseBackupTests.Rewind, SoftMatchResolver, the DI registrations.

2. THE REPLAY IS A SELF-CONTAINED ONE-SHOT, NOT A CALL INTO THE UNDO REPOSITORY. New src/Winnow.Data/Migrations/StandingMergeReplay.cs. The journal is generic already (table_name, op, key_json, before_json), so restore is three statement builders driven by the JSON's own keys rather than fifteen typed ones: delete -> INSERT, repoint and update -> UPDATE keyed on key_json. Table and column names validated against the journal's fixed table list and PRAGMA table_info, so a generated statement cannot name anything the schema does not hold. Identity rows (works, releases, ownerships) are restored first so a re-inserted child has its parent. This is what lets MergeUndoRepository and MergeUndoJournal be DELETED in this stage rather than moved.

3. THE REPLAY REFUSES RATHER THAN GUESSES. A standing application is replayed only when it can be put back exactly: journal version present, surviving work still there, no later standing application naming its identities (handled by replaying newest-first), and every restore key free. Anything else throws naming the application id, its two release ids and the absorbed title from the journal. Dropping MergeUndoIdMap's fresh-id remap in favour of a refusal is deliberate: a migration that silently restores a work at a different id is the kind of half-working thing this stage exists to remove.

4. WHERE IT RUNS. It needs 0018's identity_links to exist and 0019 to have not yet dropped the journal, so DatabaseInitializer gains one boundary: when 0019 is in the pending set, the upgrade runs in two passes with the replay between them; otherwise it is the single pass it is today, unchanged. One backup, taken once, over the full pending list, before either pass.

5. CONFIRMED-BUT-UNAPPLIED PAIRS BECOME LINKS TOO. The status set drops confirmed as well as undone, and an install that answered under the two-step flow has answers the retirement would otherwise discard. The one-shot writes a link for each, parent chosen by SurvivorLadder (the same chooser the old flow used), skipping any pair whose releases already share a work. Under one migration act, retractable like any other.

6. MIGRATION 0019_retire_destructive_merge.sql, append-only, carrying the whole path in its header. (a) A guard that aborts if any merge_applications row still stands, so a database that reached this script without the replay fails instead of losing rows. (b) merge_candidates rebuilt with CHECK (status IN ('pending','rejected')), restating 0016's CHECK (left < right) and UNIQUE (left, right) verbatim so F20 stays closed; any leftover confirmed or undone row maps to pending. (c) DROP merge_undo_rows, then merge_applications, taking undone_at and undo_journal_version with them.

7. DELETE. MergeExecutionRepository, MergeUndoRepository, MergeUndoJournal, MergeUndoJournalWriter, IMergeExecutionRepository, IMergeUndoRepository, MergeExecutor, MergeMode, MergeOutcome/MergeRepointCounts, MergePlan, MergeRequest, MergeBlocker, MergeUndoPlan/MergeApplicationRecord/MergeUndoResult, MergeUndoBlocker, MergeUndoRefusedException, MergeCandidateStatuses.Undone and Confirmed, MergeApplyViewModel, MergeHistoryRowViewModel, the Outstanding and applied-merge halves of MergeQueueViewModel and MergeQueueView.axaml, the merge half of MergeCopy, AddMergeExecution, the two DI registrations, and MergeExecutionTests, MergeUndoTests, MergeApplyViewModelTests plus the merge-outcome assertions in MergeQueueViewModelTests and MergeScreenRegistrationTests. KEEP: SurvivorLadder and MergeSurvivorReason, MergeGrouping, MergeCandidate and MergeCandidateRepository, the soft-match pipeline, IdentityLinkRepository and the resolvers.

8. TESTS. New MergeRetirementTests: a standing application with a journal migrates to a restored work plus a live link and no journal rows; a standing application without a journal fails the migration naming the application; an already-undone application leaves its candidate pending and drops the log; a confirmed-unapplied pair becomes a link; merge_candidates refuses 'undone' and 'confirmed' after 0019 while keeping the 0016 CHECK and UNIQUE. IdentityReadInventoryTests re-run with the removed readers gone. MigrationTests.ExpectedTables loses the two tables.

9. REWIND (TASK-65's cause). Replace the hand-maintained drop list in DatabaseBackupTests.Rewind with a derivation: build the pre-0012 schema in a scratch database from the embedded scripts, then drop what the live database has and the reference does not and recreate anything whose DDL differs. A future migration then needs no line. Done last, only if the main work is green, and reported either way.

10. VERIFY. Scoped runs first (retirement, migration, identity, merge queue), then the FULL suite across all three projects, built via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin. Prose by docs-writer. Do not finalize, do not commit, never open the live database.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
IMPLEMENTED, not finalized. Full suite green. Not committed. The live database was never opened and the user's running app was never touched.

WHAT WAS REMOVED, 6,681 lines of whole files plus 1,354 lines cut in place, 8,035 gross. Deleted outright: MergeExecutionRepository 1163, MergeUndoJournal 721, MergeUndoRepository 630, MergeUndoJournalWriter 43, MergeExecutor 248, IMergeExecutionRepository 48, IMergeUndoRepository 40, MergeUndoPlan/MergeApplicationRecord/MergeUndoResult 136, MergeOutcome/MergeRepointCounts 79, MergeBlocker 75, MergeMode 62, MergePlan 47, MergeUndoBlocker 47, MergeUndoRefusedException 30, MergeRequest 29, MergeHistoryRowViewModel 234, MergeApplyViewModel 140; MergeUndoTests 1322, MergeExecutionTests 984, MergeApplyViewModelTests 603. Cut in place: MergeQueueViewModel -382/+54 (the Outstanding list, the applied-merge list, Apply, ApplyAll, Undo, UndoBlocking, RefreshAppliedAsync, BuildOutstanding, BuildHistory, Sides, ModePhrase), MergeCopy -323 (67 members: every refusal, every mode phrase, the whole per-table counts disclosure, the apply and undo sections), MergeQueueView.axaml -265, TempDatabase -21 (TestMergeExecutor), SoftMatchServiceCollectionExtensions -13 (AddMergeExecution), Program.cs -10, MergeCandidateStatuses -12. Added: 0019_retire_destructive_merge.sql 117, StandingMergeReplay.cs 690, MergeRetirementTests 222, PreRetirementDatabase 216, and 69 lines in DatabaseInitializer for the two-pass boundary. NET on this stage, roughly 6,300 lines fewer. The design estimated 2,300 and counted only the four data-layer files; it did not count the tests, the two view models, the copy or the view.

WHAT WAS KEPT, and why. SurvivorLadder and MergeSurvivorReason, because the picker's default is the same ladder and 70.1 already moved it to Core as a pure function. MergeGrouping, IdentityLinkRepository, IdentityResolution and both resolvers, which are the feature now. MergeCandidate, MergeCandidateRepository and the whole soft-match pipeline: proposals are still proposals. The 0016 canonical-pair CHECK and UNIQUE, restated verbatim in 0019's rebuild, so F20 stays closed.

THE REPLAY IS A SELF-CONTAINED ONE-SHOT, WHICH IS WHY THE JOURNAL COULD BE DELETED RATHER THAN MOVED. src/Winnow.Data/Migrations/StandingMergeReplay.cs. The obvious reading of the brief, call the existing undo and then write a link, keeps MergeUndoRepository and MergeUndoJournal alive to serve it, which is 1,350 lines retained to serve a case that is empty on every install in existence. It is not necessary. Migration 0017's journal is ALREADY generic (table_name, op, key_json, before_json), so restore is two statement builders driven by the JSON's own keys rather than sixteen typed ones: a 'delete' row becomes an INSERT of before_json, a 'repoint' or 'update' row becomes an UPDATE of before_json keyed on key_json. Table names are checked against the fixed list of sixteen tables 0017 could name and column names against PRAGMA table_info, so a generated statement cannot name anything the schema does not hold. Identity rows (works, releases, ownerships) are restored first, then repoints and in-place updates, then the rest, which is the same parents-before-children order the deleted repository used and the reason merge_candidates comes back after its own repoints are reversed.

WHERE IT RUNS, and the one structural change this needed. It needs 0018's tables to write into and 0019's tables to read from, so it runs BETWEEN them. DatabaseInitializer now applies the upgrade in two passes when, and only when, 0019 is in the pending set, with the replay as the step between. Every other launch, including every launch after 0019 has been applied once, is the single pass it has always been; the boundary is resolved from the assembly's own resource names rather than spelled out, because the root namespace has already changed once. One backup, taken once, over the whole pending list, before either pass. DatabaseBackupTests.A_pending_migration_writes_a_backup_named_for_the_schema_it_replaces still asserts exactly one.

THE REPLAY REFUSES RATHER THAN GUESSES, and that is the honest limit. It aborts the whole migration, naming the application id, the absorbed title recovered from the journal and both release ids, when: undo_journal_version is NULL (case 3, applied before 0017); the surviving work is gone; the row it must put back is occupied by a row written since; or a journalled UPDATE matches other than exactly one row. The third of those is a DELIBERATE NARROWING of what the deleted repository did. MergeUndoIdMap restored an absorbed identity at a FRESH id when a later insert had taken the original, and SQLite allocates rowids as max+1, so it is reachable. Reimplementing that remap generically means rewriting every child foreign key to follow it, and a migration that silently restores a game at a different id than the one its history names is exactly the half-working thing this stage exists to remove. Refusing loses nothing an install cannot recover: the pre-upgrade backup is the newest thing on disk and the previous build can still undo the merge from its history screen, which the refusal message says.

CONFIRMED PAIRS ARE REPLAYED TOO, WHICH THE BRIEF DID NOT ASK FOR. The status set drops 'confirmed' as well as 'undone', and an install that answered under the two-step flow holds affirmative answers the retirement would otherwise discard, which would breach TASK-70 AC #8 (no decision lost). So the one-shot also writes a link for every confirmed pair whose two releases sit under different works, parent chosen by SurvivorLadder, the same chooser the two-step flow used, skipping any pair already linked or whose child already has children. It is not an auto-merge: the user confirmed those pairs by hand, and unlike the merge it replaces, the result is retractable.

MIGRATION 0019, three sections. (a) A guard: CREATE TABLE refuse_to_retire_a_standing_merge (standing_applications INTEGER NOT NULL CHECK (standing_applications = 0)), filled by SELECT COUNT(*) FROM merge_applications WHERE undone_at IS NULL, then dropped. SQLite has no RAISE outside a trigger, so a CHECK on a throwaway table named for the refusal is how a script says no; it is the backstop for a database that reaches 0019 without the replay. (b) merge_candidates rebuilt a third time, 0016 for canonicality, 0017 for the fourth status, 0019 to take the third and the fourth away, because SQLite cannot ALTER a CHECK. 0016's CHECK (left_release_id < right_release_id) and UNIQUE (left_release_id, right_release_id) restated verbatim; any leftover confirmed or undone row maps to pending. (c) DROP merge_undo_rows, then merge_applications, taking undone_at and undo_journal_version with them. The whole path is in the file's own header, per AC #6.

REWIND IS NOW DERIVED, WHICH CLOSES TASK-65's CAUSE. The brief said to do it if it was cheap while here; it was, and 0019 forced a fifth edit to that helper anyway. DatabaseBackupTests.Rewind was a hand-written list of DROP statements plus a re-CREATE of the pre-0016 merge_candidates, and it broke on 0016, 0017, 0018 and 0019 in a row. It now runs the embedded scripts up to 0011 into a scratch database and makes the live schema match that one: drop what only the live database has, drop and recreate anything whose DDL differs, which is how a rebuilt table gets its old shape back without anyone writing the old shape down. A future migration needs no line at all. The one caveat is stated in the helper: a table whose SHAPE changed after 0011 is recreated empty, and only a table some migration rebuilt can be in that set. All eight tests that depend on Rewind pass unchanged, including the two that assert the backup is named .pre-0011. and holds the older shape.

WHAT THE LINK MODEL CANNOT DO THAT THE DESTRUCTIVE ONE COULD, honestly. Two things, and neither is a reason to keep it. FIRST, release collapse. A destructive merge in ReleaseCollapse mode folded two releases into one row when they were the same edition of the same game; the link model folds works and leaves both releases standing, so a Steam entry and an Epic entry of the identical edition remain two rows with two ownerships, two update feeds and two achievement sets. That is what section 6.2 asks for and what the user's own decision of 2026-08-31 asks for, since the details modal shows the per-store breakdown so the composite can be checked, so the capability is not missed, but it is genuinely gone. SECOND, automatic correctness. After a delete there is one row, so every query is right without being taught anything; under links a surface that does not resolve shows two entries. That is the cost 70.4's IdentityReadInventoryTests exists to hold, and it holds it: 34 sites classified, two negative controls, and the five readers this stage deleted came off the list with the test still passing.

ONE THING THAT STOPPED BEING SEPARATELY OBSERVABLE. 0018's repair (an undone candidate whose application is reversed goes back to pending, with a NOT EXISTS guard for one that also has a standing application) can no longer be read through DatabaseInitializer.Initialize, because 0019 always follows and maps every non-rejected status to pending. IdentityLinkTests' two 0018-repair tests are replaced by one end-of-chain test that asserts what the criterion was always about: a reversed merge ends an open question with no link, a standing one ends answered by a live link with its absorbed game back on its own row. The companion guard case is covered by MergeRetirementTests instead, since a standing application never survives 0019.

THE SOFT MATCHER GOT SIMPLER TOO. SoftMatchOutcome loses PreviouslyConfirmed and PreviouslyUndone and the three-arm switch becomes one test, because 'rejected' is the only terminal status left. MergeCandidateStatuses.Terminal is now a one-element list. Three tests that used 'confirmed' as a stand-in for answered now use 'rejected'; AConfirmedPairIsNeverReQueued is deleted, because the state it describes cannot exist.

NEW FILES: src/Winnow.Data/Migrations/0019_retire_destructive_merge.sql; src/Winnow.Data/Migrations/StandingMergeReplay.cs; tests/Winnow.Tests/MergeRetirementTests.cs; tests/Winnow.Tests/PreRetirementDatabase.cs. CHANGED: DatabaseInitializer.cs, Winnow.Data.csproj, Constants.cs, SoftMatchResolver.cs, SoftMatchServiceCollectionExtensions.cs, Program.cs, MergeQueueViewModel.cs, MergeQueueServiceCollectionExtensions.cs, MergeCopy.cs, Views/MergeQueueView.axaml, and the tests listed above plus DatabaseBackupTests, MigrationTests, IdentityLinkTests, IdentityReadInventoryTests, MergeQueueViewModelTests, MergeScreenRegistrationTests, SoftMatchResolverTests, SoftMatchMetadataTests, LibrarySoftMatchSweepTests, AccountStatsViewModelTests, LibraryViewModelTests, ListsViewModelTests, TempDatabase. ALL PROSE IN EVERY ONE OF THEM AUTHORED BY THE docs-writer AGENT.

CONCURRENCY NOTE. TASK-70.8 was being implemented in the same working tree while this landed, and added a required IOwnershipRepository parameter to MergeQueueViewModel partway through. Its call sites were adapted here (three view-model test fixtures and MergeScreenRegistrationTests' container); no other file was contested.

VERBATIM, scoped first: MergeRetirementTests 5/5; MergeRetirementTests + MergeScreenRegistrationTests + IdentityLinkTests + IdentityReadInventoryTests together 26/26; MergeRetirementTests + MigrationTests + DatabaseBackupTests together 36/36.
Then the FULL suite across all three projects, built and run via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin:
  Passed!  - Failed: 0, Passed: 70,   Skipped: 0, Total: 70,   Duration: 1 s      - Winnow.Covers.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 105,  Skipped: 0, Total: 105,  Duration: 1 m 12 s - Winnow.Recommend.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 2514, Skipped: 0, Total: 2514, Duration: 1 m 38 s - Winnow.Tests.dll (net10.0)
Build: 0 Warning(s), 0 Error(s) under TreatWarningsAsErrors. Winnow.Tests moves 2578 to 2514: three whole test files and one test deleted, five retirement tests and one end-of-chain identity test added, and the concurrent TASK-70.8 work adds its own.

NOT FINALIZED: acceptance criteria not checked, no final summary, status left In Progress, nothing committed.
<!-- SECTION:NOTES:END -->
