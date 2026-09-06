---
id: TASK-11
title: Make repository batches transactional
status: Done
assignee:
  - '@codex'
created_date: '2026-08-29 21:52'
updated_date: '2026-09-06 21:19'
labels:
  - data
milestone: m-4
dependencies: []
priority: high
ordinal: 200
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Logical batches of repository writes do not commit atomically. A failure mid-batch can leave the database in an inconsistent state. Finding F18. Source: stabilization-2026-08-28.md Group 2. Trigger: next repository write path added or reworked.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A logical batch of writes commits or rolls back as a unit
- [x] #2 A simulated failure mid-batch leaves no partial state in the database
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Address the three F18 batches: facet replacement, list reordering, and feed surfacing recording. Add a repository batch scope using a local transaction outside a unit of work and a savepoint inside one. Inject database failures after facet deletion and during order/history writes; verify standalone rollback, ambient rollback, and successful commit. Run focused and full solution checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
F18 evidence identified exactly three repository batches: FacetRepository.SetAsync (work and release facets), GameListRepository.ReorderAsync, and FeedFeedbackRepository.RecordSurfacedAsync. All now use a shared local transaction/savepoint scope. 58 focused tests passed, including 17 new cases for injected failures after deletion or first write, preserving unrelated outer writes after a caught failure, outer commit/rollback, and cancellation during enumeration. Existing success/idempotence tests remain passing.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Made the three F18 repository batches atomic: work/release facet replacement, list reordering and feed surfacing recording. Standalone calls use a local transaction; ambient calls use a savepoint, preserving outer commit ownership and rolling back failed batches even when callers catch errors. Verified 58 focused tests, including 17 new failure/cancellation/outer-transaction cases, and all 3784 solution tests. Build passed with zero warnings/errors; diff check passed. Documented the repository batch contract in the build spec.
<!-- SECTION:FINAL_SUMMARY:END -->
