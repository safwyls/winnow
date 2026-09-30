---
id: TASK-381.15
title: 'Electron: finish remaining Details and lightbox contracts'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
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
ordinal: 433000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remaining Details/lightbox contracts require complete source-equivalent evidence beyond the populated layouts already verified.

Owns 11 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/DetailsModalScaleTests.cs
- DetailsModalScaleTests.The_width_floor_stays_above_the_cards_own_minimum [pending at split]

tests/Winnow.Tests/Enforcement/ScreenshotLightboxStructureTests.cs
- ScreenshotLightboxStructureTests.The_overlay_is_not_a_popup [pending at split]
- ScreenshotLightboxStructureTests.It_is_declared_in_the_windows_grid_after_the_modal [pending at split]
- ScreenshotLightboxStructureTests.Control_fills_use_theme_tokens_without_deferred_runtime_bindings [pending at split]

tests/Winnow.Tests/ScreenshotLightboxTests.cs
- ScreenshotLightboxTests.No_string_the_overlay_speaks_is_ever_a_placeholder [pending at split]

tests/Winnow.Ui.Tests/AchievementPresentationTests.cs
- AchievementPresentationTests.Both_surfaces_render_ingested_account_states_without_inventing_zero [pending at split]

tests/Winnow.Ui.Tests/GameDetailsTabInteractionTests.cs
- GameDetailsTabInteractionTests.Keyboard_arrows_switch_tabs_and_leave_focus_on_the_selected_tab [partial at split]
- GameDetailsTabInteractionTests.Long_overview_keeps_its_scroll_position_when_activity_is_opened [partial at split]
- GameDetailsTabInteractionTests.Wrong_game_menu_focuses_search_and_returns_without_losing_query_or_results [partial at split]

tests/Winnow.Ui.Tests/ProseMeasureTests.cs
- ProseMeasureTests.Desktop_prose_keeps_its_measure_and_left_edge_across_reading_surfaces [pending at split]
- ProseMeasureTests.Fullscreen_prose_retains_the_TV_typography_and_available_width [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every assigned original Details tab, achievement, prose/scale and lightbox contract has complete equivalent assertions using the original data/geometry boundaries.
- [ ] #2 Desktop modal and fullscreen reading/gallery paths keep complete images, readable text, correct tab ownership and keyboard/controller origin restoration.
- [ ] #3 All 11 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
