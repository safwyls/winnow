---
id: TASK-135
title: >-
  Build a feed replay harness that scores tuning changes against recorded
  outcomes
status: Done
assignee:
  - '@recommendation-engine'
created_date: '2026-09-06 16:19'
updated_date: '2026-09-11 08:22'
labels:
  - recommend
  - test
dependencies:
  - TASK-10
references:
  - docs/architecture-review-2026-09-10.md
priority: high
ordinal: 162000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The recommendation engine has no offline evaluation. Every weight and threshold in `RecommendationTuning` is defensible judgement rather than measured, and there is no way to answer whether a change made the feed better — which caps how far the engine can improve, because progress cannot be told apart from motion.

The data to answer it is already stored. Migration 0011 logs every surfacing in `feed_surfacings`, and section 6b derives launch endorsements from the join between `sessions.attributed_by = launch` and that log. That join is a labelled outcome set: surfaced then launched inside `EndorsementWindowDays` is a positive, surfaced then dismissed or marked not-interested is a negative, surfaced then ignored for N days is a weak negative.

A harness that replays the scorer over a frozen library at a historical date and reports ranking quality against what actually happened next turns tuning from taste into evidence, which is the empirical-over-clever mandate the module doc already claims for its design. Worth doing before the other engine changes, because it is what makes them measurable.

Depends on TASK-10 for label quality. Impressions are currently recorded at feed generation time rather than when a card becomes visible, so the weak-negative class — surfaced and ignored — cannot be trusted: a card the user never scrolled to is indistinguishable from one they saw and passed over. The positive class is unaffected, because a launch proves the card was seen, so partial results are meaningful before TASK-10 lands; the negative classes are not.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The harness replays RecommendationEngine over a database snapshot as of a given date, using only facts recorded on or before that date
- [x] #2 Recorded outcomes are classified into positive, negative and weak-negative from feed_surfacings, feed_verdicts and launch-attributed sessions
- [x] #3 The harness reports at least precision@k and MRR for a given RecommendationTuning
- [x] #4 Two different tunings can be compared over the same snapshot in a single run
- [x] #5 Leakage is prevented: no fact dated after the replay date can reach the scorer
- [x] #6 The harness ships outside the app and adds no new dependency to Winnow.Recommend
- [x] #7 The weak-negative class is derived only from impressions that record actual visibility, or is excluded from reported metrics until TASK-10 lands
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a standalone Winnow.Replay capture/compare tool using SQLite online backup, a capture instant and SHA-256 manifest; reject backdating and incompatible/tampered snapshots rather than reconstructing mutable present-day rows. 2. Pass the replay instant into lifecycle query classification and fence history, feedback and aggregate reads at that instant; preserve frozen metadata, ownership, identity and settings only from the captured database. 3. Load subsequent outcome records through a separate captured database and classify conservative positive, negative and weak-negative labels; exclude unproven visibility and ambiguous same-day ordering from scored metrics. 4. Compare two named RecommendationTuning configurations with deterministic ranking, judged-cohort precision@k, MRR and explicit coverage/limitations in a JSON report. 5. Add snapshot-integrity, backdating, late-write, future-dated evidence, label-boundary, two-tuning and real CLI regressions; document supported capture workflow and empirical limits without adding dependencies to Winnow.Recommend.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Architecture review 2026-09-10: temporal leakage remains in LibraryQueryRepository.cs:630 and :921 (wall-clock lifecycle window/classification), RecommendationEngine.cs:690 (history reads), and FeedFeedbackRepository.cs:131 (endorsement window without an as-of instant). Existing acceptance criteria #1 and #5 own these corrections; no duplicate replay task was created. See the report for evidence and shared desktop/fullscreen feed implications.

Implemented standalone tools/Winnow.Replay capture and compare commands. SQLite read transaction plus online backup pins committed WAL state; SHA-256 manifest and exact capture instant reject modification, backdating, advancing and schema drift without migrating captures. Mutable historical state is deliberately not reconstructed. Later outcomes remain a separate post-ranking input, joined through unique external IDs to frozen identity groups. Reports include both full rankings, positive/negative/weak-negative/unobserved labels, fixed label window, judged precision@k and MRR, coverage, tuning/threshold/seed inputs and assembly hashes. Legacy visibility provenance and same-day order remain unknowable, so weak negatives and ambiguous actions are excluded. Production dated library/lifecycle/history/feedback/aggregate paths now share an as-of boundary; future undo does not apply early. Windows Release gates:183/183 full recommendation suite plus19/19 final replay suite after two new cases, covering185 distinct current recommendation cases;72/72 feedback/history/lifecycle;108/108 bucket/account/acknowledgement/history;4/4 real desktop/fullscreen RecommendationCompositionTests. TRX:recommend-replay.trx,replay-final.trx,replay-query-boundaries.trx,ui-replay-composition.trx. Deterministic fixture changes judged precision@1 from0 to1 and MRR from0.5 to1 while retaining coverage0.5; this is synthetic validation, not measured product lift. Documentation:recommendation-engine section6d, docs/spikes/feed-replay.md, ROADMAP and decisions. No live APIs, credentials or real user outcomes used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added reproducible offline capture/compare tooling and corrected shared as-of evidence boundaries. Tunings compare the same immutable library with separately classified subsequent outcomes; weak or ambiguous evidence cannot become a judged negative. Verified185 distinct recommendation cases,72 feedback/history/lifecycle checks,108 query-boundary checks and4 desktop/fullscreen composition cases. Arbitrary historical state reconstruction and causal ranking-quality claims remain explicitly unsupported.
<!-- SECTION:FINAL_SUMMARY:END -->
