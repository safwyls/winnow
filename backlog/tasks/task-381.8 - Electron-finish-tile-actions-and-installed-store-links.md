---
id: TASK-381.8
title: 'Electron: finish tile actions and installed store links'
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
- [ ] #1 Original tile primary/secondary actions, plugin dispatch and install-refresh contracts pass with exact identities and no duplicate side effects.
- [ ] #2 Store and folder destinations refresh after install state changes and retain source validation/fallback behavior on desktop and fullscreen; tests intercept OS launches.
- [ ] #3 All 14 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
