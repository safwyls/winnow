---
id: TASK-181
title: Choose high-quality landscape artwork for game backdrops
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 23:38'
updated_date: '2026-09-10 23:48'
labels: []
dependencies: []
ordinal: 212000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Prefer suitable IGDB artwork over screenshots while preserving user background choices and rank source images by usable resolution after cropping.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Image metadata survives enrichment and storage with compatible cache refresh and migration behavior.
- [x] #2 Shared selection preserves user art and ranks suitable artwork before screenshots with deterministic fallbacks.
- [x] #3 Desktop and fullscreen large-image behavior has explicit verification coverage.
- [x] #4 Tests, migration checks and governing documentation are updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Retain image metadata through IGDB and storage and refresh stale payloads. 2. Add shared crop-aware selection and integrate presentation paths without changing galleries. 3. Test persistence, selection and UI behavior; update specs.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented source dimensions, transparency, animation and image type through IGDB payload v5 and migration 0030. Existing enriched libraries refresh through ReceptionSyncService; legacy offline IDs preserve known metadata. Shared ranking prioritizes HD artwork then HD screenshots, with unknown and smaller landscapes as fallbacks. Saved backgrounds remain first. Failed downloads advance to the next candidate. Desktop detail art now has its own display-sized lease; fullscreen keeps crossfade and stale-completion protection. Both use source proportions for decode sizing. Galleries keep screenshot order.

Verification: root dotnet build with BaseOutputPath=C:\Temp\winnow-backdrops-181 succeeded with zero warnings/errors. Root dotnet test passed 4463 tests: Winnow.Tests 3915, UI 276, Covers 112, Recommend 160; 2 Linux-only monitor tests skipped on Windows. UI tests cover desktop portrait separation, saved-background fallback, artwork-to-screenshot failures, stale/disposed leases, crossfade and wide-image sizing on both surfaces. All 30 hashes passed Verify-Migrations -BaselineRef HEAD; Test-MigrationHashes mutation checks passed. git diff --check clean. Canned HTTP/temp-database tests only; live library artwork coverage was not measured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Desktop and fullscreen now prefer suitable high-resolution IGDB artwork, rank detail after cropping, retain user backgrounds, and fall back through screenshots to covers. Image metadata refresh and migration preserve older offline libraries. Build, 4463 tests and migration integrity checks passed; Linux-only tests skipped on Windows.
<!-- SECTION:FINAL_SUMMARY:END -->
