---
id: TASK-12
title: Validate bucket threshold invariants
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 22:18'
labels:
  - data
  - recommend
milestone: m-4
dependencies: []
priority: medium
ordinal: 1600
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Bucket and staleness query thresholds are not validated at construction time. A misconfigured threshold can silently produce wrong bucket membership. Finding F21. Source: stabilization-2026-08-28.md Group 2. Trigger: next bucket or staleness query change.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Invalid threshold values (zero, negative, inverted ranges) are rejected at construction
- [x] #2 A test demonstrates rejection of each invalid case
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Validate positive bucket floors and time windows plus strict bounced/retired ordering when constructing thresholds. Preserve immutable retuning without allowing record copies to introduce invalid values. Add construction/copy regression cases and adjust synthetic fixtures to use valid thresholds.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Release validation: all 17 BucketThresholdValidationTests passed, covering zero/negative floors and windows, equal/inverted ranges, record-copy bypasses and smallest valid ranges. Defaults remain unchanged; synthetic tile fixtures now use valid thresholds.

Batch integration: Release solution build passed with zero warnings/errors. Full suite passed 3899 tests; two stale identity-inventory assertions were corrected, then all five inventory tests passed (3902 total current tests verified). Changes to the inventory scanner retain enforcement for SQL constants and bulk snapshot callers.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Bucket thresholds enforce positive floors/windows and a retired floor above the bounced floor at construction and during record copies. Verified with 17 focused tests; documented the invariants.
<!-- SECTION:FINAL_SUMMARY:END -->
