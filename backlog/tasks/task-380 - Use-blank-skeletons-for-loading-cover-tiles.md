---
id: TASK-380
title: Use blank skeletons for loading cover tiles
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 02:59'
updated_date: '2026-09-29 03:01'
labels: []
dependencies: []
type: enhancement
ordinal: 416000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user wants quiet blank cover tiles instead of per-tile loading rings. They also confirm the TASK-379 build still flickers, which must remain recorded as unresolved.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Loading cover tiles are blank, static skeletons in desktop and fullscreen; hero loading and missing-artwork states remain distinct.
- [x] #2 Verify loading transitions and package the change for the user; record the unsuccessful flicker comparison.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use the shared Artwork component to select a blank cover skeleton or the existing hero indicator, remove the obsolete Rift-only loading override, verify both surfaces and package a separate build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared cover Artwork now renders an empty static art-loading element in the existing tile geometry; only hero images add the ring/shimmer class. Removed the Rift-specific animation override. Added an art=loading option to the existing local visual fixture. Desktop and fullscreen Rift checks each observed 12 empty tiles with no child indicators, background image, pseudo-element shimmer or animation; Discover retained one hero indicator. 24 artwork/app tests, typecheck and production build passed. rift-skeleton package matches all 52 frontend output files. Recorded the user-confirmed continuing flicker against TASK-379; no graphics defaults changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced cover loading indicators with blank themed skeleton tiles across desktop/fullscreen and designs. Verified pending-artwork visuals, existing loading/fallback tests, typecheck and build, and packaged a separate executable. Physical flicker remains unresolved.
<!-- SECTION:FINAL_SUMMARY:END -->
