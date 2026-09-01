---
id: TASK-62
title: Add merge-undo capability to the Same Game review screen
status: In Progress
assignee: []
created_date: '2026-09-01 02:51'
updated_date: '2026-09-01 04:03'
labels:
  - resolve
  - ui
  - data
dependencies: []
priority: medium
ordinal: 79000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Same Game review screen should let the user see previously merged pairs and undo a merge made in error. Migration 0016 records applications in a merge_applications audit table storing the candidate id, surviving and absorbed work and release ids, the merge mode, and a summary blob. The first question is whether that table captures enough to reverse an application faithfully, and the answer shapes the work. An undo must restore the absorbed identity and everything repointed away from it, or refuse honestly when it cannot, for example if a subsequent merge has consumed the surviving row, rather than partially reversing.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The Same Game screen displays a history of applied merges drawn from merge_applications
- [ ] #2 An undo action is offered for each application whose surviving rows still exist and whose reversal would be complete
- [ ] #3 When reversal cannot be performed faithfully the undo control is disabled with a reason visible to the user
- [ ] #4 A successful undo restores the absorbed work or release, repoints everything that was moved, and marks or removes the merge_applications row
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
### Implementation plan: merge undo

Undo needs a row-level journal because the existing `summary_json` records counts, not
identities or values. Option (b), a new append-only migration 0017, is the path. No
destructive rebuild of the user's database is needed. No live install has ever applied a
merge (`MergeExecutor` has no call site outside tests), so the "predates undo" caveat is
vacuous today but must still be implemented for future upgrades.

### 1. Migration: `0017_merge_undo.sql`

Append-only. Three schema changes and one new table.

**(a)** Rebuild `merge_candidates` to admit a fourth status. SQLite cannot ALTER a CHECK
constraint, so this is the same create-copy-drop-rename 0016 performed. Nothing
foreign-keys `merge_candidates`, so no twelve-step dance is needed.

```sql
CREATE TABLE merge_candidates_rebuilt (
    id                INTEGER PRIMARY KEY,
    left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
    score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
    signals_json      TEXT,
    status            TEXT NOT NULL DEFAULT 'pending'
                      CHECK (status IN ('pending', 'confirmed', 'rejected', 'undone')),
    CHECK (left_release_id < right_release_id),
    UNIQUE (left_release_id, right_release_id)
);
-- Copy, drop, rename, recreate ix_merge_candidates_status.
```

**(b)** Two columns on `merge_applications`:

```sql
ALTER TABLE merge_applications ADD COLUMN undone_at TEXT;
ALTER TABLE merge_applications ADD COLUMN undo_journal_version INTEGER;
```

`undone_at` is NULL while the merge stands. `undo_journal_version` is NULL for merges
that predate the journal (not undoable). Every existing row gets NULL, which is correct.

**(c)** The journal table:

```sql
CREATE TABLE merge_undo_rows (
    id              INTEGER PRIMARY KEY,
    application_id  INTEGER NOT NULL REFERENCES merge_applications(id) ON DELETE CASCADE,
    seq             INTEGER NOT NULL,
    table_name      TEXT NOT NULL,
    op              TEXT NOT NULL CHECK (op IN ('repoint', 'delete', 'update')),
    key_json        TEXT NOT NULL,
    before_json     TEXT NOT NULL,
    UNIQUE (application_id, seq)
);
```

One generic journal, not fifteen typed ones. The fifteen tables have five key shapes and
three operations; fifteen typed journals would be fifteen blocks of DDL all saying the
same thing. Legibility is preserved in the undo statements, which read from this table
per `table_name` and `op`.

The hard foreign key to `merge_applications` is deliberate. 0016's identity columns name
rows that are gone by design, so a reference would forbid the deletion or cascade the
record away. A journal row is different: it cannot outlive the application it describes.

### 2. Core contracts (`Winnow.Core.Merging`)

1. Add `Undone` to `MergeCandidateStatuses`.
2. New `MergeUndoPlan` record: the application row, the computed reversibility, and a
   list of blockers if not reversible.
