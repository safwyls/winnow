---
id: TASK-92
title: 'Add user lists, and an add-to-list action on the details view'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:13'
updated_date: '2026-09-04 20:38'
labels:
  - ui
  - data
dependencies: []
priority: medium
type: feature
ordinal: 119000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
There is no way to group games by the user own intent — a shortlist to play next, a set to finish, a set to revisit. Steam collections are read during ingest but nothing user-authored exists. Add named lists the user creates and fills, and put the entry point where the user is already looking at one game: the details modal.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The user can create, rename and delete a named list
- [x] #2 The details modal offers add-to-list, showing which lists already contain the game
- [x] #3 Lists are browsable as a library filter or rail entry
- [x] #4 List membership is persisted and survives re-ingest and merges
- [x] #5 Tests cover membership persistence across a link or merge of two entries
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
The lists and list_items tables, GameList/ListItem and GameListRepository already exist (migration 0001). No migration is needed. What is missing is the resolution half and the read the details modal needs.
1. Membership stays stored per RELEASE — the user added one store entry and GameListRepository's own note defends that. Resolution happens on READ, so membership follows the resolved game rather than a stale row.
2. Core: GameListMembership record (ListId, Name, ReleaseId); IGameListRepository gains GetMembershipForGameAsync(workId) and GetMemberWorkIdsAsync(listId).
3. Data: both queries resolve through identity_links (kind same_game, retracted_at IS NULL) — a list contains a game when any release of any work in that game's live link group is a member.
4. Tests: membership survives a resolve pass over the same candidate (re-ingest), and survives linking two entries — asserted from BOTH the parent and the child work id. Also confirm no runtime path deletes a releases or works row, which is why membership cannot be cascaded away.

UI half (data half landed above).
5. Most of the lists UI already exists and was verified before anything was written: ListsViewModel/GameListViewModel/ActionPromptViewModel, the rail's LISTS and LIVE LISTS sections in MainWindow.axaml, and BeginAddToList/BeginRenameList/BeginDeleteList on LibraryViewModel driving the action-bar strip (§12.3). AC1 and AC3 are met by that code; this pass verifies them rather than rebuilding them.
6. The gap is AC2. GameListsViewModel (ViewModels/Lists): one row per hand-built list with a tick showing whether the list already contains THIS game, fed by IGameListRepository.GetMembershipForGameAsync(workId) so a list answers for the resolved game rather than for one store row.
7. Ticking appends the tile's primary release. Unticking removes every release the membership rows name, which is the only way to leave a list the user joined from a different store entry.
8. Inline in the modal's own tree, never a flyout: Avalonia's FocusAdorner does not render inside a popup, which is §10.7's and §12.3's shared reason.
9. The work id comes from the coverage entry the modal already reads, through IdentityResolution.Resolve, exactly as BuildExpansions does.
10. Copy by docs-writer; design-system.md §12.3 records the third surface.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Data-layer half done; the UI half (list management, the details-modal add-to-list action, the rail entry) is a separate agent's and this task is NOT finished.

NO MIGRATION. lists and list_items have existed since 0001, and GameList, ListItem, IGameListRepository and GameListRepository were already built. What was missing was resolution.

Membership stays STORED per release — adding a game to a list is an explicit act on the entry the user picked, and de-duplicating by resolved work would remove a row they put there by hand. It is now RESOLVED per read, so a list answers for the game rather than for the one store row. GameListRepository's class summary said 'identity links are deliberately NOT resolved here'; that was half true and has been corrected in place rather than left standing beside the correction.

New contracts for the UI agent: Winnow.Core.Domain.GameListMembership (ListId, Name, ReleaseId) and two members on IGameListRepository — GetMembershipForGameAsync(long workId) returns every list containing any release of any work in that game's live same_game group (this is what the details modal ticks), and GetMemberWorkIdsAsync(long listId) returns the resolved work ids of a list's members, folded so a linked pair is one game, ordered by the earliest position the game holds (this is what a rail entry or library filter draws). kind is same_game only, so an expansion's membership is its own.

Membership survives a link because the link model never deletes or repoints a row: after 0019 retired the destructive merge, a confirmed link cannot delete a releases or works row or repoint list_items.release_id — IdentityLinkRepository contains no DELETE at all and retraction stamps retracted_at. Verified rather than assumed.

AC #4 and #5 checked (tests/Winnow.Tests/ListMembershipResolutionTests.cs, 7 tests: membership answers from both sides of a link, is not carried by an expansion link, is unchanged by retracting the link, and survives a second resolve pass over the same candidate). #1-#3 are UI; the repository already supports create/rename/delete.

