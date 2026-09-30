---
id: TASK-381.4
title: 'Electron: finish fullscreen sizing and rendering behavior'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:44'
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
ordinal: 422000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
TV layouts need the original safe areas, hierarchy and responsive behavior; a desktop fix does not establish fullscreen fidelity or rendering responsiveness.

Owns 19 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Ui.Tests/FullscreenBackdropTests.cs
- FullscreenBackdropTests.Desktop_backdrop_tries_ranked_art_then_screenshot_and_keeps_portrait_separate [partial at split]
- FullscreenBackdropTests.Steam_hero_adapts_crop_and_crossfades_independent_geometry [partial at split]

tests/Winnow.Ui.Tests/FullscreenCoverPaddingTests.cs
- FullscreenCoverPaddingTests.Padding_uses_each_artwork_edge_and_clears_when_released [pending at split]
- FullscreenCoverPaddingTests.Tall_artwork_fills_side_gaps_without_painting_over_the_image [pending at split]

tests/Winnow.Ui.Tests/FullscreenHomeLayoutTests.cs
- FullscreenHomeLayoutTests.Reducing_interface_scale_reduces_rendered_home_covers [partial at split]
- FullscreenHomeLayoutTests.Long_title_keeps_single_line_cover_geometry [partial at split]
- FullscreenHomeLayoutTests.Root_backdrops_fill_canvas_outside_safe_margins [partial at split]
- FullscreenHomeLayoutTests.Description_length_preserves_home_cover_geometry [partial at split]

tests/Winnow.Ui.Tests/FullscreenInformationHierarchyTests.cs
- FullscreenInformationHierarchyTests.Reading_has_a_bounded_measure_and_keeps_navigation_outside_scrolling_text [pending at split]
- FullscreenInformationHierarchyTests.Activity_rows_separate_titles_dates_and_notes_while_retaining_selection [pending at split]

tests/Winnow.Ui.Tests/FullscreenRowViewportTests.cs
- FullscreenRowViewportTests.Row_refresh_keeps_neighbors_and_notifies_only_for_realization_changes [partial at split]

tests/Winnow.Ui.Tests/FullscreenScaleTests.cs
- FullscreenScaleTests.Scale_changes_all_content_with_fixed_physical_margins_and_full_backdrop [pending at split]
- FullscreenScaleTests.Scale_persists_clamps_and_resets_without_changing_text_size_until_confirmed [pending at split]
- FullscreenScaleTests.Legacy_scale_switches_to_new_baseline_while_other_preferences_and_new_adjustments_survive [pending at split]

tests/Winnow.Ui.Tests/FullscreenShelfIndicatorTests.cs
- FullscreenShelfIndicatorTests.Shelf_targets_select_by_mouse_without_taking_controller_focus [pending at split]
- FullscreenShelfIndicatorTests.Endpoint_arrows_are_disabled_and_all_shelf_dots_remain_visible [pending at split]

tests/Winnow.Ui.Tests/FullscreenStutterDiagnosticsTests.cs
- FullscreenStutterDiagnosticsTests.Scheduled_render_frames_finish_the_row_transition [pending at split]
- FullscreenStutterDiagnosticsTests.Row_navigation_keeps_geometry_stable_before_layout [pending at split]
- FullscreenStutterDiagnosticsTests.Home_navigation_preserves_geometry_and_unchanged_footer [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original reference/minimum/ultrawide and text/interface scale matrices preserve cover padding, artwork extent, information hierarchy, shelf indicators and visible row geometry.
- [ ] #2 Original layout/performance diagnostic contracts have equivalent measurements or justified mechanism-specific dispositions; resizing and returning to desktop do not introduce stale layout or persistent rendering work.
- [ ] #3 All 19 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
