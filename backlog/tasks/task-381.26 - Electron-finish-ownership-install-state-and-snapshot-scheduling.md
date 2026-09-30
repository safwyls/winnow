---
id: TASK-381.26
title: 'Electron: finish ownership, install state and snapshot scheduling'
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
ordinal: 444000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Launcher snapshots and remote ownership must agree about membership, install state and history without writing to launcher files.

Owns 23 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/AccountMembershipTests.cs
- AccountMembershipTests.The_toggle_is_disabled_until_an_account_is_confirmed [pending at split]
- AccountMembershipTests.The_toggle_defaults_to_every_account_and_persists_the_choice [pending at split]
- AccountMembershipTests.The_toggle_states_what_it_hides_with_the_figure_in_the_data_face [pending at split]

tests/Winnow.Tests/LibrarySyncSchedulingTests.cs
- LibrarySyncSchedulingTests.Program_never_blocks_on_a_sync_job [pending at split]

tests/Winnow.Tests/LifecycleSyncServiceTests.cs
- LifecycleSyncServiceTests.Batch_cap_and_persisted_attempts_prevent_failed_titles_starving_others [pending at split]
- LifecycleSyncServiceTests.A_new_mapping_is_due_immediately_without_reusing_the_old_mapping_attempt_time [pending at split]
- LifecycleSyncServiceTests.Cached_observation_is_not_duplicated_or_given_a_new_time_next_day [pending at split]

tests/Winnow.Tests/OwnershipRefreshLifecycleTests.cs
- OwnershipRefreshLifecycleTests.Account_changes_coalesce_behind_startup_and_keep_one_followup_during_the_full_operation [pending at split]
- OwnershipRefreshLifecycleTests.Shutdown_cancels_active_metadata_discards_queued_account_changes_and_releases_both_gates [pending at split]
- OwnershipRefreshLifecycleTests.Igdb_and_ownership_refresh_share_one_downstream_pipeline_without_delaying_committed_ownership_publication [pending at split]

tests/Winnow.Tests/RemoteOwnershipInventoryTests.cs
- RemoteOwnershipInventoryTests.Only_complete_resolved_inventories_enable_filtering_including_the_empty_library [pending at split]
- RemoteOwnershipInventoryTests.Cancellation_or_resolution_failure_cannot_publish_completion [pending at split]

tests/Winnow.Tests/RemoteOwnershipSyncInstallStateTests.cs
- RemoteOwnershipSyncInstallStateTests.A_sync_that_also_reads_the_owned_library_still_records_what_is_on_disk [pending at split]
- RemoteOwnershipSyncInstallStateTests.Re_syncing_does_not_erode_install_state [pending at split]

tests/Winnow.Tests/SnapshotSchedulerHistoryTests.cs
- SnapshotSchedulerHistoryTests.A_tick_captures_a_playtime_delta_as_one_snapshot_and_one_play_record [pending at split]
- SnapshotSchedulerHistoryTests.Ticks_over_unchanged_files_write_nothing_at_all [pending at split]
- SnapshotSchedulerHistoryTests.Stopping_mid_scan_rolls_the_pass_back_rather_than_writing_through_a_disposed_factory [pending at split]

tests/Winnow.Tests/SteamInstallRefreshTests.cs
- SteamInstallRefreshTests.Stable_install_and_uninstall_refresh_manifest_only_ownership_without_erasing_history [pending at split]
- SteamInstallRefreshTests.Incomplete_inventory_preserves_installed_state_and_next_complete_scan_reconciles_after_restart [pending at split]
- SteamInstallRefreshTests.Steam_management_uses_uninstall_and_never_declares_a_game_launch [pending at split]
- SteamInstallRefreshTests.Other_store_management_is_navigation_not_an_uninstall [pending at split]

tests/Winnow.Tests/StorefrontTests.cs
- StorefrontTests.Sync_uses_owned_store_ids_and_warms_the_read_only_projection [pending at split]
- StorefrontTests.Details_offer_store_link_and_readable_gog_notes_and_hide_missing_epic_link [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original account membership, remote ownership, lifecycle refresh and Steam install-state contracts pass through shared backend/API composition.
- [ ] #2 Sync and snapshot history scheduling preserve source timing, cancellation and deduplication; desktop/fullscreen receive the same completed authoritative state.
- [ ] #3 All 23 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
