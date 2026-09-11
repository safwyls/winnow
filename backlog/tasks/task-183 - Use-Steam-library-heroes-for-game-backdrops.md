---
id: TASK-183
title: Use Steam library heroes for game backdrops
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 00:14'
updated_date: '2026-09-11 00:25'
labels: []
dependencies: []
ordinal: 214000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Use Steam library heroes to improve backdrops while preserving saved backgrounds and fallbacks.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both surfaces use known Steam IDs including grouped releases without IGDB.
- [x] #2 Independent hero cache keys support missing and failed request fallbacks.
- [x] #3 Fullscreen heroes preserve composition and fade into the page below; saved backgrounds remain first.
- [x] #4 Tests pass and both surfaces are documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add high and standard hero keys and source. Select saved art, high Steam hero, IGDB landscapes, standard hero, cover. Fit fullscreen Steam heroes uncropped and fade at the artwork edge. Verify source, selection and rendering.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented separate high and standard Steam hero keys using the existing named Polly HTTP client and per-request factory lifetime. Known Steam IDs are collected from grouped entries regardless of launch choice. Desktop retains crop-aware decoding; fullscreen fits whole heroes top-centered with the last 15 percent fading into Ground and independent full-canvas crossfade surfaces. No schema or personal-library mutation. Verified existing CDN hero paths with read-only HEAD requests. Root scratch build passed with zero warnings/errors. Core3917, Covers129 and UI279 tests passed, including grouped identities, saved backgrounds, negative-cache isolation, request recovery, desktop fallback chain, fullscreen geometry/fade, both transition directions and ultrawide resizing. UI/core TRX results at C:\Temp\winnow-steam-183-results. An initial UI build copy retry caused by a concurrently running testhost resolved successfully; all tests passed. No interactive production app run.

Follow-up: controller status now uses primary Text (#F0EDE7 in the default theme), matching the clock instead of grey TextDim. Applies to the shared fullscreen header across browse/detail pages and all connection states. Desktop has no corresponding controller header. Documented the visual rule. Rebuilt UI and passed 15 fullscreen detail tests plus 11 fullscreen interaction tests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Steam heroes now improve backdrops on both surfaces. Fullscreen preserves the whole hero composition and fades its lower edge into Ground. Saved backgrounds remain first; IGDB and standard heroes provide fallbacks. Verified build and 4325 tests across core, cover-cache and UI suites.
<!-- SECTION:FINAL_SUMMARY:END -->
