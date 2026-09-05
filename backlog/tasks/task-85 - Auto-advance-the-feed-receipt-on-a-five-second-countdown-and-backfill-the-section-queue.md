---
id: TASK-85
title: >-
  Auto-advance the feed receipt on a five-second countdown and backfill the
  section queue
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-04 15:17'
updated_date: '2026-09-04 17:10'
labels:
  - recommend
  - ui
dependencies:
  - TASK-67
priority: medium
type: feature
ordinal: 112000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
TASK-67 shipped the reserve and the swap, but left the receipt's exit to the reader: a 'Show another' control on the receipt, plus a rule that a standing receipt lapses on the next verdict given anywhere in the feed. Both are replaced here.

The receipt now leaves on its own. When a card is answered and its section is holding a replacement, the receipt shows for five seconds with its Undo, a determinate radial indicator in the corner of the card counting the time down, and then the replacement takes the card's place. The 'Show another' control is removed; there is nothing left for it to do.

Two things the countdown has to be careful about. It is a time limit on the reader's undo, so it pauses while the card is under the pointer or holds focus — a receipt someone is reading is a receipt they have not finished with. And it is continuous motion, which design-system.md section 8 governs: the indicator is DETERMINATE (it states real remaining time), so TASK-79's gate on indeterminate indicators does not apply to it, but section 8 has no rule for this case and needs one. The undo is never the only route back either way: every verdict and its undo stay on the history screen after the card has gone.

The refill also changes shape. TASK-67 answered an exhausted reserve with a full re-score that rebuilt the feed, which is why it had to be held back while any receipt was still on screen. With cards advancing by themselves that is no longer viable — the reader can empty a section's queue in half a minute. The queue is now backfilled behind the scenes instead: a background pass whose VISIBLE slices are discarded and whose reserves are merged into the queues that are already there, touching no card on screen. A dismissed game carries an active verdict by then, so the next pass excludes it and its reserve genuinely holds games the queue has not seen.

