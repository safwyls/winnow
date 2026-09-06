---
id: TASK-35
title: Reject zero and negative SoftMatchSweepOptions.MaxComparisons
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 22:40'
labels:
  - resolve
milestone: m-4
dependencies: []
priority: medium
ordinal: 2700
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
`SoftMatchSweepOptions.MaxComparisons` accepts zero and negative values. A zero truncates the sweep forever without progress, the same class of validation gap as F21. No finding ID; listed in stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Construction rejects zero and negative values with an `ArgumentOutOfRangeException`
- [x] #2 A test demonstrates rejection of zero and negative inputs
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect SoftMatchSweepOptions construction and all callers. 2. Validate MaxComparisons through its init path so zero and negative object initializers throw ArgumentOutOfRangeException. 3. Add a focused theory covering both invalid values. 4. Record evidence and finalize after the focused test passes.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
MaxComparisons now validates positive values through its init accessor, so object construction rejects zero and negative caps with ArgumentOutOfRangeException. Added a two-case theory. Verification awaits the parent's serialized test slot.

MaxComparisons now rejects nonpositive values in its init accessor. Release LibrarySoftMatchSweepTests pass, including zero and negative construction cases.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Reject zero and negative comparison budgets before a sweep can stall. Focused Release tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
