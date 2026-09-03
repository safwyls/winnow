---
id: TASK-68
title: Raise ShelfProbeLimit so the last three feed shelves are scored
status: In Progress
assignee:
  - '@safwyl'
created_date: '2026-09-01 20:57'
updated_date: '2026-09-01 21:21'
labels:
  - recommend
dependencies: []
modified_files:
  - src/Winnow.Recommend/RecommendationEngine.cs
  - src/Winnow.Recommend/RecommendationTuning.cs
  - tests/Winnow.Recommend.Tests/ShelfProbeBudgetTests.cs
  - docs/recommendation-engine.md
  - src/Winnow.App/Services/IFeedService.cs
  - src/Winnow.App/Services/FeedService.cs
  - src/Winnow.App/Program.cs
  - src/Winnow.App/Views/MainWindow.axaml.cs
priority: high
type: bug
ordinal: 85000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
'RecommendationEngine.GetShelvesAsync' builds a union of per-shelf shortlists in shelf claim order and stops adding candidates once the union reaches 'RecommendationTuning.ShelfProbeLimit', default 150. Only candidates in that union are scored, and only scored candidates can fill a shelf. On the real library (1005 bucket rows, 982 candidates, 958 works, tier Settling) the first two shelves, 'patched_while_away' and 'worth_another_look', consume the entire budget. The remaining three shelves, 'ready_to_play', 'barely_touched', and 'on_your_taste', are never scored and never appear. Three-fifths of the designed feed is missing on a realistic library.

Measured: the default produces 2 shelves and 12 items in 69 ms. At ShelfProbeLimit 300, 4 shelves and 22 items in 98 ms. At 600, 5 shelves and 28 items in 103 ms; the natural union settles at 356 probes, so 600 is a ceiling that never binds. At 1200, the same 356 probes and the same feed. Restoring the full feed costs 34 ms on a pass that already runs off the UI thread inside 'Task.Run'. The per-row probes are not the bottleneck: 0.04 ms per snapshot-and-session pair, 0.02 ms per update-event read. The pass is dominated by bulk reads (facet snapshot 30.7 ms, bucket query 17.5 ms).

The probe limit exists to stop the shelf pass from reading per-ownership history for the whole library. That purpose is preserved at 600: the natural union is 356, well under 958 works. The current default of 150 is documented in 'docs/recommendation-engine.md' section 5 as "the probe union is capped at 150 ownerships, five shelves legitimately probe more than one list does, but never the whole library." The number is too small for its own stated intent. The doc's tuning table should be corrected with the measured figures when the default changes.

This shortfall was found while evaluating the feed-card swap feature. Any reserve or deeper shelf request makes it worse, because it enlarges the per-shelf shortlists that exhaust the budget first. The probe limit must be raised before or alongside any reserve work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 ShelfProbeLimit default is raised so all five shelves are scored on the real library at its current scale
- [ ] #2 The natural probe union at the new limit does not approach the total candidate count, preserving the limit's stated purpose
- [ ] #3 The feed produces all five designed shelves on a library of 958 or more works
- [ ] #4 The tuning table in docs/recommendation-engine.md section 5 is updated with the new default and the measured figures
- [ ] #5 A test verifies that all shelves populate when the candidate pool is large enough to fill them
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce on a read-only COPY of the live database (never the live file) with a temporary benchmark harness in tests/Winnow.Recommend.Tests: confirm 2 shelves / 12 items at the current default, and record the natural (uncapped) probe union plus the per-probe and fixed bulk-read costs.
2. Diagnose the SHAPE, not just the number. Root cause: the union is filled shelf-by-shelf in claim order, and the cap (150) equals the sum of the five per-shelf comfort floors (5 x MaxPerShelf x ShelfOverfetchFactor with MaxPerShelf=10). ScoreBounds.SafeShortlist deliberately EXCEEDS that floor whenever the bound says a row could still place, so the first two shelves spend the whole budget and the last three get literally zero. Any flat cap applied in claim order has this failure mode.
3. Fix structurally: interleave the per-shelf shortlists ROUND-ROBIN (shelf 5's first candidate is admitted before shelf 1's second) so a binding cap truncates every shelf's tail evenly instead of deleting whole shelves. Starvation becomes impossible at ANY cap, which is what makes the number a tuning choice again rather than a correctness dependency.
4. Re-derive the cap as a pure cost brake against the pathological library it was always meant to bound, using measured per-probe cost against the pass's irreducible bulk-read cost. Document the derivation on the parameter; do not take a number off the recon table.
5. Regression tests (the point of the task): (a) a large synthetic library where claim-order exhaustion provably occurs, asserting EVERY configured shelf is scored and non-empty - this must fail against the current claim-order fill; (b) a fairness test that pins the shape rather than the number, by driving a deliberately tiny ShelfProbeLimit and asserting every shelf still receives probes, so a future retune cannot reintroduce starvation.
6. Delegate all prose to docs-writer: docs/recommendation-engine.md section 5 tuning row (new default + measured figures) and section 6a if affected; the ~500 ms claim in IFeedService (measured 69 ms warm) and its two echoes in FeedService and MainWindow.axaml.cs; and every code/XML comment touched.
7. Build and test via --artifacts-path into the scratchpad (the user is running Winnow and holds src/Winnow.App/bin): scoped Winnow.Recommend.Tests first, then the FULL suite across all three test projects. Re-measure the shelf pass on the copy after the change and record the cost. Do not commit; do not finalize.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
## What the limit was protecting against, and whether it still does

