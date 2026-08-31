---
id: TASK-1
title: Validate M5 feed improvement on a live library
status: In Progress
assignee:
  - '@claude'
created_date: '2026-08-29 21:51'
updated_date: '2026-08-30 00:17'
labels:
  - recommend
  - enrich
  - data
milestone: m-0
dependencies: []
priority: high
ordinal: 1000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
M5's exit criterion is half-proven: backfill is tested but feed improvement on a cold library has not been measured with a live user key. Run the backfill against the user's real Steam library and measure feed quality before and after. Source: ROADMAP.md section 4, M5 row ("exit criterion half-proven, feed improvement awaiting live validation with user's key").
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Backfill runs against a real Steam library using the user's Web API key
- [ ] #2 Feed quality is measured before and after backfill on the same library
- [ ] #3 Measurable improvement is demonstrated or the criterion is revisited with documented findings
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Read-only verification of the live winnow.db: historical snapshot series present for 2022+, per-ownership monotonicity, first-played records, per-(account,year) completion markers. 2. Report data-level evidence to the user. 3. User evaluates feed quality change (the criterion's subjective half). 4. Record findings in task notes; finalize per the finalization guide.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only verification of the live database after the user's backfill run (2026-08-30): all mechanical checks passed. 141 of 141 reconstructed series converge exactly on their current playtime anchor; zero monotonicity violations attributable to the backfill; zero duplicate rows under the 0013 snapshot identity; the never-newest guard held across 633 historical play records; re-running the backfill wrote nothing. Bucket membership changed for zero ownerships, and structurally cannot: the bucket query's `latest_play` CTE reads `play_records` only, so backfill rows in `playtime_snapshots` are invisible to it. The backfill's value landed in the recommender's episode signal instead. Ownerships with at least one playtime rise went from 5 to 76, total rises from 23 to 150, multi-point series from 5 to 146. Nine of the 23 prior rises were phantom sawtooth artifacts from a one-minute source disagreement (see TASK-50), so the true pre-backfill baseline was lower than reported. Remaining for the exit criterion: the user's assessment of feed quality, which flows through scoring, not buckets.
<!-- SECTION:NOTES:END -->
