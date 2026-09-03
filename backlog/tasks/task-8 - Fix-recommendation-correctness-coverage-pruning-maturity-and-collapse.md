---
id: TASK-8
title: 'Fix recommendation correctness: coverage, pruning, maturity, and collapse'
status: In Progress
assignee:
  - '@claude'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-01 02:14'
labels:
  - recommend
dependencies: []
priority: high
ordinal: 8000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Four recommendation scoring defects. Missing genre/tag coverage is treated as negative evidence instead of absent evidence (F15). Score-bound shortlist pruning can drop valid candidates (F32). The maturity tier is biased by library composition (F33). Work collapse must happen before shortlist capacity is applied, not after (F38). Sources: stabilization-2026-08-28.md Group 2, findings F15, F32, F33, F38. Trigger: next recommender scoring change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A game with no coverage data scores neutrally, not negatively
- [ ] #2 Shortlist pruning preserves candidates above the quality threshold regardless of score proximity
- [ ] #3 Maturity tier distribution is normalized against library composition
- [ ] #4 Work-level collapse precedes shortlist capacity enforcement
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. F15 coverage: add UpdateCoverage tri-state to CandidateFacts (Unknown/Observed). Engine proves coverage from recorded update_events for the release (any announcement row = Winnow read that release's retroactive news history). Scorer applies the probably-done penalty ONLY on Observed coverage; the explanation never asserts 'nothing changed' otherwise.
2. F32 score-bound safe pruning: new ScoreBounds helper. MaxHiddenBonus(c) = 0 for already-probed rows and for never-opened rows (no hidden play episodes are possible), else WeightTriedToLikeIt. LowerBound(c) = prelim - possible hidden ProbablyDone penalty. Shortlist = every candidate whose prelim + MaxHiddenBonus >= the kth largest LowerBound, unioned with the comfort top-N. Adversarial leapfrog test: a candidate outside the old 3x shortlist whose hidden return-episode history wins.
3. F33 unbiased maturity tier: tier moves off the candidate/recent probe. New Winnow.Core contract ILibraryHistoryStatsRepository -> LibraryHistoryStats (global session count, first/last session, ownerships with multiple snapshots), optional ctor dep. Fallback when absent: deterministic uniform sample of ALL library ownerships (stable hash, TierSampleOwnerships), session count scaled to the library. Per-candidate history stays in the shortlist probe. Test: sessions spread over more titles than the sample.
4. F38 collapse before capacity: group candidates by WorkId BEFORE shortlist probing and keep one representative per work chosen by (upper bound desc, prelim desc, installed desc, releaseId asc) so the collapse cannot discard the copy that would have won. StoreCount/bought-twice is already computed per work over every ownership, so it survives; the representative carries the store choice. Same collapse on the shelves path.
5. Tests one per defect + full suite across Winnow.Tests, Winnow.Recommend.Tests, Winnow.Covers.Tests.
6. docs/recommendation-engine.md updated by docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented 2026-08-31, not finalized.

F15 (missing coverage read as negative evidence). New UpdateCoverage tri-state on CandidateFacts (Unknown/Observed). Coverage is proven by one recorded ANNOUNCEMENT update_event for the release: Steam's news endpoint serves a release's whole history rather than a window, so one stored announcement means Winnow has seen that release's update history and would have recorded anything later. RecommendationScorer.HasProbablyDoneShape was split out of the penalty so the engine can decide which rows are worth an update read at all; the penalty now requires HasProbablyDoneShape AND UpdateCoverage.Observed. Without proof the penalty is withheld entirely and nothing claims silence. EnrichAsync reads update events for stale_but_patched rows (as before) plus probably-done-shaped rows only, so the extra query stays bounded.

F32 (score-bound-unsafe pruning). New internal ScoreBounds. MaxHiddenBonus is WeightTriedToLikeIt, or exactly 0 for an already-probed row and for a row with no minutes and no play date (sessions and snapshot rises both imply playtime, so that zero is exact, not cautious). MaxHiddenPenalty is PenaltyProbablyDone where a probe could still reveal coverage on a probably-done-shaped row - probing can now LOWER a score, which the old monotonicity argument did not allow for. SafeShortlist drops a candidate only when Upper(c) < the k-th largest Lower over the pool, unioned with the old fixed slice as a comfort floor. Applied to the flat feed and to each shelf's slice. Measured on a 200-work library shaped like the real one (120 sealed, 60 bounces, 20 patched): probed 60 works, exactly the comfort floor, so correctness cost nothing.

F33 (biased maturity tier). Tier detection no longer reads the candidate probe at all. New Winnow.Core contract ILibraryHistoryStatsRepository -> LibraryHistoryStats (session count, first/last session, ownerships with snapshot rises), taken as an OPTIONAL constructor argument on RecommendationEngine; when supplied it is used verbatim. When absent, a deterministic uniform draw of TierSampleOwnerships (120, salted by TierSampleSeed) over every ownership that could hold history is scaled back to the library, with the directly observed count - which can never exceed the truth - as a floor. Rows with no minutes and no date are excluded from the draw: exact stratification, not bias. Per-candidate history stays in the shortlist probe and is memoised per request by a new HistoryReader so the two passes never pay twice for a row.

F38 (duplicates consuming shortlist capacity). ScoreBounds.CollapseByWork runs BEFORE any capacity is spent, on both entry points. Survivor is chosen by (Upper desc, score desc, installed desc, releaseId asc) so the collapse cannot discard the copy that would have won once history was read. Bought-twice is unaffected - store counts are computed per work over every ownership in the library before candidates are assembled - and the survivor carries the store choice. RecommendationFeed/ShelfFeed gained WorkCount and HistoryProbeCount as derived diagnostics (nothing stored).

Tests, all in tests/Winnow.Recommend.Tests: UpdateCoverageTests.cs (3, F15 end to end), ScorerTests.Probably_done_needs_proven_update_coverage_and_never_claims_silence_without_it (F15 unit), ShortlistBoundTests.cs (4: adversarial leapfrog, the bound in isolation, F38 collapse, probe budget on a realistic 200-work distribution), MaturityTierTests.cs (3, including sessions spread across 100 titles behind 70 louder candidates). Existing tests updated: AntiPatternTests and ShelfFeedTests now seed update coverage where they assert the probably-done verdict - without it the model is no longer entitled to that verdict, which is the point of F15.

Winnow.Recommend.Tests 92/92 green. Winnow.Covers.Tests 70/70 green. Winnow.Tests 2369/2370 - the one failure (DatabaseBackupTests.A_pending_migration_writes_a_backup_named_for_the_schema_it_replaces) is NOT from this work: TASK-5 added src/Winnow.Data/Migrations/0016_merge_canonicality.sql and that test's Rewind helper only undoes 0012-0015 (its own comment says 'A new migration adds a line'). Left for TASK-5.

REPORTED, NOT MADE: Winnow.Data needs a LibraryHistoryStatsRepository implementing ILibraryHistoryStatsRepository (one aggregate over sessions plus an EXISTS for ownerships with a snapshot rise) and Program.cs a registration, so the tier stops being an estimate. Not made because TASK-5 holds Winnow.Data.
<!-- SECTION:NOTES:END -->
