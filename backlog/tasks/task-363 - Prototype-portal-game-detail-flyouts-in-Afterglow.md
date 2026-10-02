---
id: TASK-363
title: Prototype portal game-detail flyouts in Afterglow
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 17:03'
updated_date: '2026-09-28 17:17'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 399000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants to reuse the earlier amorphous cursor portal for the game-information popover: grow from the cursor toward the side, with a shifting edge and readable information over a star field. Iterate in the existing review mock so the visual treatment can be assessed before production integration.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hovering a cover opens a portal from the cursor beside the card, with a subtly shifting edge and a star field behind complete game details.
- [x] #2 The preview remains readable and hoverable, flips or docks at viewport edges, supports keyboard and Escape, and has stationary reduced-motion and graphics fallback behavior.
- [x] #3 Desktop and fullscreen mock layouts, narrow windows and lifecycle cleanup are verified; the review mock is presented with implementation scope documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Keep the existing flyout content and placement logic; add one reusable procedural Pixi portal surface and a cursor-origin entrance, with static fallback and motion controls; inspect both surfaces and input modes; record visual evidence and present the mock.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the first review pass: a separate reusable Pixi surface, cursor-origin entrance, irregular slow-moving aperture, procedural stars and stationary HTML text. Added Living portal, Still portal and Simple preview choices; live system reduced motion and keyboard focus use the still surface. Production UI remains unchanged pending visual review.

Verification: browser inspected live cursor-origin growth, drifting rim and stationary text, desktop Library and Discover shelf, fullscreen keyboard portals, right/left placement, complete long titles, 760x560 scrolling and Page Down, 390x700 docking without horizontal overflow, Still and Simple choices, Escape, dialog and navigation cleanup. Renderer detaches and reports stopped when dismissed; no browser warnings/errors. Node/JSDOM smoke verified hover delay and bridge, live system reduced-motion changes, Escape and aria-describedby cleanup. A simulated WebGL initialization failure preserved fallback and stop/detach behavior. Syntax and whitespace checks passed. Temporary viewport override reset. Saved 14-portal-preview.png with the settled Outer Wilds portal; mock server runs on loopback 8772. OS preference switching, physical touch, screen-reader output and GPU timing were not measured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Built a review-only portal flyout with cursor-origin growth, a subtly shifting rim, procedural stars and stationary accessible text. Kept the surface separate for theme reuse, added still/simple comparisons, and verified desktop/fullscreen layouts and lifecycle behavior. Screenshot and README present the result; production integration awaits design review.
<!-- SECTION:FINAL_SUMMARY:END -->
