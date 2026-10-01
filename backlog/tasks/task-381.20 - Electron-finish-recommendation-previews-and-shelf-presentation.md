---
id: TASK-381.20
title: 'Electron: finish recommendation previews and shelf presentation'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
updated_date: '2026-10-01 13:13'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies: []
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 438000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Recommendation and library previews need source-equivalent art, rating attribution, clipping and hover/focus behavior.

Owns 10 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Ui.Tests/FeedCardBackdropTests.cs
- FeedCardBackdropTests.Released_card_ignores_late_pixels_and_reacquires_on_attach [pending at split]
- FeedCardBackdropTests.Detached_metadata_read_cannot_replace_reattached_cards_candidates [pending at split]

tests/Winnow.Ui.Tests/FeedPreviewBubbleTests.cs
- GamePreviewBubbleTests.Artwork_continues_through_pointer_without_border_seam_or_rectangular_spill [pending at split]
- GamePreviewBubbleTests.Flipped_pointer_moves_artwork_and_content_gutter_together [pending at split]

tests/Winnow.Ui.Tests/FeedPreviewRatingsTests.cs
- FeedPreviewRatingsTests.Hover_displays_each_available_population_with_its_score_and_count [pending at split]
- FeedPreviewRatingsTests.Missing_ratings_do_not_reserve_preview_space [pending at split]
- FeedPreviewRatingsTests.Closed_or_disposed_preview_cancels_pending_ratings_and_ignores_late_result [pending at split]
- FeedPreviewRatingsTests.Ratings_arriving_near_bottom_reposition_the_growing_preview_inside_window [pending at split]

tests/Winnow.Ui.Tests/FeedShelfVisualTests.cs
- FeedShelfVisualTests.Capture_cover_shelves_and_quick_details [pending at split]

tests/Winnow.Ui.Tests/LibraryHoverPreviewTests.cs
- LibraryHoverPreviewTests.Exit_or_recycle_closes_preview_and_cancels_pending_metadata [partial at split]

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original preview/backdrop/rating and shelf visual fixtures preserve artwork bounds, honest metadata and interaction geometry.
- [x] #2 Desktop hover/focus previews and fullscreen shelf identity remain readable, close at the correct boundary and avoid stale content when tiles are recycled.
- [x] #3 All 10 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the ten frozen preview, backdrop, rating and shelf presentation contracts and their complete fixtures. 2. Fix demonstrated preview geometry, artwork lifetime and stale-content gaps while preserving both surfaces and source rating attribution. 3. Execute original tests with visual capture enabled, equivalent component/API checks and isolated native pixel/layout/lifecycle checks; review source and Electron captures. 4. Record checkpoint75 and per-method evidence, commit the verified milestone, then stop for review after this tenth task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after recommendation-state milestone 60afaf06. Tenth and final task in the authorized batch. Previous complete Electron gate: 3,851 cases across 189 files. Source bubble tests require continuous artwork through a 10px pointer, 16px content padding plus pointer gutter, transparent outside pixels and consistent flipped geometry. Rating assertions require compact source attribution without counts or percentages; the method name alone does not describe its full scope.

Capture-enabled original execution exposed a dormant source-test defect: both shelf captures expected twelve cards, but the frozen fixture constructs exactly two shelves of five and FeedViewModel starts empty without a load. The other eleven original cases pass unchanged. Correcting only that count assertion to ten preserves all fixture inputs and the five-slot design; original failing output remains in task38120-source-tests.log, and capture-enabled re-execution will verify the corrected assertion and screenshots. No application behavior change is needed for this source defect.

Renderer preview now shares one SVG outline for surface/artwork/border with source pointer gutter, typography, SurfaceRaised color, compact/accessibility reception formatting and source playtime copy. Fullscreen no longer opens a duplicate hover bubble. Twelve focused preview cases and production build pass; frozen native bundle is index-jNzwlGYY.js. Corrected original capture-enabled gate passes all thirteen cases and four source fallback captures were produced; root reviewed wide/narrow quick-details images. Full renderer/API/native gates and artwork lifetime cases remain in progress.

Final verification: complete Electron component/live gate 3,859/3,859 across 190 files in 105.37s; production build index-jNzwlGYY.js; focused renderer 55 and final preview 12; corrected capture-enabled originals 13, plus 2 artwork capture repetitions; authenticated HTTP 9; native 15 new plus 13 existing consumers all pass. Root inspected source and Electron desktop/fullscreen captures, pixel transparency/continuity and flipped gutter checks. Native harness accounts for whole-shell side rail and fullscreen 4-slot pagination at 1280; actual controller navigation reaches all 5. Source supplied bitmap/floor equality differs from real native dormancy pipeline and is documented. Physical controller hardware remains unverified. Checkpoint 75 records fixture provenance and the narrow dormant-source assertion correction.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed all ten recommendation preview and shelf contracts. Desktop preview artwork now continues through its pointer with source geometry, typography, rating attribution and cancellation behavior; fullscreen keeps its selected-game hero without hover bubbles. Verified 3,859 complete Electron component/live API cases, 28 distinct native cases, 13 source cases with capture enabled, nine HTTP cases and 55 focused renderer cases. The dormant source screenshot count was corrected from twelve to its unchanged ten-card fixture, with the original failures retained as evidence. Migration report: 1,515 ported, 650 retained-backend, 32 framework-specific, 208 pending and 30 partial. See docs/spikes/2026-09-28-electron-parity/checkpoint-seventy-five.md. This completes TASK-381.11 through TASK-381.20; stop for user review.
<!-- SECTION:FINAL_SUMMARY:END -->
