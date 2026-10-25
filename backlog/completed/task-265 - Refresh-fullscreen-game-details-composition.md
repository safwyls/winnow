---
id: TASK-265
title: Refresh fullscreen game details composition
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 19:47'
updated_date: '2026-09-13 19:55'
labels: []
dependencies: []
ordinal: 307000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the approved cinematic fullscreen details mockup: prominent Play, tighter hero, compact personal history with real journal preview, wider About and larger screenshots. Preserve controller access and large-text usability. Desktop retains its existing composition.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Fullscreen details reflects approved composition using real metadata and journal content, with graceful missing-data states.
- [x] #2 Controller navigation, selected section, readable focus and large-text layout remain usable; relevant tests and rendered captures verified.
- [x] #3 Update visual specification and record separate fullscreen and desktop assessment.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Implement details composition; adjust only cinematic backdrop veil; update existing tests and add targeted real-data coverage; render and inspect representative detail views, run relevant Release UI tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented fullscreen-only composition: 80px title (72px long), compact hero, rounded filled primary action using Volt/VoltInk, active tab underline separated from focused-tab background, 30/70 overview, real hours/idle/date and optional latest saved note, heading-aligned Read more/gallery actions, larger 240px screenshot frames. Unknown hours and absent notes produce no filler. Cinematic horizontal/top veils reveal more artwork; shared artwork source/fade behavior unchanged. Overview scrolls for long titles or enlarged text, with explicit controller focus groups and focused previews brought into view. Desktop details code and shared data operations unchanged. Validation: Release UI build and 47 focused details/backdrop/interaction tests passed; git diff --check passed. Inspected headless rendered 1080p normal, missing-history, journal and long-title captures plus 720p at 140% text in C:\Temp\winnow-details-redesign. Captures use synthetic artwork and temporary fixture data; no physical-controller or live-library run performed. Header-aligned links and compact note match the approved density while fitting the normal overview without a fold.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented approved fullscreen details redesign with real history/journal content and larger art. Verified 47 UI tests and rendered normal/large-text layouts; desktop presentation unchanged.
<!-- SECTION:FINAL_SUMMARY:END -->
