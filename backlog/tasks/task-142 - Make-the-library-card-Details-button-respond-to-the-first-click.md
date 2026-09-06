---
id: TASK-142
title: Make the library card Details button respond to the first click
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 18:24'
updated_date: '2026-09-06 18:42'
labels: []
dependencies: []
priority: high
type: bug
ordinal: 169000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The user reports that clicking Details on a library card sometimes does nothing until several clicks. Reproduce the actual button route and fix lost input or delayed opening without expanding into the separate tile-gesture redesign in TASK-116.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A single click on a visible Details button opens that game reliably, including after flipping and after closing another details modal.
- [x] #2 Card action buttons retain their own pointer and keyboard actions; opening details does not accidentally close or flip the card instead.
- [x] #3 An actual-view regression reproduces the failure and verifies the fix, with relevant automated tests passing.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce pointer hit testing on the real card back at minimum/default density, including long titles and multiple store chips, and inspect event routing. 2. Keep the action buttons reachable by constraining overflowing metadata; preserve button ownership of repeated clicks if the tunnel handler also interferes. 3. Verify first-click Details, other buttons and keyboard behavior in the actual view, then run relevant regressions and document the measured cause.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced on the actual card at108x162 with a two-line title, year and three store chips: points inside Details hit the year or a store chip instead of its Button. A pointer down/up at local9,9 flipped the card and made zero update-repository calls. Center click and ten settled reopen cycles worked. Root cause is the unbounded metadata StackPanel drawing over bottom-docked actions. Fix places facts in a bounded inner ScrollViewer, reserves the scrollbar gutter and preserves the pinned action region; tunnel routing now leaves Buttons and RangeBase controls their own repeated presses.

Five actual-view headless regression cases pass twice in the isolated runner and again in the integration output. Tests cover the full interior Details hit area at 108px and 148px, three open-close cycles, repeated Play and Add to list pointer actions, keyboard activation, and metadata scrolling with pinned button positions. Inspected Skia captures confirm no metadata overlap. Full solution build passed with zero warnings or errors; main regression suite running.

Integration verification complete: all 3483 main tests and all 5 UI tests passed; solution build has zero warnings and errors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed lost Details clicks caused by metadata painting over the button on dense cards. Card facts now scroll above pinned actions, and buttons and scrollbars retain repeated pointer presses. Verified actual pointer hit targets, first-click opening and reopening at 108px and 148px, keyboard actions, scrolling and rendered captures. All 3483 main tests and 5 UI tests pass; solution build is clean.
<!-- SECTION:FINAL_SUMMARY:END -->
