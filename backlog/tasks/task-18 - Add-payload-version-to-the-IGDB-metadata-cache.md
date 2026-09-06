---
id: TASK-18
title: Add payload version to the IGDB metadata cache
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 22:18'
labels:
  - enrich
  - data
milestone: m-4
dependencies: []
priority: medium
ordinal: 1700
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The IGDB enrichment cache has no payload_version field. If a field is added to the cached shape, existing entries silently yield empty results for 30 days rather than triggering a refetch. A latent trap, not yet a bug. Finding F29. Sources: stabilization-2026-08-28.md Group 2; ROADMAP.md section 6. Trigger: next enrichment cache change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each cached IGDB entry carries a payload_version
- [x] #2 A version mismatch triggers a refetch instead of returning stale data
- [x] #3 A test demonstrates that bumping the version causes re-enrichment
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit existing IGDB payload versions before adding storage. Complete version coverage for external-id mappings and cached misses, preserve documented offline fallback, and test that obsolete payloads refetch rather than remaining fresh for the TTL.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit found existing versioned game, age-rating and search envelopes. Added external-mapping envelopes and versioned misses across all namespaces using the existing JSON version field, without a database migration. Legacy misses and obsolete versions refetch; compatible older positives remain available offline or on request failure. All 13 IgdbPayloadVersionTests passed in Release, including successful refetch misses replacing obsolete positives.

Batch integration: Release solution build passed with zero warnings/errors. Full suite passed 3899 tests; two stale identity-inventory assertions were corrected, then all five inventory tests passed (3902 total current tests verified). Changes to the inventory scanner retain enforcement for SQL constants and bulk snapshot callers.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed IGDB payload version coverage for external mappings and cached misses. Version mismatches refetch while preserving documented offline fallback. Verified with 13 cache-version regression tests.
<!-- SECTION:FINAL_SUMMARY:END -->
