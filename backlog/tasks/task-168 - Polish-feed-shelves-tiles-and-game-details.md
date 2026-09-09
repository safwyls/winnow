---
id: TASK-168
title: 'Polish feed, shelves, tiles, and game details'
status: Done
assignee: []
created_date: '2026-09-09 03:29'
updated_date: '2026-09-09 03:36'
labels: []
dependencies: []
type: enhancement
ordinal: 200000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Refine several library interactions and reorganize game details so actions are quieter, shelf meanings are discoverable, tile details are easier to reveal, and key game metadata is visible without expanding a disclosure.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Feed card action buttons do not gain a border when activated and retain a visible keyboard focus treatment
- [x] #2 Every built-in shelf has a tooltip that explains which games it contains
- [x] #3 A game tile's details dogear appears when hovering anywhere over the tile and is more opaque than before
- [x] #4 Review scores appear before the About section in game details
- [x] #5 Installation and identifier information appears directly on the main details card without a dropdown
- [x] #6 Relevant UI tests pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Adjust feed action and shelf header styling while preserving focus visibility and accessible explanations. 2. Separate the tile dogear reveal from the remaining action overlay so whole-tile hover reveals a stronger fold. 3. Reorder reception before About and replace the technical-facts disclosure with an always-visible metadata section. 4. Update structural and interaction tests, then run focused UI and enforcement tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Kept the existing whole-tile hover state for the dogear after a direct pointer selector failed the recycle lifecycle test; increased contrast through SurfaceHigh instead. Validation: full dotnet test passed with 4,150 tests passed and 2 Linux-only tests skipped on Windows. A focused 36-test UI run also passed after adding shelf-tooltip coverage.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed pressed borders from feed feedback actions, exposed shelf explanations as header tooltips, strengthened the whole-tile-hover dogear, moved reception above About, and made technical installation/identifier facts an always-visible Library section. Verified with the full solution test suite and focused headless UI coverage.
<!-- SECTION:FINAL_SUMMARY:END -->
