---
id: TASK-87
title: Let the user hide a game from Winnow
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:13'
updated_date: '2026-09-04 19:49'
labels:
  - ui
  - data
dependencies: []
priority: medium
type: feature
ordinal: 114000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Some owned entries are things the user never wants to see again — tools, betas, gifts, games they are done with. Today the only way an entry leaves the library is a merge or a non-game classification. Give the user an explicit, reversible hide action so the library, the feed and the bucket counts stop surfacing that game, without deleting the ownership row (re-ingest must not resurrect it).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A game can be hidden from the tile context menu and from the details modal
- [x] #2 A hidden game disappears from the library grid, the list view, the feed and every bucket count
- [x] #3 Hiding is persisted and survives re-ingest — a later ingest of the same ownership does not unhide it
- [x] #4 Hidden games are listed somewhere the user can find them and unhide individually
- [x] #5 Tests cover the hidden-state round trip and the exclusion from bucket queries
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Data layer only (the UI half is a separate agent).
1. Migration 0023_hidden_games.sql: hidden_games(id, work_id FK works ON DELETE CASCADE, hidden_at, unhidden_at) plus a partial unique index on work_id WHERE unhidden_at IS NULL. Append-and-stamp, the shape 0012/0018 already use: unhiding stamps unhidden_at, so 'is this game hidden' stays a query and the history stays the table. Append the checksum line.
2. Core: HiddenGame record (WorkId, Title, HiddenAt, StoreCount) and IHiddenGameRepository (HideAsync/UnhideAsync/IsHiddenAsync/GetHiddenGamesAsync) in Winnow.Core.
3. Data: HiddenGameRepository on Dapper, TimeProvider-injected like IdentityLinkRepository.
4. LibraryQueryRepository: a hidden_game CTE and one NOT EXISTS in the bucket query WHERE clause, testing the row work, its same-game parent AND its variant parent. Hiding a game must not pop its demo back into the grid. Every bucket count, the grid, the list view and the feed read that one query, so one clause covers all of them.
5. Tests in tests/Winnow.Tests on temp-file SQLite: hide/unhide round trip, exclusion from the bucket query and its counts, survival across a resolve pass over the same candidate (re-ingest does not unhide), hidden parent hides its variant, enumeration returns titles for the unhide screen.

UI half (data layer already landed).
6. Register IHiddenGameRepository in src/Winnow.App/Program.cs.
7. LibraryViewModel gains an optional IHiddenGameRepository and a HideGameCommand taking a GameTileViewModel; it hides tile.Game.ResolvedWorkId and reloads, so the grid, list view, feed and every rail count drop it through the one bucket query they all read.
8. Hide reaches the tile through the library's own context menu (MainWindow.axaml, OnLibraryContextMenuOpening) and the details modal's action row, bound through $parent[Window] to the same command.
9. Hidden games are found and unhidden one at a time on a new third settings section, LIBRARY, beside PLATFORMS and APPEARANCE — a HIDDEN GAMES card listing title, store-entry count and hidden date with an Unhide button per row, driven by GetHiddenGamesAsync.
10. All copy authored by docs-writer, in a LibrarySettingsCopy file beside MergeCopy and ExpansionCopy. design-system.md gains the new section; its old sentences go to docs/decisions.md.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Data-layer half done; the UI half (context menu, details-modal action, the hidden-games screen) is a separate agent's and this task is NOT finished.

Migration 0023_hidden_games.sql: hidden_games(id, work_id FK works ON DELETE CASCADE, hidden_at, unhidden_at) plus the partial unique index ux_hidden_games_live ON hidden_games(work_id) WHERE unhidden_at IS NULL. Append-and-stamp, the shape 0012 and 0018 use: unhiding stamps unhidden_at, so 'is this hidden' stays a query, the row is the history, and re-hiding is a fresh row with no terminal state. checksums.txt updated.

Grain is the WORK, because the grid draws one tile per resolved work.

Contracts for the UI agent (all in Winnow.Core): Winnow.Core.Domain.HiddenGame (WorkId, Title, HiddenAt, StoreEntryCount) and Winnow.Core.Repositories.IHiddenGameRepository — HideAsync(long workId), UnhideAsync(long workId), IsHiddenAsync(long workId), GetHiddenWorkIdsAsync(), GetHiddenGamesAsync(). Implementation Winnow.Data.Repositories.HiddenGameRepository(ISqliteConnectionFactory, TimeProvider?). Hide/Unhide return false when there was nothing to do, so the UI can stay quiet on a double click. Pass OwnershipBucket.ResolvedWorkId when hiding a tile.

The exclusion is one NOT EXISTS in LibraryQueryRepository's bucket query, testing three work ids per row: the row's own work, its live same_game parent and its variant_of parent. Hiding takes the whole link group, and does not pop the game's demo into the grid when its parent disappears. Every surface that reads GetOwnershipBucketsAsync inherits it with no change.

