---
id: TASK-381.17
title: 'Electron: finish activity paging, recovery and journal interactions'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 11:12'
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
ordinal: 435000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Paged activity and journal edits need to retain loaded data, drafts and selection through retries, notifications and session recovery.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/GameJournalViewModelTests.cs
- GameJournalViewModelTests.Journal_edits_and_deletes_the_saved_entry [pending at split]
- GameJournalViewModelTests.Empty_journal_names_whether_the_prompt_is_off [pending at split]

tests/Winnow.Ui.Tests/ActivityPagingInteractionTests.cs
- ActivityPagingInteractionTests.Week_navigation_discards_an_older_response_even_when_its_reader_ignores_cancellation [partial at split]
- ActivityPagingInteractionTests.Load_more_is_explicit_and_disposal_cancels_a_pending_page_without_late_publication [pending at split]
- ActivityPagingInteractionTests.Slow_account_reads_leave_the_dispatcher_available_and_cancellation_does_not_publish [pending at split]

tests/Winnow.Ui.Tests/ActivityRecoveryInteractionTests.cs
- ActivityRecoveryInteractionTests.Failed_reads_show_a_retry_action_and_retry_the_same_page [partial at split]
- ActivityRecoveryInteractionTests.Finishing_a_read_preserves_the_section_tab_chosen_while_waiting [partial at split]
- ActivityRecoveryInteractionTests.Saving_a_note_on_an_older_loaded_page_keeps_its_selection_and_does_not_refetch_page_one [partial at split]
- ActivityRecoveryInteractionTests.Account_summary_reports_loading_and_retry_without_losing_focus_or_publishing_after_disposal [pending at split]

tests/Winnow.Ui.Tests/FullscreenActivityTests.cs
- FullscreenActivityTests.Empty_sections_explain_their_content_and_horizontal_navigation_changes_weeks_outside_tabs [partial at split]
- FullscreenActivityTests.Library_reload_refreshes_notes_preserves_session_and_removes_hidden_games [partial at split]
- FullscreenActivityTests.Session_editor_saves_to_the_same_journal_repository_and_keeps_existing_rating [partial at split]

tests/Winnow.Ui.Tests/FullscreenToolsHierarchyTests.cs
- FullscreenToolsHierarchyTests.Journal_groups_keep_the_editor_and_rating_actions_reachable [pending at split]

tests/Winnow.Ui.Tests/JournalDetailsInteractionTests.cs
- JournalDetailsInteractionTests.Details_modal_edits_then_deletes_a_journal_entry [partial at split]

tests/Winnow.Ui.Tests/JournalNotificationTests.cs
- JournalNotificationTests.A_headless_or_missing_native_window_reports_unavailable_without_an_application_error [partial at split]

tests/Winnow.Ui.Tests/SessionNoteParityTests.cs
- SessionNoteParityTests.Each_editor_rejects_empty_entries_and_trims_successful_notes [partial at split]
- SessionNoteParityTests.Failed_saves_keep_the_draft_and_pending_retries_block_conflicting_edits [partial at split]

tests/Winnow.Ui.Tests/SessionRecoveryParityTests.cs
- SessionRecoveryParityTests.Recovered_sitting_keeps_one_note_and_one_activity_record [partial at split]

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original paging, recovery and fullscreen Activity behaviors preserve loaded pages, ordered records, retry outcomes and selection.
- [x] #2 Session notes, ratings, journal notifications and Details editors preserve original save/cancel/recovery semantics and focus on both surfaces, without fabricated history.
- [x] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit all 18 frozen paging, recovery, journal and notification contracts against current Electron/API behavior and exact original fixtures. 2. Correct demonstrated lifecycle, focus, edit, retry and recovery gaps on desktop and fullscreen. 3. Execute original and equivalent component/API/native tests with exact edge cases, inspect visual evidence, and record checkpoint72 plus per-method migration evidence. 4. Commit the verified milestone and proceed to TASK-381.18.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after activity tracker milestone a871eaf9. Seventh task in the authorized sequential batch.

Audit confirms all nine assigned source files still match frozen revision; original matrix is 30 cases. Native notification availability now requires a usable activation window, preserving valid tray-hidden windows and returning unavailable for missing/headless/destroyed targets. Five focused notification cases pass. Paging fixtures retain exact one-row responses and cursor/session identities; native process and lifecycle evidence is being added separately from component Promise tests.

Source30 and HTTP17 pass. Renderer now serializes Activity reads to the latest scope, cancels physical requests on disposal, edits desktop Details journal inline while preserving drafts across tabs, guards pending journal writes, and restores fullscreen prompt grouping/rating text. Build/typecheck pass; 149 focused renderer cases and final28 source-contract cases pass, plus49 account checks. Native40-case matrix is running, with existing journal consumers queued for regression checks. Initial desktop paging, account cancellation/retry focus, recovery and notification IPC cases pass; a rating-control harness assumption is being aligned with the restored inline buttons.

Native verification identified and fixed intermittent Chromium retry focus loss, outer Details dismissal during a pending journal save, and misleading local Activity trigger badges. Global LB/RB and prompt Y Keyboard hints remain. Final production build passes; 127 affected renderer cases, 8 account recovery cases, and the repeated 17-case HTTP gate pass. The 40-case native matrix is being completed with focused reruns, plus 13 earlier native journal consumers. Shared activity observer lifetime is covered separately; all 18 migration mappings remain provisional until final native evidence.

Final verification: all3789 component/live API cases across187 files pass in99.33s; original30 and finalHTTP17 pass; all40 new source native cases plus13 earlier consumers pass, with4 Account focus repeats and5 final typography reruns. Build/typecheck, formatting, diff check and migration audit pass. Eighteen screenshots reviewed. All18 assigned methods are ported; inventory1476 ported/650 retained/32 framework/238 pending/39 partial. See checkpoint72 and .tmp/task38117-native-evidence.json. Native input is simulated standard Gamepad API, not physical-controller validation.

Milestone committed as c7f3fbcb. All acceptance criteria are verified; continuing to the eighth task, TASK-381.18, within the authorized ten-task batch.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Finished Activity paging, cancellation and recovery; inline desktop journal editing; focus-preserving account retry; pending-write dismissal guards; and fullscreen prompt grouping, controller navigation, typography and hints. Verified 3,789 component/live API cases, 30 unchanged source cases, 17 HTTP cases and 53 distinct native cases, plus focused repeats. Checkpoint 72 records fixtures, adaptations and visual evidence. All 18 source methods are now ported. Continue to TASK-381.18 within the authorized batch.
<!-- SECTION:FINAL_SUMMARY:END -->
