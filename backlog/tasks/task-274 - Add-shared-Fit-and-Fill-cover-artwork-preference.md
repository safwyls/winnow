---
id: TASK-274
title: Add shared Fit and Fill cover artwork preference
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 22:36'
updated_date: '2026-09-13 22:44'
labels: []
dependencies: []
ordinal: 316000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Offer Fit (whole artwork with edge-color padding) and Fill (crop to card), defaulting to Fit while retaining stable 2:3 card geometry in desktop and fullscreen.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fit and Fill apply immediately to portrait covers on desktop and fullscreen without changing card bounds or hero/screenshot behavior.
- [x] #2 The setting is reachable in both surfaces, persists, defaults to Fit, and is covered by interaction/rendering tests.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add shared display preference and inherited cover presentation state; reuse edge padding on desktop; expose desktop Display and fullscreen Appearance controls; verify geometry, persistence, input and rendering.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared display.cover_art_mode preference (Fit default), desktop Display selector and fullscreen Appearance controller adjustment. Shared inherited presentation updates existing/new portrait images and padding without reacquiring leases; card bounds remain unchanged. Desktop grid/feed/list covers covered; fullscreen covers and settings covered. Heroes/screenshots retain their own presentation; fullscreen reset leaves shared preference intact. Validation: 663/663 UI tests; 5/5 preference tests; targeted selector checks 2/2 and final Appearance capture test 1/1. Inspected rendered fullscreen Appearance capture at C:\Temp\winnow-cover-fit-captures\fullscreen-cover-art-appearance.png. Updated visual/build specs and shared-setting help text.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added persistent Fit/Fill cover art in desktop Display and fullscreen Appearance. Fit preserves full artwork with edge-color padding; Fill crops within fixed card bounds. Verified persistence, live inheritance, padding pixels, controller/desktop interaction and lease stability; 663 UI and five preference tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
