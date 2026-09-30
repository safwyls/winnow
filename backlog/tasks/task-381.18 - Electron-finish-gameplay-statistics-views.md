---
id: TASK-381.18
title: 'Electron: finish gameplay statistics views'
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
ordinal: 436000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Statistics must expose the same recorded-session and library composition facts with consistent filter and interaction semantics.

Owns 11 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/GameplayStatsViewModelTests.cs
- GameplayStatsViewModelTests.Hidden_scope_reload_cancels_and_rejects_an_obsolete_result [pending at split]
- GameplayStatsViewModelTests.Identity_reload_updates_game_mapping_and_store_removal_resets_selection [pending at split]
- GameplayStatsViewModelTests.Search_does_not_filter_statistics_and_deactivation_rejects_pending_data [pending at split]
- GameplayStatsViewModelTests.Custom_local_day_uses_inclusive_calendar_dates_and_real_DST_duration [pending at split]
- GameplayStatsViewModelTests.Invalid_custom_dates_do_not_query_or_retain_obsolete_results [pending at split]

tests/Winnow.Ui.Tests/GameplayStatsInteractionTests.cs
- GameplayStatsInteractionTests.Xbox_import_updates_open_gameplay_store_choices_and_library_counts [partial at split]
- GameplayStatsInteractionTests.Both_surfaces_scope_dates_switch_sections_and_render_four_charts [partial at split]
- GameplayStatsInteractionTests.Desktop_error_retry_and_custom_date_validation_remain_actionable [partial at split]
- GameplayStatsInteractionTests.Fullscreen_gameplay_retry_cancel_and_resume_are_controller_actions [partial at split]
- GameplayStatsInteractionTests.Fullscreen_shell_applies_native_section_style_and_real_text_scale [pending at split]
- GameplayStatsInteractionTests.Spending_header_keeps_figures_above_the_fold [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original statistics calculations, empty/missing data, ranges and selected-store rules pass against the production backend inputs.
- [ ] #2 Desktop summary and fullscreen Gameplay paths preserve chart/selector interactions, meaningful labels and source layout boundaries.
- [ ] #3 All 11 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
