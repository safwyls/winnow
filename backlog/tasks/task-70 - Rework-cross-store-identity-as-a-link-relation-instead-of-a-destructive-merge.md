---
id: TASK-70
title: Rework cross-store identity as a link relation instead of a destructive merge
status: To Do
assignee: []
created_date: '2026-09-02 00:12'
updated_date: '2026-09-02 00:19'
labels: []
dependencies: []
ordinal: 87000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
DESIGN PASS OUTPUT. Recommendation, reasoning and staged plan for reworking the Same Game / merge subsystem. No code was changed while producing this. The stages ship as subtasks.

## What the user reported

The Same Game screen shipped across TASK-5, TASK-62, TASK-64 and TASK-66 and was used against a live library. Five complaints, which together indict the model rather than the screen:

1. No indicator of which title survives and which is folded in, or why. The user wants to specify precedence. A tiebreaker ladder exists (`ChooseWork`: holds `igdb_id`, then name not provisional, then more releases, then lowest id) but in the common cross-store case it falls through to lowest id, which is ingestion order, and the UI never states the reason.
2. Several proposals name the same game. Answering one makes the others stale, and the queue then renders BLOCKED with “Already one game. Nothing to merge.” The queue should be a living view that never shows that state.
3. Some proposals are a base game and its expansions. Grouping Civilization IV with six expansions as repeated pairwise merges is tedious. The user wants the one-to-many relation presented once, with the option to take none, some, or all.
4. The details modal should say that this game also covers these other titles. The user suggests that instead of merging destructively, the absorbed title becomes a child of the primary.
5. Undo reports success and the pair then reads as unmergeable. Merge and undo should be idempotent and bidirectional, repeatable.

## What the research found

Read in full: `game-library-design.md` sections 5.3 and 6, migrations `0016_merge_canonicality.sql` and `0017_merge_undo.sql`, `MergeExecutionRepository` (1151 lines), `MergeUndoJournal` (721), `MergeUndoRepository` (630), `MergeUndoJournalWriter`, `MergeExecutor`, `MergeQueueViewModel` (954), `MergePreviewViewModel`, `MergeCopy`, `MergeCandidateRepository`, `LibrarySoftMatchSweep`, `SoftMatchResolver`, `SoftMatchAdmission`, `LibraryQueryRepository`, `DemoConsolidation`, `LibraryViewModel`, and F09/F20 in `docs/code-review-2026-08-28.md`.

Six findings decide the design.

**F-A. Merging does not deduplicate the library grid, and never did.** The grid renders one tile per OWNERSHIP row (`LibraryQueryRepository` selects `FROM ownerships`; `LibraryViewModel` builds one `GameTileViewModel` per bucket row). Ownership is per store by the four-layer model. So a title owned on Steam and Epic is two tiles before the merge and two tiles after it, work-only or collapsed. Whatever else the shipped merge bought, it did not buy the visible unification the user expected. Any design answering point 4 must decide the grid question, which is separate from the destructive-versus-link question and is a product decision.

**F-B. Section 6.2 already prescribes the link model.** Quote: render per-release rows nested under the Work; the unified view is a query, not a stored merge. Section 6.1 says the same of buckets. The destructive executor contradicts a line of the design record.

**F-C. The codebase already contains a working non-destructive identity fold** with exactly the reversibility the user asks for. `DemoConsolidation` runs inside the bucket query, hides a demo whose full game is owned, writes nothing, deletes nothing, and its own comment states that removing the base game makes the demo reappear on the very next read. That is the pattern to extend.

**F-D. The undo machinery already needs the deleted rows for display.** `MergeUndoRepository.LoadLogAsync` recovers the absorbed title with `json_extract(j.before_json, $.name)` over `merge_undo_rows`, because the `works` row is gone. The system already reads a journal to reconstruct a row it deleted so a screen can print a name. Point 4 asks for that row to be readable directly.

**F-E. Every refusal the merge can emit exists only because rows are being destroyed.** `DistinctEditions`, `AchievementsOnBothSides`, `ConflictingUpdateEvents`, `LaterMergeConsumedIdentity`, `GameNoLongerExists`, `PredatesUndoSupport`. A link never refuses: two achievement sets stay on two releases (which 6.2 requires), two update feeds stay separate (a Steam build push is not an Epic build push, so merging `update_events` across stores was arguably wrong), two editions stay two releases.

**F-F. The read surface is far more concentrated than feared.** `ILibraryQueryRepository.GetOwnershipBucketsAsync` is the single source for the grid, the rail bucket counts, the All Games count, the filter panel options, list counts, the recommender candidate set, the feed, and the account-visibility hidden count. Facets, updates, lists and details are reached per release from a tile that query already produced. Store title counts and the enrichment target queries deliberately must NOT resolve. There is one chokepoint, not fifteen.

