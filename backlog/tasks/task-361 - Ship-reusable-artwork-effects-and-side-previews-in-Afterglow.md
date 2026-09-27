---
id: TASK-361
title: Ship reusable artwork effects and side previews in Afterglow
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 18:48'
updated_date: '2026-09-27 19:08'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-27-afterglow-artwork-mock/README.md
  - docs/electron-themes.md
type: feature
ordinal: 397000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user approved the portrait-card mock, Satin/Foil and selective highlight finishes, floating tilt and descriptive side flyouts, and wants them in the real Electron app with reusable modules for future themes.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Afterglow desktop and fullscreen use filled 2:3 artwork cards and side previews with actual backend descriptions and metadata; the returning shelf can show eight games and the Discover hero remains intact.
- [x] #2 Reusable typed artwork surface/effect and game preview components are exposed to themes; bounded persisted controls cover finish, intensity, highlight selection/material, pointer motion and card tilt without breaking existing theme packages or profiles.
- [x] #3 Effects reuse decoded artwork, limit active GPU work, respect reduced motion and keyboard input, recover gracefully without WebGL and clean up on virtualization, scrolling, theme switches and disposal.
- [x] #4 Appropriate component/lifecycle/profile tests, typecheck and production build pass; desktop/fullscreen visual checks use isolated data or an explicit fixture harness, and authoring documentation explains reuse and validation limits.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Define additive public component/options contracts; implement the lazy shared Pixi renderer and reusable artwork wrapper; integrate profile controls and compatibility validation; replace Afterglow card captions with positioned previews and portrait grids; improve artwork query reuse within existing backend contracts; verify components, theme reuse, real renderer surfaces and production build, then document.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented separate typed ArtworkEffects and GamePreview primitives plus composed GameCard; exposed them to every theme and demonstrated standalone reuse in Reading room. Added strict optional artwork profile settings and Studio controls; saved landscape/record layouts remain valid. Integrated portrait cards, eight returning titles, side previews and cached byte reuse. Desktop/fullscreen renderer fixture checks covered 1280x720, 760x560, 390x700, left navigation, 130% scale, reduced motion, actual Pixi rendering under no-unsafe-eval CSP, and Catalogue reuse. Fixed review findings for left-nav placement and pointer-focus suppression, plus keyboard effects after native scrolling. Evidence: docs/spikes/2026-09-27-afterglow-artwork-integration/README.md. Validation: 185 tests pass; 8 live-backend opt-in tests skipped; typecheck and production Electron build pass. Initial PNG/base64 transfer remains; no startup speedup or physical-controller validation claimed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Shipped the approved Afterglow portrait cards, materials, floating tilt and descriptive side previews as reusable theme components with persisted controls and artwork reuse. Verified with 185 tests, production build and desktop/fullscreen browser fixture checks; documented API composition, compatibility and validation limits.
<!-- SECTION:FINAL_SUMMARY:END -->
