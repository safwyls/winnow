---
id: TASK-89
title: Let the user search IGDB and assign metadata to a game by hand
status: Done
assignee:
  - '@claude'
created_date: '2026-09-04 18:13'
updated_date: '2026-09-04 21:35'
labels:
  - ui
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 116000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fuzzy resolution sometimes picks the wrong IGDB entry, or none. When it does, the user has no recourse: the cover, summary, year and genres stay wrong or empty with no way to correct them. Add a manual override from the details modal — search IGDB by title, pick the right entry, and pin that mapping so later enrichment passes respect it rather than overwriting it.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The details modal offers a way to search IGDB by title and see candidate results with cover, year and platform
- [x] #2 Choosing a candidate rewrites the metadata for that work and refreshes the cover
- [x] #3 A hand-assigned mapping is marked as user-pinned and is never overwritten by an automatic enrichment pass
- [x] #4 The assignment can be cleared to return the work to automatic resolution
- [x] #5 Tests cover the pin surviving a subsequent enrichment run
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Non-UI half only (the details-modal view is a separate wave; this half must not touch src/Winnow.App).

1. Search on the IGDB client. Apicalypse.SearchGames gets its OWN query body (search clause + name, cover, first_release_date, platforms.name) and IIgdbClient.SearchGamesAsync gets its OWN cache namespace ('search:'), following the precedent age ratings set: a 400 on a search query must not cost name/cover/genres/publisher for the whole library. Rate limited, cached and soft-failing through the existing handler pipeline; a failed search returns an empty list.

2. The pin. Migration 0026_igdb_pins.sql adds work_igdb_pins in the append-and-stamp shape 0023 uses: insert to pin, stamp cleared_at to clear, partial unique index on the live row, so 'is this work pinned' stays a query and the row is the history. Winnow.Core gets WorkIgdbPin + IWorkIgdbPinRepository; Winnow.Data gets WorkIgdbPinRepository.

3. Enforcement, in two places, both in Winnow.Data so no App file changes.
   a. WorkRepository.GetEnrichmentTargetsAsync excludes works with a live pin - the automatic pass never asks about them.
   b. WorkRepository.ApplyEnrichmentAsync no-ops on a pinned work - defence in depth for any other caller.

