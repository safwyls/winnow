---
id: TASK-266
title: Match fullscreen details to the approved mockup
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 20:09'
updated_date: '2026-09-13 20:25'
labels: []
dependencies: []
ordinal: 308000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Correct the previous implementation to closely match approved mock: complete uncropped screenshots with suitable resolution; faithful title, metric and status typography; compact left metrics; visible arrow links including journal without notes; horizontal and vertical dividers; Read more under description and gallery under images. User authorizes design system revision. Desktop remains separate.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Overview closely matches mock proportions, type hierarchy, dividers and link placements, including empty journal access.
- [x] #2 Screenshots preserve full source composition and use suitable thumbnail resolution across scaling.
- [x] #3 Rendered normal and scaled layouts and controller navigation verified; design specification updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use mock geometry and explicit detail typography; restore below-content arrow links and dividers; correct image framing/loading; compare representative rendered fixture to mock at normal/large text and user-like UI scale; run focused UI tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Corrected fullscreen typography and spacing against approved mock, grouped metrics with separators, restored arrow links and always-visible journal entry, and placed Read more/gallery below their content. Screenshot previews retain full source aspect and request their displayed physical resolution through independent leases. Adaptive sizing and deferred focus scrolling keep screenshots accessible at enlarged text. Desktop assessment: presentation and shared thumbnail request behavior unchanged; only a preview lease factory added to shared view model. Validation: Release build and 49 focused fullscreen details/backdrop/interaction tests passed; rendered fixtures inspected at 2560x1440 with UI scales 1.0 and 0.8, plus 1280x720 at 140% text and 120% UI. Pixel-edge assertions verify no cropping, lease assertions verify resolution, navigation tests verify links and focus visibility. No live-library or physical-controller run performed. Updated design-system.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Matched fullscreen details more closely to the approved mock: corrected font hierarchy, grouped history and journal with dividers, restored arrow links, and rendered full-frame screenshots at display resolution. Verified with 49 passing Release UI tests and rendered fixture comparisons, including enlarged-text navigation.
<!-- SECTION:FINAL_SUMMARY:END -->
