---
id: TASK-165
title: Refine metadata lookup and preserve alphabetic library browsing
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 23:44'
updated_date: '2026-09-09 00:25'
labels: []
dependencies: []
references:
  - game-library-design.md
documentation:
  - design-system.md
priority: medium
type: enhancement
ordinal: 197000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Improve large-library browsing in three related ways: strip copyright and trademark symbols from metadata search terms, add a right-edge alphabetic jump rail to both grid and list layouts, and return users to the same browsing position after closing game details.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Metadata lookup removes copyright and trademark symbols from the submitted search term without changing the stored game title
- [x] #2 Grid and list layouts show an accessible alphabetic jump rail that jumps to the requested title section and handles non-letter titles
- [x] #3 Closing game details restores the prior grid or list scroll position without jumping to the top
- [x] #4 Focused automated tests cover lookup normalization, alphabet jumps, and detail-close position restoration
- [x] #5 The alphabet spine sits immediately left of the native scrollbar, is hidden for non-name sorts, and preserves ascending or descending name order when used
- [x] #6 The alphabetical scroll control keeps the native thumb and lets pointer users press and drag across the letter spine to scrub through available title sections
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Normalize IGDB title search terms by removing copyright, registered-trademark, trademark and service-mark symbols before query and cache-key construction, with focused search tests. 2. Add a compact accessible #/A-Z jump rail shared by grid and list; derive available sections from visible titles, switch to A-Z order on activation, and scroll the active view to the first title in the section. 3. Snapshot the active viewport when details opens and restore it after an ordinary close when the visible source and view mode are unchanged. 4. Update the governing design and architecture text, then run focused tests, full build and full test suites.

5. Move the index inside the scrollbar, bind its visibility to the two name sorts, preserve the active name direction on jumps, and update rendered interaction coverage and the design specification.

6. Treat the alphabet spine and adjacent native thumb as one scroll control: capture pointer drags on the spine, map crossed rows to available title sections without altering sort direction, retain button activation for keyboard use, and verify drag behavior in both layouts.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented lookup-symbol stripping, a shared accessible #/A-Z library spine, guarded detail viewport restoration, governing documentation updates, and focused regression coverage. Rendered and inspected both grid and list variants; flattened disabled states after the first capture exposed unwanted Fluent button blocks. Build completed with zero warnings/errors. Full Windows test run passed 4,103 tests; 2 Linux-only monitor tests skipped as expected.

Follow-up requested: place the alphabet spine to the left of the scrollbar and hide it outside alphabetical sorting.

Follow-up complete: the native scrollbar is again the rightmost control, the alphabet spine occupies the adjacent inner column only for Name A-Z and Name Z-A, and jumps preserve the current direction. Headless geometry asserts the spine is left of the scrollbar in both layouts; visibility and direction are exercised in both layouts. Rendered grid/list captures inspected. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.

Second follow-up requested: merge the spine with scrollbar interaction so mouse dragging across the spine scrolls through alphabet sections.

Second follow-up complete: the alphabet strip and native thumb now behave as one scroll rail. Pointer press and drag over the strip scrubs through available title sections, pointer capture keeps the gesture active across letter buttons, capture loss safely cancels it, and keyboard activation remains available. The strip reverses for Name Z-A so downward dragging follows the visible order. Headless drag coverage passes in grid and list layouts; rendered captures were inspected. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed metadata title decoration, added alphabetical navigation, and preserved detail-close position. The finished alphabetical rail places the letter spine beside the native scrollbar only for name sorts; letters remain clickable and keyboard accessible, while mouse press-and-drag scrubs the library in the active A-Z or Z-A direction. Verified with rendered grid/list captures, geometry, visibility and drag interaction tests, a zero-warning build, and 4,103 passing tests with 2 expected Linux-only skips.
<!-- SECTION:FINAL_SUMMARY:END -->
