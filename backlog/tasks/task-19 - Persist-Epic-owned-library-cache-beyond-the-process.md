---
id: TASK-19
title: Persist Epic owned-library cache beyond the process
status: Done
assignee: []
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 22:40'
labels:
  - ingest
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 1800
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Epic owned-library cache lives only in memory and is lost when the process exits. This forces a full re-fetch on every launch when authenticated. Finding F31. Source: stabilization-2026-08-28.md Group 2. Trigger: next Epic ownership change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Epic library cache persists to disk under the data directory
- [x] #2 A restart reads the cache without re-fetching, until the configured staleness interval expires
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect the Epic cache contract and existing persistent cache pattern. 2. Add a data-directory-backed library cache with atomic, failure-tolerant reads and writes, then register it in the app composition root as needed. 3. Add restart/staleness fixture tests and record focused validation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added an App-layer SQLite implementation of IEpicLibraryCache, registered by the composition root, with a canned-handler restart test that asserts no library HTTP request after restart.

Validation: parent ran the Release focused suite (268 passing tests), including EpicLibraryTests and CacheTimestampBoundaryTests. The restart fixture uses a shared temporary SQLite cache and observes zero library HTTP requests after client recreation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Persisted the Epic owned-library cache in the data-directory SQLite database and registered it in the app. The Release focused suite verified restart reuse without a library refetch.
<!-- SECTION:FINAL_SUMMARY:END -->
