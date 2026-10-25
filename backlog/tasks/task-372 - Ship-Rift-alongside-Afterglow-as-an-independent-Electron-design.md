---
id: TASK-372
title: Ship Rift alongside Afterglow as an independent Electron design
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 21:49'
updated_date: '2026-09-28 22:22'
labels: []
dependencies: []
type: feature
ordinal: 408000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the approved Rift mock as a selectable production Electron design using the existing local backend, with quiet Afterglow retained as an alternative.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Theme Studio selects and persists Rift or Afterglow with distinct layouts and suitable defaults.
- [x] #2 Rift Discover and dense Library use real API games, cached artwork, filters and existing management actions.
- [x] #3 Reusable card materials and hero-art portals support adjacent cover previews and full-view details expansion, with live shape and activity controls.
- [x] #4 Both desktop and fullscreen retain complete shared details, journal and settings functionality, keyboard navigation, reduced motion, focus and scroll restoration.
- [x] #5 Appropriate tests, production build and isolated visual checks pass; documentation records functionality and limitations.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect the existing theme and API seams; port reusable portal effects; implement independent Rift screens; integrate theme selection and navigation; verify both designs and surfaces with tests and isolated app data; document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented separate Rift shell, Discover deck, virtualized Library and expanding details journey with shared backend screens. Added reusable PortalSurface to theme API 1, per-design appearance snapshots and authored first-selection defaults. Preserved Afterglow and cached Artwork; fixed Pixi shader precision and input bind-group disposal plus shared Details key collisions. Desktop and fullscreen verified by component tests and production-renderer fixture interaction: narrow/wide layouts, keyboard preview/focus, portal expansion, per-mode return state, internal scrolling, customization, reduced-motion lifecycle, missing art, empty search, management, journal and settings. A 2,000-game fixture mounted 50-80 cards while scrolling. Full suite against throwaway seeded backend: 26 suites passed, 236 tests passed, two fixture-dependent skips. Production build, Prettier and diff checks passed. Evidence and captures: docs/spikes/2026-09-28-rift-integration/README.md. Physical controller/TV/low-end GPU measurements and packaging are not claimed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Shipped Rift alongside quiet Afterglow in Theme Studio, with independent saved appearances, dense galleries, reusable card/portal effects and full details navigation. Verified both presentation modes through automated tests, a throwaway live backend and production-renderer browser checks; build passed. See the integration evidence for screenshots and device-validation limits.
<!-- SECTION:FINAL_SUMMARY:END -->
