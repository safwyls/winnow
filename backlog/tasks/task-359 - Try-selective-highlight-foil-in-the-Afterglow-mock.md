---
id: TASK-359
title: Try selective highlight foil in the Afterglow mock
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 18:20'
updated_date: '2026-09-27 18:28'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 395000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants foil on white or light cover areas as a practical alternative to isolating printed lettering. Extend the existing review mock without per-cover masks or remote artwork requests.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Light cover pixels receive a soft selective metallic reflection that follows the existing cursor and card interaction while dark areas retain the surface finish.
- [x] #2 Users can adjust highlight selection and strength, choose silver, gold or holographic material, and disable highlight foil independently of the existing finish.
- [x] #3 Desktop and fullscreen controls, keyboard stationary lighting, cleanup and narrow layouts are verified and documented with visual evidence.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Extend the current Pixi fragment filter with a smooth luminance mask and metallic response using its existing texture; add independent highlight controls; visually inspect contrasting covers and verify both surfaces plus fallback states.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a feathered brightness mask to the existing Pixi shader, with darker metallic reflections and moving glints in silver, gold and holographic materials. The mask uses the original print and adds no textures or artwork requests. Highlight toggle, strength and cutoff are independent of surface finish; defaults are silver, 65% strength and 72% cutoff. Visually checked silver/holographic Hollow Knight and gold Hades, including 95% versus 40% cutoff. Verified zero strength, disabling highlight controls, surface Off with highlights active, reset, desktop/fullscreen keyboard behavior, dialog cleanup, loading placeholders and records. At 760x560 the settings panel and both layouts had no horizontal overflow; fullscreen footer stayed visible. JavaScript syntax and Git whitespace checks passed; browser reported no warnings/errors. Saved 12-highlight-foil.png and documented behavior in the mock README. OS reduced-motion switching and physical touch were not exercised. Production frontends are unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added adjustable foil to light artwork pixels in the Afterglow mock. Silver, gold and holographic highlights reuse the existing shader and loaded cover. Verified wide/narrow desktop and fullscreen controls, focus and cleanup, and saved visual review evidence.
<!-- SECTION:FINAL_SUMMARY:END -->
