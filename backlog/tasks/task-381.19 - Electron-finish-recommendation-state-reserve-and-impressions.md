---
id: TASK-381.19
title: 'Electron: finish recommendation state, reserve and impressions'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
updated_date: '2026-10-01 12:49'
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
ordinal: 437000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Recommendations coordinate reserve generation, observed impressions and saved feedback; a visual card alone does not prove the original lifecycle.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/FeedFeedbackTests.cs
- FeedFeedbackTests.With_no_store_behind_it_the_controls_are_not_offered_at_all [pending at split]
- FeedFeedbackTests.The_history_takes_the_body_and_gives_it_back [partial at split]

tests/Winnow.Tests/FeedReserveTests.cs
- FeedReserveTests.The_receipt_running_out_puts_the_next_card_in_that_cards_place [partial at split]
- FeedReserveTests.The_card_that_leaves_gives_up_its_cover_lease [pending at split]
- FeedReserveTests.A_swap_tops_the_sections_queue_back_up_without_touching_the_screen [pending at split]
- FeedReserveTests.An_invalidation_arriving_during_a_pass_is_replayed_rather_than_dropped [pending at split]

tests/Winnow.Tests/FeedViewModelTests.cs
- FeedViewModelTests.Desktop_excess_items_lead_the_reserve_without_being_counted_seen [pending at split]
- FeedViewModelTests.A_supplement_cannot_publish_after_its_generation_or_feedback_changes [partial at split]
- FeedViewModelTests.The_five_shelves_arrive_in_the_engines_order_with_their_own_pitches [pending at split]
- FeedViewModelTests.A_service_that_throws_is_a_sentence_and_not_a_crash [pending at split]
- FeedViewModelTests.Rapid_library_invalidations_during_a_load_replay_once_with_the_final_state [pending at split]

tests/Winnow.Ui.Tests/FeedImpressionTests.cs
- FeedImpressionTests.Add_to_list_from_a_scrolled_card_focuses_the_picker_and_escape_returns_to_the_card [pending at split]
- FeedImpressionTests.Offscreen_reserve_promotion_waits_for_viewport_entry [pending at split]
- FeedImpressionTests.Moving_from_an_unshown_window_does_not_leave_observation_stalled [pending at split]

tests/Winnow.Ui.Tests/RecommendationCompositionTests.cs
- RecommendationCompositionTests.Production_feed_renders_cold_games_and_keeps_action_impression_verdict_provenance [partial at split]

tests/Winnow.Ui.Tests/SupplementalFeedTests.cs
- SupplementalFeedTests.Primary_reason_reaches_desktop_and_fullscreen_without_additional_copy [pending at split]
- SupplementalFeedTests.Recently_played_hides_verdict_actions_and_does_not_record_impressions [pending at split]
- SupplementalFeedTests.Arriving_optional_shelves_preserve_the_focused_game_and_existing_card [pending at split]

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original feed generation/reserve, supplemental selection and composition rules remain intact without duplicating backend scoring.
- [x] #2 Impressions and feedback persist at the same lifecycle boundaries, retain undo/error behavior and do not double-write during desktop/fullscreen navigation or refresh.
- [x] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the eighteen frozen recommendation contracts and full source fixtures, including reserve order, generations, impressions, action provenance, cover disposal and focus. 2. Correct demonstrated frontend/backend-boundary gaps without duplicating recommendation scoring; preserve desktop and fullscreen behavior. 3. Run original and equivalent component/API/native cases with exact lifecycle and viewport evidence, then record checkpoint74 and per-method mappings. 4. Commit the verified milestone and continue to TASK-381.20.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after gameplay milestone 804c1c66 and completion 49a05be9. Ninth task in the authorized ten-task batch. Previous complete Electron gate: 3,822 cases across 188 files. The initial audit reproduced an extra primary read during a three-event invalidation burst, found modal history instead of the source body toggle, and confirmed that a FeedService without a feedback store still advertised usable verdict controls. Bounded renderer, backend-fixture and native work is underway; production scoring remains shared in the backend.

Final verification: 3,851 complete Electron component/live API cases across 189 files; 116 focused renderer cases; 25 existing list transaction cases; 27 unchanged originals, 13 HTTP cases and 63 feedback/service/reserve regressions. Native evidence covers 38 distinct new cases plus two existing consumers, all passing; final changed cases used index-PxSwDjer.js. Source-exact invalidation assertions preserve the native warm-shell boundary; final-owner disposal closes the actual renderer. Captures reviewed for desktop/fullscreen history, picker and virtual keyboard. Simulated controllers do not establish physical-device coverage. Checkpoint74 records implementation, fixture adaptations and measured limits.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed all eighteen recommendation-state contracts. Refreshes coalesce behind held scoring reads; stale optional results cannot publish; body history restores cards and focus; feedback capability and visible impression boundaries match the source. Restored fullscreen Y More, history hints and list-picker Y Keyboard. Verified 3,851 component/live API cases, 40 distinct native cases, 27 original cases, 13 HTTP cases and 63 affected regressions. Migration report: 1,505 ported, 650 retained-backend, 32 framework-specific, 217 pending and 31 partial. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-seventy-four.md. Continuing to the final authorized task, TASK-381.20.
<!-- SECTION:FINAL_SUMMARY:END -->
