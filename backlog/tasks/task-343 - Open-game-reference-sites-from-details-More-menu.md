---
id: TASK-343
title: Open game reference sites from details More menu
status: Done
assignee:
  - codex
created_date: '2026-09-19 16:48'
updated_date: '2026-09-19 18:20'
labels: []
dependencies: []
type: feature
ordinal: 376000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Game details should offer quick access to the corresponding IGDB, SteamDB and SteamGridDB pages using the existing link destination preference.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen details More menus expose IGDB, SteamDB and SteamGridDB links when their target is available.
- [x] #2 Links use known game identities or explicitly labelled search fallbacks and follow existing link routing preferences.
- [x] #3 Focused tests exercise both menus and identity edge cases; documentation describes available links.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extend the shared details link list so both existing More menus expose reference sites through the current GameLinkRouter. Use verified identity-based web routes and hide unavailable targets; if a site lacks an ID route, use an explicitly labelled title search. Add focused menu-routing and identity-validation tests, and update the visual specification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified SteamDB app route, SteamGridDB Steam-ID redirect, and IGDB base-36 short route (decimal /games/ IDs are slugs and can open the wrong game). Use known IDs only; no search fallback or enrichment/network dependency. Both menus already render the shared details Links collection.

Shared details Links now adds View on IGDB using positive canonical work ID encoded as base36, plus SteamDB and SteamGridDB using the first validated Steam app ID among grouped copies. Existing desktop and fullscreen menu builders and GameLinkRouter handle display and opening. Missing identities omit links. Verified 94 GameDetailsViewModelTests/GameLinkRouterTests and 29 LinkDestinationTests/GameDetailsTabInteractionTests, including six actual menu activation cases; builds succeeded. Updated existing exact link-list expectation. No schema or credentials required. Full suite and physical-controller validation not run.

Full-suite verification exposed reference links suppressing missing-launch explanations. The shared details model now uses only primary/store actions when deciding whether to show that explanation; reference pages remain available in More on both surfaces. Updated TileActions regression expectations and visual documentation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added IGDB, SteamDB and SteamGridDB reference pages to details More on desktop and fullscreen using known identities and saved link preferences. All 123 focused tests passed; visual documentation updated.
<!-- SECTION:FINAL_SUMMARY:END -->
