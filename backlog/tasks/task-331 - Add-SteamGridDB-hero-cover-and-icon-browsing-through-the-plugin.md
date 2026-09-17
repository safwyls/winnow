---
id: TASK-331
title: Add SteamGridDB hero cover and icon browsing through the plugin
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 16:11'
updated_date: '2026-09-17 16:50'
labels: []
dependencies:
  - TASK-329
documentation:
  - doc-1
type: feature
ordinal: 373000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The bundled plugin currently supplies automatic static hero candidates only. It should contribute community artwork to the common browser so users can choose an image rather than relying on resolution ranking.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An enabled configured SteamGridDB plugin appears as a source for Hero, Cover and Icon, with paged candidates and creator/source links where supplied.
- [x] #2 Known external identifiers resolve games reliably; any fallback artwork search requires explicit selection and does not alter library identity.
- [x] #3 Existing credential setup, rate limits, bounded downloads, content filtering and cached fallback remain effective; missing credentials and failures have actionable browser states.
- [x] #4 Selected images persist independently of subsequent provider refreshes or disablement; existing automatic hero enrichment remains compatible.
- [x] #5 Standalone plugin contract tests and host integration tests cover all three slots, paging and failures; desktop and fullscreen browser behavior is verified and plugin documentation updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Extend the bundled provider with the optional stable SDK browser capability, independent of automatic hero enrichment. 2. Use documented static hero/grid/icon endpoints, validated canonical URLs, bounded paged responses, credential setup state and cache. 3. Cover paging, attribution, all kinds and failures in standalone plugin tests. 4. Verify through the common desktop/fullscreen browser after host integration.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Standalone SteamGridDB suite passed91; host integration passed inside main4960. Covers exact Steam matching, all three slots, paging, attribution, setup retry, failure/cache behavior, malformed host filtering, and retained originals after key removal/disablement. Shared desktop/fullscreen interactions verified with provider-independent fixtures and headless controller/keyboard tests; live authenticated SteamGridDB account calls were not used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Bundled SteamGridDB now supplies static paged heroes, portrait covers and PNG icons to the common browser, preserving automatic heroes. Exact IDs, bounded validation, attribution, credentials/rate limits and offline cache remain intact. Verified91 provider tests, packaged-host integration and common desktop/fullscreen UI suite; full solution6631passed. No supported collection enumeration is available.
<!-- SECTION:FINAL_SUMMARY:END -->
