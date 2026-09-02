---
id: TASK-70.6
title: Decide and implement the library grid grain for unified games
status: To Do
assignee: []
created_date: '2026-09-02 00:14'
updated_date: '2026-09-02 00:20'
labels: []
dependencies:
  - TASK-70.4
parent_task_id: TASK-70
ordinal: 93000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 5 of TASK-70. **Gated on a product decision, and blocked until the user makes it.** Do not start this without an answer to items 1 and 2 in the product-decision list on TASK-70.

The library grid renders one tile per ownership. It always has, and merging never changed that: a game owned on Steam and Epic is two tiles before unification and two tiles after it, work-only or collapsed. If the user means their library to contain one row per game, this stage is the actual fix, and every earlier stage exists so that this one can be made safely rather than by deleting rows.

**Two questions the implementer cannot answer.**

1. Does the grid show one tile per game with store chips, or one per ownership as today? Note that design-system 11.2 fixes store title counts as per tile on purpose, so a game owned twice counts in both stores. That rule can survive a per-game grid, but only if the two counts are computed from different relations, and that must be said out loud rather than discovered.
2. What is the headline playtime of a game owned on two stores: the sum across stores, the maximum, or per store only? Summing two real observations is defensible. Combining store A minutes with store B last-played into one tuple is the F10 hazard from the stabilisation review and must not happen, whatever is chosen for the headline. Per-store rows stay visible in the details modal in every option.

**Consequences to design once the answers exist.** Buckets stop classifying an ownership and start classifying a game, which changes what Bounced off and Played out mean for a cross-store title. The rail counts change. Filters that cut on store need a defined meaning for a tile that is on two. The recommender sees one candidate where it saw two, which is the point but changes shelf competition. List membership, which is per release by deliberate design, needs a display rule when both entries of one game are in one list.

**Tests.** A game owned on two stores renders one tile with two store chips. Its bucket is derived from the chosen playtime rule and is asserted against the rule, not against a number. The rail counts, the All Games count and the store title counts each agree with their own stated definition, and the store counts still count a twice-owned game twice. No tile ever displays a playtime and a last-played date drawn from different ownerships. The recommender offers a cross-store game once.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The product decisions on grid grain and on headline playtime for a twice-owned game are recorded before implementation starts
- [ ] #2 A game owned on two stores appears in the grid according to the recorded decision, and the rail counts, All Games count and store title counts each agree with their own stated definition
- [ ] #3 No tile displays a playtime and a last-played date taken from different ownerships
- [ ] #4 Bucket membership for a cross-store game is asserted against the chosen playtime rule rather than against a hard-coded number
- [ ] #5 The recommender offers a cross-store game once
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Unblocked 2026-08-31: grid grain decided as one tile per game with store chips.
<!-- SECTION:NOTES:END -->
