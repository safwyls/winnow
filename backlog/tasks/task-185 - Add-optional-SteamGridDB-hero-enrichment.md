---
id: TASK-185
title: Add optional SteamGridDB hero enrichment
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 02:46'
updated_date: '2026-09-11 03:04'
labels: []
dependencies: []
references:
  - 'https://www.steamgriddb.com/api/v2'
ordinal: 216000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add SteamGridDB hero artwork as an optional enrichment source to improve missing or unsuitable backdrops. Use exact known Steam app IDs and preserve saved user artwork.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen settings accept a user API key through protected storage, never redisplay or log it, and queue refresh after changes.
- [x] #2 A rate-limited cached client retrieves suitable static heroes by exact Steam app ID, soft-fails, and retains usable cached data offline.
- [x] #3 Background sync persists source metadata and both backdrop surfaces use it, including confirmed linked game groups, without changing identity or saved artwork.
- [x] #4 Artwork downloads use isolated bounded cache keys and safe SteamGridDB CDN URLs; existing fallbacks and aspect behavior remain.
- [x] #5 Canned-response, settings, sync and presentation tests pass; documentation describes setup, ordering and scope.
- [x] #6 A dedicated Metadata and artwork settings tab on desktop and fullscreen groups provider credentials and persists a user-adjustable automatic backdrop source order.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Create SteamGridDB enrichment client, API-key store and Polly HTTP policies over existing metadata cache. Add optional hero URL metadata and isolated CDN source. Add shared credential settings with desktop/fullscreen editors. Sync known Steam targets after startup and credential changes; use confirmed group roots. Rank SteamGridDB candidates after high Steam heroes and before IGDB. Verify failure/cache/credential/lifecycle behavior and update docs.

User requested a dedicated Metadata and artwork tab and source preferences. Move IGDB and SteamGridDB credential editing there, preserving onboarding access. Persist order among high-resolution Steam heroes, SteamGridDB and IGDB; saved backgrounds stay first and standard Steam heroes remain the last landscape fallback. Apply preference changes to both surfaces without restart.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a dedicated Metadata & artwork tab on desktop and fullscreen with shared IGDB and protected SteamGridDB credential editors. Source priority persists and updates both backdrop presentations live. Saved backgrounds retain priority; standard Steam heroes and covers remain fallbacks. SteamGridDB sync uses exact Steam app IDs, observes original works and shares artwork only through current confirmed group members. Cached API responses, bounded CDN downloads and source-specific keys preserve offline/failure behavior. Standard and ultrawide hero layout policies apply to both hero providers. Updated architecture, visual spec, README and decision history. Verification: root scratch build passed with zero warnings/errors; root test suite passed 4,557 tests, with 2 Linux-only tests skipped on Windows. Headless UI tests exercised both settings surfaces, masked credential editing, navigation, ordering, live backdrop updates and disposal. Tests use temporary databases and canned API responses; authenticated live fetching requires a user key and was not exercised. Personal library data was not modified.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added optional cached SteamGridDB hero enrichment plus dedicated desktop/fullscreen Metadata & artwork settings for IGDB and SteamGridDB credentials and live backdrop source priority. Verified with a clean root build and 4,557 passing tests; 2 Linux-only tests skipped on Windows.
<!-- SECTION:FINAL_SUMMARY:END -->
