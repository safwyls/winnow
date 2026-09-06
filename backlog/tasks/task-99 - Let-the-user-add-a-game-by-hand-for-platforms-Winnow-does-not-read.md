---
id: TASK-99
title: Let the user add a game by hand for platforms Winnow does not read
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:14'
updated_date: '2026-09-04 19:50'
labels:
  - ui
  - data
dependencies: []
priority: medium
type: feature
ordinal: 126000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow only knows what Steam, Epic and GOG write to disk. Anything bought elsewhere — itch.io, Battle.net, a physical or emulated title, a standalone installer — is invisible, so the library is not the users whole library and the bucket counts are wrong. Let the user create an entry by hand: a title, an optional executable to watch, and enough metadata to resolve a cover.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The user can create a game entry that no ingest reader produced
- [x] #2 A hand-added entry participates in the library, the feed and the bucket counts like any other
- [x] #3 An ingest pass never deletes or overwrites a hand-added entry
- [x] #4 A hand-added entry can name an executable so session monitoring can record play time for it
- [x] #5 A hand-added entry can be edited and deleted
- [x] #6 Tests cover survival across a full ingest pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Data layer only; the add/edit UI and the Monitor's exact-executable attribution are other agents.
1. Studied reconciliation first. ExternalIdResolver joins candidates on (provider, provider_id) EXACTLY or mints a new work; there is no title-based attachment, no prune step, and no DELETE FROM ownerships/releases/works anywhere in runtime code. So the guarantee rests on two facts made explicit rather than on new machinery: a hand-added ownership is keyed on store 'manual', which no reader emits, so the (release_id, store) upsert cannot reach it; and its work is created with name_is_provisional = 0, so PromoteProvisionalNameAsync and the fill-only enrichment patch both leave the user's title alone.
2. Migration 0025_manual_entries.sql: manual_entries(ownership_id PK FK ownerships ON DELETE CASCADE, executable_path, platform_label, added_at, updated_at). The presence of the row IS the origin marker — one mechanism, not two, and a table no ingest path writes.
3. Core: OwnershipStores.Manual; ManualGameDraft (Title, FirstReleaseYear, PlatformLabel, ExecutablePath, InstallPath, IgdbId, SteamAppId) with EffectiveInstallPath deriving the watched directory from the executable; ManualEntry; IManualEntryRepository (Create/Get/GetAll/Update/Delete).
4. Session monitoring works with no Monitor change: GameExecutableIndexBuilder reads ownerships.installed + install_path, so a draft naming an executable stores installed = 1 and the executable's directory. The stored executable_path is what a later exact-match change will read.
5. Delete is narrow and ordered: the ownership, then the release only when no ownership is left on it, then the work only when no release is left. A store entry that later attached to the same release survives.
6. Tests: a full resolve pass over a candidate whose title matches leaves the manual work, release, ownership and manual_entries row byte-identical; the entry appears in the bucket query and its counts; edit and delete round trips.

UI half (data layer already landed).
7. Register IManualEntryRepository in src/Winnow.App/Program.cs.
8. An ADDED BY HAND card on the new third settings section, LIBRARY: every hand-added entry with its title, year, platform label and executable, each row carrying Edit and Delete.
9. One inline form for add and edit — not a flyout, for §12.3's reason: a popup is its own root and has no adorner layer, so every ring there would have to be hand-drawn. Fields: title (required), year, platform label, executable path, IGDB id, Steam appid.
10. ManualEntryConflictException.Field is surfaced against the named field (IgdbId or SteamAppId) rather than as a form-level failure, so the user is pointed at the box to fix. A blank title is caught the same way from the repository's ArgumentException.
11. Deleting asks first and the question says what survives, matching §12.3. That makes a second destructive act in the application; §2 and §12.3 both claim there is one, so both are corrected and their old sentences appended to docs/decisions.md.
12. Saving reloads the library so the entry appears in the grid, the list view, the feed and the bucket counts immediately. Copy by docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Data-layer half done; the add/edit UI is a separate agent's and this task is NOT finished.

