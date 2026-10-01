---
id: TASK-381.16
title: 'Electron: finish activity tracker and large-history behavior'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 10:08'
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

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original tracker projection, plot geometry, selection and interaction matrices preserve chronology, zero/unknown evidence and update/session labels.
- [x] #2 Large history meets source responsiveness expectations with reproducible measurements; desktop Activity and fullscreen Play history retain accessible selection and focus.
- [x] #3 All 8 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the eight frozen activity/large-history contracts and current equivalents, including exact data volume and measurement expectations. 2. Fix demonstrated desktop/fullscreen projection, geometry, selection and responsiveness gaps. 3. Execute original and equivalent component/API/native tests, inspect visual evidence and record checkpoint71 with per-method inventory evidence. 4. Commit the verified milestone and continue to TASK-381.17.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after Details reading milestone adef2a8e. Sixth task in the authorized sequential batch.

Audit preserves exact180/700px plot and360/620px tracker fixtures. Two original tracker cases pass. Source large fixture retains125000sessions/notes and measures backend-read guard plus UI availability rather than a universal frame threshold. Preliminary actual TypeScript projection measurement with5060 input sessions and14600 snapshots: tracked formatting470-491ms, lifetime24-58ms. Fixing repeat formatting/projection and missing unread summary; classification remains in the existing adapter. Native probe will use production components for exact source props, separately from real API-backed Details/Activity/account workflows.

Focused Electron checks now70/70 with Pacific-time UTC axis assertions; production build passes. Restored30px scaled monospace total after native visual review. Initial native run:12passed,4failed,4notrun. Plot failures traced to isolated probe CSS import order/theme variable injection, not production app geometry; fullscreen large setup used a desktop-only search field. Correcting those harness boundaries and waiting for bound history/statistics before recording final UI timings. Original10 and new HTTP5 remain green.

Final verification: 3741/3741 component/live API cases across 185 files (95.72s); 22 distinct native cases; 10 unchanged source cases and 5 new HTTP cases pass. Build/typecheck, formatting and diff checks pass. Exact source-volume Details readiness improves from about 3.9s to 0.8s on both surfaces; the remaining 378–409ms cold-render gap is documented. All eight assigned methods are ported; inventory now 1458 ported, 650 retained-backend, 32 framework-specific, 244 pending and 51 partial. Evidence and adaptations: checkpoint-seventy-one.md and .tmp/task38116-native-evidence.json.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored tracker summaries, scaled typography and UTC geometry; preserved exact plot/range/acknowledgement contracts. Deferred hidden history rows and reused formatters/projections, reducing measured large-library Details readiness by about 79% without removing records. All 3741 component/live API, 22 native, 10 source and 5 HTTP cases pass. Checkpoint71 records performance limits and all eight migration mappings. Continue the authorized batch with TASK-381.17.
<!-- SECTION:FINAL_SUMMARY:END -->
