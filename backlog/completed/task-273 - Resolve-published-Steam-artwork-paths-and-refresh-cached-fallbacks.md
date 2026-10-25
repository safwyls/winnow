---
id: TASK-273
title: Resolve published Steam artwork paths and refresh cached fallbacks
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 22:12'
updated_date: '2026-09-13 22:24'
labels: []
dependencies: []
ordinal: 315000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Use published library asset metadata to recover Steam portraits and heroes missed by legacy URLs, and safely upgrade cached portrait fallbacks without blocking cached display or replacing user art.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Published hashed capsule and hero paths resolve with legacy compatibility and safe path validation.
- [x] #2 Existing nonstandard Steam fallback covers can upgrade with bounded refresh and retain usable art on errors; manual art and IGDB pins are preserved.
- [x] #3 Relevant cache, metadata and shared UI checks pass; behavior and limitations documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reuse cached rate-limited appinfo through a narrow cover resolver interface; add shared Steam asset URL handling; implement bounded fallback refresh with provenance and failure retention; test and document both presentation surfaces.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Integrated published capsule/hero resolver with shared appinfo cache and bounded language/rendition candidates. Review found and fixed malformed metadata being treated as absence, standard rendition starvation, and image validation outside decode bounds. Background fallback upgrades are one active plus 16 queued keys, with persistent cooldowns and complete-image validation. Existing leases remain unchanged; new bytes appear after disk reload/eviction/restart. User/IGDB keys excluded. Metadata and cover-selection regressions pass (99 tests); final cover and UI verification running.

Final verification: Release build; 189 cover tests; 659 headless UI tests across desktop/fullscreen; 99 related update-metadata/cover-selection tests; final metadata refinement passed 32 focused tests including a new six-day cache hit/eight-day refresh test. Published image headers are checked before cache admission without adding full pixel decoding to fetch workers. Existing fallback refresh requires complete bounded image decode, retains old art on failures, and cancels on disposal. No live library or launcher files were changed during verification.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Hardened Steam capsule and hero lookup with published hashed paths, safe URL construction, bounded localized rendition fallback and seven-day shared metadata freshness. Added gradual cache upgrades preserving existing art and explicit user choices. Cover, metadata and desktop/fullscreen UI verification passed.
<!-- SECTION:FINAL_SUMMARY:END -->
