---
id: TASK-121
title: Carry platforms on the shared IGDB game query
status: Done
assignee:
  - '@claude'
created_date: '2026-09-05 04:37'
updated_date: '2026-09-05 05:01'
labels:
  - enrichment
dependencies:
  - TASK-120
priority: medium
type: enhancement
ordinal: 148000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
An id-matched row in the wrong-game search shows a year and no platforms, while every title-search row shows both. GetByIdAsync rides IgdbClient.GetGamesAsync, whose Apicalypse body does not request platforms; SearchGamesAsync has its own body and does.

TASK-120 left this alone because widening the shared query means bumping IgdbClient.GamePayloadVersion (currently 2), which invalidates every cached game row and refetches the library. The user has decided that cost is acceptable and wants the shared query to carry platforms rather than a third query being added: "fix it right and fetch the platform. invalidating the cache once is not an issue."

The version bump is mandatory, not optional. game-library-design.md §4.4 records that changing the payload shape WITHOUT bumping degrades the response cache to empty results for 30 days. GetGamesAsync already treats a version mismatch as refetch-this-row, so bumping is the supported path.

Requests are batched (Chunk(BatchSize), BatchSize clamped to Apicalypse.MaxLimit) so the refetch is far cheaper than one request per game, but it should still be measured and stated rather than assumed.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The shared game query requests platforms and IgdbGame carries them
- [x] #2 GamePayloadVersion is bumped so cached rows refetch rather than being served with silently missing fields
- [x] #3 An id-matched row shows the same facts as a title-search row, platforms included
- [x] #4 GetByIdAsync still rides the shared path — no third query is introduced
- [x] #5 The one-time refetch cost is measured and recorded, not estimated
- [x] #6 Tests cover the version bump forcing a refetch and platforms surviving the round trip
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. WIDEN THE SHARED QUERY. Apicalypse.Games gains platforms.name — the same field path the shipped SearchGames body already uses against live IGDB, and the same {id,name} wire shape IgdbNamedDto already reads. No third query: GetByIdAsync keeps riding GetGamesAsync.

2. CARRY IT. IgdbGameDto gains Platforms (IReadOnlyList<IgdbNamedDto>?). IgdbGame gains Platforms as an INIT property defaulting to NoStrings, not a positional parameter — the same rule GameModes and PlayerPerspectives were added under, so every existing construction and every cached payload still deserializes. The name shaping (trim empties, distinct OrdinalIgnoreCase) is factored into one helper shared with IgdbSearchGameDto so an id row and a title row shape platforms identically, which is what AC3 asks for.

3. BUMP. GamePayloadVersion 2 -> 3. Mandatory per 4.4: the payload shape changed, and GetGamesAsync already treats a version mismatch as refetch-this-row.

4. KEEP THE OFFLINE GUARANTEE THE BUMP WOULD OTHERWISE REPEAL. The superseded fallback only reads the BARE pre-version-1 shape, so with no credentials a version-2 envelope would deserialize to IgdbId 0, fail the guard and be dropped — 967 rows returning nothing on an offline install. Extend the fallback to keep an older ENVELOPE's game too. This is not new scope: it is the guarantee IgdbRelationFieldTests already asserts, applied to the shape actually on disk.

5. CALLERS. FacetSyncService (one call, whole library), EnrichmentSyncService (per 40-target slice), IgdbManualAssignment.AssignAsync and GetByIdAsync, IgdbAssignmentService, the Covers.Igdb lookups (GetGamesAsync is not on their path), IgdbMaturitySync (own query, own namespace). Only GetByIdAsync changes behaviour: it passes game.Platforms where it passed [].

6. FIXTURES. IgdbFixtures.GameObject gains platforms carrying IGDB's real ids and names (6 PC (Microsoft Windows), 48 PlayStation 4), matching the search fixture. Canned responses only; no live call.

7. TESTS. Apicalypse.Games names platforms.name; platforms survive the GetGamesAsync round trip; a version-2 envelope is refetched and rewritten at version 3; a version-2 envelope is still SERVED when no refetch is possible; the id lookup and the App seam now return platforms (replacing the two Assert.Empty assertions TASK-120 left).

