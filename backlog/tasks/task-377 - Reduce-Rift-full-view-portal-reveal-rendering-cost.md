---
id: TASK-377
title: Reduce Rift full-view portal reveal rendering cost
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 01:51'
updated_date: '2026-09-29 01:57'
labels: []
dependencies: []
type: bug
ordinal: 413000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Disabling DirectComposition stops the reported RX 9070 XT and AW3423DWF fullscreen flicker, but the full-view details portal reveal remains choppy after the shuffle improvements. Evaluate the suggested CSS layer promotion and reduce avoidable expansion rendering work without changing display defaults.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Full-view expansion uses one reveal mask with fixed-size artwork and reading content, preserving source origin and organic shape.
- [x] #2 Temporary layer hints and rendering resources are removed at completion, resize, reduced motion, interruption and unmount; previews keep their existing behavior.
- [x] #3 Desktop and fullscreen are checked with focused tests and browser fixtures, and a separate portable build is prepared for hardware validation with limitations documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect clip, shader and lifecycle costs and primary browser guidance. 2. Isolate the expansion reveal into one temporary composited mask and avoid unnecessary full-surface rim work. 3. Verify geometry, lifecycle and both presentation paths; document evidence and package separately for target-hardware comparison.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented one shared expansion mask, temporary translate3d/backface and clip-path hints, and conservative edge/halo retirement once outside the pane. All 36 focused portal, geometry, details, preview and app tests pass; production build passes with existing Zod annotation notices. Browser fixture confirms one mask and unscaled fixed planes during desktop and 3440x1440 fullscreen transitions; completion removes hints and canvas and focuses Back. Physical frame pacing remains unmeasured.

Separate release/rift-portal package created without replacing prior executables. All 52 frontend output files match app.asar. Current behavior documented in Electron README and theme contract; dated evidence and screenshot recorded in docs/spikes/2026-09-28-rift-integration. This is a targeted rendering-cost reduction, not a measured native FPS or physical flicker fix.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Full-view portals now use one clipping mask, temporary compositor layer hints, and stop rim rendering once it is offscreen. Fixed content, organic shape, preview behavior, accessibility and cleanup are retained. Verified with 36 passing focused tests, production build, desktop/fullscreen browser checks including 3440x1440, and byte-verified separate portable package. Hardware frame pacing with DirectComposition disabled still needs user comparison.
<!-- SECTION:FINAL_SUMMARY:END -->
