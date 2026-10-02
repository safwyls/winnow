---
id: TASK-267
title: Unify fullscreen details tabs with redesigned overview
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 21:10'
updated_date: '2026-09-13 21:15'
labels: []
dependencies: []
ordinal: 309000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Align Updates, Journal and Library with Overview typography, spacing, dividers and action links while preserving shared operations and accessible navigation. Scope is fullscreen presentation; assess desktop separately.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Updates, Journal and Library share the Overview visual hierarchy and handle empty and populated content.
- [x] #2 Existing actions, live list states and controller focus remain functional.
- [x] #3 Rendered tabs and focused UI tests verified; design specification and desktop assessment recorded.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Review tab compositions; apply shared detail text, section and action treatments; verify populated/empty layouts and navigation with headless renders; update visual spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Fullscreen: unified section labels, explicit BodyFont weights, metadata, wrapped action links and divider spacing. Structured update headline/date/unread rows and journal date/rating/note previews; grouped Library facts and maintained live list labels. Fixed long action labels overflowing by measuring arrow links in a constrained grid. Verification: 56 Release UI tests passed across FullscreenDetailsTests, FullscreenInteractionTests, DetailsRefreshParityTests, GameDetailsTabInteractionTests and JournalDetailsInteractionTests. New six-case populated/empty tests cover 2560x1440 normal and 1280x720 at 140% text/120% UI, controller row traversal, horizontal bounds, focus visibility and live membership toggles. Inspected rendered Updates, Journal and Library fixtures in C:/Temp/winnow-tabs-verify. Desktop: no presentation or shared operation changes; existing desktop tab and journal interaction and refresh parity tests passed. No live-library or physical-controller run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Unified fullscreen Updates, Journal and Library with Overview typography, metadata hierarchy, dividers and links. Preserved actions and live states. Verified with 56 passing Release UI tests and populated/empty rendered layouts at normal and enlarged text; updated design-system.md.
<!-- SECTION:FINAL_SUMMARY:END -->
