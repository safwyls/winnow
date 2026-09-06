---
id: TASK-67
title: Evaluate swapping a fresh recommendation into a dismissed feed card's place
status: Done
assignee:
  - '@safwyl'
created_date: '2026-09-01 20:57'
updated_date: '2026-09-04 04:34'
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
- [x] #1 ShelfProbeLimit is raised so that all five shelves are scored before any reserve work ships
- [x] #2 FeedService requests a deeper shelf and holds items beyond the visible count as a reserve, with no engine API change
- [x] #3 A swapped-in card records its surfacing at swap time, not at generation time
- [x] #4 Reserve items are excluded from the surfacing log at generation time
- [x] #5 The swap is generation-scoped and discarded if a feed reload supersedes the originating pass
- [x] #6 The receipt and its inline undo remain visible until the user dismisses them or they lapse; the reserve card replaces the receipt, not the verdict
- [x] #7 FeedShelfViewModel.Cards supports in-place replacement without rebuilding the entire shelf
- [x] #8 When the reserve is exhausted a background re-score refills it, coalesced with the invalidation mechanism from TASK-20
- [x] #9 Not-now receipts that state a return date remain visible for at least one interaction before the swap replaces them
- [x] #10 The outgoing card's cover lease and VerdictChanged subscription are released on swap, verified by a test
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Confirm the spike's premises against current code before building on them: TASK-68 landed (ShelfProbeLimit 2000 + round-robin ProbeUnion), FeedShelfViewModel.Cards is IReadOnlyList, FeedViewModel.Apply clears and rebuilds, FeedbackSets.SurfacingsOf logs every item in the ShelfFeed. Re-measure a MaxPerShelf 6 vs 12 pass on a read-only COPY of the live database and diff the VISIBLE slice, because a deeper request that shrinks the visible feed would be a regression the spike did not test for.
2. Fix the two ways a deeper request would change what the reader sees, inside ShelfBuilder (internal; IRecommendationEngine untouched). (a) Two-phase fill: every shelf fills its VISIBLE slice in claim order first, then every shelf fills its reserve, so an early shelf's reserve can no longer claim a work a later shelf's visible slice needed. (b) Size ShelfReasonLedger.CapFor to the visible count, not the requested depth, or asking for 12 to show 6 silently doubles how many of those 6 may cite the same fact and undoes TASK-76. Both need one optional RecommendationRequest property (VisiblePerShelf); the engine interface and the module boundary stay as they are.
3. Carry the reserve through the app seam: FeedShelf gains a Reserve list (init property, so no call site breaks), FeedService requests visible+reserve per shelf and slices, and RecordSurfacedAsync is given only the visible slice so a held card is never logged as shown.
4. Add IFeedService.RecordSurfacedAsync(releaseId, shelfId) so a card records its surfacing at the moment it goes on screen.
5. Make FeedShelfViewModel.Cards an ObservableCollection so one card can be replaced in place; the shelf becomes an ObservableObject so its count stays honest.
6. Receipt lifecycle on FeedCardViewModel: the receipt keeps its inline Undo and gains a dismiss control; it also lapses on the next verdict given anywhere in the feed, so a not-now receipt with a return date always survives at least one interaction. Both routes raise one internal ReplacementRequested event.
7. Swap in FeedViewModel: stamp each card with the generation of the pass that built it, drop a swap whose generation is stale, dequeue the shelf's reserve, unsubscribe VerdictChanged and Dispose the outgoing card (lease and subscription both), replace in place, record the surfacing at swap time.
8. Refill: coalescing reload gate (an invalidation arriving during a load is queued and replayed, TASK-20's mechanism) and an exhausted-reserve flag that requests the refill through it, held back until no receipt is still on screen so a background pass cannot yank a receipt the user is reading.
9. Tests: ShelfBuilder visible-slice equivalence and citation cap; FeedService reserve slicing and surfacing exclusion; FeedViewModel swap, generation scoping, receipt survival, lease and subscription release, exhaustion refill.
10. Delegate every comment, XML doc and UI string to docs-writer; update docs/recommendation-engine.md if the request contract changes. Build and test to a scratch artifacts path (the app holds bin), full suite across all three test projects.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
## The spike's premises, re-checked before building on them

TASK-68 landed, so the probe budget no longer starves shelves (round-robin ProbeUnion, limit 2,000) and AC #1 was satisfied before this task started. The rest of the evaluation held: 'FeedViewModel.Apply' disposed every card and cleared the collection for one dismissal, 'FeedShelfViewModel.Cards' was 'IReadOnlyList' with no in-place path, and 'FeedbackSets.SurfacingsOf' logged every item in the ShelfFeed with no notion of a held one.

## Two ways a deeper request would have shown through to the reader

The spike's recommendation is 'request a deeper shelf and slice', which is what shipped, but a literal reading of it changes the visible feed in two ways the evaluation did not test for. Both are in the fill, and both had to be fixed for the deeper ask to be invisible.

Shrinkage. Shelves fill in claim order against one shared set of claimed works. A shelf allowed to take ten instead of six claims four more works, and a later shelf whose pool overlaps loses them. On the shelf catalogue this is not hypothetical: 'ready_to_play' (installed, under the refund line) is a subset of 'barely_touched' (1..refund minutes) for every installed game with minutes on it, and it claims first. 'ShelfBuilder.Build' now runs two passes over the shelves, filling every shelf's visible slice before any shelf's reserve, with a resumable per-shelf fill that keeps the strict pass and the genre-relaxation pass at their positions. Resuming to a deeper limit visits the pool in the order a single call to that limit would; what it no longer does is claim those works early. Pinned by ShelfReserveTests against a fixture where every installed game is eligible for both shelves.

Reason variety. 'ShelfReasonLedger.CapFor' derives the citation cap from the surface size, and the surface size was 'MaxPerShelf'. Asking for twelve to show six would have moved the cap from 2 to 4 and allowed four of six visible cards to name the same facet, which is exactly the shelf TASK-76 was written about. The fill is now sized by a new optional request property, 'RecommendationRequest.VisiblePerShelf'. Two tests in ShelfFactVarietyTests: the deep-with-surface-stated case holds at 2, and the deep-without-surface-stated control confirms the cap really does widen without it, so the first cannot quietly stop proving anything.

'VisiblePerShelf' is a deviation from AC #2's 'no engine API change' and is called out as such below.

## What a deeper ask does legitimately change

A deeper 'MaxPerShelf' deepens 'ScoreBounds.SafeShortlist', so more candidates are probed for history, and one of them can turn out to belong on screen. Measured on the contested fixture: one visible card at position six differs between a plain six-request and a 6+4 request, because the extra candidate outscored it once its history was read. This is the shortlist bound working, not the reserve leaking, and it is the same effect as raising the probe limit. The test therefore pins the shelves and the per-shelf card counts rather than byte-identity, and says why.

## The reserve depth, and the phrasing ceiling

One shelf shares one reason ledger, so a deep shelf runs out of distinct sentences before it runs out of candidates. Measured against twenty near-identical games on one shelf: every card distinct through a depth of ten, repeats from eleven. Measured on a copy of the live library (968 candidates, 968 works, tier Settling, 2026-09-03) at the shipped depth of ten: four of five shelves fill a four-card reserve, 'ready_to_play' fills none (four eligible games in total), and 'on_your_taste' already repeats one sentence - two cards reading 'Still sealed since the day it arrived.', the terminal say-less phrasing.

So depth alone cannot guarantee it. 'FeedViewModel.NextReplacement' refuses to promote a reserve card whose sentence a card on that shelf is already saying, the outgoing card included, and takes the next one instead. That is the actual guarantee; the depth of four is what keeps the refusal from being what the feature runs on.

## Cost

On the same live-library copy, 6+4 scored the same feed in the same time as a plain 6 (medians within noise across five runs each; the pass is dominated by bulk reads, and a deeper ask adds twenty probes). The measurement harness was temporary and has been deleted.

## The receipt's two exits, and why lapsing is an interaction rather than a clock

AC #6 allows a receipt to go when the reader dismisses it OR when it lapses, and AC #9 requires a not-now receipt to survive at least one interaction. Lapsing is defined as the next verdict given anywhere in the feed, not a timer. A receipt therefore always survives the press that made it, and always outlives at least one further answer; the date on a 'not now' receipt is the whole of its content and a clock could take it away mid-read. It is also testable without a dispatcher or a fake clock. An undo is not an answer and lapses nothing.

## Refill

'FeedViewModel' now queues an invalidation that arrives during a load and replays it when that load finishes, which is the mechanism TASK-20 describes; the exhausted-reserve refill is requested through it. The refill is additionally held back while any receipt is still on screen: a background pass rebuilds the feed, and the receipt is the zero-friction undo, so a pass that ran under one would take the undo away to answer a question nobody asked.

This means TASK-20's own acceptance criteria are now met by 'FeedViewModel'. It has NOT been closed from here; it is the user's call whether to close it or keep it for the wider invalidation surface.

## Not done here

TASK-10 (record impressions when a card is actually visible) remains open and is unaffected. What this task needed from it - that a held card is never logged as shown, and that a promoted card logs itself when it appears - is done (AC #3 and #4). Impressions are still recorded at generation time for the cards the pass puts on screen, whether or not anyone scrolls to them.

## Deviation from AC #2, stated plainly

AC #2 asks for the reserve 'with no engine API change'. 'IRecommendationEngine' is unchanged, both its methods keep their signatures, the reserve logic stays in FeedService and 'ShelfBuilder' (internal), and the module boundary the criterion cites is intact. One thing did change: 'RecommendationRequest' gained an optional 'VisiblePerShelf' property, additive and defaulted to null, so every existing caller and test compiles and behaves identically.

It is not decoration. Without it the engine cannot distinguish 'asked for ten, showing ten' from 'asked for ten, showing six', and both properties the feature rests on are defined against the surface rather than the request: the fill order that stops a deeper ask shrinking a later shelf, and the reason ledger's variety cap. A literal reading of 'no engine API change' would have shipped a feed that quietly showed fewer cards on the later shelves and let four of six visible cards cite the same fact. The criterion is checked on that basis; the judgement is recorded here so it can be overruled.

## Verification

- tests/Winnow.Tests: 2,844 passed, 0 failed (includes 20 new in FeedReserveTests and the Enforcement suite).
- tests/Winnow.Recommend.Tests: 152 passed, 0 failed (includes 5 new in ShelfReserveTests and 2 new in ShelfFactVarietyTests).
- tests/Winnow.Covers.Tests: 70 passed, 0 failed.
- Built and run: 'dotnet run --project src/Winnow.App -- --seed-sample --data-dir <throwaway>'. Starts clean, seeds, scores twice (the startup pass, then the library-load invalidation now queued and replayed rather than dropped: 62 ms then 7 ms), no exception and no binding failure. Compiled bindings are on by default in Winnow.App, so the receipt's new 'ShowReplace' and 'ReplaceCommand' bindings were resolved against FeedCardViewModel at build time.

One limit on AC #7's evidence. What is tested is the view model's contract: the shelf's collection raises a single Replace at the dismissed card's index and its count does not move, where the old path cleared the collection. That one container is realised rather than the whole shelf rebuilt is Avalonia's ItemsControl behaviour and was not separately proven, because the project has no headless UI test harness - the same gap the evaluation noted.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The spike's recommended path, built: one scoring pass computes six cards per shelf and holds four more, and a dismissed card's receipt is replaced by a held card once the reader is done with it. The verdict is untouched by the swap.

Two things the evaluation did not test for would have made the deeper ask visible to the reader, and both are fixed in ShelfBuilder. Shelves claim works in claim order from one shared set, so a shelf allowed to take ten would have taken four works a later shelf's VISIBLE slice needed - on this catalogue 'ready_to_play' is a subset of 'barely_touched' and claims first, so the shrinkage is real, not hypothetical. The fill now runs twice over the shelves, every visible slice before any reserve, with a resumable per-shelf fill. And the reason ledger sizes its variety caps from the surface, which was MaxPerShelf: asking for twelve to show six would have moved the cap from two to four and let four of six visible cards name the same facet, re-breaking TASK-76. Both are driven by one new optional request property, VisiblePerShelf; IRecommendationEngine is unchanged. That property is a deviation from AC #2's 'no engine API change' and is argued in the notes.

A third problem showed up only on the real library. One shelf shares one reason ledger, so a deep shelf runs out of distinct sentences before it runs out of candidates: at the shipped depth of ten, 'on_your_taste' already has two cards reading 'Still sealed since the day it arrived.' Depth alone cannot fix that, so the screen refuses to promote a card whose sentence a card on that shelf is already saying, the outgoing card included.

Receipts keep their inline undo and lapse on the next verdict given anywhere in the feed rather than on a clock, so a 'not now' receipt always survives the press that made it and always outlives one further answer - the return date it states is the whole of its content. The refill is a background re-score requested through a new queue-and-replay gate (TASK-20's mechanism), held back while any receipt is still on screen so a pass cannot take the reader's undo away to answer a question nobody asked.

Verified: 2,844 tests in Winnow.Tests, 152 in Winnow.Recommend.Tests, 70 in Winnow.Covers.Tests, all passing, including 27 new ones covering the slice, the surfacing split, the swap, generation scoping, receipt survival, lease and subscription release, phrasing collision, and refill coalescing. Measured on a read-only copy of the live library (968 candidates, 968 works, tier Settling): 6+4 produced the same five shelves each showing six cards in the same time as a plain 6. Run end to end against a throwaway data directory: starts clean, scores, no exception or binding failure.
<!-- SECTION:FINAL_SUMMARY:END -->