Survives re-ingest structurally, not by a guard: no ingest path writes hidden_games, the resolver joins only on an exact (provider, provider_id) external id, and no runtime path deletes a works row. Asserted by a test that hides a game and then re-resolves the same candidate.

AC #3 and #5 checked (tests/Winnow.Tests/HiddenGameTests.cs, 9 tests). #1 and #4 are UI. #2 is proven for the bucket query and its counts but left unchecked because the grid, list view and feed are the UI agent's to confirm.

REMAINING: register IHiddenGameRepository in src/Winnow.App/Program.cs (the data layer must not touch App), the tile context-menu and details-modal actions, and a hidden-games screen driven by GetHiddenGamesAsync.

UI half landed. Registered IHiddenGameRepository in src/Winnow.App/Program.cs.

WHERE HIDING IS DONE. LibraryViewModel takes the repository as an optional dependency and carries two commands. HideGameCommand takes one tile and is what the details modal's action band binds to (src/Winnow.App/Views/GameDetailsView.axaml, a link beside Store page — that band is about getting into the game, so hiding is the quiet answer beside it, never a primary). HideSelectionCommand acts on the whole picked set and is what the library's context menu binds to (src/Winnow.App/Views/MainWindow.axaml, beside Add to list), naming the number once there is more than one on exactly the rule AddToListLabel follows — so the grid's one-tile selection and the list view's many are one control.

THE GRAIN IS THE GAME. Both write tile.Game.ResolvedWorkId, not the row's own work, so hiding takes the whole link group and does not pop the game's demo back into the grid. After a hide the modal closes and the selection is dropped — the tiles it named no longer exist — and the library reloads. The reload is the whole mechanism: the exclusion is one NOT EXISTS in the bucket query, and the grid, the list view, the feed and every rail count read that one query, so nothing needed a second filter.

WHERE HIDDEN GAMES ARE FOUND. A new third settings section, SETTINGS › LIBRARY, beside PLATFORMS and APPEARANCE (src/Winnow.App/Views/LibrarySettingsView.axaml, LibrarySettingsViewModel). Not under Appearance, which is material and quantity, and not under Platforms, which is about connecting to a store; this screen answers what is in the library, which is also why TASK-88's toggle and TASK-99's form share it. The HIDDEN GAMES card lists title, store-entry count and the date hidden, with Unhide per row. Unhiding stamps the row and reloads the library.

DOCUMENTATION. design-system.md gained §16 'The settings surface' with §16.2 for this, and §15.1's floating-layout table and §14.2's PaneGround row now name the Library pane. Superseded sentences are in docs/decisions.md (2026-09-04). All copy and comments authored by docs-writer; the strings live in src/Winnow.App/ViewModels/LibrarySettingsCopy.cs.

TESTS: tests/Winnow.Tests/LibrarySettingsViewModelTests.cs. Hiding_a_game_removes_its_tile_and_its_bucket_count asserts the tile leaves VisibleTiles, the Never played rail count drops 2 to 1 and TotalCount drops to 1. The_context_menu_hides_every_picked_tile asserts the selection command hides both marked tiles and leaves the third. A_hidden_game_is_listed_and_can_be_put_back_one_at_a_time asserts the settings screen lists it with its store-entry count and that Unhide returns it to the library.

VERIFIED: dotnet build -p:BaseOutputPath=C:\Temp\winnow-b2\ -m:1 — 0 warnings, 0 errors. tests/Winnow.Tests 2973 passed, 0 failed. Winnow.Recommend.Tests 152 passed. Winnow.Covers.Tests 70 passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
A game can now be hidden, and a hidden game can be found and put back.

Hide sits in two places, both bound to commands on LibraryViewModel: the library's context menu, where it acts on the whole picked set and names the number once there is more than one (the rule Add to list already follows), and the details modal's action band, as a link beside Store page rather than a primary. Both write the RESOLVED work id, so hiding takes the game's whole link group and does not pop its demo back into the grid, and neither deletes anything — the ownership row stays, which is why a later ingest cannot bring the game back on screen.

Hidden games are listed on a new third settings section, SETTINGS › LIBRARY, beside Platforms and Appearance, with the title, the store-entry count and the date hidden on each row and Unhide beside it. That screen is where TASK-88's toggle and TASK-99's form also live, because all three answer one question — what is in the library.

Verified with tests/Winnow.Tests/LibrarySettingsViewModelTests.cs: hiding removes the tile from VisibleTiles, drops the Never played rail count from 2 to 1 and TotalCount to 1; the context-menu command hides both marked tiles and leaves the third; the settings screen lists the hidden game and Unhide returns it. AC2's list view is the same VisibleTiles collection the tests assert (MainWindow.axaml line 1527), and the feed reloads from the same tile source on TilesChanged, so all four surfaces inherit the one bucket-query exclusion the data-layer tests already cover. dotnet build clean; Winnow.Tests 2973 passed / 0 failed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 70 passed.
<!-- SECTION:FINAL_SUMMARY:END -->
