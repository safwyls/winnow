---
id: TASK-5
title: Build merge execution and enforce canonicality in the repository
status: In Progress
assignee:
  - '@claude'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-01 02:18'
labels:
  - resolve
  - data
dependencies: []
priority: high
ordinal: 5000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The soft-match queue proposes merges and stores confirmations, but nothing applies them. 23 cross-store pairs are pending on the user's library. The `ON DELETE CASCADE` hazard on collapsing two releases is documented and unresolved. Canonicality must be enforced in the repository layer, not by callers. Findings F09 (P1) and F20. Sources: stabilization-2026-08-28.md Group 2; ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A confirmed merge pair collapses to one canonical release with the other's external ids preserved
- [ ] #2 The repository enforces canonicality; callers cannot bypass it
- [ ] #3 The CASCADE hazard is resolved, with a test proving a merge does not orphan dependent rows
- [ ] #4 Re-running a merge on an already-merged pair is a no-op
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
## Research findings that constrain the design

**The four-layer model forbids the obvious implementation.** game-library-design.md 6.2 says
the unified cross-store view is "a query, not a stored merge", and 6 keeps Release distinct
from Work (Skyrim SE is not Skyrim). But the schema also has `ux_ownerships_release_store`
UNIQUE(release_id, store) from migration 0003, which only makes sense if one Release is
expected to carry ownerships from several stores. Both are true: a merge unifies at the Work
layer always, and collapses Releases only when the two sides are genuinely the same edition.

**Every release/work/ownership dependent, with its ON DELETE rule.** All CASCADE:

- works(id): releases.work_id, work_facets.work_id
- releases(id): external_ids, ownerships, achievements, update_events, list_items,
  merge_candidates.left_release_id AND .right_release_id, release_facets, feed_verdicts,
  feed_surfacings, update_acknowledgements
- ownerships(id): play_records, playtime_snapshots, sessions, ownership_accounts
- sessions(id): session_notes
- achievements(release_id, provider_key): achievement_unlocks

`merge_candidates` cascading off `releases` is the hazard nobody has named yet: deleting an
absorbed release destroys the user's own `confirmed` decision row.

**Uniqueness constraints that repointing can violate.** external_ids PK(provider,provider_id);
ownerships ux(release_id,store); play_records ux(ownership_id,source,observed_at,
playtime_minutes,COALESCE(last_played_at,'')); playtime_snapshots ux(ownership_id,observed_at,
playtime_minutes); update_events ux(release_id,kind,occurred_at); ownership_accounts
PK(ownership_id,account_ref); achievements PK(release_id,provider_key); list_items
PK(list_id,release_id); release_facets PK(release_id,facet_id); feed_surfacings
PK(release_id,surfaced_on).

**`platform` and `edition_note` are NULL on every real row.** ExternalIdResolver.
CreateWorkAndReleaseAsync sets only WorkId and Name. So structural edition columns cannot
carry the edition signal today; the title normaliser's `BundleEditions`, already recorded on
each candidate's `signals_json`, can.

## The surviving-identity rule (deterministic, documented)

**Work survivor**, first rule that discriminates:
1. the one with a non-null `igdb_id` - `works.igdb_id` is UNIQUE, so it is the one fact that
   cannot be copied onto the other row; preferring its holder is the only way to keep it
2. the one whose `name_is_provisional = 0`
3. the one with more releases (more of the database already agrees with it)
4. lowest `id` - oldest row, stable across re-runs

**Release survivor**, first rule that discriminates:
1. the one that has `achievements` rows. With the achievement-safety rule below at most one
   side can, so achievements never move between releases at all - the strongest possible
   reading of 6.2
2. the one with a non-null `igdb_version_id`
3. the one with more `external_ids`
4. lowest `id`

Losing-row facts are **filled, never overwritten** onto the survivor (the F03 semantics this
codebase already settled): summary, cover_url, publisher, sort_name, first_release_year,
steam_app_type, epic_categories, and a non-provisional name onto a provisional one. The
losing Work's `igdb_id`, if it holds a different one, dies with the row: the user's own
"same game" answer contradicts IGDB's claim that they are two games, and 5.3 makes the user
the authority. It is a refetchable enrichment pointer, not user data.

## Two merge modes, decided before any write

`WorkOnly` - the two releases come to share one Work; both releases survive.
`ReleaseCollapse` - WorkOnly, plus the two releases collapse to one.

