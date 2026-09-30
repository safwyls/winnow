---
id: TASK-381.27
title: 'Electron: finish platform accounts and confirmation flows'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
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
ordinal: 445000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Accounts require deliberate confirmation and clear source scope; switching store cards must not transfer credentials, drafts or account context.

Owns 25 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ConsoleAuthPromptTests.cs
- ConsoleAuthPromptTests.A_null_WinExe_standard_handle_is_not_mistaken_for_redirection [pending at split]

tests/Winnow.Tests/OwnedAccountConfirmationTests.cs
- OwnedAccountConfirmationTests.The_remote_sync_asks_about_an_account_that_never_won_a_candidate [pending at split]
- OwnedAccountConfirmationTests.A_confirmed_account_is_written_where_the_filter_can_read_it [pending at split]
- OwnedAccountConfirmationTests.Changing_the_api_key_clears_the_confirmed_account [pending at split]
- OwnedAccountConfirmationTests.A_changed_key_that_discloses_nothing_leaves_the_account_cleared [pending at split]
- OwnedAccountConfirmationTests.Removing_the_api_key_clears_the_confirmed_account [pending at split]
- OwnedAccountConfirmationTests.The_stored_key_fingerprint_is_never_the_key [pending at split]

tests/Winnow.Tests/OwnedAccountDisclosureRefetchTests.cs
- OwnedAccountDisclosureRefetchTests.An_account_with_no_populated_years_leaves_the_toggle_disabled [pending at split]

tests/Winnow.Tests/SteamConnectionPanelTests.cs
- SteamConnectionPanelTests.A_host_with_no_sign_in_still_offers_the_key_and_says_the_window_cannot_open [partial at split]

tests/Winnow.Tests/SteamConnectionSeamTests.cs
- SteamConnectionSeamTests.A_host_with_no_steam_module_answers_nothing_rather_than_throwing [partial at split]

tests/Winnow.Ui.Tests/AccountInventoryCompositionTests.cs
- AccountInventoryCompositionTests.Library_and_feed_keep_unknown_ownership_until_a_complete_inventory_and_expand_after_failure [pending at split]

tests/Winnow.Ui.Tests/FullscreenPlatformParityTests.cs
- FullscreenPlatformParityTests.Api_key_draft_is_masked_local_and_erased_on_close [pending at split]
- FullscreenPlatformParityTests.Saved_page_selection_requires_read_and_cancel_returns_no_paths [pending at split]
- FullscreenPlatformParityTests.Acquisition_export_cancels_without_writing_and_preserves_csv_encoding [pending at split]

tests/Winnow.Ui.Tests/FullscreenPlatformTests.cs
- FullscreenPlatformTests.Settings_platform_summary_refreshes_on_open_and_tracks_shared_account_state [pending at split]
- FullscreenPlatformTests.Epic_expiry_and_failed_signin_remain_truthful [pending at split]
- FullscreenPlatformTests.Platform_reads_existing_sessions_and_updates_after_epic_signout_and_signin [pending at split]
- FullscreenPlatformTests.Steam_renders_health_identity_and_actions_as_shared_state_changes [pending at split]
- FullscreenPlatformTests.Platform_tool_directional_focus_matches_the_vertical_layout [pending at split]

tests/Winnow.Ui.Tests/StoresAccountContextTests.cs
- StoresAccountContextTests.Optional_provider_states_are_named_and_neutral_on_both_surfaces [pending at split]
- StoresAccountContextTests.Replacing_key_clears_old_confirmation_and_disables_account_scope_on_both_surfaces [pending at split]
- StoresAccountContextTests.Epic_signout_and_replacement_signin_render_only_current_account [pending at split]
- StoresAccountContextTests.Persisted_unknown_install_observation_keeps_play_until_authoritative_absence [pending at split]
- StoresAccountContextTests.Details_render_selected_account_acquisition_then_withhold_conflicting_aggregate_license [pending at split]
- StoresAccountContextTests.Statistics_render_known_and_unknown_account_scope_without_ambiguous_money_totals [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original confirmation, disclosure/refetch, Steam connection and account-inventory rules preserve consent and account-specific state.
- [ ] #2 Desktop platform cards and fullscreen platform pages retain all original actions, busy/error states, focus routes and counts; headless auth fallback has equivalent behavior or an explicit host-specific disposition.
- [ ] #3 All 25 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
