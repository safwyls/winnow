---
id: TASK-381.19
title: 'Electron: finish recommendation state, reserve and impressions'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original feed generation/reserve, supplemental selection and composition rules remain intact without duplicating backend scoring.
- [ ] #2 Impressions and feedback persist at the same lifecycle boundaries, retain undo/error behavior and do not double-write during desktop/fullscreen navigation or refresh.
- [ ] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
