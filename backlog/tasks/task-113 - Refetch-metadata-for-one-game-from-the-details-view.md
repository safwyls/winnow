---
id: TASK-113
title: Refetch metadata for one game from the details view
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
ordinal: 140000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Enrichment runs on its own schedule. When a game has stale, partial or missing metadata there is no way to ask for it again — the only recourse today is the wrong-game control, which is for a different problem (the resolution is wrong, rather than the data being thin). Add a manual refetch that re-asks the sources for this one game.

Note the interaction with TASK-89: a pinned work must refetch against its pinned IGDB id, not re-resolve. The pin says which game it is; the refetch says fetch it again.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The details modal offers a refetch for one game
- [ ] #2 A pinned work refetches against its pinned IGDB id and the pin survives
- [ ] #3 The control reports what happened — updated, nothing new, or could not reach the source
- [ ] #4 It respects the existing rate limits and cannot be used to hammer a source
- [ ] #5 Progress is stated in words, per the indeterminate-progress rule settled in TASK-79
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
ENRICHMENT HALF (the service; the More-menu row and status TextBlock are the UI half).
1. GameRefetchService in Winnow.App/Services, alongside EnrichmentSyncService (§5.1: the sync services live there because they need repositories Core cannot reference). Modelled on IgdbMaturitySync's soft-fail discipline.
2. RefetchAsync(workId) re-asks BOTH sources for one work, bypassing the 30-day/7-day TTLs by passing cacheTtl: TimeSpan.Zero — IgdbClient.Cutoff and SteamStoreClient.Cutoff both read a non-positive TTL as DateTime.MaxValue, i.e. 'nothing cached counts', which is the supported force-refetch path and not a cache bypass hack.
3. AC#2 — a pinned work refetches against its pinned IGDB id. IWorkIgdbPinRepository.GetAsync(workId) returns the pin; when present, go straight to GetGamesAsync(pinnedId) and never call ResolveByExternalIdsAsync. The pin is read, never written, so it survives.
4. AC#4 — two independent brakes. The Polly rate limiters on both typed clients still apply (IGDB 4 req/s, Steam 2 req/s), and a per-work cooldown refuses a second refetch of the same work inside RefetchOptions.Cooldown (default 5 min) with a distinct outcome the UI can word.
5. AC#3/#5 — GameRefetchOutcome { Updated, NothingNew, NotConfigured, Unreachable, Unresolved, TooSoon, WorkNotFound } with a GameRefetchResult carrying counts. Every outcome is a word, never a percentage: no total is knowable in advance.
6. Registration: one AddSingleton line in Program.cs's App-seams block. Reported rather than edited if it can be avoided.
7. Tests against canned fixtures only.
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