8. MEASURE THE REFETCH, do not estimate it. Two tests drive the real client through the real 4 req/s limiter with 967 ids and count requests and wall time: the single-call path (FacetSyncService) and the 40-target sliced path (EnrichmentSyncService). Figures recorded in the notes.

9. PROSE. Every comment, XML doc comment, 4.4 edit and docs/decisions.md entry authored by docs-writer. Code lands first without prose, docs-writer fills it, then the final build and test run.

10. VERIFY. dotnet build -p:BaseOutputPath=C:\Temp\winnow-r1\ -m:1, then dotnet test per project --no-build. No app run.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
IMPLEMENTED. Apicalypse.Games now names platforms.name; IgdbGameDto carries the expansion; IgdbGame.Platforms is an init property defaulting to NoStrings; IgdbJson.PlatformNames is shared with IgdbSearchGameDto so an id row and a title row shape platforms identically. GamePayloadVersion 2 -> 3. IgdbManualAssignment.GetByIdAsync passes game.Platforms where it passed []; it still rides GetGamesAsync and no third query exists.

FIELD-NAME PROVENANCE. platforms.name is the field path the shipped Apicalypse.SearchGames body has been using against live IGDB since the wrong-game search landed, and the wire shape (platforms: [{id,name}], ids 6 PC (Microsoft Windows) and 48 PlayStation 4) is the one already pinned in IgdbFixtures.SearchGames. Not from memory. An attempt to re-check IGDB's published field table at api-docs.igdb.com returned HTTP 403; it was NOT worked around and no credentialed call was made.

ONE BUG THE BUMP WOULD HAVE CAUSED, FIXED IN THE SAME CHANGE. GetGamesAsync keeps a mismatched payload and serves it when no refetch is possible, but the fallback read only the BARE pre-envelope shape. Every payload on a current install is a version-2 ENVELOPE, which deserializes bare as IgdbId 0, fails the > 0 guard and would have been dropped — so on an install with no credentials and no network the bump turned every cached game into nothing at all instead of a row missing one field. The fallback now reads an older envelope first and falls through to the bare shape. Covered by a test.

MEASURED REFETCH COST (not estimated). Two tests drive the real IgdbClient through the real 4 req/s IgdbRateLimiter against canned fixtures with 967 ids:
- One call, the way FacetSyncService asks: 3 requests, batches of 400/400/167 asserted from the request bodies. Three requests fit the limiter's 4-permit bucket, so the limiter adds zero delay. Measured 158 ms end to end.
- 40-target slices, the way EnrichmentSyncService asks: 25 requests, measured 6 s wall clock, which is the token bucket's own arithmetic (4-permit bucket refilled 4/s puts the 25th permit at t=6s).
The two passes share the game: cache namespace, so the whole one-time cost is 3 to 25 requests and at most about six seconds, whichever pass reaches an id first.

CALLERS CHECKED. FacetSyncService.ReadIgdbAsync (one call, whole library), EnrichmentSyncService.EnrichSliceAsync (per 40-target slice), IgdbManualAssignment.AssignAsync and GetByIdAsync, IgdbAssignmentService (already passed result.Platforms straight through), IgdbMaturitySync (own query, own maturity: namespace, untouched), Winnow.Covers.Igdb IgdbCoverSource and IgdbSteamCoverLookup (neither is on the GetGamesAsync path), and the two IIgdbClient stand-ins in tests. Nothing broke: Platforms is an init property, so every positional construction still compiles.

PROSE. Every comment, XML doc comment and documentation edit authored by the docs-writer subagent, not by me. It also found and corrected a design-system.md 10.9 sentence I had not listed (it claimed the id-match row carries no platform list). game-library-design.md 4.4's payload-version bullet was stale since the version mechanism landed and now states the per-namespace versions; the sentence it used to say, plus the three superseded doc comments and the 10.9 sentence, are quoted verbatim in docs/decisions.md.

VERIFIED. dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-r1\ -m:1 — succeeded, 0 warnings, 0 errors. dotnet test per project --no-build: Winnow.Tests 3200 passed, Winnow.Recommend.Tests 152 passed, Winnow.Covers.Tests 70 passed, 0 failed anywhere. Baseline was 3191/152/70; the 9 new tests are the new IgdbPlatformFieldTests file. The app was not run.

