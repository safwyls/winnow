---
id: TASK-120
title: Searching an IGDB id should return that exact game
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 03:49'
updated_date: '2026-09-05 04:13'
labels:
  - ui
  - enrichment
dependencies:
  - TASK-89
priority: medium
type: enhancement
ordinal: 147000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User request: "also searching for an igdb id should return the exact result". Typing a known IGDB id into the wrong-game search currently runs it as a title query, so the one record the user named is not what comes back.

The lookup already exists: IIgdbClient.GetGamesAsync([id]) is what IgdbManualAssignment.AssignAsync uses to fetch a chosen game. The work is routing a numeric query to it and presenting the hit.

One wrinkle worth designing around rather than tripping over: a query of digits is not unambiguously an id. Real titles are numeric or start numeric — 2064, 1979 Revolution, 7 Days to Die, 428. Treating every all-digit query as an id would break searching for those by name. The suggested shape is to do both: when the query is all digits and a game with that id exists, put it first and mark it as an id match, then list the title-search results beneath. That way naming an id is exact without making numeric titles unsearchable. Decide deliberately and say what you chose.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Entering a known IGDB id returns that game
- [x] #2 The id match is distinguishable from title matches rather than silently mixed in
- [x] #3 A numeric title is still findable by name
- [x] #4 An id that matches nothing reports that plainly and does not read as a failed search
- [x] #5 The id lookup reuses the existing GetGamesAsync path and its cache rather than adding a query
- [x] #6 Tests cover an id hit, an id miss, and a numeric title
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
SHAPE CHOSEN: do both, as the task suggested, and say why.

An all-digit query runs the id lookup AND the title search, and the id hit leads the list. Routing digits to the id lookup alone would make 2064, 1979 Revolution, 7 Days to Die and 428 unfindable by name, and there is no way to tell from the query which the user meant. Doing both costs one extra request against a path that is already cached, and it is the only shape where naming an id is exact without taking anything away.

WHAT COUNTS AS AN ID: every character a digit, and it parses to a positive long. A title that merely starts with digits (1979 Revolution) is a title, so it never runs the id lookup at all.

1. ENRICHMENT HALF, delegated to the enrichment-api agent. IgdbManualAssignment.GetByIdAsync(long, ct) calling _igdb.GetGamesAsync([id]) -- the same path and the same cache AssignAsync already uses, per AC5, with no new query and no new field on the shared query body. Soft-fails to null like SearchAsync. App seam gains IIgdbAssignmentService.GetCandidateByIdAsync(long, ct) returning IgdbCandidate?, null for both a miss and a failure.
2. IgdbGame carries no Platforms, so the id-match row draws its year and no platform list. Adding platforms to the shared GetGamesAsync query would bump the cached payload version and force a full re-fetch of every game's metadata against a rate-limited API; IIgdbClient's own doc comment records why that query is kept narrow. Recorded rather than done.
3. VIEW MODEL. GameIgdbMatchViewModel.IgdbIdIn(string) decides. SearchAsync runs the id lookup first when it returns an id, then the title search, drops the id-matched game out of the title results if it appears there too, and puts the id row first. IdMissed is set when an id was queried and nothing came back.
4. MARKING IT. IgdbCandidateViewModel gains IsIdMatch, set through the constructor, and IdMatchLabel. The row draws the mark in the existing outlined store-chip idiom (Border.store-chip: 1px Line, radius 3, body face 9px, TextDim) immediately before the name, on a Grid Auto,* so the name keeps the star column and still trims -- the same bounding lesson TASK-118 is fixing one line below.
5. AN ID THAT MATCHED NOTHING. Its own TextDim line above the results, gated on ShowIdMiss, independent of ShowNoMatches. When both come up empty both lines draw, which is two facts stated plainly. TextDim and not Amber, per 10.9: a search that matched nothing is not a failure.
6. THE FIELD SAYS SO. FieldWatermark and FieldLabel reworded to name the id, so the capability is discoverable. Copy by docs-writer.
7. SCOPE. The details modal only. LibrarySettingsView draws the same candidate row from the same view model, so it inherits nothing and loses nothing; its own search is a different view model and is out of scope.
8. TESTS in IgdbMatchViewModelTests: an id that hits and leads the list wearing the mark, an id that hits and is de-duplicated out of the title results, an id that misses and still shows title results with the id line beside them, an id and a title that both miss, and a numeric title (1979 Revolution) that is still found by name. The enrichment agent covers the seam itself.
9. PROSE all delegated to docs-writer, including design-system.md 10.9 and the docs/decisions.md entry.
10. VERIFY: dotnet build -p:BaseOutputPath=C:\Temp\winnow-p1\ -m:1, then dotnet test per project --no-build. No app run.
<!-- SECTION:PLAN:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
An all-digit query that parses to a positive long now runs the id lookup AND the title search. A hit leads the list marked as an id match and is de-duplicated out of the title results beneath; a miss gets its own line beside the results rather than reading as a failed search. A title that merely starts with digits never reaches the id path, so 1979 Revolution and 7 Days to Die stay findable by name — that case is what forbids routing every numeric-looking query to the id lookup. New seam members: IgdbManualAssignment.GetByIdAsync and IIgdbAssignmentService.GetCandidateByIdAsync, both soft-failing, both riding the existing GetGamesAsync path and its cache rather than adding a query. Field copy changed to match: the watermark reads "Title or IGDB id". Verified by IgdbIdLookupTests (11 tests at the service seam, including that the lookup asks the shared game query and never the title search, and that a non-positive id never reaches the client) plus four view-model tests added during finalization covering the id match leading the list, not being repeated by the title search, a digits-prefixed title never reaching the id lookup, and an id miss reported beside standing title results. Full suite 3191/152/70, clean build.
<!-- SECTION:FINAL_SUMMARY:END -->