3. New `MergeUndoBlocker` enum: `LaterMergeConsumedIdentity`, `PredatesUndoSupport`,
   `GameNoLongerExists`, `AlreadyUndone`.
4. New `IMergeUndoRepository` with `PlanUndoAsync(long applicationId)` and
   `UndoAsync(long applicationId)`.

### 3. Executor capture changes (`MergeExecutionRepository`)

5. Move `RecordAsync` to the front of `ApplyPlanAsync`. Insert the
   `merge_applications` row first with `undo_journal_version = 1` and a placeholder
   `summary_json`. Update `summary_json` at the end. The whole merge is already a
   single transaction, so nothing is ever visible half-written.
6. Thread `applicationId` and a monotonic `seq` counter through `UnifyWorksAsync`,
   `CollapseReleasesAsync`, `FoldOwnershipsAsync`, `FoldOwnershipAsync`, and
   `RepointMergeCandidatesAsync`.
7. Before every mutating statement, insert a capture row into `merge_undo_rows`. The
   capture's WHERE clause must be character-for-character the WHERE clause of the
   statement it precedes. For UPDATE OR IGNORE / DELETE residue pairs, capture twice:
   the full set as `repoint` before the UPDATE, the residue as `delete` before the
   DELETE. Each capture is correct on its own rather than clever about covering both.
8. Three new capture points that have no counterpart today:
   - The surviving `works` row before the COALESCE fill (`update`).
   - The surviving `ownership_accounts` rows before the in-place merge (`update`).
   - The `works`, `releases`, and `ownerships` rows before their DELETEs (`delete`).
9. All capture statements use SQLite's `json_object()` so no row round-trips through
   C#.

### 4. Undo repository (`MergeUndoRepository`, new file in `Winnow.Data`)

10. `PlanUndoAsync`: gate one. Query the `merge_applications` row. Check four blockers
    in order: already undone, predates undo support, game no longer exists, later merge
    consumed an identity. The last check queries whether any later un-undone
    `merge_applications` row names any of this row's four identity columns.
11. `UndoAsync`: gate two inside a transaction. Verify every journal row's current state
    matches what the merge left behind. Then restore in reverse order: parents before
    children. Re-insert deleted rows, revert repointed rows, restore updated rows.
    Handle absorbed-id reuse: restore the identity at its original id when free, at a
    fresh one when not. Verify summary_json counts after the restore. Set `undone_at`.
    Set the `merge_candidates` row to status `undone`.
12. The undo repository's table list must equal the executor's dependent-table inventory
    (`AssertDrainedAsync`). A table added to one and not the other fails loudly.
13. Re-canonicalize restored `merge_candidates` pairs to satisfy 0016's
    `CHECK (left_release_id < right_release_id)`.

### 5. Executor wrappers (`MergeExecutor` in `Winnow.Resolve`)

14. Add `PreviewUndoAsync` and `UndoAsync` wrappers so the policy stays in
    `Winnow.Resolve` and `Winnow.App` never touches SQL (section 5.1).

### 6. UI

15. The merge history screen computes reversibility each time it loads, never cached.
16. Four disabled-state reasons, each with its own sentence:
    - A later merge absorbed one of these games (name it, offer "undo it first").
    - This merge predates undo support.
    - A game this merge touched no longer exists.
    - Already undone (show as history, no control).
17. History rows read in user language: which two games became one, and when. Counts go
    in a detail view.
18. The Flare accent marks only unread updates (design-system rule). Disabled controls
    must not use it.
19. An undone pair's history row offers a distinct "merge again" affordance, not the
    ordinary confirm button.

### 7. Tests

20. Write the 35 tests enumerated in the investigation notes. Prioritize the
    faithfulness round-trip (tests 1-6), the deduplication payload tests (7-14), and the
    refusal tests (23-30) first; these are the ones that prove the journal captures
    enough and the undo leaves the database clean.

