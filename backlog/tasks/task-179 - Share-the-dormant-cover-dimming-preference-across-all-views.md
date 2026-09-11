---
id: TASK-179
title: Share the dormant-cover dimming preference across all views
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 21:11'
updated_date: '2026-09-10 21:17'
labels:
  - ui
dependencies: []
ordinal: 210000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Audit all cover views and make the dim dormant covers option a single persisted application preference. Merges currently bypasses the toggle and fullscreen stores an independent dimming option.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All dormancy-bearing cover views honor the shared preference, including live changes in Merges.
- [x] #2 Desktop and fullscreen controls share one persisted choice across mode switches and restart.
- [x] #3 Audit records each cover surface and preserves intentional non-dormancy artwork treatments.
- [x] #4 Focused behavior and UI tests plus build pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit all ramp/cover rendering paths. 2. Wire merge rows and leases to the shared ramp. 3. Remove fullscreen preference divergence while preserving its separate motion state. 4. Verify live toggle, persistence and both surfaces; update governing documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit: GameTileView and FeedCardView use tile DisplayAlpha; RowCoverView uses tile DormancyAlpha; fullscreen browse/feed/list covers use FullscreenCover backed by the tile ramp (focused and background art intentionally vivid). Merges alone called static Dormancy.VividAlphaFor and always leased both layers; now reads the shared injected ramp and uses CoverPresenter for live layer changes. Details, IGDB search, metadata previews, screenshots and lightboxes explicitly request vivid art; fixed-opacity fullscreen backdrops are decorative treatments, not dormancy. Fullscreen previously persisted fullscreen.dim-covers independently; now delegates to Shared.Display and mirrors only dimming into its separate ramp, preserving reduced motion. Program registers the shared desktop/merge ramp once. CoverPresenter now releases pending leases on close and requests the floor if dimming changes during initial decode.

Verification: dotnet build --no-restore passed with zero warnings/errors. Full dotnet test --no-restore passed: main 3903, UI 253, recommendation 160, covers 112 (4428 total); two Linux-only smoke tests skipped on Windows. Actual MergeQueueView images changed 0/1/0 opacity from the shared display toggle without reload. Fullscreen controller test verifies desktop-to-fullscreen and fullscreen-to-desktop changes and reset preserving the shared preference. Factory test verifies persistence/reload, legacy-key conflict and independent reduced motion. Merge tests verify saved false applies on initial/repeated load; presenter tests verify vivid-only decode, enabling dimming during initial decode, and immediate release of pending leases. git diff --check passed. Debug-only --dim-covers remains an explicit temporary screenshot override; normal application settings use the shared value. No production host or live library was used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Audited all cover surfaces and fixed the two divergent paths: Merges now uses the shared live dormancy ramp, and fullscreen uses the same persisted display.dim_dormant_covers setting as desktop. Removed independent fullscreen dimming behavior while preserving separate motion settings and intentional vivid/detail/backdrop treatments. Added live UI and persistence regressions, plus safe pending-cover cleanup. Full build and all 4428 applicable tests passed; Linux-only tests skipped on Windows.
<!-- SECTION:FINAL_SUMMARY:END -->
