---
id: TASK-184
title: Crop fullscreen Steam heroes on standard aspect displays
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 00:31'
updated_date: '2026-09-11 00:33'
labels: []
dependencies: []
ordinal: 215000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The lower edge of uncropped Steam heroes is abrupt at 16:9. Fill standard aspect fullscreen views while retaining the successful uncropped ultrawide treatment.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Below 21:9, Steam heroes fill the fullscreen canvas with centered cropping and the existing canvas fade.
- [x] #2 At 21:9 and wider, heroes retain the uncropped composition and image-edge fade.
- [x] #3 Resizing updates incoming and outgoing layout, fade and decode size; desktop behavior is unchanged.
- [x] #4 Relevant fullscreen tests pass and the visual rule is updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Gate whole-image fitting on canvas aspect ratio at least 21:9. Use fill geometry and crop-aware decode below that threshold. Refresh per-layer gradients when crossing the threshold. Test both cinematic and browse layouts through resizing and transitions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Fullscreen hero fitting now switches at 21:9. Narrower canvases use UniformToFill with the existing canvas fade and crop-aware decode width; ultrawide canvases retain whole-image fitting and their image-edge fade. Crossing the boundary refreshes gradients and geometry for incoming and outgoing layers. Desktop code remains unchanged; existing desktop fallback checks pass. UI rebuilt successfully and 38 backdrop, details and interaction tests passed. Geometry cases cover 16:9, exact21:9 and32:9, browse/cinematic fades, transitions and resize back to16:9. Initial ultrawide assertion was corrected for Avalonia pixel rounding. No production app or personal data changes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fullscreen Steam heroes now crop to fill below21:9 and retain whole-image fitting at21:9 and wider. Verified UI build and38 relevant tests, including resize and crossfade geometry. Updated visual specification and decision history.
<!-- SECTION:FINAL_SUMMARY:END -->
