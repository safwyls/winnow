---
id: TASK-381.12
title: 'Electron: finish merge review and suggestion refresh'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 02:20'
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
ordinal: 430000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Merge review crosses selection, plugin suggestions, cancellable refresh and nested Details, making isolated component success insufficient.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/MergeScreenRegistrationTests.cs
- MergeScreenRegistrationTests.The_screen_resolves_from_the_container_and_loads_every_list [pending at split]
- MergeScreenRegistrationTests.Omitting_the_link_registration_breaks_the_container_by_name [pending at split]

tests/Winnow.Tests/MergeSuggestionRefreshTests.cs
- MergeSuggestionRefreshTests.Requests_are_serial_and_a_later_request_runs_again_after_the_first_snapshot [pending at split]
- MergeSuggestionRefreshTests.Failed_and_cancelled_passes_release_the_gate_without_advancing_revision [pending at split]
- MergeSuggestionRefreshTests.Cancelling_a_waiter_does_not_cancel_another_surfaces_active_pass [pending at split]
- MergeSuggestionRefreshTests.Synchronous_matching_work_is_dispatched_away_from_the_requesting_thread [pending at split]

tests/Winnow.Tests/MergesDetailsTests.cs
- MergesDetailsTests.A_row_click_opens_the_librarys_detail_modal_for_that_entry [pending at split]
- MergesDetailsTests.A_row_with_no_tile_opens_nothing_and_says_so [pending at split]

tests/Winnow.Tests/PluginMergeSuggestionTests.cs
- PluginMergeSuggestionTests.Imported_console_entry_gets_one_suggestion_without_linking_or_replacing_source_records [pending at split]
- PluginMergeSuggestionTests.Reimport_and_refresh_preserve_the_users_answer_and_source_entries [pending at split]
- PluginMergeSuggestionTests.Account_changes_and_repeated_imports_do_not_duplicate_releases_or_suggestions [pending at split]

tests/Winnow.Ui.Tests/MergePlatformPickerTests.cs
- MergePlatformPickerTests.Fullscreen_controller_selects_platform_and_remembers_it_on_reopen [pending at split]
- MergePlatformPickerTests.Desktop_picker_applies_option_closes_and_updates_accessible_status [pending at split]

tests/Winnow.Ui.Tests/MergeRowActionsTests.cs
- MergeRowActionsTests.Long_titles_keep_cards_and_trailing_actions_inside_the_desktop_pane_when_resized [partial at split]
- MergeRowActionsTests.Row_body_promotes_while_details_and_radio_have_independent_pointer_and_keyboard_actions [partial at split]

tests/Winnow.Ui.Tests/MergeSuggestionRefreshTests.cs
- MergeSuggestionRefreshTests.Desktop_pointer_and_keyboard_refresh_report_busy_failure_and_recovery_within_minimum_pane [partial at split]
- MergeSuggestionRefreshTests.Fullscreen_controller_and_keyboard_refresh_report_busy_failure_and_recovery [partial at split]
- MergeSuggestionRefreshTests.Automatic_publication_updates_fullscreen_when_pending_count_is_unchanged [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original merge screen composition, plugin suggestion and manual-refresh contracts preserve cancellation, stale-result rejection and selected members.
- [ ] #2 Platform pickers, row actions and Details return retain source identity choices, safe confirmations and focus routes on both surfaces.
- [ ] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
TASK-381.7 visual evidence: fullscreen merge member sheets still use compact desktop-sized title/action text (roughly 14px) beside 16px store chips. Compare MergeMemberSheet and parity-merge.css with the source fullscreen tokens while completing this merge-review checkpoint. Evidence: .tmp/task3817-labels-initial-results/library-labels-fullscreen--aaaf7-mber-store-without-clipping/fullscreen-merge-2.png. Store labels and no-clipping assertions are covered by TASK-381.7; they do not establish overall merge-sheet typography parity. This task remains To Do; no implementation has started.
<!-- SECTION:NOTES:END -->
