---
id: TASK-381.32
title: 'Electron: finish theme colors, contrast and persistence'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-01 23:32'
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
- [x] #1 Original bundled/JSON/user-theme and appearance preference contracts preserve catalogue precedence, legacy hoard compatibility and saved authored theme behavior.
- [x] #2 Original dark/Dawn text/control contrast matrices pass for desktop and fullscreen, including translucency/accessibility fallbacks and interactive state colors.
- [x] #3 All 14 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary, then continue to TASK-381.33 within the user-authorized TASK-381.31 through TASK-381.35 batch.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Compare all fourteen frozen source methods with existing theme/catalogue/appearance coverage and execute the unchanged source fixtures. 2. Preserve original bundled and authored palette precedence, legacy IDs, token matrices, backdrop fallbacks and saved choices; repair only demonstrated Electron gaps. 3. Verify actual desktop/fullscreen controls and all interactive contrast states in serialized native journeys, with persistence and restore-focus behavior. 4. Integrate exact executed method mappings, review screenshots and measured contrast, update checkpoint eighty-seven and domain documentation, then commit and continue to TASK-381.33.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
TASK-381.31 completed at a256cf60. Starting task two of the authorized five-task batch. Root owns official mapping, docs, Backlog, review and commits. Source/API and native test lanes remain serialized; focused Electron tests precede a frozen production build and native verification. Broad component/API gate is reserved for the batch end unless new failures justify earlier execution.

All fourteen original methods expand to 31 cases and pass unchanged (main23, UI8). Underlying theme implementation and source files match frozen revision; eleven original captures retained. A reusable test-only exporter records61color tokens across9themes and756states, after46116 original round-trip comparisons. Renderer review found bundled audit warnings missing without local themes and fullscreen pressed/current underline states missing; fixes and native contrast checks are underway. No AC checked yet.

Milestone commit 1d85c480 records implementation, exact source mappings and checkpoint eighty-seven. All four acceptance criteria are verified. The complete migration gate remains open for other tasks.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed theme colors, contrast and persistence on desktop and fullscreen. Fixed chained HSV rounding, bundled audit warnings and fullscreen current-action styling. Verification: 31 unchanged source cases, 270 focused renderer cases, 9 final markup cases, 22 current-state cases, 11 distinct native journeys, TypeScript and build pass. Golden fixture preserves 61 original colors across 756 states; native minimum enabled action text contrast is 4.807:1. Eleven methods ported; three narrow framework-specific methods concern sparse mutable brushes and actual compositor feedback. Audit validates 1646 ported, 705 retained-backend, 40 framework-specific, 37 pending and 7 partial; complete gate still fails as expected. Evidence: checkpoint-eighty-seven.md. Continue to TASK-381.33 after the milestone commit.
<!-- SECTION:FINAL_SUMMARY:END -->
