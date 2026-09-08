---
id: TASK-163
title: Highlight desktop game tile borders on hover like the promo site
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 18:19'
updated_date: '2026-09-08 18:21'
labels: []
dependencies: []
ordinal: 195000
---

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Tiles show a theme-accent border on hover and keyboard action focus without layout changes or clipped edges
- [x] #2 Selection remains visible after pointer exit and recycled tiles clear hover styling
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reuse tile reveal state and existing inset selection ring treatment; verify pointer and keyboard behavior; update visual spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Compared website/app/globals.css demo-tile hover outline with desktop selection ring. Reused actions-visible state and theme Volt brush on an inset 2px ring. Build passed with zero warnings; all 24 CardDetailsInteractionTests passed, including hover exit, persistent selection, keyboard focus and recycled-container regression.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added promo-style accent border to desktop game tiles on hover and keyboard focus, preserving selected borders and tile geometry. Clean build and 24 passing UI interaction tests.
<!-- SECTION:FINAL_SUMMARY:END -->
