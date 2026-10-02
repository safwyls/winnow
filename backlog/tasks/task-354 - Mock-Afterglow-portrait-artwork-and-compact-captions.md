---
id: TASK-354
title: Mock Afterglow portrait artwork and compact captions
status: Done
assignee:
  - '@codex'
created_date: '2026-09-27 16:08'
updated_date: '2026-09-27 16:23'
labels:
  - frontend
  - design
dependencies: []
type: spike
ordinal: 390000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Landscape cover crops and oversized caption areas weaken Afterglow. The user approved a design review before production changes, preserving the Discover hero and exploring portrait covers, compact metadata, and customization.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An interactive mock preserves the Discover hero and demonstrates portrait shelves, library grid and compact records with representative long and short titles.
- [x] #2 Desktop and fullscreen previews include mismatched artwork, missing artwork and loading states, with adjustable cover size and metadata density.
- [x] #3 The mock is visually checked at wide and narrow sizes, documented as a proposal, and presented for review without changing application or artwork delivery behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reuse Afterglow visual tokens, fonts, hero composition and local artwork copies. 2. Build a standalone interactive HTML mock with Discover, Library and appearance controls. 3. Check layouts, keyboard interaction and image fitting in the browser at desktop, constrained and fullscreen sizes. 4. Record evidence and present the mock; performance implementation stays outside this task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Created the standalone mock with copied cached Steam/IGDB cover files and bundled Afterglow fonts. Desktop at 1440x900 shows five aligned Borderlands cards; 2:3 frames contain 3:4 covers without cropping. Search and unplayed filters, full-title preview, record view, size/caption/palette controls, loading and missing states, and fullscreen arrow navigation have been exercised. Production frontend and image delivery remain unchanged.

Completed visual checks at 1440x900 and 760x560 on desktop and fullscreen compositions. No horizontal overflow; footer stays in the viewport. Browser console recorded no errors or warnings. Reset temporary browser viewport and left the interactive library mock open as the review deliverable. Six screenshots and an asset/verification README are included. The static preview server uses port 8772 and has no backend connection.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered a standalone Afterglow design mock for review: intact portrait artwork, compact captions, preserved Discover hero composition, portrait returning thumbnails, grid/records views, and cover-size/caption/palette controls. Verified filters, long-title inspection, hero selection, mixed/missing/loading art, fullscreen arrow navigation, constrained viewport behavior and JavaScript syntax. Production frontend and artwork transport are unchanged; user design review is the next step.
<!-- SECTION:FINAL_SUMMARY:END -->
