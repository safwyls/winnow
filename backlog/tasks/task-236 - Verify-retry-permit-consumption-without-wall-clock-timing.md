---
id: TASK-236
title: Verify retry permit consumption without wall-clock timing
status: Done
assignee:
  - codex
created_date: '2026-09-11 16:38'
updated_date: '2026-09-11 16:40'
labels: []
dependencies: []
ordinal: 268000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fix CI conformance assertion that confuses token bucket replenishment with minimum request spacing
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All registered rate-limited clients prove one acquired permit per retry attempt
- [x] #2 Provider conformance tests pass without request spacing thresholds
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Read registered token-bucket diagnostics in the test fixture, assert cumulative acquired leases, run conformance tests and push to PR 12
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
CI run 34621448772 failed only the Storefront retry spacing assertion (16.8ms gap); all migration and 469 UI tests passed. Token buckets budget permits rather than guarantee spacing across refill/scheduling boundaries. Test now reads cumulative successful leases from actual DI-registered token buckets, including inherited update-signal budgets, and requires exactly one used budget with three acquisitions. Production behavior unchanged for desktop and fullscreen. All 104 provider conformance cases pass; final strengthened assertion also passes all 14 rate-limited client cases. git diff --check passes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced flaky wall-clock retry spacing assertion with exact cumulative permit consumption from the registered provider budget. Verified 104 conformance cases plus all 14 retry-budget cases. No production code changed.
<!-- SECTION:FINAL_SUMMARY:END -->
