---
id: TASK-258
title: Soften fullscreen card entrances with a left-to-right fade
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 18:15'
updated_date: '2026-09-13 18:19'
labels: []
dependencies: []
ordinal: 300000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add a quick materializing entrance to fullscreen cards to soften abrupt shelf and library transitions. The requested scope is fullscreen; desktop presentation remains unchanged.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fullscreen card entrances progress left to right without delaying navigation or moving layout.
- [x] #2 Reduced motion displays cards immediately; leaving or rebuilding a page cancels old animation work.
- [x] #3 Document both surface outcomes and verify build and relevant UI behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add a fullscreen-only opacity entrance staggered by column, capped at 300ms. Keep button focus and hit targets immediate. Stop timers on detach and snap for reduced motion. Apply to Home, Library and search; document desktop as unchanged. Build and verify focused headless UI behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Fullscreen: Home, Library and Search fade art and labels over 180ms with a 24ms column stagger capped at 120ms. Cover border remains fully visible for focus. Per-card timers stop on detach and motion preference changes snap immediately. Desktop: no presentation edits, per fullscreen-only request; desktop/fullscreen recommendation composition and supplemental-feed parity tests included. Verification: solution build passed with zero warnings/errors; 199 focused UI tests passed (Fullscreen, RecommendationComposition, SupplementalFeed), including new completion, immediate focus, reduced-motion and detach checks. Inspected the 1920x1080 rendered fullscreen frame at C:/Temp/winnow-card-entrance-qa/fullscreen-entrance-False.png. Preview uses placeholder covers; real-art motion feel has not been manually assessed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a brief left-to-right fullscreen card fade that preserves immediate focus and respects Reduce motion. Documented fullscreen behavior and desktop scope. Build clean; 199 relevant UI tests pass; rendered placeholder layout inspected.
<!-- SECTION:FINAL_SUMMARY:END -->
