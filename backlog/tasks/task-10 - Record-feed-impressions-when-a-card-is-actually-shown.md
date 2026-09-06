---
id: TASK-10
title: Record feed impressions when a card is actually shown
status: Done
assignee:
  - '@steam-ingest'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 22:18'
labels:
  - recommend
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 1500
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Impressions are currently recorded at generation time, not when the card is visible to the user. This overstates impression counts and distorts the feedback signal. Finding F16. Source: stabilization-2026-08-28.md Group 2. Trigger: next feed presentation change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An impression is recorded only when the card enters the visible viewport
- [x] #2 A card generated but never scrolled into view records no impression
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Remove generation-time and swap-time impression writes. FeedView observes actual card intersection with the clipped scroll viewport only while feed/shelf/card and host window are visible and active. Forward visibility to FeedViewModel, which validates current generation and records each release once per UTC day across reloads. Keep repository day deduplication and service failure isolation. Add service and view-model regressions plus Avalonia headless viewport tests for offscreen cards, scroll entry, inactive/hidden feed/window, reloads and reserve replacement. Coordinate builds with parent; send changed specification text to parent.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed generation/backfill and reserve-promotion writes. FeedView checks clipped card rectangles, effective visibility, active/visible/non-minimized window and center-point modal occlusion (including disabled visible controls). Event-driven checks plus a 100 ms render-scene fallback; detached views stop the timer and invalidate pending callbacks. FeedViewModel validates current card generation and deduplicates release impressions per UTC day across reloads; the repository retains its existing day-level idempotence. Seven Avalonia headless tests passed for viewport scrolling, hidden/inactive/minimized state, history/reloads/new day, hidden reload, offscreen reserve promotion, modal occlusion, and reattachment. All 67 focused feed unit/repository tests passed. The TASK6 startup-thread headless test also passed during final combined run. Shared scoring-document correction supplied to coordinator.

Batch integration: Release solution build passed with zero warnings/errors. Full suite passed 3899 tests; two stale identity-inventory assertions were corrected, then all five inventory tests passed (3902 total current tests verified). Changes to the inventory scanner retain enforcement for SQL constants and bulk snapshot callers.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Feed impressions now originate from actual active-window viewport observations, not scoring or reserve promotion. Offscreen, hidden, inactive, minimized and modal-covered cards produce no impression; reloads deduplicate per release/day. Verified with 7 headless feed tests and 67 focused feed unit/repository tests.
<!-- SECTION:FINAL_SUMMARY:END -->
