---
id: TASK-381.32
title: 'Electron: finish theme colors, contrast and persistence'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-01 22:27'
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
ordinal: 450000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Bundled, legacy and authored themes must retain their palette, contrast and saved selection across Electron surfaces and reloads.

Owns 14 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/BundledThemeTests.cs
- BundledThemeTests.Authored_themes_ship_without_a_local_theme_directory [pending at split]
- BundledThemeTests.Selecting_dawn_applies_its_solid_default_and_retains_audit_findings [pending at split]

tests/Winnow.Tests/DawnThemeContrastTests.cs
- DawnThemeContrastTests.Legacy_files_infer_variant_and_explicit_variant_round_trips [pending at split]

tests/Winnow.Tests/ThemeContrastTests.cs
- ThemeContrastTests.Applying_a_theme_writes_the_brushes_in_place [pending at split]
- ThemeContrastTests.A_substituted_backdrop_is_not_the_same_answer_as_a_refused_one [pending at split]
- ThemeContrastTests.The_field_opens_only_when_the_desktop_is_actually_arriving [partial at split]

tests/Winnow.Tests/ThemeJsonTests.cs
- ThemeJsonTests.Every_token_matches_at_every_slider_position [partial at split]

tests/Winnow.Tests/UserThemeStoreTests.cs
- UserThemeStoreTests.Preparing_the_theme_folder_reports_nothing_when_there_is_no_folder [partial at split]

tests/Winnow.Ui.Tests/AppearanceInteractionTests.cs
- AppearanceInteractionTests.Restored_focus_keeps_scrolled_position_but_keyboard_focus_reveals_control [pending at split]
- AppearanceInteractionTests.Fresh_appearance_uses_first_choices_and_collapses_warnings [pending at split]

tests/Winnow.Ui.Tests/DawnControlContrastTests.cs
- DawnControlContrastTests.Desktop_action_templates_keep_labels_and_focus_visible_in_every_interactive_state [pending at split]
- DawnControlContrastTests.Dawn_selected_segments_and_chip_hover_keep_contrast_over_tinted_fills [pending at split]
- DawnControlContrastTests.Fluent_templates_follow_dark_light_dark_switches_without_stale_control_brushes [pending at split]
- DawnControlContrastTests.Fullscreen_real_action_template_keeps_focus_underline_and_labels_readable [pending at split]

Batch boundary: the user authorized TASK-381.31 through TASK-381.35 in order. Keep one implementation task active and commit each milestone. After completing this task, continue to TASK-381.33; pause for user review after TASK-381.35.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original bundled/JSON/user-theme and appearance preference contracts preserve catalogue precedence, legacy hoard compatibility and saved authored theme behavior.
- [ ] #2 Original dark/Dawn text/control contrast matrices pass for desktop and fullscreen, including translucency/accessibility fallbacks and interactive state colors.
- [ ] #3 All 14 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, then continue to TASK-381.33 within the user-authorized TASK-381.31 through TASK-381.35 batch.
<!-- DOD:END -->
