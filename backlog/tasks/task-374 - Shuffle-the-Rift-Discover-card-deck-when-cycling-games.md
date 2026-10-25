---
id: TASK-374
title: Shuffle the Rift Discover card deck when cycling games
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 00:52'
updated_date: '2026-09-29 01:03'
labels: []
dependencies: []
type: enhancement
ordinal: 410000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Cycling Discover currently replaces the cover positions abruptly. Give left and right navigation the feel of moving cards through a physical deck while keeping the selected game and details synchronized.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Left/right controls, neighboring covers and arrow keys trigger a directional shuffle in both desktop and fullscreen.
- [x] #2 Rapid changes settle on the latest selected game without stale cards, lost keyboard focus or delayed details activation; reduced motion switches immediately.
- [x] #3 The app and shared mock use the animation, with focused tests, visual verification and documentation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add a reusable transform-only deck animation controller; retain cover identity across slots and animate from current poses on interruption; wire directional navigation in app and mock; verify both modes and reduced motion, document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented a shared DOM/Web Animations controller, keyed production cover slots, and a generated mock bundle. Motion lasts 520 ms and follows navigation direction. New input captures interpolated poses before cancellation; reduced motion, resizing, hidden documents and unmount stop animation. Verified desktop/fullscreen controls, neighboring covers, rapid keyboard reversals, focus retention and details activation during motion using fixture browser previews. Reduced motion changed selection immediately. The mock was checked in both modes and its app/CSS references versioned after observing cached scripts. Twelve focused deck/app/data tests, typecheck, production build, Node syntax check and git diff --check passed. Build retained existing Zod annotation notices. Evidence and mock mid-motion capture are in docs/spikes/2026-09-28-rift-integration/README.md. Native executable was not repackaged; no GPU benchmark or hardware controller test.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added directional card shuffling to Rift Discover in both the Electron source and shared mock. Rapid changes retarget current poses, focus and details remain immediate, and reduced motion skips animation. Verified with 12 focused tests, the production build, and desktop/fullscreen browser interactions; documented the reusable module and captured the mock mid-shuffle.
<!-- SECTION:FINAL_SUMMARY:END -->