Supersedes TASK-67 acceptance criteria 6 (the receipt's exit) and 9 (a not-now receipt surviving one interaction); the five-second window and the hover pause are what guarantee a return date is readable now. TASK-67's criteria 3, 4, 5, 7, 8 and 10 still hold and must not regress.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The receipt's manual replacement control is gone from the card
- [x] #2 A receipt whose section is holding a replacement counts down for five seconds and is then replaced by it, with no input from the reader
- [x] #3 The countdown is shown as a determinate radial indicator in the corner of the card, stating time remaining rather than merely that time is passing
- [x] #4 Undo cancels the countdown and restores the card in place
- [x] #5 The countdown pauses while the card is under the pointer or holds focus, and resumes when it is not
- [x] #6 Under reduced motion the indicator is still shown and still accurate, and does not animate continuously; design-system.md section 8 states the rule for a determinate indicator
- [x] #7 A receipt with no replacement available does not count down and keeps its place, as it did before
- [x] #8 After a card is swapped in, its section's queue is backfilled by a background pass that rebuilds no shelf and disturbs no card on screen
- [x] #9 A backfill never enqueues a game that is on screen, already queued, or already swapped in during the current pass
- [x] #10 Backfills are coalesced: at most one in flight and one waiting, and a backfill from a superseded pass is discarded
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Card side: remove ReplaceCommand and ShowReplace, and give FeedCardViewModel a countdown it owns but does not drive — a deadline, a remaining fraction, and a paused flag. Starting it is conditional on the section holding a replacement, so a receipt that cannot be answered never shows a clock that ends in nothing.
2. Drive it from one ticker on FeedViewModel rather than a timer per card: the feed can hold several counting receipts at once, and one dispatcher timer that runs only while at least one is counting is cheaper and easier to reason about than N. The tick itself is an internal method taking the instant, so tests advance it directly and the timer stays a thin untested shell — the seam CoverPresenter already uses for the dispatcher.
3. Pause on pointer-over and on focus-within. The card already tracks IsPointerOver for the tile; the receipt needs focus too, because Undo is a Tab stop and a keyboard reader must not lose it.
4. Reduced motion: read it where the card already reads it (Tile.SnapDormancy off DormancyRamp) and tick coarsely rather than smoothly. The indicator stays present and accurate either way, because it is determinate — this is why TASK-79's gate does not apply, and design-system.md section 8 needs the rule saying so.
5. View: an Arc in the card's corner, swept from the view model's fraction. Transitions through a style, never a local value (section 12.5). Delegate the visual to the avalonia-ui agent with its charter.
6. Backfill: replace TryRefill's full reload with a background pass whose visible slices are discarded and whose reserves are merged into the live queues. Filter every incoming item against what is on screen, already queued, or already spent this generation. Generation-scoped, coalesced one-in-flight-one-waiting, and it must not touch a card or a shelf that is already drawn.
7. Keep the reload path (TilesChanged) as it is: that one legitimately rebuilds, and its coalescing gate stays.
8. Tests: countdown start conditions, expiry, undo cancelling it, pause and resume, reduced-motion cadence, backfill merge and its three exclusions, coalescing, generation scoping. Then the full suite across all three projects, and a run against a throwaway data directory.
9. Delegate all prose to docs-writer, including the new section 8 rule and the recommendation-engine note about the backfill pass.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
## The receipt's clock

Five seconds, held rather than reset while the reader is on the card. Two things made the hold non-negotiable rather than a nicety. The window is a time limit on an undo, which is the one control on this screen a reader may need to reach in a hurry; and Undo is a Tab stop, so a keyboard reader tabbing to it would otherwise have it disappear under them. The hold is therefore driven by pointer-over AND keyboard-focus-within, and the view reads focus from 'InputElement.IsKeyboardFocusWithin' rather than the GotFocus/LostFocus pair, because that pair reports a transient loss while focus moves from the card to the Undo inside it — a gap in which the clock would have advanced.

The undo is never the only route back regardless: every verdict and its undo stay on the history screen after the card has gone, which is what keeps a five-second window defensible at all.

One ticker for the feed, not one per card. Several receipts can be counting at once, and the cards have to be walked anyway to find them. It advances by the time that actually passed rather than by the interval it asked for, so a late timer on a loaded UI thread cannot leave a receipt standing past its five seconds with the arc short of its end. The ticker itself is a thin shell over TimeProvider.CreateTimer; 'FeedViewModel.Tick(TimeSpan)' is internal and the tests drive it directly, which is the seam CoverPresenter already uses for the dispatcher.

## Reduced motion, and why TASK-79 does not gate this

design-system.md section 8 forbids shipping an INDETERMINATE indicator until TASK-79 states the rule. This one is determinate: it states real time remaining, so it is information rather than decoration and withholding it would cost the reader the only statement of how long the undo has left. Section 8 had no rule for that case and now has one. Under reduced motion the ring stays and stays accurate; what goes is the continuous movement — the ticker steps once a second (five visible steps) instead of thirty times a second, and the view's sweep transition is removed by a style rather than being present as a local value, which section 12.5 requires precisely so section 8 can remove it.

## Backfill instead of refill

TASK-67 answered an exhausted reserve with a full re-score that rebuilt the feed, and had to hold it back while any receipt was on screen because a rebuild replaces the card the receipt is on and takes its undo with it. With cards advancing by themselves that is no longer viable: a reader can empty a section's queue in half a minute.

The queue is topped up instead. The pass runs, its visible slices are discarded, and its reserves are merged into the queues already standing behind the shelves — no shelf rebuilt, no card on screen moved, which is what makes it safe to run under a receipt. It returns games the queue has not seen because the dismissal that emptied the slot is already stored: the answered game is hard-excluded from the next pass, every shelf shifts up, and something new arrives at the bottom. Everything on screen, already queued, or already spent this pass is dropped on the way in, so a shifted pass cannot re-offer the card the reader is looking at.

The pass's VISIBLE items are considered for the queue too, not just its reserve. Once the answered games are excluded those are the shelf's best remaining candidates, and this shelf is not going to redraw itself to show them; whether one belongs in the queue is decided by the spent set the same way a held item is.

One read in flight and one waiting. A reader answering a run of cards asks for a backfill on every swap, and a queue of reads would all return the same answer at increasing cost.

## Verification

- tests/Winnow.Tests: 2,848 passed, 0 failed. FeedReserveTests now covers the clock (start conditions, the five-second run and its stated progress, the hold under pointer and under keyboard focus, resumption from where it was held, reduced-motion cadence), the swap it drives, and the backfill (topping the queue up without moving a card on screen, the three exclusions, one-in-flight-one-waiting coalescing, and the generation guard).
- tests/Winnow.Recommend.Tests: 152 passed, 0 failed. tests/Winnow.Covers.Tests: 70 passed, 0 failed.
- Built and run against a throwaway data directory after the view landed: starts clean, seeds, scores, no exception and no binding failure. Compiled bindings are on by default, so the ring's bindings ('IsCountingDown', 'CountdownSweep', 'Tile.SnapDormancy') were resolved against the view model at build time.
- 'ShowReplace', 'ReplaceCommand', 'LapseReceipt' and the string 'Show another' no longer appear anywhere in src or tests.

Two limits on the evidence, both the same gap this project has had all along — there is no headless Avalonia UI harness. The ring's PLACEMENT and its style-based transition removal under reduced motion were verified by reading the markup and by the compiled-binding build, not by an automated test; what a test pins is the view model side, which is where the determinate value and the stepped cadence live. And the pointer and focus flags are set by the code-behind from 'OnPointerEntered'/'OnPointerExited' and 'InputElement.IsKeyboardFocusWithin'; the tests drive those flags directly rather than through real input.

## One layout change worth knowing about

The ring sits at the card's top right, which is where a long title's first line lands. Rather than let the two overlap, the title's TextBlock now carries a permanent 24px right margin reserving the ring's lane — permanent rather than only while counting, because a margin appearing with the receipt would re-wrap the title under the reader and could change the card's height, and the receipt was built to swap the action line with no reflow. It costs about 7% of the title's lane on a control that is already two lines with character ellipsis.

## Retuned to three seconds (2026-09-04, after the task closed)

The receipt window was changed from five seconds to three at the user's request. 'FeedCardViewModel.Countdown' is the one place the number lives; acceptance criterion #2 and the summary above still say five and are left as written, because they record what was agreed at the time rather than what the constant says now.

Nothing about the argument changes shape. The window is still a floor rather than a deadline — the clock is held for as long as the reader is on the card, by pointer or by keyboard focus — and the undo still outlives the card on the history screen. Both matter more at three seconds than at five: the pause is now carrying most of the weight of keeping the undo reachable, since three seconds is about as long as it takes to read the receipt's one line and move toward the control beside it.

Under reduced motion the stepped cadence is unchanged at one second, so the ring now moves in three visible steps rather than five.

The tests were changed to express the window rather than the number: they tick 'FeedCardViewModel.Countdown' and halves of it, and the reduced-motion case derives its expected progress from the constant. A further retune is a one-line change with no test to follow it.

Verified: 2,848 tests in Winnow.Tests passing, 24 of them in FeedReserveTests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The receipt now leaves on its own. A card the reader answers keeps its place for five seconds with its Undo and a determinate ring in the corner counting the time down, and then the section's next held card takes the slot. The manual 'Show another' control is gone, and so is the rule that a standing receipt lapsed on the next verdict given elsewhere in the feed — each receipt has its own clock.

The clock is HELD, not reset, while the pointer is over the card or anything in it has focus. That is not a nicety: five seconds is a time limit on an undo, and Undo is a Tab stop, so a keyboard reader tabbing to it would otherwise have it vanish under them. The view reads focus from IsKeyboardFocusWithin rather than the GotFocus/LostFocus pair, which reports a transient loss while focus moves from the card into the Undo inside it — a gap the clock would have advanced through. The undo is never the only route back regardless; every verdict keeps one on the history screen.

One ticker drives the whole feed rather than a timer per card, and it advances by the time that actually passed rather than by the interval it asked for, so a late timer cannot leave a receipt standing past its window with the ring short of its end. The ring is determinate — it states real time remaining — which is why design-system.md section 8's gate on INDETERMINATE indicators (TASK-79) does not apply to it; section 8 now carries the rule for this case: the indicator stays and stays accurate under reduced motion, and what goes is the continuous movement, the ticker stepping once a second instead of thirty times.

The refill became a backfill. TASK-67 answered an exhausted reserve by re-scoring and rebuilding the feed, which is why it had to wait for every receipt to leave the screen; with cards advancing by themselves a reader empties a section's queue in half a minute and that was no longer viable. A backfill scores the feed again, discards everything except what it can add to the queues already standing, and moves no card on screen — so it is safe to run under a receipt. It returns games the queue has not seen because the verdict the reader just gave is already stored and hard-excluded from the next pass. Anything on screen, already queued or already spent this pass is dropped on the way in; one read in flight and one waiting; a backfill from a superseded pass is discarded.

Verified: 2,848 tests in Winnow.Tests, 152 in Winnow.Recommend.Tests, 70 in Winnow.Covers.Tests, all passing. Ten of them are new and cover the clock, the hold under both pointer and keyboard focus, resumption, the reduced-motion cadence, and the backfill's merge, exclusions, coalescing and generation guard. Run end to end against a throwaway data directory after the view landed: starts clean, scores, no exception or binding failure. The ring's placement and its style-based transition removal were verified by reading the markup and by the compiled-binding build rather than by an automated test, because the project has no headless UI harness.
<!-- SECTION:FINAL_SUMMARY:END -->
