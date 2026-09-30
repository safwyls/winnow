---
id: TASK-381.20
title: 'Electron: finish recommendation previews and shelf presentation'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original preview/backdrop/rating and shelf visual fixtures preserve artwork bounds, honest metadata and interaction geometry.
- [ ] #2 Desktop hover/focus previews and fullscreen shelf identity remain readable, close at the correct boundary and avoid stale content when tiles are recycled.
- [ ] #3 All 10 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