## Recommendation

**Make identity unification a LINK at the Work layer, resolved on read, and stop collapsing releases entirely.**

The decisive argument is not elegance, it is the failure mode. Under the destructive model a bug loses rows permanently, which is why 0017 exists and why its own header enumerates what the receipt could not restore: `list_items.position`, `release_facets.rank`, `feed_surfacings.shelf_id`, the folded `ownerships` and `ownership_accounts` rows, eight COALESCE-filled `works` columns, and eleven repointed tables with no record of which rows moved. Under the link model, a surface that fails to resolve shows exactly what the app shows today before a merge: two entries for one game. **The link is purely additive, so an unresolved link degrades to the status quo, never to corruption.** That asymmetry outweighs the destructive model, whose one real advantage is that after the delete every query is automatically correct because there is only one row.

The secondary arguments agree. Undo becomes retracting a row rather than replaying a fifteen-table journal. Idempotent re-link falls out with no status machine. The details modal can list what a title covers because the row still exists. One-to-many is N child rows in one transaction rather than N sequential pairwise operations each invalidating the next. Both `igdb_id` values survive on their own rows instead of the absorbed one dying with the row, so the child keeps being enriched and can still fill the group. And roughly 2,300 lines of executor, journal, journal writer, undo repository and their tests are deleted rather than maintained.

The honest cost: every user-facing read of works or ownerships must resolve links or show duplicates, and that discipline must be enforced by a test rather than by memory. If it cannot be, keep destruction; see What would change this recommendation.

## The two relations are different facts

Steam Prey and Epic Prey are one game sold twice. Civilization IV and Beyond the Sword are two products where one depends on the other. Structurally both are child-points-at-parent with at most one parent per child, so **one table with a `kind` column is sufficient DDL**. Semantically they are not interchangeable in any read, and the design must say so.

- **`same_game` changes identity.** The child contributes no additional title to the library count; its playtime is the same game being played; its releases nest under the parent for achievements per 6.2; a feed dismissal of one side must suppress the other; the sweep must never propose the pair again.
- **`expansion_of` changes presentation only, by default.** The child is a separate product, separately owned and separately played. Summing 30h of Civilization IV with 120h of Beyond the Sword produces a number no source reported about either. Collapsing an unplayed expansion into a played parent destroys probably the best recommendation the app can make: you played the base game for two hundred hours and never opened the expansion. So `expansion_of` changes no count, no playtime, no bucket and no recommendation. It groups in the details modal and, behind a setting, in the grid.

Consequence: the expansion feature is inert by default and ships without touching a query, which is why it is late in the order and low risk.

**Population note, which bounds point 3 sharply.** Epic DLC and GOG DLC never enter the database: `EpicLibrarySource` skips manifests and catalog entries with a non-empty `mainGameItem.id`; `GogLibrarySource` skips entries where `GameId` differs from `RootGameId`. Steam DLC generally has no separate library entry. So the only expansions in a Winnow database are Steam appids Valve types as games whose titles extend another owned title, which is exactly the Civilization IV case. A dedicated detector is required (normalised-title prefix containment plus shared publisher and year proximity) because the soft matcher scores title distance and will not propose them.

## Schema sketch (migration 0018, additive)

```sql
CREATE TABLE identity_acts (
    id           INTEGER PRIMARY KEY,
    kind         TEXT NOT NULL CHECK (kind IN ('link', 'unlink')),
    performed_at TEXT NOT NULL,
    note         TEXT
);

CREATE TABLE identity_links (
    id              INTEGER PRIMARY KEY,
    act_id          INTEGER NOT NULL REFERENCES identity_acts(id) ON DELETE CASCADE,
    child_work_id   INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    parent_work_id  INTEGER NOT NULL REFERENCES works(id) ON DELETE CASCADE,
    kind            TEXT NOT NULL CHECK (kind IN ('same_game', 'expansion_of')),
    source          TEXT NOT NULL CHECK (source IN ('user', 'hard_id')),
    evidence_json   TEXT,
    applied_at      TEXT NOT NULL,
    retracted_at    TEXT,
    CHECK (child_work_id <> parent_work_id)
);

CREATE UNIQUE INDEX ux_identity_links_live
    ON identity_links(child_work_id) WHERE retracted_at IS NULL;

CREATE INDEX ix_identity_links_parent
    ON identity_links(parent_work_id) WHERE retracted_at IS NULL;
```

Properties that fall out of the shape:

- The partial unique index gives every work at most one live parent, so resolution is a single LEFT JOIN and two-cycles are impossible.
- Depth is fixed at one: a parent may not be a child and a child may not be a parent. Enforced in the repository plus a database-integrity test (SQLite cannot express it as a CHECK). Linking a group whose chosen primary already has children re-parents those children inside the same act.
- Append-only. Retracting stamps `retracted_at` rather than deleting, so history is the table itself and `merge_applications` becomes unnecessary.
- `act_id` makes a one-to-many grouping a single act with a single undo, which is points 3 and 5 in one column.
- Re-linking after an unlink is a fresh row. No terminal state, and no re-confirmation affordance to build.

