---
id: TASK-203
title: Cache only the minimal Steam lifecycle review projection
status: Done
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:29'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Enrich.Steam/SteamLifecycleClient.cs:78'
  - 'tests/Winnow.Tests/SteamStore/SteamLifecycleClientTests.cs:91'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 234000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R15. Evidence: Source verified. SteamLifecycleClient stores the original review response in its cache before stripping authors and review prose from EvidenceJson. The test described as verifying minimal persistence checks only returned RawJson rather than the backing cache. Unneeded third-party review text and account data remain in the local database despite the deliberately minimal evidence contract.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Persisted lifecycle cache entries contain only a versioned minimal projection required by the feature, excluding author identifiers and review prose.
- [x] #2 Previously stored raw cache entries are expired or migrated without breaking offline behavior.
- [x] #3 Cold/warm-cache integration tests inspect actual SQLite cache payloads as well as returned evidence.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Persist and parse a versioned minimal review timestamp projection before any cache write, retaining original observation timestamps. 2. Migrate all existing raw lifecycle review cache rows in a new coordinated migration, preserving valid evidence for offline cache reads and dropping malformed entries. 3. Add cold/warm SQLite payload inspection, upgrade fixtures and malformed/future-version tests; update only lifecycle evidence guidance and verify focused client tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Windows Release focused SteamLifecycleClientTests, LifecycleSyncServiceTests and GogInstallReconciliationTests passed33/33. Actual SQLite integration covers cold response persistence, reopened offline warm cache with zero requests, migration of all valid legacy rows including unvisited old entries, exact fetched_at retention and page completeness, invalid raw row deletion, and malformed/future-version cache repair. Migration0037/hash verified with all37 hashes. Review projections contain only10 approved properties and no author/profile/prose. Existing24-hour TTL and user-facing signals unchanged; shared lifecycle data serves desktop/fullscreen, so no presentation behavior change. No live HTTP or launcher data used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Persist version2 minimal review timestamp projections before caching; sanitize all legacy raw review cache rows while retaining valid observation time and fresh offline behavior. Verified33/33 focused integration/client tests and37 migration hashes.
<!-- SECTION:FINAL_SUMMARY:END -->
