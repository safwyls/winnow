---
id: TASK-165
title: Refine metadata lookup and preserve alphabetic library browsing
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 23:44'
updated_date: '2026-09-09 01:24'
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
- [x] #7 Pointer hover over the alphabet expands the adjacent native scrollbar; pointer press and vertical drag continuously maps to the scrollable extent like dragging its thumb, while nearby letters form a restrained horizontal wave that snaps under reduced motion.
- [x] #8 The spine uses slightly larger glyphs and the ordinary arrow cursor, while the current scroll location has a persistent glow whose intensity falls off across neighboring alphabet stops in grid and list views.
- [x] #9 Every alphabet glyph shares one fixed visual centerline, and sub-row pointer movement produces continuously varying horizontal displacement rather than switching among discrete wave bands.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Normalize IGDB title search terms by removing copyright and trademark symbols before query and cache-key construction, with focused tests. 2. Add an accessible #/A-Z rail shared by grid and list, derive available sections from visible titles, and preserve the active alphabetical direction. 3. Snapshot and restore the active viewport across ordinary detail close. 4. Update governing documentation and run focused and full verification. 5. Place the rail immediately left of the native scrollbar and hide it outside name sorts. 6. Capture pointer drags on the spine while retaining button activation for keyboard use. 7. Map spine dragging proportionally to the active ScrollViewer extent, engage the native thumb on hover, and add a pointer-following horizontal wave. 8. Increase glyph legibility, restore the arrow cursor, and derive a persistent location glow from the visible title section. 9. Center every glyph in an exact 12px column and drive its transform directly from a compact cosine curve with no inherited easing, so sub-row pointer movement remains continuous.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented lookup-symbol stripping, a shared accessible #/A-Z library spine, guarded detail viewport restoration, governing documentation updates, and focused regression coverage. Rendered and inspected both grid and list variants; flattened disabled states after the first capture exposed unwanted Fluent button blocks. Build completed with zero warnings/errors. Full Windows test run passed 4,103 tests; 2 Linux-only monitor tests skipped as expected.

Follow-up requested: place the alphabet spine to the left of the scrollbar and hide it outside alphabetical sorting.

Follow-up complete: the native scrollbar is again the rightmost control, the alphabet spine occupies the adjacent inner column only for Name A-Z and Name Z-A, and jumps preserve the current direction. Headless geometry asserts the spine is left of the scrollbar in both layouts; visibility and direction are exercised in both layouts. Rendered grid/list captures inspected. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.

Second follow-up requested: merge the spine with scrollbar interaction so mouse dragging across the spine scrolls through alphabet sections.

Second follow-up complete: the alphabet strip and native thumb now behave as one scroll rail. Pointer press and drag over the strip scrubs through available title sections, pointer capture keeps the gesture active across letter buttons, capture loss safely cancels it, and keyboard activation remains available. The strip reverses for Name Z-A so downward dragging follows the visible order. Headless drag coverage passes in grid and list layouts; rendered captures were inspected. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.

Third follow-up requested: replace the clunky section jumps with true thumb-equivalent continuous scrubbing, minimal rail gap, hover linkage to the native scrollbar, and a restrained Niagara-style letter wave.

Third follow-up complete: the spine's hit area now meets the scrollbar with a 0-2px tested layout gap and right-aligned glyphs. Hover applies the scrollbar's engaged class and expands its thumb to at least 8px. Dragging maps pointer Y directly to the ScrollViewer's scrollable extent; a midpoint drag is asserted at 48-52% in both grid and list layouts. The nearest letter pulls 13px left with 9/5/2px neighboring falloff, and reduced-motion mode removes the 70ms transform transition. Rendered grid/list captures were inspected. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.

Fourth follow-up requested: slightly larger alphabet text, no resize cursor over the spine, and a current-scroll-position glow with outward dimming.

Fourth follow-up complete: alphabet glyphs increased from 10px to 11px and both enabled and disabled stops now retain the arrow cursor. Grid and list scroll-change events derive the current visible title section from the active offset; that stop receives a compact theme-derived Volt halo while three neighbors fall back through Volt, Text, and TextDim. Pointer wave and location glow remain independent. Headless coverage asserts typography, cursor, current-section mapping, center and neighbor classes, and glow fill in both layouts. Rendered captures were inspected. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.

Fifth follow-up requested from an in-app capture: correct the alphabet centerline and replace the visibly stepped wave with a continuous function.

Fifth follow-up complete: each glyph now occupies an exact 12px column and rendered centerlines agree within 0.01px. The four class-based displacement bands and inherited button easing were removed. Pointer distance now feeds a compact cosine curve on every move, yielding a 13px peak and continuously varying intermediate transforms; the focused test verifies a distinct sub-row value at 0.35 rows. Rendered grid/list captures show a smooth arc. Zero-warning build and all 4,103 Windows tests passed; 2 Linux-only monitor tests skipped as expected.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed metadata title decoration, added alphabetical navigation, and preserved detail-close position. The alphabet rail now combines continuous thumb-equivalent scrubbing, a pointer-following cosine wave, a theme-aware current-location glow, and an exact fixed glyph centerline. The wave bypasses inherited easing so each pointer coordinate renders directly instead of stepping or chasing stale positions. Verified with rendered grid/list captures, 0.01px alignment and sub-row transform assertions, a zero-warning build, and 4,103 passing tests with 2 expected Linux-only skips.
<!-- SECTION:FINAL_SUMMARY:END -->
