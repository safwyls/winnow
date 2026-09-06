---
id: TASK-112
title: Show ratings and reviews from IGDB and the storefront
status: In Progress
assignee:
  - '@enrichment-api'
created_date: '2026-09-05 02:50'
updated_date: '2026-09-06 00:27'
labels:
  - ui
  - enrichment
dependencies: []
priority: medium
type: feature
ordinal: 139000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal states no critical or player reception. IGDB carries aggregate ratings (its own user rating and an aggregated critic rating with counts); Steam carries review summaries. Surface what is available and be explicit about which source each number comes from — an unattributed score is worse than none.

Check what each source actually permits and provides before designing the display; do not assume a Steam review summary is available through the endpoints Winnow already uses.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Available ratings are shown in the details modal, each attributed to its source
- [ ] #2 The number of ratings behind a score is shown, so a 9 from four people does not read like a 9 from four thousand
- [ ] #3 A game with no rating data shows nothing rather than a zero or an empty scale
- [ ] #4 Fetching is rate-limited, cached and soft-failing
- [ ] #5 What each source provides, and what it does not, is recorded in docs/facet-provenance.md
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
ENRICHMENT HALF (data only).
1. IGDB: add rating,rating_count,aggregated_rating,aggregated_rating_count to the shared Apicalypse.Games query (same request, same payload-version bump as TASK-111). Names established from IGDB's published protobuf at https://api.igdb.com/v4/igdbapi.proto: Game.rating (double, 30), rating_count (int32, 31), aggregated_rating (double, 3), aggregated_rating_count (int32, 4). total_rating/total_rating_count are deliberately NOT used — the approved design wants the two IGDB figures kept apart and attributed, not blended.
2. Steam: add include_reviews:true to SteamStoreJson.BuildGetItemsQuery — one extra boolean on the keyless, 100-appid-batched IStoreBrowseService/GetItems call Winnow already makes. Names established from Valve's webui/common.proto (the same file the repo already cites for StoreItem_RelatedItems): StoreBrowseItemDataRequest.include_reviews (bool, 9); StoreItem.reviews (StoreItem_Reviews, 23) with summary_filtered/summary_unfiltered/summary_language_specific; StoreItem_Reviews_StoreReviewSummary { review_count u32, percent_positive i32, review_score i32, review_score_label string }. review_score_label is Steam's own words ('Very Positive'), which the approved design requires alongside the percentage and the count.
3. Persist: migration 0028 adds work_ratings(work_id, source, score, rating_count, label, observed_at, PK(work_id, source)), shaped on work_maturity. Sources igdb_users / igdb_critics / steam. No stored verdict, no blended average — the three figures stay apart because the design shows three attributed figures.
4. Absence is absence: a source with no figure writes no row, so AC#3 (nothing rather than a zero) is a property of the data, not of the view.
5. AC#5 — record BOTH sources in docs/facet-provenance.md: endpoint, field path, batch, cache, TTL, and what each source does NOT provide. Prose delegated to docs-writer.
6. Tests against canned fixtures only.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
DESIGN DECISIONS, 2026-09-05, from the combined details-modal design pass (mock at mock-details.html). Approved by the user:
- Band 4 order becomes: corrections, updates, ABOUT+screenshots, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS. This reverses the recorded reason at GameDetailsView.axaml:899 that ALSO COVERS leads because it is a fact about identity; the superseded sentence goes to docs/decisions.md.
- Ratings become a reception line in Band 1 under year and publisher, NOT a new section. Three figures, each attributed with its count: IGDB users, IGDB aggregated critics, Steam.
- Steam shows its own label ("Very Positive") with the percentage and count on hover.
- Acquisition facts: acquired_at and license_type in the left column under ON DISK. Price paid NEVER appears in this modal — section 7 never be smug; "$59.99 / never opened" is the sentence the product must not write. Price goes to export and account stats.
- Screenshots go inside ABOUT as a thumbnail strip that expands one shot to a hero above it, inline in the modal tree, no popup.
- Refetch is a More menu row with its status on a Band 3 TextBlock outside the scroll region.
- The update list is renamed so it stops colliding with the Band 2 rail; the rail keeps SINCE YOU PLAYED. Mock placeholder is "What landed" and a better name is welcome.
- TASK-115 ships BOTH halves in one pass: the release-to-today axis AND the backfilled monthly bars.
- The rule that governs future additions: a label section heading in Band 4 is earned by a list of rows the user can act on, ABOUT being the single prose exception. A fact about the game goes in Band 1; a fact about this copy goes in the left column; a picture goes inside ABOUT; an act goes in the More menu with its status on the strip.
<!-- SECTION:NOTES:END -->