The cap bounds how many ownerships one shelf pass reads per-row history for (snapshots, sessions, update events). Its stated purpose in docs/recommendation-engine.md section 5 was "never the whole library". Measured on a read-only COPY of the live database (990 candidates, 966 works, tier Settling; the live file was never opened): that claim was never the cap's doing. The natural uncapped probe union is 376 of 966 works, and what holds it there is the score bound in ScoreBounds.SafeShortlist, not the ceiling. The cap is a backstop for a library where the bound stops discriminating, and at 2,000 it still is one - it just does not bind at this scale, which is correct for a brake that is meant to bound a pathological library rather than trim a normal one.

The number 150 was the sum of the five per-shelf comfort floors (5 x MaxPerShelf x ShelfOverfetchFactor). SafeShortlist deliberately EXCEEDS that floor whenever the bound says a row could still place, so the budget was sized against the floor and spent against the bound. Per-shelf uncapped demand on the real library: patched_while_away 27, worth_another_look 147, ready_to_play 4, barely_touched 184, on_your_taste 18.

## The shape chosen, and why

A flat cap applied in claim order is the bug, not the number: it deletes whole later shelves instead of trimming each shelf's tail, and a bigger number only postpones that. The fix is structural. RecommendationEngine.ProbeUnion now interleaves the per-shelf shortlists round-robin, rank by rank - every shelf's best candidate is admitted before any shelf's second best - deduplicated by ownership id. Starvation is now impossible at ANY cap: measured, all five shelves populate on the real library even at a budget of 5.

A per-shelf floor parameter was considered and rejected: the interleave gives every shelf its fair share with no new parameter to keep in step with the shelf count, and it degrades correctly when a shelf's pool is short (ready_to_play has only 4 eligible rows, and its unused rounds fall through to the shelves that still have depth).

With fairness structural, cost is the cap's only remaining job, so the number is derived from cost. Measured on the copy: the shelf pass costs 46.6 ms median with the budget at zero (it is dominated by bulk reads, not probes) and 23 microseconds per probe. 46.6 / 0.023 = 2,026, so 2,000 is the point at which per-row history reading would cost as much as the bulk reads it rides on - where the pass would double. 600 was not taken from the recon table.

## Regression tests

tests/Winnow.Recommend.Tests/ShelfProbeBudgetTests.cs, seeded once per class (IClassFixture + IAsyncLifetime) over ~217 games with five deliberately disjoint shelf pools, sized so the two early shelves' shortlists alone exceed the old default.

- Every_shelf_is_scored_when_the_probe_budget_binds, a Theory over probe limits 5, 25 and 150. Asserts HistoryProbeCount == limit first (so a fixture that stopped exhausting the budget fails loudly instead of passing vacuously), then that all five shelves are populated. Pins the SHAPE, not the number.
- The_default_probe_budget_does_not_change_the_feed_it_bounds. Compares the default-tuning feed to the same pass with the budget removed (int.MaxValue) and asserts identical shelves, items and order. This is the half of the bug that survives once starvation is impossible: a default too small for the library's natural probe demand silently changes which games are recommended.

Verified they catch the regression, not just the fix. Against the exact pre-fix state (claim-order fill + default 150) all 4 fail, and the limit-150 case reproduces the reported bug verbatim - Actual: ["patched_while_away", "worth_another_look"]. Against the fixed shape but the OLD default of 150, the three fairness cases pass and the default test still fails, so the two tests cover the two halves independently.

## Measured cost after the change

Interleaved A/B in one process on the copy: 150 probes 57.5 ms median, 376 probes 60.4 ms median - 2.9 ms for the extra 226 probes, on a pass that already runs off the UI thread inside Task.Run. The full five-shelf feed is about 3 ms more expensive than the two-shelf feed it replaced, not the 34 ms the recon estimated (that figure included warm-up drift). At the new default the feed is 5 shelves / 28 items / 376 probes.

## Documentation

All prose delegated to docs-writer. docs/recommendation-engine.md section 5 tuning row now reads 3 / 2,000 with the cost derivation, and section 4a gained a paragraph on the probe union's interleave. The false sentence ("the probe union is capped at 150 ownerships ... but never the whole library") is gone. The IFeedService "~500 ms" claim and its three echoes (FeedService, Program.cs, MainWindow.axaml.cs) now say ~60 ms measured warm, keeping the off-UI-thread argument, which never depended on the number being large.

## Verification

Built and tested via --artifacts-path into the scratchpad; src/Winnow.App/bin was not touched and the running app was not disturbed. Benchmarks ran against a copy of the database, never the live file. Full suite across all three test projects: Winnow.Covers.Tests 70 passed, Winnow.Recommend.Tests 102 passed, Winnow.Tests 2463 passed, 0 failed.

## Adjacent drift found, NOT fixed (out of scope)

docs/recommendation-engine.md section 5 still lists ShelfGenreCap as 4 "per 10-item shelf" while the code is 3 and MaxPerShelf is 6; RecommendationRequest.MaxPerShelf carries an XML comment beginning "10:" against a value of 6; section 6a says "the patched shelf's ten slots". These predate this task and were left alone.
<!-- SECTION:NOTES:END -->
