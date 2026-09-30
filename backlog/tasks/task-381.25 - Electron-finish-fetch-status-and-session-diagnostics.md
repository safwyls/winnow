---
id: TASK-381.25
title: 'Electron: finish fetch status and session diagnostics'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original fetch aggregation, title-bar status, diagnostics logging and watcher failure/recovery contracts retain accurate progress, terminal state and error visibility.
- [ ] #2 Desktop and fullscreen diagnostics/application-settings routes remain accessible without stealing focus or exposing secrets; cancellation/disposal stop obsolete updates.
- [ ] #3 All 13 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