REMAINING: the lists UI, the details-modal add-to-list action fed by GetMembershipForGameAsync, and the rail/filter entry fed by GetMemberWorkIdsAsync. IGameListRepository is already registered in Program.cs, so no DI change is needed.

UI HALF LANDED (avalonia-ui agent). The data half above is unchanged and was consumed exactly as specified.

WHAT ALREADY EXISTED, VERIFIED BEFORE ANYTHING WAS WRITTEN. AC1 and AC3 were already met by code from the lists wave and were re-verified rather than rebuilt: ListsViewModel/GameListViewModel/ActionPromptViewModel, the rail's LISTS and LIVE LISTS sections in MainWindow.axaml, the manual list as an AND term over the library, and BeginAddToList / BeginRenameList / BeginDeleteList on LibraryViewModel driving the action-bar strip (§12.3, no flyouts). Existing tests in ListsViewModelTests.cs prove each: a list is built from the selection, renaming survives a reload and keeps the rail alphabetical, deleting asks first and keeps the titles, a list's count drops when one of its titles leaves the library.

WHAT WAS MISSING, AND IS NOW BUILT: AC2. src/Winnow.App/ViewModels/Lists/GameListsViewModel.cs (GameListsViewModel and GameListEntryViewModel) plus GameListsCopy.cs, a LISTS section in src/Winnow.App/Views/GameDetailsView.axaml between EXPANSIONS and ABOUT, and BuildListsAsync / ToggleListMembershipAsync / GameWorkIdFor on LibraryViewModel.

THE TICKS COME FROM THE RESOLVED READ. IGameListRepository.GetMembershipForGameAsync(workId), so the modal answers for the GAME rather than for the one store row the user happened to open: a list is ticked when any release of any work in the game's live same_game group is a member. The work id comes from the coverage entry the modal already reads, through SameGameResolution.Resolve, exactly as BuildExpansions derives its own.

STORED PER RELEASE, RESOLVED PER READ, WHICH DECIDES WHAT UNTICKING DOES. Ticking appends the tile's primary release. Unticking removes every release the membership rows name — not just the primary — because that is the only way to leave a list you joined from a different store's copy of the game. The membership record's ReleaseId is what makes that possible, which is why the row carries it.

INLINE, NEVER A FLYOUT. Avalonia's global FocusAdorner does not render inside a popup, which is §10.7's reason for the modal having no flyout and §12.3's reason for the action-bar strip. The control is a real templated CheckBox, so it toggles on Space and reports its state to automation; §12.2's rule holds, a checked box is Volt whoever ticked it.

LIVE LISTS ARE NOT OFFERED. A live list holds a rule and finds its own members, so there is nothing to tick. With no hand-built lists the section still draws and states the direction (§7: an empty state is a direction, not a mood).

WHAT WAS NOT CHANGED, AND WHY. The rail count and the manual-list AND term still fold in memory over GameTileViewModel.ReleaseIds rather than calling GetMemberWorkIdsAsync. That is already link-resolved — a tile's Entries are every visible store entry of the folded game — and ApplyFilter runs on every keystroke in the search box, so a per-list database round trip there would be a regression for an answer the loaded read model already holds. GetMemberWorkIdsAsync remains the contract for any caller that does not have the tiles.

TESTS (tests/Winnow.Tests/ListsViewModelTests.cs, three new): the modal ticks the lists that already hold this game and leaves the others unticked; a live list is not offered and the empty state is stated; ticking a row puts the game in the list and unticking takes it out, asserted through the repository and the rail count both. The cross-store case is already proven at the data layer by ListMembershipResolutionTests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Lists are stored per release and resolved per read, so a list answers for the resolved game rather than for one store row. Data half: GameListMembership plus GetMembershipForGameAsync(workId) and GetMemberWorkIdsAsync(listId) on IGameListRepository, both resolving through identity_links (kind same_game, retracted_at IS NULL); no migration was needed since lists/list_items have existed since 0001. UI half: create/rename/delete via BeginAddToList/BeginRenameList/BeginDeleteList driving the action-bar strip, the rail LISTS and LIVE LISTS sections, and a new GameListsViewModel giving the details modal one tick per hand-built list, inline in the modal tree rather than a flyout because Avalonia FocusAdorner does not render inside a popup. Live lists are not offered — they find their own members, so there is nothing to tick. Membership survives links because the link model deletes nothing: IdentityLinkRepository contains no DELETE and retraction stamps retracted_at. Verified by 102 passing tests including ListMembershipResolutionTests (membership answers from both sides of a link, is not carried by an expansion link, is unchanged by retracting the link, and survives a second resolve pass) and ListsViewModelTests, plus a clean solution build.
<!-- SECTION:FINAL_SUMMARY:END -->
