---
id: TASK-284
title: Share hover previews between feed and library covers
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 03:24'
updated_date: '2026-09-14 03:33'
labels: []
dependencies: []
ordinal: 326000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Library browsing should offer the same compact game preview as the feed without maintaining duplicate presentation or loading code.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Feed and desktop library covers use shared preview presentation and data loading.
- [x] #2 Immediate hover, exit dismissal, ratings, hero art, and window bounds work across both surfaces; recycling releases preview resources.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extract shared GamePreviewViewModel and GameHoverPreview controller. Reuse for feed and virtualized desktop library tiles; retain fullscreen hero. Verify hover, ratings, clipping and recycling.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Extracted GamePreviewViewModel for cancellable ratings and leased backdrop loading, GameHoverPreview for shared content/placement/hover dismissal/exclusivity, and GamePreviewBubble for the art-backed pointer shape. Desktop library tiles retarget/dispose on recycle and detach; feed uses the same components. Fullscreen retains its selected-game hero presentation. Full UI run: 697 passed with two failures (overly strict new hover request-count assertion and prior default-sort UI-thread read). Corrected both, then 56 focused UI tests passed covering library previews, feed previews, bubble pixels, details actions and startup threading. Preview metadata begins immediately; pointer re-entry during layout can issue another request. git diff check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added feed-style hover previews to desktop library covers through shared loading and popup components. Verified pointer interaction, ratings, bounds, lifecycle, and existing library actions with UI tests; fullscreen retains its hero preview.
<!-- SECTION:FINAL_SUMMARY:END -->