### Data/Resolve pass refinements (2026-08-31)

21. Capture partition, not full-set-plus-residue. Step 7's 'capture the full set as
    repoint before the UPDATE, the residue as delete before the DELETE' double-counts
    every non-moving row: it would journal a repoint whose key names the SURVIVOR's own
    row, and the undo would then drag the survivor's row onto the absorbed id. Step 7's
    own requirement (each capture correct on its own) is met instead by partitioning at
    capture time: the repoint capture carries the anti-join UPDATE OR IGNORE applies
    implicitly (NOT EXISTS a row already on the survivor's key), and the residue capture
    keeps the DELETE's WHERE verbatim. The two sets are disjoint and exhaustive.

22. key_json is a full equality predicate, not merely an identifier. For repoint it holds
    the primary key AND the repointed column(s) at their POST-merge values, so 'the row
    still sits on the parent this merge moved it to' is the same COUNT(*) = 1 as 'the row
    still exists'. For delete it holds the key whose freedom the restore needs. For
    merge_candidates that key is (left_release_id, right_release_id), not id, because
    0016's UNIQUE on the pair is the constraint a restore can collide with.

23. Superseded repoint rows. A release repointed by the work unify is then deleted by the
    release collapse, in one application. Gate two exempts a repoint row whose primary key
    also appears as a delete row for the same table in the same application; the restore
    order (deletes first, parents first) puts the row back before the repoint reversal
    runs, so nothing is lost.

24. Re-canonicalisation comes free from capturing both pair columns. The journal stores the
    pre-merge (left, right) whole rather than a substitution rule, so the restored pair is
    the pair as it stood. MIN/MAX is applied unconditionally afterwards because an absorbed
    id restored at a FRESH id may sort on the other side of its partner.

25. Verify-then-mutate. The fresh id for a reused absorbed identity is computed as
    COALESCE(MAX(id),0)+1 before any write, so gate two runs to completion before the first
    restore statement, and drift aborts having written nothing.

26. summary_json count verification distinguishes the three merge_candidates deletes by
    data, not by ordering: the answered pair is the delete whose before-pair is exactly
    {absorbed, surviving}; a residue delete names the absorbed release; a pending-collision
    delete does not. Only the residue counts toward duplicate_rows_dropped, matching the
    executor.

27. MergeExecutor takes IMergeUndoRepository as an OPTIONAL constructor parameter. Winnow.App
    is held by concurrent work this pass, so Program.cs cannot register the implementation
    yet; the wrappers throw a named error until it does. TASK-62's second half must add
    services.AddSingleton<IMergeUndoRepository, MergeUndoRepository>().

28. MergeUndoBlocker gains None and ApplicationNotFound beside the plan's four: the plan's
    list can express neither 'reversible' nor 'no such application'.

29. SoftMatchResolver gains an explicit undone case and SoftMatchOutcome a trailing
    PreviouslyUndone = 0. Without it an undone pair falls through to default: and is
    counted as 'already pending', which is a false statement about a terminal row.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
### Recommendation

Option (b): undo needs richer auditing. A new append-only migration 0017 suffices; no
destructive rebuild of the user's database is needed. Nothing is lost.

### Finding 1: what merge_applications captures today

`merge_applications` records which two identities, which mode, when, and how many rows
moved in aggregate. `summary_json` is a serialized `MergeRepointCounts`, one integer per
dependent table plus a single `duplicate_rows_dropped` scalar.

Two of those fields are structurally always zero: `achievements` and
`achievement_unlocks` never move. The surviving-release rule prefers the side holding
them, and a collapse with achievements on both sides is refused outright.

`duplicate_rows_dropped` aggregates drops across eight different tables into one number.
It cannot say which table lost rows.

Conclusion: it is a receipt, not a reversal record. It records no row identities and no
column values.

### Finding 2: what the merge destroys that the audit does not record

Walked every table in `MergeExecutionRepository.cs`. Results by category:

**Destructive in-place edits with no record at all (2 operations):**
- `works`, surviving row: COALESCE fill across `igdb_id`, `sort_name`,
  `first_release_year`, `summary`, `cover_url`, `publisher`, `steam_app_type`,
  `epic_categories`, plus a conditional `name` / `name_is_provisional` promotion.
  Nothing records which columns were filled or their prior values.
- `ownership_accounts`, surviving row: `playtime_minutes`, `last_played_at`, and
  `source` taken whole from the absorbed row when it was seen later; `last_seen_at`
  takes MAX, `first_seen_at` takes MIN. The survivor's prior values are overwritten.

**Deleted rows whose identity fields are gone (audit keeps only the id):**
- `works`, absorbed row: `name`, `sort_name`, `first_release_year`, `summary`,
  `cover_url`, `publisher`, `steam_app_type`, `epic_categories`,
  `name_is_provisional`, `igdb_id`.
- `releases`, absorbed row: `work_id`, `igdb_version_id`, `name`, `platform`,
  `edition_note`.
- `ownerships`, collision-folded row: `acquired_at`, `license_type`,
  `price_paid_cents`, `price_source`, `install_path`, `installed`.

**Repointed rows with no record of which rows moved (11 tables):**
- `releases`, `work_facets`, `external_ids`, `ownerships` (non-colliding),
  `play_records`, `playtime_snapshots`, `sessions`, `ownership_accounts`
  (non-colliding), `update_events`, `update_acknowledgements`, `feed_verdicts`.

**Dropped rows carrying payload columns not reconstructible from the survivor (4 tables):**
- `list_items`: dropped row carries its own `position`.
- `release_facets`: dropped row carries its own `rank`.
- `feed_surfacings`: dropped row carries its own `shelf_id`.
- `merge_candidates`: three separate destructive operations. The confirmed pair is
  deleted (taking `score` and `signals_json`). Pending proposals colliding with a
  decided row moving onto their pair are deleted entirely. Remaining rows are
  repointed with UPDATE OR IGNORE and the residue deleted.

**Safe (no undo concern):**
- `sessions`: no unique constraint, every row moves whole, `session_notes` ride along
  on `session_id`.
- `achievements` / `achievement_unlocks`: never move. Asserted, not assumed. This is a
  design property worth keeping.

Headline: eleven of fifteen repointed tables are ambiguous after the fact, two operations
are destructive in-place edits with no record, and four tables drop rows carrying payload
that cannot be reconstructed from the survivor.

### Finding 3: subsequent merges

A later merge can make an earlier undo unfaithful in two ways:

**(a)** A later merge consumes an identity this one produced. Merge A absorbs R2 into R1;
merge B absorbs R1 into R5. Undoing A would have to restore R2 and leave R1's rows on
R1, but R1 no longer exists. Even a perfect journal cannot reconstruct a state in which
R1 exists, because the result never existed.

**(b)** A later operation moves or deletes a row this merge moved. The journal names a row
that is no longer where it was left.

Detection uses two gates, not one heuristic:

**Gate one** (cheap, drives the UI's enabled state): the application's surviving work
(and, for a release collapse, its surviving release) still exists, AND no later un-undone
`merge_applications` row names any of this row's surviving or absorbed ids in any of its
four identity columns. This is LIFO scoped to identities actually touched, so two merges
on unrelated games can be undone in either order.

**Gate two** (inside the transaction, the proof): every journal row recorded as repointed
still exists and still sits on the parent this merge moved it to; every row recorded as
deleted has a key that is currently free; every row recorded as updated still exists; and
after the restore the row counts match what `summary_json` claimed. Any mismatch throws,
the transaction rolls back, and the database is untouched. Same shape as the existing
`AssertDrainedAsync` tripwire.

**Surrogate-id reuse:** SQLite allocates rowids as `max+1` without AUTOINCREMENT. If the
absorbed work or release held the highest id, a later insert can take it. Rebuilding
`works`, `releases`, and `ownerships` onto AUTOINCREMENT was considered and rejected:
three table rebuilds and every dependent foreign key, to prevent something the undo can
handle directly. Release and work ids never leave the database (the cover cache keys on
provider ids, nothing on disk persists a rowid), so undo restores the absorbed identity
at its original id when free and at a fresh one when not. The user cannot observe the
difference.

### Finding 4: ON DELETE and the 0016 constraints

The cascades do not obstruct undo. Restoring runs in the opposite order to the merge,
parents before children, so a re-inserted work exists before its releases are repointed
back onto it. Nothing is deleted during an undo, so no cascade fires.

0016's two constraints on `merge_candidates` constrain the restore correctly.
`CHECK (left_release_id < right_release_id)` means a third-party pair moved back onto the
absorbed release must be re-canonicalized, not merely un-substituted: the absorbed id may
sort on the other side of its partner than the surviving id did.
`UNIQUE (left_release_id, right_release_id)` means restoring a deleted pending proposal
must find its pair free, which gate two checks.

The `merge_applications` row must be inserted first with a placeholder summary and its
`summary_json` updated at the end. The whole merge is already a single transaction.

### Finding 5: no legacy burden

`MergeExecutor` is registered in DI but has no call site anywhere in `src/`.
`IMergeExecutionRepository` is registered in `Program.cs` and never resolved outside
tests. The live database holds zero `merge_applications` rows. The caveat "merges applied
before this lands are not undoable" is vacuous today but must still be implemented via
`undo_journal_version` for future installs that upgrade.

### The merge_candidates question: a fourth status, `undone`

Four alternatives were evaluated:

- **`confirmed`**: disqualifying. `GetConfirmedUnappliedCandidateIdsAsync` selects
  `status = 'confirmed'` with the two sides on different works, so the next
  `ApplyAllConfirmedAsync` pass silently re-merges the pair. This is a loop.
- **`pending`**: does not loop (nothing auto-applies a pending pair, section 5.3
  forbids it), but re-asks a question the user has now answered twice.
- **`rejected`**: stops the loop and needs no schema change, but overstates. Rejected
  means "these are different games". A user may undo because the merge picked the wrong
  survivor or collapsed editions that should stay apart. That is a complaint about the
  merge, not about the pair.
- **`undone`**: correct. Not `confirmed`, so the SQL predicate in `BuildPlanAsync`
  excludes it. Not `pending`, so `SoftMatchResolver` treats it as terminal exactly as
  it treats `rejected` (the resolver preloads every row and only `Pending` rows are
  eligible for a score update or withdrawal). And it is honest: the review screen can
  say the pair was merged and unmerged.

What stops an immediate re-merge is four things, in order of how hard they are to defeat:
the SQL predicate in the planner, the SQL predicate in the batch selector, the resolver's
terminal-status rule, and a deliberate re-confirmation by the user. That last one should
be a distinct "merge again" affordance on the history row showing the prior undo, not the
ordinary confirm button.

### UI rules

Reversibility depends on every later merge, so the enabled state must be recomputed each
time the screen loads. Never cached.

Four disabled-state reasons, each needing its own sentence in the UI:

1. A later merge absorbed one of these games. Name that merge and offer the constructive
   path: undo it first. This is the only reason with a user action attached.
2. This merge predates undo support (`undo_journal_version IS NULL`). Vacuous today,
   real for any install that upgrades later.
3. A game this merge touched no longer exists.
4. Already undone. Show as history with no control.

History rows read in user language: which two games became one, and when. Counts belong
in a detail view. The Flare accent marks only unread updates (design-system rule), so a
disabled control must not use it.

### Test list

**Faithfulness round-trip:**

1. Full-database diff (dump every table ordered, compare against pre-merge snapshot) for
   a release collapse.
2. Same for a work-only merge, asserting every release of the absorbed work moves back.
3. Survivor's own rows never move: seed with ownership, play records, sessions, list
   membership, facets; assert ids and parents are identical before and after.
4. Survivor's COALESCE-filled work columns revert to NULL where the merge filled them,
   left alone where already set.
5. The `name` / `name_is_provisional` promotion is reverted.
6. The `ownership_accounts` in-place merge is reverted field by field, including
   `first_seen_at` and `last_seen_at`.

**Deduplicated rows (the cases a count cannot restore):**

7. A byte-identical `play_records` observation dropped under
   `ux_play_records_observation` is re-inserted on the restored ownership; the survivor
   keeps exactly one copy.
8. Same for `playtime_snapshots`.
9. A collision-deleted `ownership_accounts` row comes back with its own playtime,
   `last_played_at`, `source`, and seen-window, not the survivor's.
10. A dropped `list_items` row comes back with its own `position`, differing from the
    survivor's. This proves the journal captured payload, not just a key.
11. Same for `release_facets` and its `rank`.
12. Same for `feed_surfacings` and its `shelf_id`.
13. `work_facets`, the key-only case, included for completeness.
14. An equivalent `update_events` collision comes back on the restored release; the
    survivor keeps its own.

**Collision-heavy composite:**

15. One merge in which every dedup path fires at once (same-store ownership fold with
    colliding play records, snapshots, account rows, plus colliding list items, release
    facets, surfacings, work facets, update events) round-trips to a byte-identical
    database.

**merge_candidates:**

16. A release-collapse undo re-inserts the deleted pair with its original score and
    `signals_json` at status `undone`.
17. A work-only undo flips the standing row from `confirmed` to `undone`.
18. Third-party pairs repointed onto the survivor move back and are re-canonicalized
    to satisfy 0016's CHECK.
19. Pending proposals deleted because a decided row moved onto their pair are restored.

**Loop prevention:**

20. `GetConfirmedUnappliedCandidateIdsAsync` does not return an undone candidate.
21. `ApplyAsync` on an undone candidate returns not-applied with
    `CandidateNotConfirmed`.
22. A fresh soft-match sweep over the same library neither re-queues the pair nor trips
    `UNIQUE (left_release_id, right_release_id)`.

**Refusal:**

23. Merge A, then merge B consuming A's survivor. A reports not-undoable for the right
    reason. Calling undo on A throws and leaves the database byte-identical.
24. Undo B then A; both succeed.
25. Two merges on disjoint identities undo in either order.
26. `undo_journal_version IS NULL` reports the predates-undo reason.
27. A journalled row deleted by something else aborts the undo with nothing partially
    restored.
28. The absorbed id has been reused: the identity is restored at a fresh id and every
    journalled child lands on it.
29. Undoing twice does not double-restore.
30. A failure mid-undo leaves the database exactly as it was (mirror of the existing
    `A_failure_mid_merge_leaves_the_database_exactly_as_it_was`).

**Structural:**

31. The undo repository's table list equals the executor's dependent-table inventory;
    a table added to one and not the other fails loudly.
32. `summary_json` counts are verified during the undo; a mismatch aborts.

**Buckets:**

33. A merge that combined two ownerships' playtime moves a game between derived buckets;
    the undo moves it back. Seeded across zero playtime and boundary thresholds per the
    standing bucket-test rule.

**Migration:**

34. 0017 applies over a database populated by 0001-0016 carrying `merge_candidates` in
    all three existing statuses and preserves every row and status.
35. The twice-rebuilt `merge_candidates` still rejects self-pairs and mirror duplicates.

### Data and Resolve pass landed (2026-08-31). No UI — that is the second half.

Scope held to Winnow.Core, Winnow.Data, Winnow.Resolve and tests/Winnow.Tests.
Winnow.App untouched (concurrent settings work holds it).

**Migration 0017_merge_undo.sql** — merge_candidates rebuilt to admit 'undone';
undone_at and undo_journal_version on merge_applications; merge_undo_rows
(application_id FK ON DELETE CASCADE, seq, table_name, op, key_json, before_json,
UNIQUE (application_id, seq)). DDL as the plan specified.

**Journal shape.** key_json is a full equality predicate, not just an identifier.
repoint: primary key + repointed column(s) at POST-merge values, before_json the
pre-merge parent value(s). delete: the key a restore needs free, before_json every
column. update: primary key, before_json the columns that could have changed.
Sixteen tables: the fifteen dependents that move, plus works, which is nobody's
dependent but is filled in place and deleted. achievements and achievement_unlocks
contribute nothing and are asserted to stay put.

**Executor.** RecordAsync now runs first with a placeholder summary and
undo_journal_version = 1; SummariseAsync rewrites summary_json at the end. Every
mutating statement is preceded by a capture carrying that statement's WHERE.

**Gate one** (MergeUndoRepository.PlanUndoAsync / ListUndoPlansAsync): already
undone, predates undo support, surviving identity gone, later un-undone
application whose work ids or release ids overlap this one's. Names the blocking
application so the UI can offer 'undo it first'.

**Gate two** (inside UndoAsync's transaction): every non-superseded repoint row
still matches its post-merge key; every restore key is free; every in-place row
still exists; and summary_json's per-table counts, ownerships_folded,
achievements = 0, and duplicate_rows_dropped all agree with the journal. Any
mismatch throws and rolls back.

**Undone wiring, all four checks tested.** BuildPlanAsync's status = 'confirmed'
predicate; GetConfirmedUnappliedCandidateIdsAsync's; SoftMatchResolver gained an
explicit terminal arm plus SoftMatchOutcome.PreviouslyUndone (without it an undone
row fell to default: and was counted as pending); and GetPendingAsync does not
offer it, so re-merging needs a distinct affordance the screen must add.

**Departures from the plan, all with reasons, recorded as plan items 21-29 above.**
The one that changed behaviour rather than shape: step 7's 'capture the full set
then the residue' would have journalled a repoint whose key names the SURVIVOR's
own row. Captures now partition at capture time.

Two further departures found while testing, both real defects the plan did not
anticipate:
  * merge_candidates is the one table whose deleted row's key is legitimately
    occupied at undo time — the executor deletes a pending proposal BECAUSE a
    decision is moving onto its pair. Gate two exempts a pair this same undo is
    about to vacate, and the restore reverses merge_candidates repoints before
    re-inserting merge_candidates deletes.
  * a repoint reversal must redirect its KEY through the id map, not only the
    value it writes back. A release repointed by the work unify and then deleted
    by the collapse is restored possibly at a fresh id; without the redirection
    it kept the surviving work.

**Tests.** tests/Winnow.Tests/MergeUndoTests.cs — 33 tests covering the plan's
list: full-database round-trip for collapse and work-only, survivor rows never
move, both in-place overwrite cases, each payload-carrying dedup case (list
position, facet rank, shelf id, folded ownership account, play record, snapshot,
work facet, update event), the collision-heavy composite, all four
merge_candidates cases including re-canonicalisation and the displaced proposal,
loop prevention through all four gates, LIFO refusal and its converse, disjoint
merges in either order, predates-undo, drift abort, id reuse, double undo,
mid-undo failure, the inventory equality, a column-coverage check against
pragma_table_info, summary-count drift, and the bucket move-and-move-back.
Migration tests 34 and 35 added to MigrationTests.

DatabaseBackupTests.Rewind updated for 0017 (drops merge_undo_rows before
merge_applications; 0017 SchemaVersions row added to the delete).

**Full suite: 2601 passed, 0 failed, 0 warnings** (Winnow.Tests 2433,
Winnow.Recommend.Tests 98, Winnow.Covers.Tests 70).

**Left for the second half / TASK-64:** Program.cs must register
services.AddSingleton<IMergeUndoRepository, MergeUndoRepository>(). MergeExecutor
takes it as an optional constructor parameter and its PreviewUndoAsync /
HistoryAsync / UndoAsync wrappers throw a named error until it is registered.
<!-- SECTION:NOTES:END -->
