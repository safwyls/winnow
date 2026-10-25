---
id: TASK-281
title: Use five-card desktop feed shelves
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 02:19'
updated_date: '2026-09-14 02:23'
labels: []
dependencies: []
ordinal: 323000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Fit curated desktop shelves within the default window without horizontal overflow.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop built-in and supplemental shelves show up to five cards; extras remain reserved and fullscreen remains unchanged.
- [x] #2 Five-slot geometry fits default window and preserves feedback replacement and navigation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Cap desktop presentation in FeedViewModel, prepend excess items to reserve, change FeedGrid geometry to five slots, verify layout and replacement behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop presentation takes five items for all shelves and prepends remaining primary items to the hidden reserve. Fullscreen still uses all primary plus reserve candidates. FeedGrid uses five fixed slots. Documentation updated. Verified 46 grid/viewmodel unit tests, 26 reserve tests and 6 UI tests. Default 1280x820 floating-shell layout with 228px rail, pane margins and vertical scrollbar fits without horizontal overflow. Built-in and supplemental reserve/impression tests confirm sixth replaces first and is recorded only on viewport entry. Keyboard navigation and short shelves verified.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Desktop shelves now show five covers and fit the default window without horizontal scrolling. Extra candidates remain reserved and fullscreen retains its full set. Verified 72 unit and 6 UI tests.
<!-- SECTION:FINAL_SUMMARY:END -->
