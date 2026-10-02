---
id: TASK-357
title: Try Satin cover lighting in the Afterglow mock
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 17:10'
updated_date: '2026-09-27 17:18'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
type: spike
ordinal: 393000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The cursor portal feels separate from the artwork. The user approved trying a material response: soft cursor light and a restrained sheen integrated with caption reveal.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The mock defaults to Satin lighting over the existing cover art, preserving its crop, geometry and readable captions.
- [x] #2 Users can compare Matte, Satin and Foil, adjust intensity and disable the effect; keyboard focus and reduced motion have a static treatment.
- [x] #3 Desktop and fullscreen interactions, constrained layouts and renderer idle/cleanup behavior are checked and documented with review evidence.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Replace the portal shader with lighting sampled from the already loaded cover; add finish/intensity controls and coordinate hover timing; verify pointer, keyboard, controls and layout in both mock surfaces; save review evidence.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the portal with material lighting using decoded DOM cover images and centered-crop texture sampling. Satin 55% default; Matte/Foil/Off, intensity and stationary-light controls. Browser verified cursor response and settled state (one canvas, ticker stop), static keyboard/unchecked-follow mode, Foil at 100%, Off disabled controls, reset, fullscreen arrows and Enter, loading and mixed artwork, dialog/record cleanup, 2052x1272 and 760x560 layouts with pinned footer and no horizontal overflow. No new renderer warnings/errors. System reduced motion uses the same verified stationary path; OS preference and physical touch were not exercised. Source syntax and whitespace checks passed. Review screenshot: 10-satin-artwork.png.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Prototyped Satin lighting that follows the pointer over unchanged artwork, with Matte/Foil and intensity controls. Desktop/fullscreen browser checks covered input, controls, fallback artwork, layout and renderer settling/cleanup; syntax checks passed. Updated review documentation and capture.
<!-- SECTION:FINAL_SUMMARY:END -->
