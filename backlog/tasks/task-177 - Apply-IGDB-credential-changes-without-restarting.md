---
id: TASK-177
title: Apply IGDB credential changes without restarting
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 18:15'
updated_date: '2026-09-10 18:20'
labels: []
dependencies: []
ordinal: 208000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Saving or removing IGDB credentials takes effect in the running app, with metadata fetching resumed automatically across desktop, fullscreen and setup.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Credential and token caches refresh safely after successful changes, including same-client secret rotation and removal
- [x] #2 Saving starts a serialized metadata refresh with existing progress and library updates
- [x] #3 Desktop, fullscreen and wizard copy describe immediate activation; regression tests pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Make credential changes invalidate runtime authentication safely. 2. Queue a serialized metadata refresh and refresh library views. 3. Update shared settings and wizard copy on both surfaces. 4. Add regression tests, build and run the suite.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented authentication mutation gates, post-commit runtime cache invalidation and a coalescing background refresh behind startup. Shared desktop, fullscreen and wizard forms now apply credentials without restart. Desktop and fullscreen headless interaction tests exercise queued/active status and secret clearing. Runtime tests cover cached absence, same-client rotation, removal/fallback, failed transaction and in-flight token persistence. Queue tests cover startup ordering, coalescing, failure recovery and shutdown. Full build passed with zero warnings; full suite passed 4,415 tests with 2 Linux-only skips on Windows. Reviewer found no blocking issues. No live credentials or production data used. Existing metadata cache and lifecycle scheduling rules remain in force.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
IGDB credential saves and removals take effect during the session and queue serialized metadata refreshes. Updated desktop, fullscreen and first-run setup copy and governing docs. Verified by full build, 4,415 passing tests, 2 platform skips, and code review.
<!-- SECTION:FINAL_SUMMARY:END -->