`merge_candidates` changes: the status set drops `confirmed` and `undone` and keeps `pending` and `rejected`. A pair is answered affirmatively if and only if a live link exists between its two resolved works, so the answer has exactly one home. The 0016 `CHECK (left_release_id < right_release_id)` and `UNIQUE (left, right)` are restated verbatim, because that part of 0016 is still right.

## Resolution, and where it deliberately does not apply

**RESOLVE (`same_game` only):**

- `LibraryQueryRepository.GetOwnershipBucketsAsync` — the chokepoint. Add a resolved work id alongside the existing demo-consolidation pass. Feeds the grid, rail counts, All Games, filter options, list counts, the recommender, the feed, the account-visibility count.
- `LibraryViewModel` display title and cover — read the parent name and cover so both store entries read as one game even while the grid stays ownership-grained.
- `GameDetailsViewModel` — new Also covers section (point 4) and Expansions section.
- `MergeCandidateRepository.GetPendingAsync` and `MergeQueueViewModel` — pairs are read and grouped by resolved work.
- `SoftMatchAdmission.CouldPropose` and `LibrarySoftMatchSweep.BuildRequests` — the existing `left.WorkId == right.WorkId` test becomes resolved equality. This single change makes the existing retire machinery withdraw linked pairs automatically.
- Feed suppression — `feed_verdicts` and `feed_surfacings` are keyed by release, and dismissing one store entry must suppress the other. Read as any release under the resolved work.

**DO NOT RESOLVE, each for a stated reason:**

- `GetFacetTargetsAsync` and the enrichment targets (`WorkEnrichment`, `ProvisionalNameTarget`, `WorkRepository.GetAllAsync`) — every work row still needs enriching on its own ids; resolving here starves the child, and the child `igdb_id` is what fills the group.
- `update_acknowledgements` — a Steam build acknowledged is not an Epic build acknowledged.
- `achievements` and `achievement_unlocks` — 6.2 requires two sets, two rows, never blended.
- `IStoreTitleCounts` — counts are per tile by design (design-system 11.2): a game owned on two stores counts in both.
- `list_items` — list membership is an explicit user act on a store entry. Display de-duplicates by resolved work only if and when the grid becomes work-grained.

## What happens to 0016, 0017 and the undo journal

- **0016 (a), `merge_candidates` canonicality — KEEP.** F20 stays closed; the constraint is restated by the 0018 rebuild.
- **0016 (b), `merge_applications` — SUPERSEDED** by `identity_links` plus `identity_acts`. Dropped in the retirement migration after replay.
- **0017 (a), the `undone` status — SUPERSEDED and must go.** It is exactly what makes re-merge terminal (point 5).
- **0017 (b) and (c), `undone_at`, `undo_journal_version` and `merge_undo_rows` — DROPPED** with the executor.
- **Deleted code:** `MergeUndoJournal`, `MergeUndoJournalWriter`, `MergeUndoRepository`, `MergeExecutionRepository`, the fifteen-table repoint inventory, the cascade tripwire, `MergeMode`, `MergeBlocker`, `MergeUndoBlocker`, `MergeUndoPlan`, and most of `MergeExecutionTests` and `MergeUndoTests`. `ChooseWork` survives, demoted from write-path decider to the default suggestion in the primary picker.

That work was not wasted. It produced the dependent-table inventory, closed F20, and its own migration header is the best available evidence against destroying rows: it enumerates precisely what a destructive merge cannot give back.

## Migration reality

The live database has at least one merge applied and undone. Three cases:

1. **Applications with `undone_at` set.** The rows are already restored. 0018 need only reset the `merge_candidates` row from `undone` to `pending` so the pair returns to the (now grouped) queue. This alone fixes point 5 on the live database.
2. **Applications standing with `undo_journal_version` set.** The absorbed rows are gone but recoverable. The supported path is REPLAY: run the existing undo for each standing application, then write one `identity_links` row from the restored child work to the survivor, then drop the journal tables. It reuses code that exists and is tested, and it is the only path that keeps the decision AND recovers the rows. Run it as a one-shot at the C# layer, not in SQL.
3. **Applications standing with `undo_journal_version` NULL (pre-0017).** Not replayable. Record as an unrecoverable prior unification and leave it destructive. The 0017 header states no live install has ever applied one, so this case is currently empty.

**For an install that cannot rebuild, that is the whole path, and it requires dropping nothing.** The user has said they will drop and rebuild, which is faster; note before doing so that `playtime_snapshots` and `sessions` are the one thing that is NOT re-derivable, because the longitudinal series only accumulates over time.

