---
id: TASK-70.2
title: 'Add the identity link schema, repository and resolver, inert'
status: In Progress
assignee:
  - '@claude'
created_date: '2026-09-02 00:13'
updated_date: '2026-09-02 00:58'
labels: []
dependencies: []
parent_task_id: TASK-70
ordinal: 89000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 1 of TASK-70. Adds the link schema and its resolver and ships them inert: nothing reads a link yet, so the app behaves exactly as before and the stage is safe to release on its own.

**Migration 0018, additive only.** `identity_acts` and `identity_links` exactly as sketched in TASK-70, including the partial unique index on `child_work_id WHERE retracted_at IS NULL` (this is what makes at most one live parent per work a schema fact rather than a convention) and the partial index on `parent_work_id`. Nothing existing is rebuilt, nothing is dropped. Migration 0018 also carries the one live-database repair that is safe now: reset any `merge_candidates` row at status `undone` whose `merge_applications` row has `undone_at` set back to `pending`, because those rows were restored and the pair is genuinely open again.

**`IIdentityLinkRepository` in Core, `IdentityLinkRepository` in Data.** Operations: read the live link map; link a set of children to one parent under one act, re-parenting any existing children of a chosen parent inside the same act; retract an act whole; read link history. Every write is one transaction and one act. The depth-one invariant (a parent may not be a child, a child may not be a parent) is asserted in the repository because SQLite cannot express it as a CHECK.

**`IdentityResolution` in Core.** An immutable snapshot: work id to resolved parent work id, parent to children, plus link kind. Built from one query. Two resolvers are separate by construction because the kinds are not interchangeable: `ResolveSameGame` folds identity, `GroupExpansions` only groups for display and must never be used where a count, a playtime, a bucket or a recommendation is computed. Make that impossible to get wrong at the type level rather than by comment.

**Tests.** Linking two works produces one live link and one act. Linking a child that already has a parent replaces the live link, leaves the retracted row in place, and leaves history readable. Retracting an act restores every child it moved to the parent it had before that act. Link, retract, link again, retract again, four times, ends in the same state as one link followed by one retract. A cycle attempt and a depth-two attempt both fail. The partial unique index rejects two live parents for one child at the schema level. `DatabaseBackupTests.Rewind` covers 0018. The migration repair converts an undone candidate whose application is undone back to pending, and leaves a candidate whose application still stands alone.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Migration 0018 creates identity_acts and identity_links with a partial unique index that makes at most one live parent per work a schema-level fact
- [ ] #2 A work can be linked, the link retracted, and the same link made again any number of times, ending in a state identical to a single link
- [ ] #3 Retracting an act restores every child it touched to the parent it had immediately before that act
- [ ] #4 Cycles and links deeper than one level are rejected, with a test that asserts it at both the repository and the database level
- [ ] #5 The same-game resolver and the expansion grouper are separate types, so a caller cannot accidentally fold an expansion into an identity
- [ ] #6 No existing query, screen or count changes behaviour when this stage ships
- [ ] #7 Migration 0018 resets merge_candidates rows left at undone by an already-reversed application back to pending
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Migration 0018_identity_links.sql, additive only. identity_acts (id, kind IN link/unlink, performed_at, note) and identity_links (id, act_id FK, child_work_id FK, parent_work_id FK, kind IN same_game/expansion_of, source IN user/hard_id, evidence_json, applied_at, retracted_at, CHECK child <> parent). Partial UNIQUE index on child_work_id WHERE retracted_at IS NULL — at most one live parent per work becomes a schema fact. Partial index on parent_work_id. Nothing existing is rebuilt or dropped.
2. DEPARTURE from the TASK-70 sketch, stated in the migration header: identity_links also carries retracted_by_act_id. Acceptance #3 requires retracting an act to restore each child to the parent it had immediately before that act, and without this column 'which rows did act N displace' is a timestamp-matching heuristic. With it, it is a foreign key.
3. Migration 0018 also carries the one safe live-database repair: merge_candidates rows at status undone whose merge_applications row has undone_at set go back to pending. Guarded by NOT EXISTS a standing application.
4. Core (BCL-only): IdentityLinkKind, IdentityLinkSource, IdentityAct, IdentityLink, IdentityLinkRequest, IdentityLinkRefusal + IdentityLinkRefusedException. IIdentityLinkRepository in Core/Repositories: GetResolutionAsync, LinkAsync, RetractActAsync, GetHistoryAsync.
5. Core: IdentityResolution as an immutable snapshot built from one query, exposing TWO SEPARATE TYPES because the kinds are not interchangeable — SameGameResolution (Resolve(workId) folds identity; ChildrenOf) and ExpansionGrouping (BaseOf/ExpansionsOf, grouping only, NO Resolve method at all). A caller cannot fold an expansion into an identity because there is no method that would do it.
6. Data: IdentityLinkRepository. One transaction, one act per write. Depth-one asserted in the repository (SQLite cannot CHECK it): the chosen parent may not itself be a live child; any child being linked that is currently a live parent has its own children re-parented to the new parent inside the same act. Retract stamps the act's live rows and re-inserts the displaced prior links under the unlink act.
7. Register IIdentityLinkRepository in the composition root. Nothing reads it. INERT.
8. Tests: link produces one live link and one act; re-linking a child that has a parent replaces the live link, leaves the retracted row and readable history; retract restores each child's prior parent; link/retract four times equals one link then one retract; link/retract/link equals one link; cycle and depth-two rejected at the repository AND a database-integrity query proving no live child is also a live parent; the partial unique index rejects two live parents for one child at the schema level; SameGameResolution ignores expansion_of and ExpansionGrouping ignores same_game; MigrationTests expects the two new tables; DatabaseBackupTests.Rewind undoes 0018; the repair converts an undone candidate whose application is undone and leaves one whose application still stands.
9. Prove inertness: the full suite passes unchanged apart from the new tests.
10. Scoped tests, then the FULL suite across all three projects, via --artifacts-path into the scratchpad.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
IMPLEMENTED, not finalized. Shipped INERT: nothing reads a link.

