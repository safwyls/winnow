---
id: TASK-381.25
title: 'Electron: finish fetch status and session diagnostics'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
updated_date: '2026-10-01 19:47'
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
ordinal: 443000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Background fetch and session-watcher failures need accurate, reachable status and logs even while another page or dialog is open.

Owns 13 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ApplicationSettingsViewModelTests.cs
- ApplicationSettingsViewModelTests.Windows_registration_quotes_the_executable_and_starts_in_background [pending at split]

tests/Winnow.Tests/DiagnosticLoggingTests.cs
- DiagnosticLoggingTests.Startup_fault_is_persisted_without_a_host_even_when_its_normal_sink_is_open [pending at split]
- DiagnosticLoggingTests.Failure_to_write_startup_diagnostics_preserves_exit_code_and_alert [pending at split]
- DiagnosticLoggingTests.Startup_cancellation_does_not_write_failure_diagnostics [pending at split]

tests/Winnow.Tests/FetchStatusTests.cs
- FetchStatusTests.A_pass_with_nothing_to_do_shows_nothing [pending at split]
- FetchStatusTests.The_field_names_what_is_left_as_a_count [pending at split]
- FetchStatusTests.The_count_falls_as_the_pass_advances [pending at split]
- FetchStatusTests.The_field_disappears_on_completion [pending at split]
- FetchStatusTests.A_report_that_arrives_after_completion_does_not_bring_it_back [pending at split]
- FetchStatusTests.The_singular_and_the_plural_are_both_written [pending at split]

tests/Winnow.Ui.Tests/SessionWatcherDiagnosticsTests.cs
- SessionWatcherDiagnosticsTests.Settings_keep_log_access_after_recovery_on_both_surfaces [partial at split]
- SessionWatcherDiagnosticsTests.Failed_folder_open_exposes_manual_path_and_retry_clears_it [partial at split]

tests/Winnow.Ui.Tests/TitleBarFetchStatusTests.cs
- TitleBarFetchStatusTests.Fetch_progress_fits_caption_updates_and_survives_fullscreen_round_trip [pending at split]

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original fetch aggregation, title-bar status, diagnostics logging and watcher failure/recovery contracts retain accurate progress, terminal state and error visibility.
- [x] #2 Desktop and fullscreen diagnostics/application-settings routes remain accessible without stealing focus or exposing secrets; cancellation/disposal stop obsolete updates.
- [x] #3 All 13 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute and audit all13 frozen source methods/14 cases with their original progress counts, caption geometry, diagnostic privacy and folder retry assertions. 2. Restore a passive desktop fetch caption with a separate live reporter adapter, cancel stale reads and keep progress updates independent of full library refresh. 3. Persist bounded structured startup failure metadata without arbitrary messages or paths, preserve alert/exit/cancellation, and align native login-setting reads with writes. 4. Preserve accessible log actions after recovery and show the actual manual path on folder failures, clearing it on retry on both surfaces. 5. Verify focused component/API and isolated native cases plus desktop/fullscreen captures, update checkpoint80/mappings/docs, commit and continue to TASK-381.26.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after TASK-381.24 implementation c071f1a2 and completion12cdd35. Baseline3992 component/live API cases and31 native pass. Inventory1571 ported,665 retained,32 framework-specific,145 pending,22 partial. Read-only preparation confirms missing fetch caption, unsafe arbitrary-message startup persistence, login-option read/write mismatch and Settings log retry feedback gap. Preserve unrelated .claude/settings.local.json and scc-report.html. Root owns Backlog/docs/maps/integration; bounded host, renderer and native owners preserve one another edits.

All14 assigned original cases pass unchanged:11 main plus3 UI. Two additional original watcher failure/recovery surface cases also pass (16 executed bodies, no skips); captures/TRX use task38125-original-captures and task38125-dotnet-results. The new authenticated HTTP progress/health test passes, preserving997→1→0 snapshots, distinct events, wrong-operation recovery isolation and no private exception message/path in diagnostics. The isolated fixture builds with0 warnings/errors and exposes only authenticated test-control routes.

Final verification:202 focused host/component cases,16 original executed cases,1 authenticated API and6 distinct native cases pass. Caption font fix additionally passes3 targeted cases. Build/typecheck/format pass. Both log captures inspected after exact IPC-wrapper removal; affected native cases rerun successfully. Evidence checkpoint-eighty.md and task38125-native-evidence.json. All13 methods ported; inventory1584 ported/665 retained/32 framework/134 pending/20 partial. Actual autorun entries and physical devices were not exercised.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored passive fetch caption and isolated progress refresh, bounded private startup diagnostics, matching Windows login read/write options, and exact recoverable log feedback on desktop/fullscreen. Verified202 focused cases,16 source cases,1 API and6 native cases; all13 assigned contracts ported. See checkpoint80 for fixtures, captures and native limits.
<!-- SECTION:FINAL_SUMMARY:END -->
