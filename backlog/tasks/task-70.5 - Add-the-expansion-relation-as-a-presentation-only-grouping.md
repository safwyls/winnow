---
id: TASK-70.5
title: Add the expansion relation as a presentation-only grouping
status: To Do
assignee: []
created_date: '2026-09-02 00:14'
updated_date: '2026-09-02 00:15'
labels: []
dependencies:
  - TASK-70.3
parent_task_id: TASK-70
ordinal: 92000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stage 4 of TASK-70. The second half of point 3: presenting a base game with its expansions as one group. It uses the same table as same-game linking and deliberately none of its semantics.

**The relation is not the same fact.** Steam Prey and Epic Prey are one game sold twice; Civilization IV and Beyond the Sword are two products where one depends on the other. `expansion_of` therefore changes no count, no playtime, no bucket and no recommendation. It groups for display only. Summing 30h of the base game with 120h of an expansion produces a number no source reported about either, and folding an unplayed expansion into a played parent would delete what is probably the best recommendation the app can make: you played this for two hundred hours and never opened the expansion. If a later product decision wants a rolled-up figure, it is an additional derived number shown beside the two real ones, never a replacement for them.

**A separate detector, because the soft matcher will not find these.** The matcher scores title distance, and Civilization IV against Civilization IV Beyond the Sword is not a near-title match. Propose expansions by normalised-title prefix containment (the child normalises to the parent tokens plus a suffix), gated on shared publisher and year proximity, and never auto-applied.

**The population is small and known.** Epic DLC and GOG DLC never reach the database: `EpicLibrarySource` skips entries with a non-empty `mainGameItem.id` and `GogLibrarySource` skips entries where `GameId` differs from `RootGameId`. Steam DLC generally has no separate library entry. So the candidates are Steam appids Valve types as games whose titles extend another owned title. Size the feature to that; do not build a general DLC subsystem.

**Presentation.** A group card in the same grammar as the same-game card: the base game, its proposed expansions, a checkbox each, applied as one act. On the game details modal, an Expansions section listing the children with their own playtime and last-played, stated as separate products. In the library grid, an optional setting groups expansions under the base game; off by default.

**Tests.** An expansion link changes no bucket count, no All Games count, no store title count and no playtime figure anywhere. The recommender still scores and can still recommend an unplayed expansion whose parent is played out. The detector proposes Civilization IV with its expansions and does not propose two unrelated titles sharing a first token. Applying a group of six is one act with one undo. The details modal lists expansions separately from covered titles, and the two sections are never merged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 An expansion link changes no count, no playtime, no bucket and no recommendation anywhere in the app
- [ ] #2 A base game and its expansions are proposed as one group and applied as one act, with the user free to take none, some or all
- [ ] #3 The expansion detector proposes title-extension candidates the soft matcher cannot find, and never applies one automatically
- [ ] #4 The details modal lists expansions in their own section, separate from covered titles, each with its own playtime and last-played
- [ ] #5 An unplayed expansion of a played-out base game is still reachable by the recommender
- [ ] #6 Grouping expansions in the library grid is a setting, and it is off by default
<!-- AC:END -->
