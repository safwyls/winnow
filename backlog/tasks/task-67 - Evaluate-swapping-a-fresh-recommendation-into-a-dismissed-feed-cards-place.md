---
id: TASK-67
title: Evaluate swapping a fresh recommendation into a dismissed feed card's place
status: To Do
assignee: []
created_date: '2026-09-01 20:57'
updated_date: '2026-09-01 20:57'
labels:
  - recommend
  - ui
dependencies:
  - TASK-68
priority: medium
type: spike
ordinal: 84000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
### Evaluation: swap a fresh card into the feed after a dismissal

Dismissal today does not shrink the feed. 'FeedCardViewModel.GiveAsync' stores the verdict, sets 'IsSetAside', and the card keeps its place with a receipt line: "Off the feed." for not-interested, "Back on (date)" for a snooze, with an inline Undo. The card disappears only on the next full feed pass; 'FeedViewModel.Apply' disposes every card, clears the Shelves collection, and rebuilds from scratch. The scoring pass refills the freed slot from the shortlist, so the feed does not shrink. The feature is therefore not "stop the feed shrinking" but "replace the receipt with a fresh card before the next full pass."

The naive design, re-scoring the library per dismissal, is cheap in isolation. A full re-score ('FeedbackSets.LoadAsync' plus 'RecommendationEngine.GetShelvesAsync' at MaxPerShelf 6) costs 69 ms warm median, 354 ms on a process's first pass, measured on the live database copy. Ten sequential dismiss-and-re-score passes measured 67-123 ms, median 73-104 ms across two runs. Each pass issues roughly 1330 SQL round trips: four bulk reads (bucket query 1005 rows at 17.5 ms, facet snapshot at 30.7 ms, 1029 identities, 1045 ownerships), one history-stats aggregate, three feedback reads, then 150 snapshot reads, 150 session reads, and about 18 update-event reads. Per-row probes cost 0.04 ms per snapshot-and-session pair, 0.02 ms per update-event read. None of it runs on the UI thread; 'FeedService' wraps the pass in 'Task.Run' because Microsoft.Data.Sqlite completes synchronously. For scale, the merge screen measured 2052 ms of frozen UI per action at 200 items; a feed re-score is two orders of magnitude cheaper and on the right thread. The cost objection is not the query. It is that 'FeedViewModel.Apply' rebuilds the entire feed on the UI thread, every card disposed and every cover re-leased, because one card was dismissed.

The feed does not virtualise. Shelves render in an ItemsControl over a StackPanel; each shelf's cards in an ItemsControl over FeedGrid, a plain measuring-and-arranging Panel. Every card is realised: 12 today, 28 with the probe limit raised. Replacing one item in an observable collection realises one container and re-measures that shelf's six cards; clearing the collection (what Apply does now) rebuilds everything. This is expected Avalonia ItemsControl behaviour and should be confirmed in a headless test, as the project has no headless UI test harness today. 'FeedShelfViewModel.Cards' is 'IReadOnlyList', not observable, so no in-place swap path exists yet. Each card owns its 'CoverPresenter' and a reference-counted lease; an outgoing card's Dispose only decrements the pool slot. Two feed cards never share a work and the wall's leases are separate width slots, so a swap cannot blank a neighbour; 'DecodedLru' drops evicted art rather than disposing it, so a live presenter cannot lose its pixels underneath it. A swapped-in card is usually a cover miss: reading and decoding at the feed's width bucket (160 at 100% scaling, 240 above 125%) costs about 4.8 ms off the UI thread, with procedural placeholder art showing until it lands. The discipline Apply already follows is mandatory for a swap: unsubscribe VerdictChanged and Dispose the outgoing card, or both the lease and the event subscription leak.

