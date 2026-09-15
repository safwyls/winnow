---
id: TASK-293
title: Fix clipped library Display preferences
status: Done
assignee:
  - '@codex'
created_date: '2026-09-15 01:10'
updated_date: '2026-09-15 01:16'
labels: []
dependencies: []
type: bug
ordinal: 335000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The library Display popover clips labels and controls along its right edge when its vertical scrollbar appears.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop Display preferences show complete labels and controls with scrolling for shorter windows.
- [x] #2 Assess fullscreen equivalents and record verification for both surfaces.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect sizing, remove conflicting width constraints and wrap content, then verify layout and preference interactions.

Keep the checkbox focus ring inside its own layout bounds and verify an unchecked checkbox with keyboard focus.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Removed the fixed 264px content width, raised the presenter maximum to 420px, allowed checkbox labels to wrap, and added focus/scrollbar clearance. Desktop: built via targeted dotnet test and visually inspected real-font headless captures at normal height and a constrained 280px viewport scrolled to the final option (C:/Temp/winnow-display-fixed.png and C:/Temp/winnow-display-short.png). Temporary capture instrumentation was removed. Fullscreen: its separate settings rows do not use this popover; FullscreenSettingsTests and FullscreenSettingsHierarchyTests passed, including library layout at 100% and 140% text. All 21 targeted tests passed; constrained-height interaction rerun passed. git diff --check passed.

Follow-up: the focus ring used a negative margin inside a 16px checkbox column, so it could still be clipped at the control boundary. Reserved the full 22px ring width and centered the 16px checkbox inside it; aligned helper text with the shifted label. Verified an unchecked checkbox focused with NavigationMethod.Tab in a real-font headless render at C:/Temp/winnow-display-focus-fixed.png: the complete ring is visible. All three CoverPresentationTests passed; temporary capture instrumentation removed. Fullscreen uses separate controls and is unaffected by this desktop-local template change.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed Display popover sizing and checkbox focus clipping. The focus ring now fits inside the checkbox layout bounds. Verified keyboard-focus rendering, scrolling, and targeted desktop/fullscreen tests.
<!-- SECTION:FINAL_SUMMARY:END -->
