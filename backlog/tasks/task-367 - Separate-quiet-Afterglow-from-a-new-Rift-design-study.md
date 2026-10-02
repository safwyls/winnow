---
id: TASK-367
title: Separate quiet Afterglow from a new Rift design study
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 19:34'
updated_date: '2026-09-28 19:51'
labels: []
dependencies: []
type: feature
ordinal: 403000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Floating materials and hero portals have outgrown the quiet editorial identity of Afterglow. Preserve effects separately, restore Afterglow before the effects, and present a distinct design for review.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Afterglow uses quiet portrait cards and hover/focus captions on desktop and fullscreen without materials, tilt or portals; reusable effects and functional improvements remain available.
- [x] #2 Rift has independent Discover and Library screens, preserved card and hero portal interactions and functional customization.
- [x] #3 Both designs open separately; desktop, fullscreen, keyboard, constrained layouts and reduced motion are checked and documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Preserve effect modules in a Rift study. Restore the pre-effect Afterglow mock from d748cb6 and equivalent app presentation without reverting API/cache improvements. Build independent Rift layouts around existing effects. Verify app and browser surfaces, capture and document.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Split complete. Afterglow app opts out of effects and uses overlay captions; optional shared preview API preserves existing defaults and Catalogue behavior. Restored standalone Afterglow from d748cb6 while retaining filled frames and eight returning games. New Rift mock has an independent rail/dock, featured-card stage, shelves, collection, palettes and shared still-mode control; effect modules forked from 66dbf61. App: typecheck/build and 199 tests pass, 8 existing integration tests skipped. Browser: app desktop/fullscreen at 1280x720 and 680x620; Rift desktop/fullscreen, 760x560, 390x700, long-title portals, search/filter/records/dialogs, palette/still/shape/reset, keyboard hero navigation. Fixed short-window row sizing and focus-induced hero scrolling. JS syntax, unique IDs, local asset references and whitespace checks pass. Captures and README saved for both designs. No real library used; Rift is a review mock rather than an installed theme.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored quiet Afterglow while retaining shared effects, cache improvements and material preferences. Created a separate Rift UI study designed around luminous floating cards and artwork portals. Verified app tests/build and browser behavior across desktop, fullscreen and narrow windows; both previews remain independently available.
<!-- SECTION:FINAL_SUMMARY:END -->
