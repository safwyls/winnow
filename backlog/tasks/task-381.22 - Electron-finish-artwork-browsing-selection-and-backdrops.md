---
id: TASK-381.22
title: 'Electron: finish artwork browsing, selection and backdrops'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
updated_date: '2026-10-01 17:13'
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

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original browser/plugin, preference, candidate-order and selection contracts pass with exact persisted identity and revision behavior.
- [x] #2 Both surfaces apply saved and automatic backdrop choices consistently, recover from unavailable images and release replaced artwork without losing the visible image.
- [x] #3 All 16 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the sixteen frozen artwork browser/plugin, preference, selection and backdrop contracts against shared backend and existing Electron coverage. 2. Preserve retained policy and add equivalent API/component/native coverage, fixing only demonstrated gaps on each surface. 3. Verify original fixtures, saved identities/revisions, unavailable-source fallbacks, retained pixels and detach cleanup; inspect desktop/fullscreen visuals. 4. Record checkpoint77 and exact mappings, commit the verified milestone, then continue to TASK-381.23.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after TASK-381.21 implementation70e38edf and completion3561e712. Baseline:3904 Electron component/live API cases pass; source inventory frozen at cf45d9f1127243a987d3cf6e664a32fc767ecb67. Unrelated scc-report.html and .claude/settings.local.json remain untouched.

Verification: all 3,918 Electron component/live API cases pass across 196 files, together with 17 unchanged original cases, 17 distinct native cases, 15 unique HTTP cases and two application preference regressions. Fixed live Current projection while preserving canonical writes, shared source-order publication, fullscreen page sizing/type/hints and fractional measurements at 21:9. Native fixture adaptations and reruns are documented in checkpoint77. The sixteen source methods resolve as six ported and ten retained-backend. Physical-controller validation remains separate. The 720p fullscreen preview at 140% text and source typography were visually inspected.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Current artwork follows the displayed header while saved choices and canonical revisions remain authoritative. Source-order changes apply immediately; fullscreen artwork preserves TV typography, a visible preview and controller/keyboard hints; exact ultrawide geometry survives interface zoom. All 3,918 component/live API, 17 native, 17 original, 15 HTTP and two application cases pass. See docs/spikes/2026-09-28-electron-parity/checkpoint-seventy-seven.md. Continue to TASK-381.23 after this milestone commit.
<!-- SECTION:FINAL_SUMMARY:END -->
