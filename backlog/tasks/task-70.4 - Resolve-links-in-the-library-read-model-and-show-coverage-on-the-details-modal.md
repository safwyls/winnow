---
id: TASK-70.4
title: Resolve links in the library read model and show coverage on the details modal
status: To Do
assignee: []
created_date: '2026-09-02 00:14'
updated_date: '2026-09-02 00:15'
labels: []
dependencies:
  - TASK-70.3
parent_task_id: TASK-70
ordinal: 91000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 3 of TASK-70. Answers point 4 and makes a link visible in the library rather than only on the Same Game screen.

**Resolve at the chokepoint, and only there.** `LibraryQueryRepository.GetOwnershipBucketsAsync` gains a resolved work id, computed in the same pass that already runs demo consolidation and the non-game filter. That one query feeds the grid, the rail bucket counts, the All Games count, the filter panel options, list counts, the recommender candidate set, the feed and the account-visibility hidden count, so teaching it once teaches all of them. This stage does not change the grid grain: it still renders one tile per ownership, so no count moves and no bucket moves. What changes is that both store entries of one game now read the primary title and the primary cover, which is the visible half of the fix.

**Feed suppression resolves.** `feed_verdicts` and `feed_surfacings` are keyed by release. Dismissing the Steam entry of a game must suppress the Epic entry, or the feed will offer the same game twice under two store badges. Read them as any release under the resolved work.

**Details modal gains coverage.** An Also covers section on the game details modal lists every work linked into this one, each with its store badge, its own playtime, its own last-played and its per-release achievement rows, which is section 6.2 rendered literally for the first time. Each row carries a Separate this control that retracts just that link, so unlinking is available where the user notices the problem rather than only on a review screen.

**Do not resolve, and assert it.** `GetFacetTargetsAsync`, `WorkEnrichment`, `ProvisionalNameTarget`, `WorkRepository.GetAllAsync`, `update_acknowledgements`, `achievements`, `achievement_unlocks`, `IStoreTitleCounts` and `list_items` must keep reading unresolved, each for the reason recorded in TASK-70. Add an architecture test that enumerates the surfaces which read `works` or `ownerships` and asserts each is on the resolve list or the do-not-resolve list, so a new surface added later cannot silently join neither.

**Tests.** Two ownerships of one linked game render two tiles, both titled with the primary name and both drawing the primary cover. Bucket counts, All Games and the store title counts are byte-identical before and after linking. Dismissing one store entry in the feed suppresses the other. The details modal lists the covered titles with their own playtime and their own achievement rows, and never a blended percentage. Separate this retracts one link and leaves the rest of the act intact. Enrichment still targets the child work after it is linked. The architecture test fails when a new query reading `works` is added to neither list.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The bucket query carries a resolved work id, computed in the same pass as demo consolidation, and every surface fed by it inherits it
- [ ] #2 Both store entries of a linked game display the primary title and the primary cover
- [ ] #3 Library counts, bucket counts and store title counts are unchanged by linking at this stage
- [ ] #4 Dismissing a linked game in the feed suppresses every store entry of that game
- [ ] #5 The details modal lists the titles this game covers, each with its own store, playtime, last-played and per-release achievement rows, and never a blended percentage
- [ ] #6 A control on each covered title retracts just that link, leaving the rest of the group intact
- [ ] #7 An architecture test enumerates every surface reading works or ownerships and fails if one is on neither the resolve nor the do-not-resolve list
<!-- AC:END -->