STUDIED RECONCILIATION FIRST, and the answer changed the design. ExternalIdResolver joins candidates on an exact (provider, provider_id) external id or mints a new work; there is no title-based attachment to an existing work, no prune step, and no DELETE FROM ownerships/releases/works anywhere in runtime code. So the hard part is not building a guard, it is making two facts that were already true explicit and testable:
  1. store = 'manual'. OwnershipRepository.UpsertAsync conflicts on (release_id, store) and no reader emits that store, so no resolve pass can reach the row however it resolves.
  2. name_is_provisional = 0 on the work. ExternalIdResolver.PromoteProvisionalNameAsync renames only while that flag is set, and EnrichmentSyncService's patch is fill-only, so a hand-typed title is never a candidate for replacement.

Migration 0025_manual_entries.sql: manual_entries(ownership_id PK FK ownerships ON DELETE CASCADE, executable_path, platform_label, added_at, updated_at). The presence of the row IS the origin marker — one mechanism, not two, and a table no ingest path writes. No new column on ownerships, deliberately: a second marker would be a second thing to keep in step. checksums.txt updated.

Contracts for the UI agent (Winnow.Core): ManualGameDraft (Title, FirstReleaseYear, PlatformLabel, ExecutablePath, InstallPath, IgdbId, SteamAppId; EffectiveInstallPath prefers InstallPath and otherwise takes the executable's directory), ManualEntry (OwnershipId, ReleaseId, WorkId, Title, FirstReleaseYear, PlatformLabel, ExecutablePath, InstallPath, AddedAt, UpdatedAt), ManualEntryConflictException (Field names IgdbId or SteamAppId), OwnershipStores.Manual, and IManualEntryRepository — CreateAsync(draft), GetAsync(ownershipId), GetAllAsync(), UpdateAsync(ownershipId, draft), DeleteAsync(ownershipId). Implementation Winnow.Data.Repositories.ManualEntryRepository(ISqliteConnectionFactory, TimeProvider?). Update and Delete return false when the id is not a hand-added entry; a blank title throws ArgumentException; a supplied IGDB id or Steam appid already in the library throws ManualEntryConflictException BEFORE anything is written, so the form can name the field to fix.

SESSION MONITORING NEEDS NO MONITOR CHANGE. GameExecutableIndexBuilder reads ownerships.installed and install_path and walks that directory for executables, so a draft naming an executable stores its directory and sets installed = 1 and the game is watched today. The stored executable_path is evidence for a later exact-match change in the Monitor, not an index yet.

Delete is narrow and ordered: the ownership, then the release only when no other ownership hangs off it, then the work only when it has no releases left. A Steam entry that later attached to the same release (via a Steam appid the user supplied by hand) keeps its game — tested.

AC #3 and #6 checked (tests/Winnow.Tests/ManualEntryTests.cs, 13 tests; the survival test creates an entry, runs two full resolve passes over a candidate with the same title, and asserts the ManualEntry record, the ownership's store and install path, and the work's name are byte-identical). #1, #2, #4 and #5 are unchecked because each needs the UI or an end-to-end monitoring run, though the repository half of #5 is tested.

REMAINING: register IManualEntryRepository in src/Winnow.App/Program.cs, the add/edit form, and a delete affordance. Optional follow-up for the Monitor agent: attribute a process by manual_entries.executable_path exactly rather than by walking the install directory.

UI half landed. Registered IManualEntryRepository in src/Winnow.App/Program.cs.

WHERE IT LIVES. The ADDED BY HAND card on the new third settings section, SETTINGS › LIBRARY (src/Winnow.App/Views/LibrarySettingsView.axaml, LibrarySettingsViewModel), beside the hidden-games list (TASK-87) and the explicit-content toggle (TASK-88), because all three answer what is in the library. One inline form serves add and edit, in the pane's own tree and not a flyout — §12.3's reason, that a popup is its own root with no adorner layer, so every focus ring inside one would have to be hand-drawn. Fields: title (required), year, free-text platform label, executable path, IGDB id, Steam appid.

FIELD-LEVEL ERRORS. ManualEntryConflictException.Field is switched on and the message is set on IgdbIdError or SteamAppIdError, so the user is pointed at the box to fix rather than told the form failed; the form stays open on what they typed, and nothing was written because the repository raises the conflict before it starts. A blank title comes back from the repository's ArgumentException and lands on TitleError. Non-numeric ids and a malformed year are caught in the form and never reach the repository.

ONE THING THE FORM HAD TO GO AND FETCH. UpdateAsync writes works.igdb_id and the release's external ids as given, and ManualEntry carries neither, so an edit form that opened with those boxes blank would silently clear the ids that got the game its cover. BeginEditAsync therefore reads the IGDB id off the entry's own work and the Steam appid off its release before opening. That read is registered in tests/Winnow.Tests/IdentityReadInventoryTests.cs under DO NOT RESOLVE, with the reason.

DELETE ASKS FIRST (§12.3), and the question names what survives: only the entry the user typed goes. Danger is on that confirm button and on nothing else on the screen. That makes a SECOND destructive act in the application, and §2 and §12.3 both claimed there was one — both corrected, with the superseded sentences in docs/decisions.md (2026-09-04) per AGENTS.md.

Every write reloads the library, so a hand-added game reaches the grid, the list view, the feed and the bucket counts immediately rather than on the next launch.

All copy and comments authored by docs-writer; strings in src/Winnow.App/ViewModels/LibrarySettingsCopy.cs.

TESTS: tests/Winnow.Tests/LibrarySettingsViewModelTests.cs. A_hand_added_game_appears_in_the_library asserts the saved entry, the row's detail line, the stored executable_path, the derived install path and installed = true (the two fields GameExecutableIndexBuilder reads to decide what to watch), and the tile and TotalCount in a library that was empty. A_hand_added_game_can_be_edited asserts the edit reaches both the list and the library. A_hand_added_game_is_deleted_only_after_a_confirmation asserts that Begin leaves the row in place, Cancel leaves it in place, and only Confirm removes it from the repository and the library. Three more cover the conflicting Steam appid landing on its own field with nothing written, a blank title, and a non-numeric id.

VERIFIED: dotnet build -p:BaseOutputPath=C:\Temp\winnow-b2\ -m:1 — 0 warnings, 0 errors. tests/Winnow.Tests 2973 passed, 0 failed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
A game can now be added by hand, edited and deleted, from the ADDED BY HAND card on the new SETTINGS › LIBRARY section.

One inline form serves add and edit — not a flyout, for §12.3's reason that a popup has no adorner layer. Title is the only required field; year, a free-text platform label, an executable path, an IGDB id and a Steam appid are optional. Naming an executable stores it and derives the install path, setting installed, which is what puts the entry in the session watcher's index. Either id gets the game cover art.

ManualEntryConflictException.Field is surfaced against the field that conflicted rather than as a generic failure, so the form points at the box to fix and stays open on what was typed; nothing is written, because the repository refuses before it starts. Deleting asks first and the question names what survives — a second destructive act, so §2 and §12.3's claim that there was only one is corrected, with the superseded sentences recorded in docs/decisions.md.

Verified with tests/Winnow.Tests/LibrarySettingsViewModelTests.cs: the form creates an entry that appears as a tile in a library that was empty and moves TotalCount to 1, with the executable, the derived install path and installed = true stored — the two fields GameExecutableIndexBuilder reads (AC4); an edit reaches both the list and the library; delete removes it only after the separate confirm command, and Cancel leaves it in place (AC5); a Steam appid already in the library lands on SteamAppIdError with nothing written. dotnet build clean; Winnow.Tests 2973 passed / 0 failed.

AC2's feed half is inherited rather than separately asserted: a hand-added entry is an ordinary ownership row, so it arrives through the same bucket query the grid, the list view and the feed all read, and the save reloads that query.
<!-- SECTION:FINAL_SUMMARY:END -->
