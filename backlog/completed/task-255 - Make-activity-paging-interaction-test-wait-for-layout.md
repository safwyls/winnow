---
id: TASK-255
title: Make activity paging interaction test wait for layout
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 06:58'
updated_date: '2026-09-13 07:00'
labels: []
dependencies: []
type: bug
ordinal: 297000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Post-merge Windows CI failed because the fullscreen activity paging test searched the visual tree after a data refresh without completing layout of replacement content.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The test explicitly exercises asynchronous first-page completion and finds the rendered Load more control reliably.
- [x] #2 The test still verifies explicit paging, cancellation on disposal, and rejection of late results.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Gate the first repository read, await completion and layout before visual lookup, retain cancellation assertions, and run repeated focused checks plus the Release UI suite.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Confirmed causality: with the gated asynchronous first response but without UpdateLayout, the test reproduces the exact CI Sequence contains no matching element failure. Restoring layout passes all four ActivityPagingInteractionTests. Explicit paging is asserted before clicking; cancellation and unchanged selection after the late response remain checked. This is a fullscreen test-harness correction only; desktop production code is unchanged, with both desktop/fullscreen slow-read cases included in focused verification.

Validation complete: all 584 Release UI tests passed; repaired test passed ten consecutive isolated runs; all four focused activity tests passed; git diff --check passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Made first-page completion explicitly asynchronous and completed Avalonia layout before visual lookup. Added assertion that paging does not start before clicking; preserved disposal cancellation and late-result checks. Omitting layout reproduced the exact CI failure. All 584 Release UI tests and ten repeated repaired-test runs passed.
<!-- SECTION:FINAL_SUMMARY:END -->
