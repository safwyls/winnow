---
id: TASK-104
title: 'Add a game by browsing for its executable, and fetch its metadata'
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 22:50'
updated_date: '2026-09-06 17:46'
labels:
  - ui
  - enrichment
dependencies:
  - TASK-89
priority: medium
type: enhancement
ordinal: 131000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Adding a game by hand (TASK-99) currently makes the user find and type every fact themselves. It should start from the thing they actually have: the executable on disk.

Let the user browse for an .exe, then derive what can be derived from it — the file description, product name and company in its version info, the folder name, and the install path — and use that to search IGDB and propose a match. The proposal must be confirmable and overridable, never silently applied: the same confirm-or-correct shape TASK-89 built for the wrong-game control, which this should reuse rather than duplicate.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The add-a-game flow offers a file browser for the executable
- [x] #2 Title and metadata are proposed from the executable version info, product name, folder name and path rather than typed by hand
- [x] #3 The proposed IGDB match is shown for confirmation and can be overridden or rejected before anything is written
- [x] #4 A game whose executable yields nothing useful still falls back to the manual form rather than dead-ending
- [x] #5 The executable chosen is the one session monitoring watches
- [x] #6 The IGDB search and confirm surface is reused from TASK-89, not duplicated
- [x] #7 Tests cover the derivation from version info and the reject path
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
UI + App-seam wave. Nothing under Winnow.Core, Winnow.Data or Winnow.Enrich.* changes: every contract this needs already exists (ManualGameDraft, IManualEntryRepository, IIgdbAssignmentService.SearchAsync).

1. The picker. IExecutableFilePicker + TopLevelExecutableFilePicker in Winnow.App/Services, single-select, modelled line for line on ISteamAccountPageFilePicker / TopLevelSteamAccountPageFilePicker - TopLevel.StorageProvider, marshalled to the UI thread, TryGetLocalPath so a file reached through a provider with no path on disk is dropped rather than turned into an unreadable path, and never throwing. That is the repository's own precedent for an OS dialog and there is no second one.

2. The derivation, split so the interesting half needs no disk. ExecutableFacts.Derive(path, fileDescription, productName, companyName) is PURE string work (Path.GetDirectoryName/GetFileName touch no disk) and is where every test lands. IExecutableInspector / FileVersionInfoExecutableInspector is the thin disk half: FileVersionInfo.GetVersionInfo, read-only, never executed, and every exception caught - a locked file, a file that is not a PE image and a file with no version info all come back as facts with a null title rather than as a throw into the UI.
   Candidate order: FileDescription, ProductName, containing folder (walking up past container segments - bin, binaries, win64, x64, release, common, steamapps...), file name. Each candidate is cleaned (strip .exe, strip -Win64-Shipping and the other build suffixes, underscores to spaces, collapse whitespace) and then rejected if it normalises to a stub (game, launcher, start, client, data, app...), an engine or runtime name (unreal engine, unity, godot, gamemaker, electron, javaw...), a bare number, or fewer than two characters. First survivor wins; no survivor means no proposed title, which is the degrade case AC4 is about.
   InstallPath is deliberately NOT proposed: ManualGameDraft.EffectiveInstallPath already takes the executable's own directory, and that is the narrow answer session monitoring wants.

3. TASK-89 reused, not duplicated. The search goes through the existing IIgdbAssignmentService.SearchAsync and the rows are the existing IgdbCandidateViewModel - same cover path (IgdbImageUrl.ImageId -> CoverKey.Igdb -> ICoverCache at row width), same 34x51 geometry, same bounded ScrollViewer, same 'Use this' copy. IgdbCandidateViewModel gains one property, FirstReleaseYear, because the add form fills a year field and YearText is formatted for display. GameIgdbMatchViewModel itself is NOT reused: it assigns against a work id and pins, and in this flow there is no work yet and nothing may be written before Save.

4. The flow, on the screen that already exists. The ADDED BY HAND header gets a second control that opens the form AND the picker in one gesture; inside the form the EXECUTABLE field gains Browse, so the file route is reachable both ways. Choosing an executable fills EXECUTABLE, proposes TITLE, states what it read, and searches IGDB with it. Choosing a candidate fills TITLE, YEAR and IGDB ID and writes nothing. The user may edit TITLE and search again (override), dismiss the proposal (reject), or ignore it and type the form as before (fallback). Save is still the only write, and still the same CreateAsync/UpdateAsync path, so ManualEntryConflictException.Field still lands under its own field.

5. Registration: two lines in Program.cs beside the Steam picker; LibrarySettingsViewModel takes the picker, the inspector, IIgdbAssignmentService and ICoverCache as optional constructor parameters, so a host that omits them gets the pre-104 form and not a screen that will not open.

6. Tests in tests/Winnow.Tests: the derivation table (version info wins, folder wins when version info is a stub, engine and launcher names rejected, build suffixes stripped, nothing usable yields no title), the inspector over a non-PE file and a missing file, and the view-model paths - browse fills and proposes, a candidate fills the draft and writes nothing, dismiss leaves the form usable, an executable that yields nothing still saves by hand.

7. All prose - copy constants, XAML comments, XML doc comments, design-system.md 16.3 and the docs/decisions.md line - delegated to docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
2026-09-06 UI verification: exercised the compiled LibrarySettingsView in an isolated Avalonia.Headless 11.3.20/Skia window with the real App resources. Enter on Add from a file invoked an injected IExecutableFilePicker, opened the manual form, filled C:/Games/Prey/Prey.exe, derived Prey from the path, and rendered the existing IgdbCandidateViewModel proposal row. Enter on Use this filled the editable title, year 2017 and IGDB id 777 while preserving the executable and keeping the form open. Enter on Dismiss removed proposals and retained the editable draft and executable. Inspected add-proposal.png and add-rejected.png: candidate confirmation, separate Save, and fallback form are visible. No repository was registered and no data was written. The native Windows file dialog was not driven; the picker seam was substituted. Harness and captures: C:/Temp/winnow-task105. Derivation, rejection, persistence and session-monitoring evidence is supplied separately by the coordinating agents existing test run.

Combined verification: coordinating agent ran the existing ManualGameFromExecutableTests and GameExecutableIndexTests in a 164-test passing run. Named cases cover file-description and product-name derivation, folder fallback, shipping suffix cleanup, missing/non-PE files, no-title manual save, candidate selection without writes, rejection, edited-title override, chosen executable persistence, and installed-game executable attribution. The rendered Add from a file / Use this / Dismiss interaction recorded above verifies the live control bindings. The shared IIgdbAssignmentService and IgdbCandidateViewModel provide reuse without prematurely pinning a work that does not yet exist.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Already implemented. Verified rendered browse/propose/confirm/dismiss controls with an injected picker, plus derivation, fallback, no-write confirmation, executable persistence and monitoring tests. All 164 tests in the combined verification run passed; native OS dialog was not driven.
<!-- SECTION:FINAL_SUMMARY:END -->
