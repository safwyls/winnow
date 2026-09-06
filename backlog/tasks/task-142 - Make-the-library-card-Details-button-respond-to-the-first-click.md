---
id: TASK-142
title: Make the library card Details button respond to the first click
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 18:24'
updated_date: '2026-09-06 19:04'
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
Reproduce normal-motion first clicks and reopen cycles across density range; correct dense hover layout and lost card input; extend actual-view regressions and inspect captures.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Reproduced on the actual card at108x162 with a two-line title, year and three store chips: points inside Details hit the year or a store chip instead of its Button. A pointer down/up at local9,9 flipped the card and made zero update-repository calls. Center click and ten settled reopen cycles worked. Root cause is the unbounded metadata StackPanel drawing over bottom-docked actions. Fix places facts in a bounded inner ScrollViewer, reserves the scrollbar gutter and preserves the pinned action region; tunnel routing now leaves Buttons and RangeBase controls their own repeated presses.

Five actual-view headless regression cases pass twice in the isolated runner and again in the integration output. Tests cover the full interior Details hit area at 108px and 148px, three open-close cycles, repeated Play and Add to list pointer actions, keyboard activation, and metadata scrolling with pinned button positions. Inspected Skia captures confirm no metadata overlap. Full solution build passed with zero warnings or errors; main regression suite running.

Integration verification complete: all 3483 main tests and all 5 UI tests passed; solution build has zero warnings and errors.

Reopened after remaining missed clicks and hover overlap were reported; prior settled reduced-motion checks did not cover these states.

Normal-motion tests at100/140/180ms across108/148/200px passed three open-close cycles. A separate hover test fails at108px: never-opened stat painted bounds (10,70,72,13) intersect the Steam chip (51,68,45,16). Hover fix puts all store chips on a separate row and bounds wrapped stat text to two lines. Expanded motion edge checks remain in progress.

Additional actual-view failure reproduced with normal motion: at80ms the enabled Details button on a54.8%-opaque back missed its mapped local4,4 hit point, which resolved to Border;120ms also failed intermittently. Removing only the interactive back scale transition keeps its hit geometry stable while retaining the delayed80ms opacity fade and front turn. All15 card tests pass after the fix, covering108/148/200px, animated edge clicks at80/100/120/140/180ms, immediate reopening, hover bounds, other buttons, keyboard and scrolling.

Coordinator integration UI run: all 20 actual-view tests pass, including 15 card interaction/layout cases and 5 Epic details/store-link cases. Inspected final 108px single-store and three-store hover captures. Full solution build passed with zero warnings/errors; broader main regressions pending final Epic scheduling guard.

Final verification: full main suite 3496/3496, UI suite 20/20, and final selection-refresh focused suite 185/185 passed. Solution build is clean.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fixed two additional reproduced failures: dense hover stats overlapped store chips, and scaling the appearing card back caused early Details edge clicks to miss. Store chips now occupy their own row; stats wrap to two bounded lines; the back fades at its final size while the front still turns. Verified normal-motion clicks and reopen cycles at 108/148/200px, keyboard and scrolling, actual hover geometry and rendered captures. Main 3496, UI 20 and final focused 185 tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
