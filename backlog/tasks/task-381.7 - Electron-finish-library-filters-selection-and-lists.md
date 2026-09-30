---
id: TASK-381.7
title: 'Electron: finish library filters, selection and lists'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:45'
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
ordinal: 425000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Saved filters, list membership and bulk selection combine persisted state with local browsing position and can diverge across the two surfaces.

Owns 24 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/LibrarySettingsViewModelTests.cs
- LibrarySettingsViewModelTests.Hiding_a_game_removes_its_tile_and_its_bucket_count [pending at split]
- LibrarySettingsViewModelTests.The_context_menu_hides_every_picked_tile [pending at split]
- LibrarySettingsViewModelTests.A_hidden_game_is_listed_and_can_be_put_back_one_at_a_time [pending at split]
- LibrarySettingsViewModelTests.The_explicit_toggle_persists_and_reaches_the_library [pending at split]
- LibrarySettingsViewModelTests.The_explicit_preference_survives_a_reload [pending at split]

tests/Winnow.Tests/ListAtomicWriteTests.cs
- ListAtomicWriteTests.A_failed_bulk_change_leaves_no_partial_list_even_when_an_outer_transaction_commits [pending at split]
- ListAtomicWriteTests.Failed_mutations_keep_the_last_committed_model_and_open_context [pending at split]

tests/Winnow.Tests/StoreChipLayoutTests.cs
- StoreChipLayoutTests.The_list_store_column_holds_three_chips [pending at split]
- StoreChipLayoutTests.Every_merges_row_draws_the_store [pending at split]

tests/Winnow.Ui.Tests/CollectionExplanationTests.cs
- CollectionExplanationTests.Built_in_collections_expose_explanations_on_desktop_and_fullscreen [pending at split]

tests/Winnow.Ui.Tests/DerelictOverrideCompositionTests.cs
- DerelictOverrideCompositionTests.Desktop_context_menu_removes_selected_group_only_in_Derelict [pending at split]
- DerelictOverrideCompositionTests.Fullscreen_library_options_activate_removal_for_selected_group [pending at split]
- DerelictOverrideCompositionTests.Persistence_failure_keeps_group_in_Derelict_and_reports_the_problem [pending at split]
- DerelictOverrideCompositionTests.Removal_survives_new_services_and_later_evidence_for_every_grouped_copy [pending at split]

tests/Winnow.Ui.Tests/HideBrowsingPositionTests.cs
- HideBrowsingPositionTests.Hiding_keeps_viewport_and_search_still_resets_it [pending at split]

tests/Winnow.Ui.Tests/LibraryMultiSelectionTests.cs
- LibraryMultiSelectionTests.Context_action_updates_every_selected_game_and_leaves_unselected_game [partial at split]

tests/Winnow.Ui.Tests/ListPromptParityTests.cs
- ListPromptParityTests.Existing_choices_and_new_list_action_remain_available_after_a_failed_save [partial at split]
- ListPromptParityTests.Pending_prompt_disables_conflicting_actions_and_disposed_pages_do_not_navigate [partial at split]

tests/Winnow.Ui.Tests/ListWriteParityTests.cs
- ListWriteParityTests.Pending_membership_preserves_latest_intent_and_reports_the_committed_result [partial at split]
- ListWriteParityTests.A_failed_list_action_keeps_the_open_list_and_shows_a_retry_message [partial at split]

tests/Winnow.Ui.Tests/MissingFilterOptionsParityTests.cs
- MissingFilterOptionsParityTests.Xbox_import_appears_in_store_filters_and_selects_its_titles [partial at split]
- MissingFilterOptionsParityTests.Saved_and_current_rules_remain_restrictive_when_matching_choices_disappear [partial at split]

tests/Winnow.Ui.Tests/RailListControlsTests.cs
- RailListControlsTests.Statistics_sits_with_screens_and_stays_reachable_on_both_surfaces [pending at split]
- RailListControlsTests.Context_menu_offers_removal_only_for_a_static_list_selection [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original settings/filter options, collection explanations, Derelict overrides and store labels retain their values and meaning after refresh.
- [ ] #2 Selection, hide, rail list controls and list prompts/writes preserve browsing position, atomic writes, conflict/retry behavior and controller focus on both surfaces.
- [ ] #3 All 24 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
