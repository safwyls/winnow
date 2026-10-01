---
id: TASK-381.8
title: 'Electron: finish tile actions and installed store links'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:45'
updated_date: '2026-10-01 03:22'
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
ordinal: 426000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
A launch/install or plugin action must target the selected ownership and expose current store/folder links after installation changes.

Owns 14 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/GameLinkRouterTests.cs
- GameLinkRouterTests.Selects_supported_destinations_and_reports_fallbacks [partial at split]

tests/Winnow.Tests/InstallActionRefreshTests.cs
- InstallActionRefreshTests.Reload_updates_open_Epic_actions_without_losing_store_page_or_editor_draft [pending at split]

tests/Winnow.Tests/PluginGameActionIntegrationTests.cs
- PluginGameActionIntegrationTests.Resource_identifier_fallback_is_promoted_when_the_real_title_becomes_available [pending at split]
- PluginGameActionIntegrationTests.Imported_actions_survive_offline_refresh_and_route_through_the_active_provider_with_launch_attribution [pending at split]
- PluginGameActionIntegrationTests.Uninstall_removes_play_and_stale_buttons_cannot_bypass_the_latest_observation [pending at split]

tests/Winnow.Tests/TileActionsTests.cs
- TileActionsTests.No_sentence_is_ever_a_placeholder [pending at split]
- TileActionsTests.The_detail_panel_offers_exactly_the_tiles_action [pending at split]
- TileActionsTests.Every_tile_launches_through_the_librarys_own_command [pending at split]
- TileActionsTests.The_detail_panel_launches_through_the_same_command_as_the_tile [pending at split]
- TileActionsTests.The_grid_actions_carry_the_librarys_own_commands [pending at split]
- TileActionsTests.The_tile_names_the_bucket_the_way_the_rail_does [pending at split]

tests/Winnow.Ui.Tests/StoreLinkAfterInstallTests.cs
- StoreLinkAfterInstallTests.Steam_uninstall_menu_row_disappears_when_refreshed_ownership_is_uninstalled [pending at split]
- StoreLinkAfterInstallTests.Open_details_refreshes_install_to_play_without_losing_the_store_link [pending at split]
- StoreLinkAfterInstallTests.Epic_store_link_keeps_its_text_and_hit_target_during_and_after_install_dispatch [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original tile primary/secondary actions, plugin dispatch and install-refresh contracts pass with exact identities and no duplicate side effects.
- [x] #2 Store and folder destinations refresh after install state changes and retain source validation/fallback behavior on desktop and fullscreen; tests intercept OS launches.
- [x] #3 All 14 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit all fourteen frozen source contracts and existing Electron/backend evidence, preserving every theory row and identity assertion.
2. Delegate bounded tile command/copy coverage, install-refresh/store links and plugin action integration. Coordinator owns shared-file integration, link destination matrix, docs and native scheduling.
3. Repair demonstrated gaps on both surfaces, retaining controller focus, store/folder labels and unsaved editor state. Intercept every OS dispatch and use disposable data.
4. Run focused component/API checks and serialized native cases with screenshot review, then the complete component/API suite separately. Promote mappings only after equivalent evidence passes; update inventory and checkpoint evidence and commit.
5. Stop at TASK-381.8 for user review. Leave TASK-381.9 unstarted until prompted.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audited all fourteen frozen contracts, including the eighteenth router row through authenticated Steam Play. Implemented shared primary commands, collection descriptions, plugin source labels, stable store-link geometry and fullscreen sizing. Native measurements exposed moving More controls, small feedback/copy text and compounded 4K scaling; the complete component suite caught changed feed descriptions. All were corrected without weakening source assertions.

Final build/typecheck, formatting and whitespace checks pass. All 3,490 component/live API cases across 177 files pass in 86.65 seconds, with no skips. All 49 distinct native cases pass, including both surfaces, all four original store-link viewport sizes, exact Steam 440 dispatch, real plugin actions, shared pending operations, 4K proportions and enlarged text. All three unchanged PluginGameActionIntegrationTests pass. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-sixty-three.md and .tmp/task3818-native-summary.json.

All fourteen assigned methods are resolved: twelve ported and two retained backend. Inventory: 1,364 ported, 628 retained backend, 32 framework-specific, 338 pending and 73 partial; 411 remain unresolved. Physical controllers, the full .NET suite and installers remain later validation. Runs used disposable data with OS calls intercepted. TASK-381.9 remains To Do pending the user's continuation.

Reviewable implementation milestone: 510a5861 (Finish Electron tile actions and installed store links). Pausing at this task boundary for user review. TASK-381.9 has not been started.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Tiles, fullscreen X and Details share primary dispatch, pending operations and uncertain retries. Install refresh preserves drafts, destinations and store hit targets. Plugin evidence is visible; fullscreen text and 4K proportions are corrected. Build and all 3,490 component/API, 49 native and 3 original backend tests pass. All fourteen assigned methods are resolved; 411 pending/partial methods remain overall. Review evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-sixty-three.md. Stop here; TASK-381.9 remains To Do.

Milestone commit: 510a5861. Paused for user review before TASK-381.9.
<!-- SECTION:FINAL_SUMMARY:END -->