4. Assignment service in Winnow.Enrich.Igdb (IgdbManualAssignment), the precedent being IgdbMaturitySync: search, assign (pin + overwrite the work's metadata columns, unlike the fill-only automatic path), clear (unpin, leaving metadata in place for the automatic passes to correct).

5. Tests. Search against canned fixtures in the existing IgdbTestHost. A pin-survives-enrichment test that runs EnrichmentSyncService over a pinned work and asserts igdb_id, name, year, summary, cover and publisher are all unchanged - the criterion #5 test. Repository tests for pin/clear/re-pin.

6. IdentityReadInventoryTests: add WorkIgdbPinRepository's read of works to the DO-NOT-RESOLVE list (the pin is stored against the work id the caller was handed, exactly as HiddenGameRepository.HideAsync does).

7. All prose (SQL comments, code comments, XML doc comments) delegated to docs-writer.

UI HALF (this wave). Details modal only; Winnow.Enrich.*, Winnow.Core/Reading and Winnow.Data/Migrations untouched.

8. Registration. services.AddSingleton<IWorkIgdbPinRepository, WorkIgdbPinRepository>() beside IHiddenGameRepository / IWorkMaturityRepository in Program.cs. IgdbManualAssignment is already registered by AddIgdbEnrichment.

9. App-layer seam. IIgdbAssignmentService + IgdbAssignmentService in Winnow.App/Services, wrapping IgdbManualAssignment - the precedent IUpdateFlagService/UpdateFlagService and IFeedService/FeedService set. The view model depends on the interface so it is fakeable in a pure view-model test, and the adapter is the one App type naming the enrichment service.

10. Placement, and the reason. NOT a sixth labelled section. The modal already stacks ALSO COVERS, EXTENDS, EXPANSIONS, LISTS, ABOUT and the update list; a seventh panel is how it becomes a stack of panels. The control goes INSIDE the ABOUT section as its footer, because ABOUT is the IGDB record in prose - summary, and beside it the year, publisher and cover the same record supplied - so the recourse sits under the wrong answer it corrects. At rest it is one quiet line, not a section: the modal grows by one row and nothing else. Inline disclosure, never a flyout (10.7 / 12.3 - FocusAdorner does not render inside a popup).

11. GameIgdbMatchViewModel + IgdbCandidateViewModel in Winnow.App/ViewModels, built by LibraryViewModel.OpenDetailsAsync and handed to GameDetailsViewModel as one more optional seam, exactly as coverage, expansions and lists are. Work id from the existing GameWorkIdFor(target); null service or null work id renders no control at all.

12. Candidate covers ride the EXISTING image path, no new one: IgdbImageUrl.ImageId(result.CoverUrl) -> CoverKey.Igdb(imageId) -> ICoverCache.GetAsync at row width. The IGDB cover source already accepts an image-id key and needs no credentials for it.

13. All five statuses rendered distinctly. Assigned reloads and reopens the modal on the same ownership, which is SeparateAsync's own precedent, so the user sees the corrected cover, title, year and summary where they asked for it, with a confirmation note carried across the reopen. WorkNotFound, MetadataUnavailable, IgdbIdClaimedByAnotherWork and Failed each keep the controls in place under their own Amber sentence. A search that returned nothing is a sixth, TextDim state. A busy line in words, never a spinner (section 8).

14. Clear is drawn only while a live pin exists, read with GetPinAsync when the modal opens. Clearing updates the control in place and does not reload, because clearing writes no metadata.

15. All prose - XAML copy, the status sentences, tooltips, automation names, code and XML doc comments, and a design-system.md section under 10 - delegated to docs-writer.

16. Tests: pure view-model tests over a fake IIgdbAssignmentService covering search results, the five statuses, the empty result set, and clear.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Non-UI half implemented (search + pin + enforcement). Details-modal UI is a separate wave and is deliberately untouched; nothing under src/Winnow.App was modified.

WHAT LANDED

Search (src/Winnow.Enrich.Igdb):
- Apicalypse.SearchGames / Apicalypse.SearchTerm / Apicalypse.DefaultSearchLimit. Search rides its OWN Apicalypse body ('fields name,cover.image_id,cover.url,first_release_date,platforms.name; search "…"; limit N;') rather than widening the shared Games query - the precedent the age-ratings query set, for the same reason: one 400 on the shared body costs name, cover, genres, themes, modes, perspectives and publisher for the whole library.
- SearchTerm SANITIZES rather than rejects (unlike IsSafeStringValue, which is right for machine-generated store ids): a user-typed title containing a quote should still search, so the quote, backslash, semicolon and control characters become spaces and whitespace is collapsed.
- IIgdbClient.SearchGamesAsync + IgdbClient implementation. Own cache namespace ('search:<limit>:<lowercased term>') and own payload version, so a search 400 cannot invalidate the metadata cache. Rate limiting, retry and auth are unchanged - it goes through the same typed HttpClient handler pipeline, no Task.Delay anywhere.
- IgdbOptions.SearchResultLimit (20) and SearchCacheTtl (1 day, against the 30-day CacheTtl: a search RESULT SET changes when IGDB adds a game, unlike a shipped game's own fields).
- Soft-failing: a blank term, an unconfigured client and a failed request all return an empty list, and a failure is NOT cached.
- Field list established from IGDB v4's published endpoint field tables plus what this repository already proves (cover.image_id/url via IgdbJson.CoverUrl, first_release_date as Unix seconds). No live API call was made; tests answer from canned fixtures in IgdbFixtures.

The pin:
- Migration 0026_igdb_pins.sql, table work_igdb_pins, append-and-stamp in 0023's shape: pin inserts, clear stamps cleared_at, ux_work_igdb_pins_live keeps at most one live row per work, re-pinning stamps and inserts afresh. checksums.txt updated.
- Winnow.Core: WorkIgdbPin, WorkIgdbPinAssignment, WorkIgdbPinOutcome, IWorkIgdbPinRepository.
- Winnow.Data: WorkIgdbPinRepository. PinAsync overwrites the IGDB-derived columns unconditionally, including back to NULL, because the stored values belonged to the game the resolver got wrong; name is the exception (COALESCE'd over blank, since works.name is NOT NULL) and writing a real name clears name_is_provisional. Storefront-observed columns are not touched. Refuses with an outcome (never an exception) when the work does not exist or another work already holds that igdb_id - works.igdb_id is UNIQUE.

Enforcement, both in WorkRepository so no App file changes:
- GetEnrichmentTargetsAsync excludes works with a live pin. Primary enforcement: the pass never even asks about a pinned work.
- ApplyEnrichmentAsync carries the same NOT EXISTS on the read-back and the UPDATE. Defence in depth for any other caller, and it matters for the columns the chosen entry has nothing for - otherwise the WRONG game's publisher would be filled in around the pin.

Assignment service: IgdbManualAssignment (Winnow.Enrich.Igdb, modelled on IgdbMaturitySync) with SearchAsync / AssignAsync / ClearAsync / GetPinAsync. AssignAsync deliberately refuses to pin when IGDB cannot supply the chosen game's metadata: pinning without metadata would leave the wrong metadata AND block the automatic pass from correcting it.

Cover refresh needs no new mechanism: the IGDB cover key is derived from the stored cover_url's image id, so rewriting cover_url refreshes the tile.

DELIBERATE NON-CHANGE: GetProvisionalNameTargetsAsync is NOT guarded. A provisional name is a fact about the work's own row and the resolver's rename touches neither identity nor IGDB-derived metadata. The only way a pinned work stays provisional is an IGDB record with no name at all, and IgdbManualAssignment refuses to pin when no record comes back.

All prose (SQL comments, code comments, XML doc comments, game-library-design.md sections 4.4 and 6.4) authored by the docs-writer subagent, not by the implementing agent. No TODO(docs-writer) markers remain.

Verification, PowerShell, scratch output path so no assembly lock:
  dotnet build -p:BaseOutputPath=C:\Temp\winnow-d2\ -m:1
    Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test tests\Winnow.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-d2    Passed! - Failed: 0, Passed: 3004, Skipped: 0, Total: 3004
  dotnet test tests\Winnow.Covers.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-d2    Passed! - Failed: 0, Passed: 70, Skipped: 0, Total: 70
  dotnet test tests\Winnow.Recommend.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-d2    Passed! - Failed: 0, Passed: 152, Skipped: 0, Total: 152

AC 3 checked: EnrichmentSyncServiceTests.A_pinned_work_survives_an_enrichment_pass runs a real enrichment pass over a pinned work and asserts igdb_id, name, provisional flag, year, summary, cover and publisher are all unchanged, and that IGDB and the Steam store were never asked about the appid. WorkIgdbPinTests.An_enrichment_write_against_a_pinned_work_changes_nothing covers the second guard directly.
AC 5 checked: same test, which is the one the criterion names.

AC 1, 2 and 4 deliberately left unchecked. The mechanisms all exist and are tested below the view - search returns candidates with cover, year and platforms; assignment rewrites metadata and the cover key follows cover_url; clearing returns the work to the enrichment target query - but the user cannot yet reach any of them, because the details-modal UI is the next wave's work. They are the UI agent's to check.

NOT FINALIZED: status left In Progress and no final summary written, because the UI half is outstanding.

The host still needs one registration line in src/Winnow.App/Program.cs, beside the IHiddenGameRepository and IWorkMaturityRepository lines:
  services.AddSingleton<IWorkIgdbPinRepository, WorkIgdbPinRepository>();
IgdbManualAssignment itself is already registered by AddIgdbEnrichment.

UI HALF LANDED (second wave). Details modal only; Winnow.Enrich.*, Winnow.Core/Reading and Winnow.Data/Migrations untouched.

REGISTRATION
- Program.cs: IWorkIgdbPinRepository -> WorkIgdbPinRepository, beside IHiddenGameRepository / IWorkMaturityRepository. Without it the pin repository the enrichment half needs is never resolvable in the composed app.
- Program.cs: IIgdbAssignmentService -> IgdbAssignmentService, beside IUpdateFlagService.

PLACEMENT, AND THE REASON
Not a sixth section. The modal already stacks ALSO COVERS, EXTENDS, EXPANSIONS, LISTS, ABOUT and the update list; a seventh panel is how it becomes a stack of panels. The control is the FOOTER OF THE ABOUT SECTION, because ABOUT is the IGDB record in prose and the year, publisher and cover above it came from the same record - so the recourse sits under the answer it corrects. At rest it is one quiet line and the modal grows by one row. The search is disclosed inline in the modal's own tree, never a flyout (10.7 / 12.3 - FocusAdorner does not render inside a popup). Recorded as design-system.md 10.9.

WHAT LANDED
- src/Winnow.App/Services/IIgdbAssignmentService.cs, IgdbAssignmentService.cs - the 5.1 seam in front of IgdbManualAssignment, the only App type naming it. Nothing on the seam throws: IgdbManualAssignment already soft-fails Search and Assign, and the two pass-through reads (Clear, GetPin) are caught here.
- src/Winnow.App/ViewModels/GameIgdbMatchViewModel.cs (+ IgdbCandidateViewModel) and GameIgdbMatchCopy.cs.
- GameDetailsViewModel gains an optional igdbMatch seam and ShowIgdbMatch.
- LibraryViewModel gains an optional IIgdbAssignmentService, BuildIgdbMatchAsync (work id via the existing GameWorkIdFor, live pin read on open), AfterAssigningIgdbAsync and ReopenDetailsAsync. SeparateAsync was folded onto ReopenDetailsAsync rather than keeping two copies of reload-and-reopen.
- GameDetailsView.axaml: the ABOUT footer block plus the TextBox.field styles copied from the LIBRARY settings screen's hand-added-game form. Code-behind hands the candidate rows their render scaling and runs the search on Enter.

DESIGN DECISIONS WORTH KEEPING
- Assigned reloads and reopens the modal on the same ownership, carrying the confirmation across, so the user sees the corrected cover, title, year and summary where they asked for it. That is SeparateAsync's own arrangement, for its own reason. The four refusals keep the controls in place under their own Amber sentence and each says something different, because IgdbIdClaimedByAnotherWork is the only one the user can act on. A search that matched nothing is a sixth, TextDim state - not a failure. Busy is words in a status field, never a spinner (section 8), so reduced motion has nothing to disable.
- Candidate covers ride the EXISTING image path and add none: IgdbImageUrl.ImageId(CoverUrl) -> CoverKey.Igdb(imageId) -> ICoverCache, which the registered IGDB cover source answers with no credentials.
- Clear is drawn only while a live pin stands and does NOT reload, because clearing writes no metadata.

THE ONE THING THAT HAD TO BE REDONE
The first cut returned Winnow.Enrich.Igdb.Model types (IgdbSearchResult, IgdbAssignmentResult, IgdbAssignmentStatus) across the seam, and Enforcement/ArchitectureBoundaryTests failed twice: it enforces game-library-design.md 5.1 both as a source scan over src/Winnow.App/Views and /ViewModels and as a metadata scan over every type in those namespaces. The fix was not an exemption. The seam now owns its own read models - IgdbCandidate and IgdbAssignmentOutcome in Winnow.App.Services - and the enrichment types stop at the adapter, which is the layer allowed to name them.

All prose (XAML copy, the status sentences, tooltips, automation names, code and XML doc comments, and design-system.md 10.9) authored by the docs-writer subagent. No TODO(docs-writer) markers remain anywhere in the tree.

VERIFICATION, PowerShell, scratch output path:
  dotnet build -p:BaseOutputPath=C:\Temp\winnow-f1\ -m:1
    Build succeeded. 0 Warning(s) 0 Error(s)
  dotnet test tests\Winnow.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-f1    Passed! - Failed: 0, Passed: 3066, Skipped: 0, Total: 3066
  dotnet test tests\Winnow.Recommend.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-f1    Passed! - Failed: 0, Passed: 152, Skipped: 0, Total: 152
  dotnet test tests\Winnow.Covers.Tests --no-build -p:BaseOutputPath=C:\Temp\winnow-f1    Passed! - Failed: 0, Passed: 70, Skipped: 0, Total: 70
Baseline was 3046 / 152 / 70; the 20 new tests are IgdbMatchViewModelTests (16) and IgdbAssignmentModalTests (4).

AC EVIDENCE
AC 1: IgdbAssignmentModalTests.Choosing_a_candidate_rewrites_the_metadata_and_the_cover drives the search through the real LibraryViewModel and asserts the candidate carries cover key, year and platforms; IgdbMatchViewModelTests covers the same over the view model plus the no-year/no-platform row. Compiled bindings (AvaloniaUseCompiledBindingsByDefault) make the build a check that every binding in the new markup resolves. What no test covers is how it LOOKS - that needs a run.
AC 2: the same test asserts the reopened tile's year, publisher, summary and cover key all changed, with the pin written by the real WorkIgdbPinRepository.
AC 4: The_assignment_can_be_cleared_and_the_pin_goes_with_it asserts the row is gone from work_igdb_pins and the control folds away; A_clear_that_did_not_land_keeps_the_control covers the refusal.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fuzzy IGDB resolution now has a recourse. The non-UI half (search on the IGDB client with its own query body and cache namespace, work_igdb_pins in migration 0026, and enforcement in both WorkRepository.GetEnrichmentTargetsAsync and ApplyEnrichmentAsync) shipped in the first wave; this wave built the surface and wired the host.

The control is the footer of the details modal's ABOUT section, not a section of its own: ABOUT is the IGDB record in prose and the year, publisher and cover above it came from the same record, so the correction sits under the answer it corrects, and at rest it is one quiet line. The search discloses inline in the modal's own tree, never a flyout, because Avalonia's FocusAdorner does not render inside a popup. A candidate row draws a 34x51 cover, the name, the year in Plex and the platforms in Jakarta - the four facts that separate Prey (2006) from Prey (2017) - and the covers ride the existing image path, since the image id inside IGDB's cover URL is already a cover key the registered source answers. Assigning reloads the library and reopens the modal on the same ownership with a confirmation carried across, so the corrected cover, title, year and summary appear where the user asked for them; the four refusals each keep the controls in place under their own Amber sentence, and an empty result set is a sixth, quieter state. Clear appears only while a pin stands. Recorded as design-system.md 10.9.

The App-layer seam IIgdbAssignmentService / IgdbAssignmentService is the only App type naming IgdbManualAssignment, and it owns its own read models - IgdbCandidate and IgdbAssignmentOutcome - because the architecture-boundary tests enforce game-library-design.md 5.1 on what a view model NAMES as well as on what it holds, and returning the enrichment records across the seam failed both scans.

Verified with dotnet build -p:BaseOutputPath=C:\Temp\winnow-f1\ -m:1 (succeeded, 0 warnings, 0 errors) and dotnet test per project against that output path: Winnow.Tests 3066 passed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 70 passed, 0 failed anywhere, up from a 3046 / 152 / 70 baseline. Criteria 1, 2 and 4 rest on IgdbAssignmentModalTests, which drives the real LibraryViewModel over a migrated SQLite database and asserts the reopened tile's year, publisher, summary and cover key all changed and that clearing removes the pin row; criteria 3 and 5 rest on the first wave's EnrichmentSyncServiceTests.A_pinned_work_survives_an_enrichment_pass. What no test covers is how the control looks on screen, which needs a run.
<!-- SECTION:FINAL_SUMMARY:END -->
