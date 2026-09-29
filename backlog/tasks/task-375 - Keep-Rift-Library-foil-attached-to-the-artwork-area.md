---
id: TASK-375
title: Keep Rift Library foil attached to the artwork area
status: Done
assignee:
  - '@codex'
created_date: '2026-09-29 01:30'
updated_date: '2026-09-29 01:36'
labels: []
dependencies: []
type: bug
ordinal: 411000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Library captions make the mock size its foil canvas taller than the artwork. The resize observer then invalidates the effect immediately. Assess the production counterpart too.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Library foil remains visible and follows pointer movement on desktop and fullscreen independently of caption height.
- [x] #2 Keyboard focus, reduced motion and Discover materials retain their behavior; assess the production counterpart.
- [x] #3 Record browser evidence and focused regression verification.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reproduce the disappearing canvas. Size its renderer from untransformed artwork dimensions and map input from the stable button. Verify both modes and production behavior, document and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced the disappearing material canvas while depth remained active. The mock compared the caption-inclusive button height to its cover ResizeObserver. Renderer dimensions now come from the untransformed cover and input is normalized separately. Browser verified desktop and fullscreen pointer-following foil, taller context captions, stationary keyboard and Still mode, and Discover materials after cycling. Production already measures its artwork surface correctly; all 20 artwork-effect tests passed. Mock Node syntax and git diff --check passed. Evidence and screenshot are in the Rift integration spike. This repair is separate from the user-reported physical fullscreen monitor flicker.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed the mock-only Library foil disappearance by sizing the shader to artwork rather than captions. Verified desktop/fullscreen pointer and keyboard behavior, Still mode and Discover, plus 20 existing production tests and syntax validation.
<!-- SECTION:FINAL_SUMMARY:END -->
