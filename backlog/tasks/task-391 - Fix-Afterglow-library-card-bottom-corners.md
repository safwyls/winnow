---
id: TASK-391
title: Fix Afterglow library card bottom corners
status: Done
assignee:
  - '@codex'
created_date: '2026-10-09 18:57'
updated_date: '2026-10-09 19:05'
labels: []
dependencies: []
type: bug
ordinal: 469000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Afterglow library title captions leave visible arcs at the bottom corners of rounded cover cards. The supplied screenshot shows bright seams where artwork and caption layers meet the card boundary.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Afterglow library cards have clean, consistent rounded bottom corners when captions are visible, with artwork and text contained on desktop and fullscreen.
- [x] #2 Hover, keyboard focus, missing artwork and corner-size preferences remain usable; focused rendered verification and the frontend build pass.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce the corner seams in the production Afterglow Library renderer with a disposable fixture and inspect artwork/caption clipping.
2. Apply the smallest scoped style correction and verify desktop/fullscreen hover and focus at multiple corner sizes, including missing artwork.
3. Record visual evidence and relevant checks, then commit the fix.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the bright bottom arcs with a white test cover in the production Afterglow Library on desktop and fullscreen. Separate antialiased cover/caption clipping left artwork visible at the rounded boundary. Overlay captions now live inside the artwork surface, which composites both layers with an identity opacity filter before the outer card applies its single rounded edge. Inline captions and flyouts retain their layout; the button keeps its external keyboard focus outline.

Validation: npm run build passes (existing nonfatal Zod annotation warnings). Existing afterglow-cards and artwork-effects component suites pass all 30 tests. An isolated native Electron probe mounting the production Library passed 20 rendered states: desktop/fullscreen × radius 0, 18 and 32 × rest, hover and keyboard focus, plus missing artwork on both surfaces. Checked visible captions, focus-visible outlines and no renderer exceptions. Reviewed final bright-cover, maximum-radius focus and missing-artwork captures at .tmp/afterglow-corners-0AArRN. Temporary probe is .tmp/afterglow-corners-verify.cjs; initial comparisons are retained under .tmp/afterglow-corners-*. No user library was opened; all runs used explicit temporary data directories. No backend or unrelated full-suite rerun was needed for this presentation correction.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed Afterglow library bottom-corner seams by compositing the overlay caption with its cover before rounding the outer card. Verified desktop/fullscreen hover, keyboard focus, missing artwork and corner-size preferences in 20 native rendered states; 30 focused component tests and the frontend build pass.
<!-- SECTION:FINAL_SUMMARY:END -->
