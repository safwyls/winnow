---
id: TASK-86
title: Feed cards do not fill the slot the grid arranges them into
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-04 17:53'
updated_date: '2026-09-04 18:04'
labels:
  - ui
  - recommend
dependencies: []
priority: high
type: bug
ordinal: 113000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Every card in a feed section is arranged into an identical rect: 'FeedGrid.ArrangeOverride' computes one 'itemWidth' for the whole panel and arranges every child at that width and at the row's tallest height, the latter deliberately so that the Play button and the store chips 'land on one line across the row however long the reasons above them ran'.

The card never fills that rect. Measured on the real library at a 1280px window (panel 953px, 2 columns, slot 469.0px), walking the visual tree down from the arranged child:

    slot=469.0 | ContentPresenter=469.0 | FeedCardView=469.0 | ContentPresenter=469.0 | Button=448.0
    slot=469.0 | ContentPresenter=469.0 | FeedCardView=469.0 | ContentPresenter=469.0 | Button=460.0
    slot=469.0 | ContentPresenter=469.0 | FeedCardView=469.0 | ContentPresenter=469.0 | Button=451.0

Every level passes the full width down until the card's root Button, which takes its own desired width instead. Avalonia's default Button alignment is not Stretch, and 'Button.feedcard' never overrides it, so the card sizes to content and sits left-aligned in its slot. The desired width is driven by the reason sentence: a wrapped TextBlock desires the width of its longest LINE, so a card's right edge lands wherever that card's longest line happened to end. Reported by the user as card sizes being content-driven and inconsistent, with the ragged right edges marked on a screenshot.

The vertical half is the same defect and costs more. FeedGrid arranges each card to the row's height for the stated reason above; a card that does not stretch vertically renders at its own height instead, so the row equalisation that exists to line up the action rows has been doing nothing.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A feed card fills the width of the slot FeedGrid arranges it into, so every card in a section has the same width and their right edges line up
- [x] #2 A feed card fills the height of the row it is arranged into, so the action row and store chips line up across the row as FeedGrid's row equalisation intends
- [x] #3 A test pins the card root's alignment, so a future restyle cannot silently reintroduce content-driven sizing
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce by measurement, not by eye: walk the visual tree from the arranged child down to the card root and print each level's Bounds.Width against the slot. Done — the slot and every level to the card's inner ContentPresenter are 469.0; the Button is 448/460/451.
2. Fix at the level that fails: 'Button.feedcard' takes HorizontalAlignment and VerticalAlignment Stretch, in the style rather than as local values (design-system section 12.5 makes that the rule for Transitions and the same argument applies to anything a style may need to override).
3. Re-measure to confirm every level is the slot width, and confirm the vertical half by printing heights against the row height.
4. Pin it: a markup test in the Enforcement suite asserting the card root stretches on both axes, since the defect is invisible in code review and there is no headless UI harness to catch it.
5. Check the height picture after the fix. FeedGrid equalises per ROW; if row-to-row still reads as ragged once the cards actually fill their rows, say so and treat feed-wide equalisation as a separate question rather than widening this bug.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
## Diagnosis

The screenshot the user annotated showed the ragged right edges; the measurement located the level responsible. Walking the visual tree down from each child FeedGrid had just arranged, on the real library at a 1280px window (panel 953px, 2 columns, slot 469.0):

    slot=469.0 | ContentPresenter=469.0 | FeedCardView=469.0 | ContentPresenter=469.0 | Button=448.0
    slot=469.0 | ContentPresenter=469.0 | FeedCardView=469.0 | ContentPresenter=469.0 | Button=460.0
    slot=469.0 | ContentPresenter=469.0 | FeedCardView=469.0 | ContentPresenter=469.0 | Button=451.0

Every level carries the full slot width down to the card's root Button, which takes its own desired width instead. An Avalonia Button does not stretch by default. The desired width comes from the reason sentence: a wrapping TextBlock desires the width of its longest LINE, so each card's right edge landed wherever that card's longest line ended — which is exactly why the widths looked content-driven.

The vertical half was the same defect and cost more. FeedGrid arranges every card in a row to the row's tallest height, with a comment saying why: so the action row and the store chips land on one line across the row. A card that does not stretch vertically renders at its own height inside that rect, so the equalisation had been doing nothing since it was written.

What made it invisible in review: 'Button.feedcard' already carried two Stretch setters. They are HorizontalContentAlignment and VerticalContentAlignment, which align the content INSIDE the button and were never the problem.

## Fix and confirmation

Two setters on the same style. Re-measured on the same library and window:

    slot=469.0x192.0 | ContentPresenter=469.0x192.0 | FeedCardView=469.0x192.0 | ContentPresenter=469.0x192.0 | Button=469.0x192.0

Identical on both axes, at every level, for every card.

## On the height question the user raised

With the cards now filling their rows, heights are uniform across the whole feed at this window size: every card's desired height measured 192 on all five shelves, because the 162px cover plus margins dominates rather than the reason text. FeedGrid equalises per ROW rather than feed-wide, so row-to-row could still diverge at a window narrow enough that the text column drives the height instead of the cover. Not pursued here — it is speculative at the measured widths and would be a different change (a shared height scope across five sibling panels). Worth its own task if it is ever seen.

## Verification

- 2,850 tests passing in Winnow.Tests, including the two new theory cases.
- The new test was confirmed to FAIL with the two setters removed and pass with them restored, so it is not passing vacuously.
- Measured in the running app against a copy of the real library, before and after.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The cards were already being handed identical rects; they just were not filling them. 'FeedGrid.ArrangeOverride' computes one slot width for the whole panel and arranges every card in a row to that width and to the row's tallest height. Walking the visual tree on the real library at a 469px slot showed every level carrying 469.0 down to the card's root Button, which then drew at 448, 460 and 451 — an Avalonia Button does not stretch by default, so each card took its own desired size and left-aligned inside a slot that was already correct. That desired width comes from the reason sentence, because a wrapping TextBlock desires the width of its longest LINE, which is precisely why the widths looked content-driven and why a section's right edges were ragged.

The vertical half was the same defect and cost more: FeedGrid equalises each row's height so the action rows and store chips line up across it, and a card that does not stretch vertically had been quietly ignoring that since it was written.

What hid it is that the style already carried two Stretch setters — HorizontalContentAlignment and VerticalContentAlignment, which align the content inside the button and were never the problem. The fix is the two ELEMENT alignments on the same style, and the comment beside them names that distinction because it is the thing a future reader will otherwise 'fix' back.

Verified by measurement in the running app against a copy of the real library: every level now reports 469.0 x 192.0, identical on both axes for every card. 2,850 tests pass, including a new two-case theory that reads the markup and asserts both setters — confirmed to fail with them removed and pass with them restored, so it is not passing vacuously. It is asserted against markup because the project has no headless UI harness and this defect is invisible in code review.

Heights are uniform across the whole feed at the measured width (every card 192, the cover dominating the text). FeedGrid equalises per row rather than feed-wide, so a narrow enough window could still diverge; that is left alone as speculative and would be a different change.
<!-- SECTION:FINAL_SUMMARY:END -->