Collapse is permitted only when ALL hold:
- structural editions agree: platform, edition_note, igdb_version_id each both-NULL or equal
- **achievement-safe**: not both sides carry achievement rows. `achievements` has no provider
  column, so two stores' achievement sets under one release_id would make 6.2's
  "never blend across platforms" unenforceable at query time. Refusing the collapse is the
  only way the schema can keep that promise
- no lossy unique-key collision. Exact-duplicate residue is fine (play_records and
  playtime_snapshots unique keys cover every non-id column, so a collision there is the same
  byte-identical observation twice - migration 0013 already established that dropping it is
  deduplication, not loss). An `update_events` collision on (kind, occurred_at) whose
  build_id/title/url/raw_json differ IS a distinct fact, and downgrades the merge to WorkOnly

The Resolve-layer service may additionally downgrade to WorkOnly on title evidence - differing
`BundleEditions` in the stored signals payload. It can only downgrade. **The repository is the
floor: it re-derives its own safety verdict and ANDs it with the caller's intent, so no caller
can talk it into an unsafe collapse.** That is AC #2.

## How the CASCADE hazard is resolved

Not by re-plumbing the FKs. Rebuilding a dozen tables to swap CASCADE for RESTRICT is a large
append-only cost for a guarantee we can get exactly, in-transaction, for free:

1. repoint every dependent explicitly, table by table, in a fixed order
2. before each DELETE, run a **tripwire**: count the rows still pointing at the row about to
   die, across every dependent of that table. Non-zero throws, the transaction rolls back,
   and the database is untouched
3. only then delete - the DELETE removes an empty shell and the cascades have nothing to take

This gives RESTRICT's safety with no schema churn, and it is directly testable (AC #3). The
one FK that genuinely must change behaviour is `merge_candidates` - see the migration.

## Migration 0016 (append-only; two reasons)

1. **F20 canonicality.** `merge_candidates` today permits (A,A) and both (A,B) and (B,A).
   SQLite cannot ALTER TABLE ADD CONSTRAINT, so this is a 12-step rebuild: canonicalise
   existing rows to left<right, collapse mirrors with terminal decisions beating pending,
   drop self-pairs, recreate with `CHECK (left_release_id < right_release_id)` and
   `UNIQUE (left_release_id, right_release_id)`.
2. **`merge_applications`.** The audit and idempotency record: candidate_id, surviving and
   absorbed release/work ids, mode, applied_at, summary_json. Deliberately **no foreign keys**
   - the absorbed release is gone by design, exactly the reasoning migration 0012 used for
   `update_acknowledgements.acknowledged_through`. This is also what makes the confirmed
   `merge_candidates` row safe to lose to the cascade when its release is absorbed: the
   decision survives here, and no future sweep can re-propose a pair whose release no longer
   exists.

## Ownership and history folding

