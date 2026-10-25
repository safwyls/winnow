---
id: TASK-285
title: Show game artwork icons in the Windows jump list
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 03:35'
updated_date: '2026-09-14 03:39'
labels: []
dependencies: []
ordinal: 327000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Recent-game taskbar entries need recognizable game artwork instead of repeating the Winnow icon.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Recent game links use locally cached Windows icons generated from game artwork with app-icon fallback.
- [x] #2 Icon generation stays off the UI thread, respects data isolation, and is verified with image and shell tests.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Generate multi-size PNG-backed ICO files from the shared cover pipeline on a background worker. Cache content-addressed icons under the active data root, publish after icon preparation with cancellation and fallback, and verify encoding plus native shell publishing.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added background icon generation through the existing cover pipeline: square center crops in 16/24/32/48/64/128 PNG-backed ICO frames. Content-addressed files live in the active data directory. Recent-game links preserve available cached icons on refresh, then publish updated art; missing art or timeout falls back to Winnow. Shared desktop/fullscreen process uses the same Windows jump list; non-Windows path remains guarded. App build: zero warnings/errors. Nine targeted tests passed including frame decoding, cache isolation/reuse, missing artwork, recent ordering, and native shell publication with an actual generated icon under an isolated identity. No live taskbar visual inspection performed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added per-game cover-art icons to Windows taskbar recent entries with local caching and app-icon fallback. Nine tests including native shell publication passed.
<!-- SECTION:FINAL_SUMMARY:END -->
