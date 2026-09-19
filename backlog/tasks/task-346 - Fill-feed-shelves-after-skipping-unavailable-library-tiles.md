---
id: TASK-346
title: Fill feed shelves after skipping unavailable library tiles
status: Done
assignee:
  - '@codex'
created_date: '2026-09-19 17:17'
updated_date: '2026-09-19 18:28'
labels: []
dependencies: []
type: bug
ordinal: 379000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Desktop can show four cards despite available replacements because it caps recommendations before dropping entries missing from the loaded library.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop fills up to five available cards from primary and reserve entries in order, keeping remaining available entries in reserve.
- [x] #2 Fullscreen continues showing all available entries; tests cover supplemental shelves and exhausted pools.
- [x] #3 Document fill policy and run focused feed regression tests.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Filter entries against library tiles before partitioning visible cards and replacements. Add desktop and fullscreen regression coverage, update the visual spec, and run focused tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Filter primary and reserve entries before the desktop five-card split; fullscreen still exposes every available entry. Added six regression cases covering both surfaces, supplemental feeds, unavailable tiles and exhausted pools. Updated reserve fixtures to use full desktop shelves so their swap/backfill assertions remain exercised. Validation: 136 feed tests and 66 headless feed UI tests passed with scratch build output; git diff --check passed. Production library was not modified.

Final branch verification: isolated full Release build passed with zero warnings/errors; full solution tests passed with 6,692 passed, zero failed and two Linux-only skips on Windows. Verified all 45 migration hashes against origin/main and passed migration integrity mutation checks. The first shared-output attempt was discarded due to dependency collisions; final results use normal per-project output in an isolated checkout.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Desktop shelves now fill gaps from available reserve games in recommendation order. Fullscreen retains all available cards. Verified with 202 focused feed and UI tests and documented the fill policy.
<!-- SECTION:FINAL_SUMMARY:END -->