## Product decisions for the user, not the implementer

1. After unification, does the grid show one tile per game with store chips, or one per ownership as today? Today it is per ownership and merging changed nothing about that (F-A). This is the actual fix for still seeing two Prey tiles.
2. If the grid becomes per game: is headline playtime the sum across stores, the maximum, or per store only? Summing two real observations is defensible; combining store A minutes with store B last-played into one tuple is the F10 hazard and must not happen.
3. Do expansion children count as titles in the library count?
4. Does expansion playtime roll up to the base game anywhere, or nowhere?
5. Is Different games revocable? Today it is permanent and never re-queued. If linking is bidirectional, rejection arguably should be too.
6. Should hard-id links (IGDB `external_games` per 5.3 step 1, and the gamesdb work in TASK-37) auto-apply without review, given `source = hard_id` is recorded?
7. Rebuild versus replay for the live database, given item 2 above about snapshots.

## What would change this recommendation

- **If the resolve-here / do-not-resolve-there inventory cannot be held by a test**, so that a future surface silently reads `works` directly. Then the automatic correctness of the destructive model is worth more than reversibility, and the right answer is to keep destruction and fix the five points inside it: a stored survivor choice, a grouped queue, a coverage table populated from the journal, and the re-confirmation affordance that was specified and never built.
- **If a measured 1,000-game library shows the resolved join or the grouped queue materially degrading load.** Expected negligible (a table with tens of rows, one LEFT JOIN) but it must be measured, not assumed.
- **If the user says the only thing wanted is one row per game with store entries invisible everywhere, forever.** A link still reaches that, but destruction reaches it with fewer moving parts.
- **If replay of standing merges proves unreliable on the live database.** Then the migration answer is rebuild-only, and installs that cannot rebuild get a documented two-mechanism period, which is bad enough to reconsider.

## Delivery order

Six stages, each shippable, recorded as subtasks. Stage 0 is independent of everything else and improves the current build with no schema change; if the recommendation is rejected, Stage 0 still stands.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The queue never shows a pair that cannot be acted on: no BLOCKED card, no already-one-game message, no stale entry, at any point in a session where pairs are being answered
- [ ] #2 The card states which title takes precedence and why in plain words, including when the only reason is that it was added first, and the user can choose a different one before answering
- [ ] #3 A base game and several expansions are presented as one group and applied as one act, with the user free to take none, some or all
- [ ] #4 A unified title survives as a row, and the game details modal lists the other titles it covers, each with its own store, playtime and last-played
- [ ] #5 Link and unlink are idempotent and repeatable: a pair can be linked, unlinked and linked again any number of times, and no screen ever declares the pair terminal
- [ ] #6 A surface that has not been taught to resolve links shows the pre-link view (two entries for one game) and never shows doubled, missing or corrupted data
- [ ] #7 same_game and expansion_of are distinguishable in the schema and behave differently in counts, playtime, buckets and recommendations, with expansion_of changing none of them by default
- [ ] #8 An install that cannot be rebuilt migrates without losing any decision or any row, and the migration path is documented in the migration file itself
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Decisions recorded 2026-08-31. Four product decisions, made after reading the design pass. These settle items 1, 2, 3/4 and 7 from the product-decision list.

1. Grid grain: one tile per game, store chips showing where it is owned. Linked entries collapse to a single tile. This is the actual answer to the complaint that merging never visibly unified anything; the grid has always rendered one tile per ownership, so merging a pair left two tiles on screen. TASK-70.6 is unblocked.

2. Headline playtime: sum across stores. The cost is stated plainly. No single source reports the sum, so the figure is Winnow's own composite and must be understood as such. The F10 hazard still binds: minutes from one store must never be displayed beside a last-played date from another store. The summed playtime needs its own coherent last-played value, and the per-store breakdown must remain visible on the details modal so the composite can be checked.

3. Expansions are titles; their playtime stays separate. An expansion counts as a game the user owns. Grouping an expansion under a base game is presentation only. Civilization IV's hours stay Civilization IV's. The recommender can still surface an unplayed expansion of a played-out parent, which the design pass identified as probably the best recommendation the app can make in that situation. This settles both items 3 and 4: expansion playtime does not roll up.

4. Migration path for the live database: drop and rebuild. The user accepts losing their merge decisions and roughly a week of self-accumulated playtime snapshots. What survives: the Year in Review backfill re-runs and restores 2022 onward, ownership and purchase history are re-derivable from storefront files, and the user has no process-monitor sessions or journal notes. What is lost: the standing merge decision and the playtime snapshot rows accumulated between install day and rebuild. The replay path is still required for any install that cannot rebuild, so TASK-70.7 retains it; this decision settles only what happens to the user's own database.
<!-- SECTION:NOTES:END -->
