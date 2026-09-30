---
id: TASK-381.33
title: 'Electron: finish typography and floating layout behavior'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
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
ordinal: 451000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Font packages, live typography changes and floating chrome can alter measured controls and clipping even when token values match.

Owns 13 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/FloatingLayoutTests.cs
- FloatingLayoutTests.The_layout_moves_only_the_ground_and_the_caption [partial at split]
- FloatingLayoutTests.A_field_is_one_step_above_the_pane_in_either_layout [pending at split]
- FloatingLayoutTests.An_unknown_stored_layout_reads_as_unset [pending at split]
- FloatingLayoutTests.A_stored_layout_comes_back [partial at split]
- FloatingLayoutTests.An_overridden_session_never_writes_a_layout [pending at split]

tests/Winnow.Ui.Tests/SegmentedSelectorContainmentTests.cs
- SegmentedSelectorContainmentTests.Segment_states_preserve_the_rounded_group_boundary [pending at split]

tests/Winnow.Ui.Tests/ThemeTypographyControlsTests.cs
- ThemeTypographyControlsTests.Desktop_font_pickers_and_size_follow_the_selected_theme [partial at split]
- ThemeTypographyControlsTests.Desktop_maximum_size_keeps_typography_controls_reachable_and_reset_restores_authored_values [partial at split]

tests/Winnow.Ui.Tests/ThemeTypographyRuntimeTests.cs
- ThemeTypographyRuntimeTests.Desktop_text_and_reading_line_height_follow_live_resources_without_scaling_geometry [partial at split]
- ThemeTypographyRuntimeTests.Missing_font_falls_back_by_role_and_installed_fonts_are_available [partial at split]
- ThemeTypographyRuntimeTests.Plain_inputs_follow_theme_face_and_size [pending at split]
- ThemeTypographyRuntimeTests.Native_fullscreen_popout_sizes_consent_hints_and_keyboard_from_stable_baselines [pending at split]
- ThemeTypographyRuntimeTests.Fullscreen_combines_theme_and_page_scale_without_accumulation_or_scaling_icons [partial at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original runtime font/typography control matrices update actual rendered text and preserve source metrics, measure and keyboard/controller targets.
- [ ] #2 Floating layout and segmented selectors remain contained at the original window/scaling boundaries on both applicable presentation paths.
- [ ] #3 All 13 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