Same-store ownership collision (both sides own on Steam) under ReleaseCollapse: pick the
survivor ownership (lowest id), repoint the loser's play_records, playtime_snapshots, sessions
and ownership_accounts onto it, then delete the empty loser. `UPDATE OR IGNORE` then DELETE
for the two observation tables - the residue is byte-identical by construction. sessions have
no unique key, so they all move and session_notes ride along on session_id. ownership_accounts
colliding on account_ref: keep the row with the later `last_seen_at` intact (one coherent
observed tuple - F10's lesson) and widen `first_seen_at` to the earlier of the two; first/last
seen is genuinely a range, playtime/last_played is not.

## Work

1. `src/Winnow.Data/Migrations/0016_merge_canonicality.sql` + MigrationTests coverage
2. Core: `Winnow.Core/Merging/` - MergePlan, MergeMode, MergeBlocker, MergeOutcome,
   MergeApplication; `IMergeExecutionRepository { PlanAsync, ApplyAsync }`
3. Data: `MergeExecutionRepository` - one transaction, explicit repointing, tripwires,
   idempotent (already-applied and already-merged both return a no-op outcome)
4. Data: `MergeCandidateRepository` canonicalises on insert and rejects self-pairs
5. Resolve: `MergeExecutor` - refuses anything not `confirmed`, downgrades on bundle-edition
   evidence, logs, reports. Depends on Core abstractions only
6. Tests in `tests/Winnow.Tests/MergeExecutionTests.cs`: rollback leaves the DB unchanged;
   collision-heavy (same-store ownerships both sides, overlapping external ids, play history
   both sides); distinct editions preserved; achievements never blended; idempotency; the
   cascade tripwire; canonicality migration against mirrored and self-paired rows
7. Scoped tests, then the full suite. Prose via docs-writer. No commit, no finalize.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implementation landed, scoped tests green (40/40 across MergeExecutionTests + MigrationTests).

Files:
- src/Winnow.Data/Migrations/0016_merge_canonicality.sql (new; append-only)
- src/Winnow.Core/Merging/{MergeMode,MergeBlocker,MergeRequest,MergePlan,MergeOutcome}.cs (new)
- src/Winnow.Core/Repositories/IMergeExecutionRepository.cs (new)
- src/Winnow.Data/Repositories/MergeExecutionRepository.cs (new)
- src/Winnow.Resolve/MergeExecutor.cs (new)
- src/Winnow.Data/Repositories/MergeCandidateRepository.cs (canonicalising insert, GetAsync)
- src/Winnow.Core/Repositories/IMergeCandidateRepository.cs (GetAsync)
- src/Winnow.Resolve/SoftMatchServiceCollectionExtensions.cs (AddMergeExecution)
- tests/Winnow.Tests/MergeExecutionTests.cs (new, 17 tests)
- tests/Winnow.Tests/MigrationTests.cs (0016 test, merge_applications in ExpectedTables)
- tests/Winnow.Tests/SoftMatchSweepBudgetTests.cs (fake repo gains GetAsync)

Two merge modes, decided before any write. WorkOnly always; ReleaseCollapse only when
structural editions agree (platform / edition_note / igdb_version_id), at most one side
carries achievements, and no update_events collision disagrees on build_id/title/url/raw_json.

Surviving identity, first rule that discriminates. Work: holds igdb_id (UNIQUE, so the one
fact that cannot be copied across); then non-provisional name; then more releases; then
lowest id. Release: holds achievements (so achievements never move at all); then
igdb_version_id; then more external ids; then lowest id.

Cascade hazard resolved without changing any ON DELETE rule. Every dependent is repointed by
an explicit statement; before each DELETE a tripwire counts what still references the row and
throws if anything does, rolling the whole transaction back. Proven by
A_stranded_dependent_aborts_the_merge_instead_of_being_cascade_deleted and
A_failure_mid_merge_leaves_the_database_exactly_as_it_was, both of which compare a full
row-level snapshot of all 21 affected tables before and after.

Migration 0016 was needed for two reasons. F20: merge_candidates permitted self-pairs and
both orientations, and SQLite cannot ALTER TABLE ADD CONSTRAINT, so it is a table rebuild
adding CHECK (left_release_id < right_release_id) plus the canonical UNIQUE key, canonicalising
and de-mirroring existing rows with terminal decisions beating pending ones and a rejection
beating a confirmation. Second, merge_applications: merge_candidates cascades off releases,
so an absorbed release would take the user's own confirmed decision with it. The new table has
no foreign keys, deliberately, for the reason 0012 gave update_acknowledgements.

Nothing was run against the live database. Prose delegated to docs-writer.

Two coordinator-requested items handled alongside the merge work.

1. DatabaseBackupTests.Rewind did not undo 0016, so the pre-migration-backup tests failed
   against the new migration. 0016 is the first migration that REBUILDS a table rather than
   adding one, so the rewind restores the pre-0016 merge_candidates shape (the 0001 table plus
   ix_merge_candidates_status) and drops merge_applications, rather than only deleting journal
   rows. DatabaseBackupTests is green again.

2. LibraryHistoryStatsRepository added in Winnow.Data against the already-written
   ILibraryHistoryStatsRepository, plus registration in Program.cs beside the other
   repositories. One statement: COUNT/MIN/MAX over sessions, and a COUNT over ownerships with
   an EXISTS for a playtime_snapshots pair where a later observation reports more minutes than
   an earlier one. IsEstimate stays false on every path - that is the whole point, since the
   recommender otherwise falls back to a scaled sample. No migration needed. Covered by
   tests/Winnow.Tests/LibraryHistoryStatsTests.cs (empty library, session bounds, and a rising
   series counted where a flat one is not).

Also registered IMergeExecutionRepository and AddMergeExecution() in Program.cs so the merge
surface is resolvable; no view model or view was touched.
<!-- SECTION:NOTES:END -->
