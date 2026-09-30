---
id: TASK-381.31
title: 'Electron: finish update acknowledgement composition'
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
ordinal: 449000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Unread badges, grouped release watermarks and read/unread commands span multiple surfaces and must remain consistent after partial writes or refresh.

Owns 9 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Ui.Tests/UpdateAcknowledgementCompositionTests.cs
- UpdateAcknowledgementCompositionTests.Selection_acknowledges_only_patches_visible_before_a_new_push_arrives [pending at split]
- UpdateAcknowledgementCompositionTests.Desktop_context_menu_binds_mark_as_read_only_for_Patched_selection [pending at split]
- UpdateAcknowledgementCompositionTests.Fullscreen_library_options_activate_mark_as_read_for_selected_game [pending at split]
- UpdateAcknowledgementCompositionTests.Patched_selection_marks_grouped_and_multiple_games_read_leaves_unselected_and_allows_later_patches [pending at split]
- UpdateAcknowledgementCompositionTests.Mark_selection_as_read_requires_an_unread_selection_in_Patched [pending at split]
- UpdateAcknowledgementCompositionTests.Patched_selection_reports_persistence_failure_and_keeps_unread_games [partial at split]
- UpdateAcknowledgementCompositionTests.Production_details_marks_and_restores_only_the_displayed_group_releases [partial at split]
- UpdateAcknowledgementCompositionTests.A_push_arriving_after_details_open_is_not_acknowledged_and_reopening_reads_its_watermark [partial at split]
- UpdateAcknowledgementCompositionTests.Counts_respect_never_played_unknown_date_and_the_effective_play_boundary [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 All original acknowledgement composition contracts preserve exact per-release correlation, watermark and grouped-count semantics through the production API.
- [ ] #2 Desktop and fullscreen library, feed, Details and timeline refresh consistently after mark/read restore, including refusal, partial success and retry without duplicate writes.
- [ ] #3 All 9 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
