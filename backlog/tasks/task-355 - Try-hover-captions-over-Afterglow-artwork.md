---
id: TASK-355
title: Try hover captions over Afterglow artwork
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 16:44'
updated_date: '2026-09-27 16:49'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 391000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants the reviewed portrait mock to show captions over the artwork on hover, removing the text area beneath covers.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Discover shelf and library grid captions overlay the art on hover and keyboard focus without changing card dimensions; long titles stay readable.
- [x] #2 Desktop and fullscreen previews are checked, display controls remain usable, and the updated mock is presented for review.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Overlay grid captions with a readable gradient and focus/touch fallbacks while keeping records readable. 2. Update mock copy and evidence. 3. Check idle, hover and fullscreen focus states in the browser, then leave the revised mock open.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Changed only the standalone mock: absolute captions reveal over a dark gradient on hover/focus, full long titles wrap, idle grid has no text area, touch fallback remains visible, and records retain their labels. Visually checked hover and Discover focus. Desktop idle card and image heights both measured 291px; fullscreen long-title overlay fit within a 326px card without overflow. Caption options and record layout verified through browser controls; console clean. Updated README and two captures; mock left open for review.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered hover/focus caption overlays for the Afterglow mock, with full-title wrapping and no layout shift. Verified desktop grid, Discover shelf, fullscreen keyboard focus and display options. Application and artwork transport remain unchanged.
<!-- SECTION:FINAL_SUMMARY:END -->