A reserve drawn from the existing scoring pass is nearly free. Default tuning probes 150 works and surfaces 12 items. Requesting 'MaxPerShelf' 12 instead of 6 measured 97 ms against 99 ms for 6, inside the noise, on the real library (1005 bucket rows, 982 candidates, 958 works, tier Settling). Items 7-12 already respect the per-shelf franchise cap (1) and genre cap (3) against 1-6, because 'ShelfBuilder.Fill' applies caps across the whole run; a reserve item is cap-legal by construction. Dismissing a release cannot change any other candidate's score (verdicts feed hard-exclusion sets, not the taste profile), so a held reserve is exactly what a re-score would return. One conservative gap: removing a card frees its cap counts, so a cap-skipped candidate could become eligible but the reserve would not include it.

A live bug blocks this. 'ShelfProbeLimit' (150) starves the last three shelves. The union of per-shelf shortlists fills in claim order and stops at 150, so 'patched_while_away' and 'worth_another_look' consume the budget; 'ready_to_play', 'barely_touched', and 'on_your_taste' are never scored. At 300 the feed grows from 2 shelves / 12 items to 4 / 22 (98 ms). At 600 the natural union settles at 356 probes, 5 shelves and 28 items, 103 ms. A deeper shelf request for reserve purposes enlarges the shortlists that exhaust the budget first, so the probe limit must be raised before or with any reserve work.

TASK-10 is a prerequisite. Surfacings are recorded at generation time for every item in the computed feed. A held reserve item that is never shown would be logged as shown, earning the -0.20 recently-surfaced penalty the next day and corrupting endorsement joins within the three-day window. Reserve items must be excluded from 'FeedbackSets.SurfacingsOf'; a swapped-in card must record its surfacing at swap time.

TASK-20 interacts. 'FeedViewModel.OnTilesChanged' drops invalidations arriving during a load. A reload landing mid-swap clears the collection and discards the reserve. Swaps must be generation-scoped: a reload wins, and a swap from a superseded pass must not apply.

Undo is the strongest argument against immediate swapping. Today the undo receipt sits on the card at zero friction. Swap it out and the only remaining route is the history screen. 'FeedViewModel.OnVerdictRevoked' restores by walking live cards; if the card is gone, the revoke has no effect until the next full pass. Not-now receipts carry a return date the user only sees while something stays on screen to say it.

Recommended path: hold a pre-computed reserve and delay the swap behind the existing receipt. The receipt preserves the in-place undo; the replacement lands when the receipt is dismissed or lapses. Implement the reserve in 'FeedService' by requesting a deeper shelf and slicing; no engine API change, module boundary (design doc S5.1) intact. When exhausted, refill by a background re-score coalesced against TASK-20's mechanism, never a synchronous re-score per dismissal. Not recommended: re-scoring on every dismissal (correct, ~75 ms off-thread, but forces a full-page rebuild for one card) or doing nothing (defensible, but not the requested feature). What would change this: if a shelf's reserve is routinely empty on a smaller library (the 'ready_to_play' pool, four items today, is the warning), a reserve is theatre. If TASK-10 is not done first, impression corruption makes the feature actively harmful.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 ShelfProbeLimit is raised so that all five shelves are scored before any reserve work ships
- [ ] #2 FeedService requests a deeper shelf and holds items beyond the visible count as a reserve, with no engine API change
- [ ] #3 A swapped-in card records its surfacing at swap time, not at generation time
- [ ] #4 Reserve items are excluded from the surfacing log at generation time
- [ ] #5 The swap is generation-scoped and discarded if a feed reload supersedes the originating pass
- [ ] #6 The receipt and its inline undo remain visible until the user dismisses them or they lapse; the reserve card replaces the receipt, not the verdict
- [ ] #7 FeedShelfViewModel.Cards supports in-place replacement without rebuilding the entire shelf
- [ ] #8 When the reserve is exhausted a background re-score refills it, coalesced with the invalidation mechanism from TASK-20
- [ ] #9 Not-now receipts that state a return date remain visible for at least one interaction before the swap replaces them
- [ ] #10 The outgoing card's cover lease and VerdictChanged subscription are released on swap, verified by a test
<!-- AC:END -->