SCHEMA AS BUILT (migration 0018_identity_links.sql, additive only; nothing rebuilt, nothing dropped).
identity_acts(id, kind CHECK IN link/unlink, performed_at, note).
identity_links(id, act_id -> identity_acts ON DELETE CASCADE, child_work_id -> works ON DELETE CASCADE, parent_work_id -> works ON DELETE CASCADE, kind CHECK IN same_game/expansion_of, source CHECK IN user/hard_id, evidence_json, applied_at, retracted_at, retracted_by_act_id -> identity_acts ON DELETE SET NULL, CHECK child <> parent, CHECK (retracted_at IS NULL) = (retracted_by_act_id IS NULL)).
ux_identity_links_live UNIQUE ON identity_links(child_work_id) WHERE retracted_at IS NULL; ix_identity_links_parent ON parent_work_id WHERE retracted_at IS NULL; ix_identity_links_act ON act_id.

TWO DEPARTURES FROM THE TASK-70 SKETCH, both stated in the migration header.
(1) retracted_by_act_id, plus the CHECK pairing it with retracted_at. AC #3 requires that retracting an act restore every child to the parent it had IMMEDIATELY BEFORE that act. Without the column, 'which live links did act N displace' has to be recovered by matching retracted_at against the act's timestamp, which is a heuristic that breaks the moment two acts share a second. With it, it is a foreign key. The paired CHECK is also what makes the ON DELETE SET NULL harmless: any delete of an act that displaced links would fire SET NULL and immediately violate the CHECK, so the statement is refused rather than corrupting the pairing. Nothing deletes acts; the table is append-only.
(2) ix_identity_links_act, because retracting an act and restoring what it displaced are both reads keyed by act.

CORRECTION TO ONE CLAIM IN THE DESIGN. TASK-70 says the partial unique index makes two-cycles impossible. It does not: A-child-of-B alongside B-child-of-A is two different children, which the index permits. Depth one is what refuses the second link. Stated in the migration header and covered by A_two_cycle_is_refused.

DEPTH ONE, BOTH HALVES. Half one: the chosen parent may not itself be a live child — refused with IdentityLinkRefusal.ParentIsAlreadyAChild, because re-parenting a whole group under its grandparent would be a decision nobody made. Half two: any work hanging off a work that is BECOMING a child is re-parented onto the new parent inside the SAME act, so one retraction puts every one of them back. This is the design's 'linking a group whose chosen primary already has children re-parents those children inside the same act', read the only way that preserves depth one without refusing a legitimate regrouping.

RETRACTION IS APPEND-ONLY. RetractActAsync stamps the act's live rows with retracted_at + retracted_by_act_id under a new unlink act, then RE-INSERTS the links that act had displaced as fresh live rows under the unlink act. A retracted row is never un-retracted, so the table is its own journal. Retracting an already-retracted act returns false and writes nothing.

TWO RESOLVERS, SEPARATE BY CONSTRUCTION (AC #5). IdentityResolution is built from ONE query of the live links and exposes SameGame and Expansions as different types. SameGameResolution.Resolve(long) -> long is TOTAL: every work resolves, to its parent or to itself. It is the function a count, a playtime, a bucket or a recommendation calls. ExpansionGrouping has NO Resolve at all, and BaseOf(long) -> long? returns null for a work that is not an expansion — it never falls back to the work itself, so it cannot be dropped into a position expecting a resolver. Folding an expansion into an identity is not a mistake you can make; there is no method that would do it. That is the user's decision of 2026-08-31 made structural: expansions are titles and their playtime does not roll up.

THE 0018 REPAIR. merge_candidates rows at 'undone' whose merge_applications row has undone_at set go back to 'pending'. NOT EXISTS guard holds back any candidate that also has an application still standing.

REMOVED ONE DEAD MEMBER. IdentityLinkRefusal.MixedKinds was declared and never thrown — a request carries one kind for the whole act, so a mixed act cannot be expressed. Removed rather than reserved: the enum is not serialised, so there is nothing for a reserved slot to protect.

NEW FILES. src/Winnow.Data/Migrations/0018_identity_links.sql; src/Winnow.Core/Identity/{IdentityConstants,IdentityLink,IdentityLinkRefusedException,IdentityResolution}.cs; src/Winnow.Core/Repositories/IIdentityLinkRepository.cs; src/Winnow.Data/Repositories/IdentityLinkRepository.cs; tests/Winnow.Tests/IdentityLinkTests.cs. CHANGED: src/Winnow.App/Program.cs (registration only, read by nothing), tests/Winnow.Tests/MigrationTests.cs (ExpectedTables gains identity_acts, identity_links and merge_undo_rows, which had been missing since 0017), tests/Winnow.Tests/DatabaseBackupTests.cs (Rewind drops the two tables, links first, and forgets the 0018 journal row). All prose authored by the docs-writer agent.

VERBATIM, full suite across all three projects, built and run via --artifacts-path into the scratchpad because the user is holding src/Winnow.App/bin:
  Passed!  - Failed: 0, Passed: 70,   Skipped: 0, Total: 70,   Duration: 1 s   - Winnow.Covers.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 102,  Skipped: 0, Total: 102,  Duration: 49 s  - Winnow.Recommend.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 2497, Skipped: 0, Total: 2497, Duration: 52 s  - Winnow.Tests.dll (net10.0)
Build: 0 Warning(s), 0 Error(s) under TreatWarningsAsErrors. Not committed.
<!-- SECTION:NOTES:END -->
