---
id: TASK-381.26
title: 'Electron: finish ownership, install state and snapshot scheduling'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
updated_date: '2026-10-01 20:04'
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

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original account membership, remote ownership, lifecycle refresh and Steam install-state contracts pass through shared backend/API composition.
- [x] #2 Sync and snapshot history scheduling preserve source timing, cancellation and deduplication; desktop/fullscreen receive the same completed authoritative state.
- [x] #3 All 23 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute all23 frozen source methods/30 expected cases and audit the retained ownership/install/snapshot implementations without rewriting shared services. 2. Restore exact account-scope label, positive-only formatted data count and own-scope caveat on desktop/fullscreen. 3. Render safe cached GOG patch notes in both Details paths, retaining storefront provenance and missing-link behavior. 4. Add bounded real API/fixture proof for account persistence and owned storefront cache projection, then focused component/native checks with captures. 5. Record individual dispositions and checkpoint81, commit and continue to TASK-381.27.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after TASK-381.25 implementation d66f7617. Inventory1584 ported/665 retained/32 framework/134 pending/20 partial. Read-only preparation .tmp/task38126-prep.md identifies17 shared retained methods,5 renderer/API contracts and1 narrow C# source scanner. Root owns Backlog/docs/mappings/integration; bounded owners preserve each other edits. Native runs wait for all build/test activity to finish.

All30 assigned original source cases pass without skips in task38126-source-results/task38126-source.trx. The retained service fixtures preserve exact lifecycle50/51 attempts, remote inventory six outcomes, two cancellation/resolver failures, Steam install stability and snapshot244-to281 timing. Source title limitations (no history row seeded; cancellation before resolver) will remain explicit in checkpoint81. Renderer restoration is in progress; final promotion waits API/components/native.

Final checks pass:30 source cases,10 HTTP cases (6new+4management),307 focused renderer cases,13 typography leaf cases and8 distinct native cases. TypeScript/format/production build and fixture build pass. Both surfaces visually inspected; native1234 count uses actual1235-to1 library and persisted choice; cached Panzer/Hades links use saved browser destination with captured OS boundary. Original misconfigured destination runs retained in evidence. All23 methods resolved:5ported17retained1narrow framework scanner. Inventory1589/682/33/111pending/20partial. Checkpoint-eighty-one.md and task38126-native-evidence.json record scope/limits; no physical-device or held-initial-startup claim.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored exact account visibility label/count/caveat and readable cached GOG notes on desktop/fullscreen. Retained shared ownership/install/snapshot behavior with30 unchanged source cases,10API,307component and8native passes. Five methods ported,17retained and1narrow C# scanner classified; checkpoint81 records exact fixtures, captures and limits.
<!-- SECTION:FINAL_SUMMARY:END -->
