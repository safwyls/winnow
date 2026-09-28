---
id: TASK-366
title: Use game artwork inside the Afterglow details portal
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 17:54'
updated_date: '2026-09-28 18:04'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 402000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Replace the portal mock star field with artwork from the game being previewed, so the opening feels connected to that title while keeping readable details and the improved pacing.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Available hero artwork fills the portal behind legible details; titles without a hero use their own cover, with a neutral fallback for missing or failed artwork.
- [x] #2 Artwork is reused between hovers and late loads cannot show the wrong game.
- [x] #3 Desktop and fullscreen retain fixed non-scrolling details, shape controls, smooth reveal, reduced motion and graphics fallback.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Map existing local hero assets to the sample catalog. Replace procedural stars with a shaded image sample in the reusable portal surface, cache decoded images and textures, and preserve the CSS fallback. Verify both surfaces, game switching, missing artwork and still mode, then document and capture the mock.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented hero/landscape mapping, cover fallback, neutral missing/failed state and shaded fixed artwork in both WebGL and CSS. Browser checked desktop/fullscreen heroes, bright Sable and missing/cover fallback; fixed planes remain 560/620 px. Live desktop entrance measured 362.6 ms, zero scrollable descendants, 0.2/0.3 ms median/p95 submission over 390 draws. Node/JSDOM passed out-of-order decodes, cache reuse, failure, still/fallback and disposal checks. Shape/reduced-motion lifecycle is unchanged from TASK-365; keyboard stationary behavior checked, OS preference switching not repeated. Syntax and whitespace passed; screenshot 18-hero-portal.png and README record evidence. Mock only.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The details portal now reveals shaded artwork from the selected game, reusing local decoded images and private textures. Checked desktop/fullscreen visuals, live reveal, image-switch races, fallback and cleanup; saved capture 18. Production frontend unchanged.
<!-- SECTION:FINAL_SUMMARY:END -->
