---
id: TASK-180
title: Create IGDB exploration collection for Yaak
status: Done
assignee: []
created_date: '2026-09-10 22:37'
updated_date: '2026-09-10 22:51'
labels: []
dependencies: []
modified_files:
  - docs/api/igdb-exploration.yaak.postman_collection.json
type: task
ordinal: 211000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Provide an importable API-request collection for exploring IGDB data, with safe credential placeholders and requests relevant to Winnow's enrichment research.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A Yaak-supported collection file imports into Yaak
- [x] #2 Collection includes Twitch application-token setup and uses variables rather than embedded secrets
- [x] #3 Collection includes documented exploratory requests for core IGDB game metadata
- [x] #4 Collection includes every IGDB endpoint currently called by Winnow, with representative request bodies matching production usage
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inventory runtime IGDB API and CDN calls from Winnow.Enrich.Igdb and Winnow.Covers.Igdb. 2. Add a Winnow production requests folder containing each distinct request shape and image rendition used by the app. 3. Validate JSON, variables, production-route coverage, and the relevant IGDB query tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Created docs/api/igdb-exploration.yaak.postman_collection.json as a Postman Collection v2.1 file, a format Yaak documents as importable. Validated it with PowerShell ConvertFrom-Json and checked that every {{variable}} reference is defined at collection scope. git diff --check passed for this artifact; an unrelated pre-existing edit remains in src/Winnow.App/Views/ApplicationSettingsView.axaml.

Runtime inventory: POST id.twitch.tv/oauth2/token; POST /v4/external_games; POST /v4/games with full metadata, age-rating legacy, age-rating fallback, title-search, and lifecycle bodies; unauthenticated GET images.igdb.com using t_cover_big_2x, t_screenshot_huge, and t_1080p_2x. Artworks and screenshots are fields expanded by /games, not separate API calls.

Added nine requests under '1. Winnow production requests': external-ID resolution; full metadata; primary and fallback age ratings; title search; lifecycle signals; and exact cover, screenshot, and fullscreen-backdrop CDN renditions. Validation parsed the collection, resolved all 16 variables, and asserted all runtime route families, query fragments, and size tokens. The IGDB-focused test filter passed 254/254 tests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Created and expanded the Yaak-importable IGDB collection. It now covers every runtime IGDB request shape used by Winnow, including /games artwork and screenshot expansion and the t_1080p_2x fullscreen backdrop fetch. Verified collection structure and route coverage with a PowerShell validator and passed 254 IGDB-focused tests.
<!-- SECTION:FINAL_SUMMARY:END -->
