---
id: TASK-364
title: Refine portal reveal and expose shape controls
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 17:23'
updated_date: '2026-09-28 17:36'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 400000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Refine the standalone Afterglow portal mock so the details are a fixed reading plane revealed by the growing aperture, without transient or settled scrolling. Add adjustable contour controls for user review.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Portal details stay at a fixed size during opening with no scrollbar; the aperture reveals stationary content.
- [x] #2 Display options expose roundness and edge waviness sliders with visible values and reset defaults.
- [x] #3 Desktop and fullscreen, reduced motion, narrow windows and graphics fallback retain legible details and correct lifecycle behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Replace surface scaling with a shared aperture mask for the shader and fixed HTML. Add two shape controls and a small outline preview. Verify both mock surfaces, constrained heights, slider extremes, still mode and fallback; document evidence and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a fixed HTML reading plane revealed by a shared superellipse/ripple aperture in CSS and Pixi. Added Roundness and Edge waviness controls with outline preview and restore defaults. Desktop uses 560 px and fullscreen 620 px, capped before opening; narrow previews use compact typography and whole-line description excerpts. Simple preview keeps its earlier scrolling behavior.

Verification: browser observed identical 560 px panel/body height during and after the live entrance, no text transform, overflow clip and zero scrollable portal descendants. Exercised desktop Library and Discover, fullscreen keyboard navigation, long titles, slider endpoints/reset, Still and Simple modes, 760x560 fullscreen and 390x700 desktop/fullscreen; viewport reset. No browser warnings/errors. Node/JSDOM verified pointer delay, live reduced-motion change, Escape/ARIA cleanup, fallback reveal completion and stopped animation frames; analytic contour comparison covered nine shape combinations and four times with error below 1.3e-13. JavaScript syntax and Git whitespace passed. Screenshots 15 and 16 and README record the evidence. Mock only; physical OS preference switching, touch, screen-reader output and GPU timing not measured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The Afterglow mock now reveals stationary, non-scrolling details through a growing portal, with roundness and edge waviness sliders. Browser checks across desktop, fullscreen and constrained viewports plus DOM/geometry checks verified layout, controls, reduced-motion handling and fallback lifecycle. Ready for visual review.
<!-- SECTION:FINAL_SUMMARY:END -->
