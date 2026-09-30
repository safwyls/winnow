---
id: TASK-381.22
title: 'Electron: finish artwork browsing, selection and backdrops'
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
ordinal: 440000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Artwork choices and source order must agree across plugin browsing, saved selections and automatic desktop/fullscreen backdrops.

Owns 16 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ArtworkBrowserPluginIntegrationTests.cs
- ArtworkBrowserPluginIntegrationTests.Packaged_SteamGridDB_browses_all_slots_and_keeps_selected_original_after_disable_and_restart [pending at split]
- ArtworkBrowserPluginIntegrationTests.Host_filters_wrong_kind_offhost_and_malformed_plugin_candidates_before_registration [pending at split]

tests/Winnow.Tests/ArtworkBrowserServiceTests.cs
- ArtworkBrowserServiceTests.Current_cover_uses_live_library_projection_but_never_replaces_a_saved_choice [pending at split]

tests/Winnow.Tests/ArtworkPreferencesTests.cs
- ArtworkPreferencesTests.Saved_order_survives_restart_and_invalid_entries_keep_all_sources [pending at split]
- ArtworkPreferencesTests.Failed_write_does_not_publish_or_notify [pending at split]
- ArtworkPreferencesTests.Source_blocks_reorder_while_saved_art_and_standard_hero_keep_their_positions [pending at split]

tests/Winnow.Tests/ArtworkSelectionIntegrationTests.cs
- ArtworkSelectionIntegrationTests.Service_shares_confirmed_copies_but_keeps_expansions_and_unlinked_works_separate [pending at split]
- ArtworkSelectionIntegrationTests.Configured_imports_survive_refresh_and_reopen_then_reset_the_group_slot [pending at split]

tests/Winnow.Tests/BackdropSelectionTests.cs
- BackdropSelectionTests.Landscape_selection_uses_crop_resolution_and_excludes_unsuitable_assets [pending at split]
- BackdropSelectionTests.Hd_screenshot_precedes_unknown_or_small_art_and_legacy_order_is_stable [pending at split]
- BackdropSelectionTests.Saved_background_leads_and_duplicates_are_removed [pending at split]
- BackdropSelectionTests.Known_Steam_heroes_bracket_IGDB_after_the_saved_background [pending at split]
- BackdropSelectionTests.Grouped_non_Steam_playable_copy_keeps_known_Steam_hero_ids [pending at split]

tests/Winnow.Ui.Tests/ArtworkPreferenceBackdropTests.cs
- ArtworkPreferenceBackdropTests.Fullscreen_unowned_group_root_background_precedes_owned_child_heroes [pending at split]
- ArtworkPreferenceBackdropTests.Desktop_live_order_retains_pixels_until_replacement_and_unsubscribes_on_dispose [pending at split]
- ArtworkPreferenceBackdropTests.Fullscreen_live_order_retains_pixels_and_detach_releases_subscriptions [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original browser/plugin, preference, candidate-order and selection contracts pass with exact persisted identity and revision behavior.
- [ ] #2 Both surfaces apply saved and automatic backdrop choices consistently, recover from unavailable images and release replaced artwork without losing the visible image.
- [ ] #3 All 16 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
