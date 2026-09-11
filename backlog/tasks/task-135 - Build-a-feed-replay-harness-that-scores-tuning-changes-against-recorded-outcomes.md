---
id: TASK-135
title: >-
  Build a feed replay harness that scores tuning changes against recorded
  outcomes
status: To Do
assignee: []
created_date: '2026-09-06 16:19'
updated_date: '2026-09-11 05:07'
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
- [ ] #1 The harness replays RecommendationEngine over a database snapshot as of a given date, using only facts recorded on or before that date
- [ ] #2 Recorded outcomes are classified into positive, negative and weak-negative from feed_surfacings, feed_verdicts and launch-attributed sessions
- [ ] #3 The harness reports at least precision@k and MRR for a given RecommendationTuning
- [ ] #4 Two different tunings can be compared over the same snapshot in a single run
- [ ] #5 Leakage is prevented: no fact dated after the replay date can reach the scorer
- [ ] #6 The harness ships outside the app and adds no new dependency to Winnow.Recommend
- [ ] #7 The weak-negative class is derived only from impressions that record actual visibility, or is excluded from reported metrics until TASK-10 lands
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Architecture review 2026-09-10: temporal leakage remains in LibraryQueryRepository.cs:630 and :921 (wall-clock lifecycle window/classification), RecommendationEngine.cs:690 (history reads), and FeedFeedbackRepository.cs:131 (endorsement window without an as-of instant). Existing acceptance criteria #1 and #5 own these corrections; no duplicate replay task was created. See the report for evidence and shared desktop/fullscreen feed implications.
<!-- SECTION:NOTES:END -->
