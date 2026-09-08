---
id: TASK-165
title: Refine metadata lookup and preserve alphabetic library browsing
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 23:44'
updated_date: '2026-09-08 23:58'
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
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Normalize IGDB title search terms by removing copyright, registered-trademark, trademark and service-mark symbols before query and cache-key construction, with focused search tests. 2. Add a compact accessible #/A-Z jump rail shared by grid and list; derive available sections from visible titles, switch to A-Z order on activation, and scroll the active view to the first title in the section. 3. Snapshot the active viewport when details opens and restore it after an ordinary close when the visible source and view mode are unchanged. 4. Update the governing design and architecture text, then run focused tests, full build and full test suites.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented lookup-symbol stripping, a shared accessible #/A-Z library spine, guarded detail viewport restoration, governing documentation updates, and focused regression coverage. Rendered and inspected both grid and list variants; flattened disabled states after the first capture exposed unwanted Fluent button blocks. Build completed with zero warnings/errors. Full Windows test run passed 4,103 tests; 2 Linux-only monitor tests skipped as expected.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed copyright and trademark decoration from IGDB title queries while preserving stored titles; added the accessible #/A-Z jump spine to grid and list; and restored the exact unchanged viewport after details closes. Verified through rendered grid/list captures, focused interaction and normalization tests, a zero-warning solution build, and 4,103 passing tests with 2 expected Linux-only skips.
<!-- SECTION:FINAL_SUMMARY:END -->