One enforcement failure was hit and fixed on the way: DocumentationConsistencyTests forbids the word 'superseded' in a governing document, and the first draft of the 4.4 bullet used it in its ordinary sense. Reworded by docs-writer to 'older payload'.

FINALIZATION. Added one more view-model test, IgdbMatchViewModelTests.An_id_match_draws_the_same_detail_line_as_a_title_result, so AC3 rests on evidence rather than inference: the id row and the title row beneath it are asserted to agree on HasYear, HasPlatforms and HasDetailLine, and the id row's PlatformsText and PlatformsTooltip are asserted directly. Its doc comment is docs-writer's.

Final verification, all three test projects against the same scratch output path, run sequentially:
- dotnet build Winnow.slnx -p:BaseOutputPath=C:\Temp\winnow-r1\ -m:1 -> Build succeeded. 0 Warning(s). 0 Error(s).
- Winnow.Tests --no-build -> Passed! Failed: 0, Passed: 3201, Skipped: 0, Total: 3201.
- Winnow.Recommend.Tests --no-build -> Passed! Failed: 0, Passed: 152, Skipped: 0, Total: 152.
- Winnow.Covers.Tests --no-build -> Passed! Failed: 0, Passed: 70, Skipped: 0, Total: 70.
Baseline 3191/152/70; the ten added tests are the nine in IgdbPlatformFieldTests plus this one. The app was not run.

AC evidence:
1 The_shared_game_query_asks_for_platforms, Game_metadata_carries_the_platforms.
2 The_game_payload_version_moved_past_the_shape_that_had_no_platforms, A_payload_written_under_the_previous_version_is_refetched_and_rewritten, A_payload_written_under_the_previous_version_is_still_served_when_no_refetch_is_possible.
3 An_id_matched_row_carries_the_same_facts_as_a_title_search_row (enrichment), IgdbIdLookupTests App-seam assertions, An_id_match_draws_the_same_detail_line_as_a_title_result (view model), IgdbCandidateRowLayoutTests (the row binds PlatformsText and PlatformsTooltip).
4 IgdbIdLookupTests.The_id_lookup_rides_the_shared_game_query_not_the_title_search, unchanged and passing; Apicalypse still exposes Games, AgeRatings, AgeRatingsWithoutDeprecatedFields and SearchGames and no fourth body.
5 The_whole_library_refetches_in_three_requests_when_it_is_asked_for_at_once and The_whole_library_refetches_in_twenty_five_requests_when_it_is_asked_for_in_slices.
6 A_payload_written_under_the_previous_version_is_refetched_and_rewritten and Platforms_survive_the_cache_round_trip.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The shared IGDB games query now asks for platforms.name, so an id-matched row in the wrong-game search draws the same facts as a title-search row instead of a year and nothing else. IgdbGame gained Platforms as an init property (positional-constructor compatible, the rule GameModes was added under), IgdbGameDto and IgdbSearchGameDto now shape platform names through one shared helper so the two row kinds cannot disagree, and GetByIdAsync still rides GetGamesAsync — no third query. GamePayloadVersion went 2 to 3, which is what makes cached rows refetch rather than answer with the new field silently empty for the rest of the 30-day TTL.

Bumping exposed a trap and it is fixed here: the superseded-payload fallback that keeps an offline install answering read only the bare pre-envelope shape, so every version-2 envelope on a current disk would have been dropped rather than served stale. It now reads an older envelope first.

The one-time refetch cost was measured, not estimated, by driving the real client through the real 4 req/s limiter with 967 ids: 3 requests and 158 ms asked for in one call (batches of 400/400/167, inside the 4-permit bucket so the limiter adds nothing), 25 requests and 6 seconds asked for in 40-target slices. Field names came from the shipped SearchGames body and the pinned fixture, not memory; api-docs.igdb.com returned 403 and that block was not worked around.

All prose — comments, XML doc comments, game-library-design.md 4.4, design-system.md 10.9 and the docs/decisions.md entry quoting the four superseded passages — was authored by the docs-writer subagent. Verified by a clean build (0 warnings) and 3201/152/70 passing across the three test projects.
<!-- SECTION:FINAL_SUMMARY:END -->
