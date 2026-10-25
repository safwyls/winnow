---
id: TASK-295
title: Keep feed-card hover chrome aligned
status: Done
assignee: []
created_date: '2026-09-15 23:14'
updated_date: '2026-09-15 23:38'
labels: []
dependencies: []
references:
  - design-system.md
modified_files:
  - src/Winnow.App/Views/FeedCardView.axaml
  - src/Winnow.App/Views/FeedCardView.axaml.cs
  - src/Winnow.App/Views/GameTileView.axaml
  - src/Winnow.App/Views/GameTileView.axaml.cs
  - tests/Winnow.Ui.Tests/FeedCardActionTests.cs
type: bug
ordinal: 337000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Desktop feed cards reuse GameTileView inside a larger cover frame. On hover, the embedded tile lifts independently while the feed feedback strip and outer highlight stay behind, exposing artwork above the highlight, opening a gap above the action strip, and crowding the Details fold against the highlight.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Hovering a desktop feed card moves the cover artwork, shared hover overlay, Details fold, feedback action strip, and highlight as one aligned surface.
- [x] #2 Standalone desktop library tiles retain their existing hover lift and highlight behavior.
- [x] #3 Focused feed-card actions remain reachable with stable card layout and do not overlap the shared primary action.
- [x] #4 Relevant headless UI tests cover the embedded lift alignment; fullscreen behavior is assessed and remains unchanged.
- [x] #5 A hovered feed card reserves enough space above its resting frame for the complete top outline to remain inside the shelf viewport.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Mark embedded GameTileView lift containers so the shared tile hover transform is suppressed only in feed usage. 2. Apply the same 2 px lift, shadow, and reduced-motion behavior to the feed CoverFrame, which contains artwork, feedback actions, and the feed highlight. 3. Add headless geometry assertions for aligned embedded hover chrome and rerun focused UI tests. 4. Confirm the separate fullscreen feed path is unaffected.

5. Reserve the 2px lift distance in the feed card's layout slot, assert the hovered frame stays within the card bounds, and visually verify the first shelf row.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Moved the 2px lift and shadow from the embedded GameTileView face to the feed CoverFrame so the artwork, shared overlay, Details fold, feedback strip, and feed highlight transform together. The standalone tile selector remains unchanged. Closed the inner-border seam with a 47px content inset against the 48px strip. Reduced motion still snaps the transform; keyboard focus reveals actions without lifting the frame. Rendered 180px and 240px headless captures were visually inspected.

Validation: 48 FeedCardActionTests and CardDetailsInteractionTests passed; 18 FullscreenBrowseTests passed; git diff --check passed.

Follow-up: reserved the 2px lift distance as top margin on CoverFrame. The geometry test now proves the frame rests at y=2 and lands at y=0 after its -2px hover transform, keeping the complete outline inside a clipped first-row viewport. All 22 FeedCardActionTests passed, the 240px rendered capture shows the full top edge, and git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Aligned all desktop feed-card hover layers and reserved the lift distance above the cover so the top outline remains visible. Verified the chrome geometry, first-row boundary, action behavior, standalone tiles, and fullscreen path with rendered captures and focused UI tests.
<!-- SECTION:FINAL_SUMMARY:END -->
