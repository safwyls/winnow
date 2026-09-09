---
id: TASK-165
title: Refine metadata lookup and preserve alphabetic library browsing
status: Done
assignee:
  - '@codex'
created_date: '2026-09-08 23:44'
updated_date: '2026-09-09 02:08'
labels: []
dependencies: []
references:
  - game-library-design.md
documentation:
  - design-system.md
modified_files:
  - src/Winnow.App/ViewModels/LibraryViewModel.cs
  - src/Winnow.App/Views/MainWindow.axaml
  - src/Winnow.App/Views/MainWindow.axaml.cs
  - tests/Winnow.Ui.Tests/ListBrowsingPositionTests.cs
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
- [x] #5 The alphabetical scroll control keeps the native thumb and lets pointer users press and drag across the letter spine to scrub through available title sections
- [x] #6 The spine uses slightly larger glyphs and the ordinary arrow cursor, while the current scroll location has a persistent glow whose intensity falls off across neighboring alphabet stops in grid and list views.
- [x] #7 Every alphabet glyph shares one fixed visual centerline, and sub-row pointer movement produces continuously varying horizontal displacement rather than switching among discrete wave bands.
- [x] #8 The alphabet spine scrubs directly among alphabet sections without engaging or proportionally controlling the native scrollbar; its wave and glow follow the pointer gesture, and ordinary scrollbar scrolling remains independent.
- [x] #9 For every non-name sort, the right-side scrub rail remains visible as an unlabeled notch scale; dragging it moves proportionally through the current ordering while name sorts retain direct alphabet-section scrubbing.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Give the shared browse rail explicit alphabet and notch modes derived from the active sort. 2. Render 27 quiet notches in the existing footprint for non-name sorts without exposing empty letter buttons to accessibility. 3. Reuse pointer wave/glow feedback for both modes; route alphabet drags to populated sections and notch drags proportionally through the active ordering, without engaging the native thumb. 4. Update documentation and grid/list interaction tests, inspect rendered states, then run full verification.
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

Sixth follow-up requested: align the glow with the wave peak by deriving both from the same pointer signal while the rail is active.

Clarification: the pointer row was not the correct shared source because alphabet sections are unevenly populated. The active effects should instead follow the title position represented by the scrollbar offset.

Sixth follow-up complete: scrollbar offset is converted to a fractional position in the ordered visible-title sequence, then interpolated between the adjacent titles' alphabet rows. That single value now drives both cosine displacement and halo falloff, including half-row ties. Hover no longer pulls the wave toward a pointer row that may not represent the content. Focused grid/list tests and rendered captures passed. Full build completed with zero warnings/errors; all 4,103 Windows tests passed and 2 Linux-only monitor tests skipped as expected.

Seventh follow-up requested: separate the alphabet scrubber from the native scrollbar and make it navigate alphabet sections directly.

Seventh follow-up complete: the alphabet spine no longer engages the native thumb or maps pointer Y to scrollbar extent. Hover only drives the local wave and halo. Press-drag resolves each pointer position to the nearest populated alphabet stop and brings that section into view, suppressing repeated jumps while remaining on the same target. Leaving restores the viewport-derived halo; normal scrollbar movement updates that resting cue independently. The frontend-design guidance kept the gesture as the one expressive motion while the native scrollbar returned to quiet chrome. Focused and rendered grid/list checks passed. Full build completed with zero warnings/errors; all 4,103 Windows tests passed and 2 Linux-only monitor tests skipped as expected.

Eighth follow-up requested: retain the browse rail for non-name sorts as a notch scale.

Eighth follow-up complete: the browse rail now remains present for every sort. Name sorts show the accessible #/A-Z buttons and retain section-based scrubbing; dormant, recent, playtime, and manual-list orders show 27 unlabeled TextFaint notches in the same footprint and scrub proportionally through that ordering. Both modes share the pointer-following wave and location halo, while the native scrollbar remains visually and behaviorally independent. The frontend-design guidance led to reusing the existing 24px rail and motion signature instead of adding a second control. Focused headless tests cover grid/list mode switching, 27-notch geometry, midpoint scrubbing, and native-thumb independence. Rendered alphabet and notch states were inspected in both layouts. Full build completed with zero warnings/errors; all 4,103 Windows tests passed and 2 Linux-only monitor tests skipped as expected.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Removed metadata title decoration, preserved detail-close position, and built one adaptive browse rail for grid and list views. Name sorts expose accessible alphabet-section scrubbing; every other sort converts the same rail to a quiet 27-notch position scale with proportional scrubbing. The wave and halo remain local to the rail, and the adjacent native scrollbar behaves independently. Verified with four rendered layout/mode captures, focused headless interaction coverage, a zero-warning build, and 4,103 passing tests with 2 expected Linux-only skips.
<!-- SECTION:FINAL_SUMMARY:END -->
