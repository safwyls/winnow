---
id: TASK-6
title: Replace UI-thread N+1 queries with bulk read models
status: Done
assignee:
  - '@timestamp_boundaries'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 22:18'
labels:
  - ui
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 1300
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The library and startup view models issue per-item queries on the UI thread. Replace these with bulk read models that load in a single round trip. Finding F13. Source: stabilization-2026-08-28.md Group 2. Trigger: next library or startup view-model change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Library view populates from a single bulk query, not per-item fetches
- [x] #2 No repository call executes on the UI thread
- [x] #3 Startup time does not regress
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Replace per-work release and per-release ID queries with one library snapshot command returning bucket facts, works, ownerships, releases, IDs and lists. 2. Load snapshot and ancillary startup metadata on a worker before publishing UI state; bulk-load list membership. 3. Preserve existing bucket consolidation, artwork precedence and list ordering. 4. Verify query count, UI-thread isolation, real SQLite equivalence and representative startup timing; coordinate full integration build.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented one multi-result library snapshot command in a deferred read transaction, reusing bucket consolidation and repository column projections. Library and Review loads no longer fetch releases, IDs, ownerships or work records per item. Library, Review, Display and LibrarySettings startup repository reads run on workers before UI publication. List memberships load in bulk. Existing focused tests: 153 relevant library/Review/list/settings cases pass; both new snapshot tests pass after correcting deep result comparison. Cached 1000-game read: previous 2003 leases/3705.3 ms versus snapshot 1 lease/20.6 ms. Method and scope recorded in docs/spikes/library-bulk-read.md. Headless startup thread-isolation test pending shared UI project compilation.

Headless StartupReadThreadTests passed in the coordinated UI run: guarded real SQLite repositories were never entered on the UI thread across Library, Review, Display and LibrarySettings loads, while published view-model notifications remained on UI. Validation totals for this task: 155 focused main tests and one headless startup isolation test. Shared design documentation integrated by coordinator. Pre-window appearance initialization and unrelated editing commands are outside this startup-load change; timing measures cached library data loading, not total process launch.

Integrated architecture inventory updated for the new reader boundaries: BucketSql remains resolving; GetSnapshotAsync explicitly preserves raw metadata beside its already-resolving bucket result; Library and Review LoadAsync are the resolving presentation readers. Scanner now names SQL constants correctly and detects ILibraryQueryRepository.GetSnapshotAsync callers. All five inventory tests pass, including a new negative control proving both kinds are caught and named.

Batch integration: Release solution build passed with zero warnings/errors. Full suite passed 3899 tests; two stale identity-inventory assertions were corrected, then all five inventory tests passed (3902 total current tests verified). Changes to the inventory scanner retain enforcement for SQL constants and bulk snapshot callers.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced library and Review per-item reads with a shared bulk snapshot and bulk list memberships. Startup reads run on workers and publish on UI. Verified 155 focused main tests plus headless thread-isolation coverage. A 1000-game cached data load improved from 3705.3 ms / 2003 leases to 20.6 ms / 1 lease; measurement method and limits are in docs/spikes/library-bulk-read.md.
<!-- SECTION:FINAL_SUMMARY:END -->
