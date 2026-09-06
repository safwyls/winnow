---
id: TASK-139
title: >-
  Implement ILibraryHistoryStatsRepository so tier detection stops running on a
  sample
status: Done
assignee:
  - '@codex'
created_date: '2026-09-06 16:20'
updated_date: '2026-09-06 22:39'
labels:
  - recommend
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 2100
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
docs/recommendation-engine.md section 9 records that `ILibraryHistoryStatsRepository` has no `Winnow.Data` implementation. The engine takes it as an optional constructor argument and falls back to a deterministic uniform draw of `TierSampleOwnerships` rows scaled back up to the library.

Section 6 states what that costs: the session count and span that decide Tier 2 are compared against a scaled figure rather than a real total, `LibraryHistoryStats.IsEstimate` has to be threaded through so a scaled number is never shown to a user as a total, and every feed pays for the sample at 120 ownerships times two point reads.

The seam was designed for this, so registering a real implementation changes the precision of the tier and nothing about its meaning. Small, self-contained, and it removes a per-feed cost.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Winnow.Data implements ILibraryHistoryStatsRepository with exact aggregate queries over the whole library
- [x] #2 The implementation is registered in the composition root and the engine uses it
- [x] #3 LibraryHistoryStats.IsEstimate is false when the aggregate answered and true when the sample did
- [x] #4 The sampling fallback still works and is still tested when the repository is absent
- [x] #5 The per-feed sampling reads no longer occur when the aggregate is registered
- [x] #6 Section 6 and section 9 are updated to describe the implemented state
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify the existing aggregate repository, engine wiring, and sampling fallback against the design and tests. 2. Add any missing enforcing coverage for exact aggregates, IsEstimate, and the no-sampling aggregate path. 3. Coordinate composition-root and design-document updates with the parent agent. 4. Record evidence and finalize after all acceptance criteria are objectively met.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified in source: LibraryHistoryStatsRepository already provides one exact whole-table aggregate query, RecommendationEngine uses it when registered and retains the deterministic sample fallback when absent, and Program already registers it. Existing LibraryHistoryStatsTests cover empty, session extremes, rising snapshots, and IsEstimate=false. Parent agent must update docs/recommendation-engine section 9 (section 6 already describes aggregate and fallback) and run serialized tests before finalization.

Added A_registered_aggregate_skips_tier_sampling_point_reads: injected counting snapshot/session repositories compare identical aggregate and absent-repository feeds; the aggregate path is asserted to read fewer history points than the fallback's ten-row sample.

Confirmed existing Data aggregate and App registration. Added read-count regression proving the registered aggregate bypasses tier-sampling reads while absent-repository fallback remains tested. Updated recommendation spec sections6/9. Release build and focused LibraryHistoryStats/MaturityTier tests pass in the combined268main+22recommend run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Verified the existing exact aggregate path, added explicit sampling-bypass evidence and corrected stale wiring documentation. Exact and fallback tier tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
