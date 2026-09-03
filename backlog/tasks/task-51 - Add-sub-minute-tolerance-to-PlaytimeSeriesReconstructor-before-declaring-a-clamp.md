---
id: TASK-51
title: >-
  Add sub-minute tolerance to PlaytimeSeriesReconstructor before declaring a
  clamp
status: Done
assignee:
  - '@claude'
created_date: '2026-08-30 00:17'
updated_date: '2026-08-30 01:02'
labels:
  - enrich
dependencies: []
priority: medium
ordinal: 51000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`PlaytimeSeriesReconstructor` widens the anchor from minutes to seconds for its backward walk, but `playtime_forever` arrives in whole minutes while Year-in-Review months arrive in seconds. When a game's entire cumulative playtime falls inside a single covered month, the month's seconds can exceed the anchor's floored-to-minutes value by up to 59 seconds, driving the running total below zero and triggering the clamp. The clamped series loses its floor point and emits no episode signal. On the live database this cost 70 of 141 series their signal. The series are not incorrect (the clamp is conservative by design), but a tolerance of under 60 seconds before declaring the clamp would recover them without compromising the safety the clamp provides.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A month whose delta exceeds the running total by fewer than 60 seconds does not trigger a clamp; the running total floors to zero instead
- [x] #2 Previously clamped single-month series produce a floor point and a non-empty reconstruction
- [x] #3 Monotonicity is preserved: no emitted point is higher than its successor in the cumulative sequence
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify the claim on the live database, read-only. DONE: 141 distinct ownerships carry Year-in-Review month-end snapshots (observed_at ending 23:59:59), and 70 of them hold exactly ONE point. A single-month series that does not clamp emits two points - the month itself plus the pre-coverage floor - so one point means clamped. 70 of 141 confirmed exactly as the description states.

2. The arithmetic. Reconstruct widens the anchor with 'Math.Max(0, anchorMinutes) * 60L', but playtime_forever arrives already floored to whole minutes while the months arrive in seconds. A game whose whole cumulative history sits inside one covered month therefore reports up to 59 seconds more in that month than the floored anchor can hold, 'previous' goes negative by that remainder, and the clamp fires on what is a unit-width rounding artefact rather than a real disagreement between Valve systems.

3. Add ToleranceSeconds = 60 to PlaytimeSeriesReconstructor. When 'previous' is negative but the shortfall is fewer than 60 seconds, floor the running total to zero and continue the walk instead of setting clamped and breaking. Per the err-low principle the floor point takes the lower value, which is zero. A shortfall of 60 seconds or more is a genuine disagreement and still clamps, unchanged.

4. Because the walk continues rather than breaking, the not-clamped branch runs and emits the pre-coverage floor point at ordered[0].PrecedingMonthEndUtc with value zero, and RemainderMinutes becomes 0 rather than null. That is correct: the covered months explain the whole total, so there is no pre-coverage remainder, and zero is a fact rather than the unknown the clamp reports.

5. Monotonicity survives by construction. Each point is emitted before its month is subtracted, and flooring the running total to zero can only lower what follows in the backward walk, so the reversed sequence is still non-decreasing.

6. Do not add a field to PlaytimeSeriesReconstruction. Clamped keeps its meaning - the walk stopped - and a tolerated shortfall is not a stop.

7. Tests in PlaytimeSeriesReconstructionTests: a single-month series exceeding the anchor by under a minute produces two points, the older being the zero floor, and is not clamped; the boundary at exactly 59 and exactly 60 seconds of shortfall lands on either side; monotonicity is asserted over the tolerated series; the two existing clamp cases, which overshoot by 280 minutes and by 100 minutes, still clamp untouched.

8. Scoped tests, then the full suite across all three test projects.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
VERIFIED ON THE LIVE DATABASE (read-only, Mode=ReadOnly, zero writes).
141 distinct ownerships carry Year-in-Review month-end snapshots (observed_at ending
23:59:59) and exactly 70 of them hold a single point. A single-month series that does not
clamp emits two points - the month itself plus the pre-coverage floor - so one point means
clamped. The 70-of-141 figure in the description is confirmed exactly.

IMPLEMENTED (not finalized - left for orchestrator review).

src/Winnow.Enrich.SteamWeb/Model/PlaytimeSeriesReconstruction.cs
- New 'public const long ToleranceSeconds = 60'.
- New branch before the clamp: 'if (previous < 0 && previous > -ToleranceSeconds)' floors the
  running total to zero and CONTINUES the walk rather than setting clamped and breaking.
  A shortfall of 60 seconds or more still clamps, unchanged.
- Because the walk continues, the not-clamped branch runs and the pre-coverage floor point is
  emitted at ordered[0].PrecedingMonthEndUtc with value zero, and RemainderMinutes is 0
  rather than null. Zero is a fact here - the covered months explain the whole total - not
  the unknown a clamp reports.
- Per the err-low principle the floor point takes the LOWER value, which is zero.
- No field added to PlaytimeSeriesReconstruction. Clamped keeps its meaning: the walk
  stopped. A tolerated shortfall is not a stop.

Monotonicity (AC 3) holds by construction: each point is emitted BEFORE its month is
subtracted, and flooring the running total to zero can only lower what the backward walk
emits next, so the reversed sequence is still non-decreasing. Asserted directly.

Tests added to tests/Winnow.Tests/SteamWeb/PlaytimeSeriesReconstructionTests.cs:
- A_month_overshooting_the_floored_anchor_by_under_a_minute_floors_instead_of_clamping
  (AC 1, AC 2) - anchor 5 minutes against a 340-second month, a 40-second shortfall.
  Two points out, the older being the zero floor, Clamped false, RemainderMinutes 0.
- The_tolerance_stops_at_sixty_seconds (AC 1 boundary) - 359 seconds tolerated, 360 clamps.
- A_tolerated_shortfall_keeps_the_series_monotonic (AC 3) - a mid-walk tolerated shortfall,
  monotonic minutes, monotonic timestamps, no negative point.

The two existing clamp cases are untouched and still pass:
A_backward_walk_that_would_cross_zero_clamps_and_stops overshoots by 280 minutes and
A_zero_anchor_against_claimed_play_keeps_only_the_present by 100 - both far outside the
tolerance, so the conservative clamp still does its job.

Deliberately NOT affected: SteamPlaytimeBackfillService writes reconstructed points straight
through the repositories rather than through the resolver, so TASK-50's resolver-level
tolerance cannot touch these series. Confirmed on the live database - the five non-sawtooth
one-minute rises (ownerships 263 Astroneer, 277 Fallout 4, 452 Baldur's Gate III,
496 Len's Island, 555 Cloudheim) are genuine month-to-month reconstruction points and one
real session, and none of them is absorbed.

All comment and XML-doc prose in the changed files was authored by the docs-writer agent.
Not committed.

Full suite green: 2034 + 74 + 70 passed, 0 failed, 0 warnings under TreatWarningsAsErrors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
A 60-second tolerance in PlaytimeSeriesReconstructor floors the running total to zero and continues the walk instead of clamping, restoring the floor point to single-month series (70 of 141 on the live database were affected, verified 2026-08-30); verified by 3 new reconstruction tests including the previously clamped single-month case now emitting a floor point, monotonicity preserved, full suite green.
<!-- SECTION:FINAL_SUMMARY:END -->
