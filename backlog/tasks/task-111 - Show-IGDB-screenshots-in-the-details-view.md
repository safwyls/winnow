---
id: TASK-111
title: Show IGDB screenshots in the details view
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
ordinal: 138000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The details modal carries no imagery beyond the cover. IGDB supplies screenshots per game; pull them and show them, so the modal says what the game looks like rather than only what its box looks like. Reuse the existing cover cache and disk-cache discipline rather than adding a second image path, and respect the same soft-failing, rate-limited rules the other IGDB calls follow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A game with IGDB screenshots shows them in the details modal
- [ ] #2 Images use the existing cover cache and disk cache, not a second image path
- [ ] #3 A game with no screenshots shows nothing rather than an empty frame
- [ ] #4 Fetching is rate-limited, cached and soft-failing like the other IGDB clients
- [ ] #5 The modal does not grow past the window, per the bounded-scroll rule set in TASK-105
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
ENRICHMENT HALF (data only; a separate change builds the UI).
1. Ask IGDB for the media. Add screenshots.image_id,screenshots.url,screenshots.width,screenshots.height and artworks.image_id,artworks.url,artworks.width,artworks.height to Apicalypse.Games — the ONE shared games query, no second request. Field names established from IGDB's own published protobuf schema at https://api.igdb.com/v4/igdbapi.proto (unauthenticated, no Client-ID/Bearer): Game.screenshots (repeated Screenshot, field 33) and Game.artworks (repeated Artwork, field 6); both messages carry image_id/url/width/height.
2. Bump IgdbClient.GamePayloadVersion 3 -> 4 IN THE SAME COMMIT (§4.4). Measure the resulting refetch cost rather than quoting TASK-121's figure.
3. Project onto IgdbGame.ScreenshotImageIds / ArtworkImageIds as init properties (never positional), so payloads cached under version 3 still deserialize and remain the fallback GetGamesAsync already serves when a refetch cannot happen.
4. Persist: migration 0028 adds work_images(work_id, source, kind, image_ids, observed_at, PK(work_id, source, kind)), shaped on work_maturity (0024) — one row per (work, source, kind), image ids comma-joined VERBATIM in IGDB's order, no derived value stored. IWorkImageRepository in Core, WorkImageRepository in Data.
5. Write it from EnrichmentSyncService's existing IGDB step — the /games response the publisher already needed, so no extra request.
6. Thread the size token (AC#2, the real blocker). IgdbCoverSource resolves the token from the key's provider instead of always using IgdbCoverOptions.ImageSizeToken: CoverProviders.Igdb -> ImageSizeToken (t_cover_big_2x, unchanged), CoverProviders.IgdbScreenshot ('igdb-shot') -> new ScreenshotSizeToken (t_screenshot_huge). Screenshots ride the SAME source, SAME pipeline, SAME disk cache — no second image path — and every existing igdb_/steam_ cache stem and token is byte-for-byte unchanged.
7. Tests against canned fixtures only, no live calls.
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
