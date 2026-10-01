---
id: TASK-381.31
title: 'Electron: finish update acknowledgement composition'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-01 22:55'
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

Batch boundary: the user authorized TASK-381.31 through TASK-381.35 in order. Keep one implementation task active and commit each milestone. After completing this task, continue to TASK-381.32; pause for user review after TASK-381.35.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All original acknowledgement composition contracts preserve exact per-release correlation, watermark and grouped-count semantics through the production API.
- [x] #2 Desktop and fullscreen library, feed, Details and timeline refresh consistently after mark/read restore, including refusal, partial success and retry without duplicate writes.
- [x] #3 All 9 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, then continue to TASK-381.32 within the user-authorized TASK-381.31 through TASK-381.35 batch.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute the nine frozen acknowledgement composition methods and compare exact source fixtures with existing Electron/API assertions. 2. Preserve displayed release watermarks, Patched-only selection eligibility, grouped/multiple selection and partial failure/retry behavior on both surfaces. 3. Verify the production API with exact grouped update fixtures and serialized native desktop/fullscreen journeys; fix demonstrated gaps only. 4. Review visual evidence and source mappings, run appropriate regression gates, update documentation and checkpoint86, commit, then continue to TASK-381.32 within the authorized five-task batch.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The user authorized five more sequential tasks: TASK-381.31 through TASK-381.35. This authorization supersedes the older per-task stop wording. Keep one implementation task active, verify and commit each, then stop after TASK-381.35. Root owns integration, official mapping, Backlog, evidence documentation and commits.

All 15 unchanged source cases pass for the nine assigned methods; original file matches frozen revision cf45d9f1127243a987d3cf6e664a32fc767ecb67. Renderer review found Patched action eligibility, synchronous duplicate suppression and already-saved release retry gaps. Exact grouped API fixture and desktop/fullscreen native journeys are in progress; no acceptance criteria checked yet.

All 15 unchanged source cases, 13 backend cases and 203 renderer cases across four files pass. TypeScript and production build pass. Independent review found no actionable issues. Native initial run has eight passing journeys and five harness mismatches under repair; no native completion claim yet. The original direct-repository count method will be ported through its exact four-row HTTP replacement, preserving the audit rules unchanged.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Ported all nine acknowledgement composition contracts. Library Mark as read now requires an unread selection in Patched, captures the full displayed selection before awaiting, suppresses repeated activation and retries only releases still unread. Fullscreen save errors use the panel reading scale. Verified 15 unchanged source cases, 13 backend cases, 203 renderer cases and 13 distinct native journeys; final CSS correction also passed 16 focused cases and paired native refusal checks. TypeScript/build and migration audit pass; inventory is 1635 ported, 705 retained-backend, 37 framework-specific, 48 pending and 10 partial. Complete migration gate remains expected to fail. Checkpoint eighty-six records presentation adaptation, exact fixtures and simulated-controller limits. Continue to TASK-381.32 after milestone commit.
<!-- SECTION:FINAL_SUMMARY:END -->
