---
id: TASK-20
title: Coalesce feed invalidation so events are not dropped during active load
status: Done
assignee:
  - '@beta_ui'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 22:23'
labels:
  - recommend
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 1900
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Feed invalidation events that arrive while a load is already in progress can be dropped, leaving the feed stale until the next manual or timed refresh. Finding F34. Source: stabilization-2026-08-28.md Group 2. Trigger: next feed refresh change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An invalidation event received during an active load is queued and replayed after the load completes
- [x] #2 A test demonstrates that rapid invalidation during load does not lose the final state
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reproduce an invalidation while the feed service holds an active scoring pass. 2. Assert the pending invalidation triggers exactly one replay and that the final snapshot reaches the shelves. 3. Run the focused feed test suite in the shared build slot, record the evidence, then finalize.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Validation: focused Release FeedViewModelTests passed 19/19 in C:\Temp\winnow-beta-final. The regression holds the first feed read, sends three tile-change invalidations, and proves one replay applies the final snapshot.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a gated regression for coalesced feed invalidation. Verified the replay and final shelf state with the focused FeedViewModel suite (19 passed).
<!-- SECTION:FINAL_SUMMARY:END -->
