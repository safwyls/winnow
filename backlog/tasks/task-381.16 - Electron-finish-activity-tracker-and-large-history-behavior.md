---
id: TASK-381.16
title: 'Electron: finish activity tracker and large-history behavior'
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
ordinal: 434000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
History visualization must distinguish measured activity from missing evidence and remain interactive with a large library history.

Owns 8 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ActivityTrackerViewModelTests.cs
- ActivityTrackerViewModelTests.Acknowledgement_refreshes_plot_marks_without_changing_range [pending at split]
- ActivityTrackerViewModelTests.Linked_copy_scope_and_sparse_history_are_explicit [pending at split]

tests/Winnow.Ui.Tests/ActivityTimelinePlotTests.cs
- ActivityTimelinePlotTests.Lifetime_months_have_equal_width_and_named_pointer_and_keyboard_selection [partial at split]
- ActivityTimelinePlotTests.Session_bars_are_ten_pixels_and_collisions_preserve_all_observations [partial at split]
- ActivityTimelinePlotTests.Updates_cluster_without_a_count_limit_and_request_tracked_range [partial at split]
- ActivityTimelinePlotTests.Narrow_layout_keeps_date_labels_and_update_targets_inside_plot [partial at split]

tests/Winnow.Ui.Tests/ActivityTrackerInteractionTests.cs
- ActivityTrackerInteractionTests.Range_buttons_switch_units_and_keep_monthly_and_session_records_reachable [partial at split]

tests/Winnow.Ui.Tests/LargeHistoryResponsivenessTests.cs
- LargeHistoryResponsivenessTests.Measure_details_activity_and_account_summary [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original tracker projection, plot geometry, selection and interaction matrices preserve chronology, zero/unknown evidence and update/session labels.
- [ ] #2 Large history meets source responsiveness expectations with reproducible measurements; desktop Activity and fullscreen Play history retain accessible selection and focus.
- [ ] #3 All 8 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
