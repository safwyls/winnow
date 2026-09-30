---
id: TASK-381.30
title: 'Electron: finish Steam account capture and reported history'
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
ordinal: 448000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Captured Steam account pages carry provenance and truncation limits needed for honest spending/history display and the existing acquisition CSV export. Broader portable export remains TASK-2 and is outside this parity task.

Owns 17 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/AcquisitionExportTests.cs
- AcquisitionExportTests.Export_preserves_acquisition_facts_and_quotes_titles [pending at split]
- AcquisitionExportTests.Command_reports_saved_cancelled_and_failed_destinations [pending at split]
- AcquisitionExportTests.Missing_export_service_disables_the_command [pending at split]

tests/Winnow.Tests/SteamAccount/SteamAccountPageTruncationTests.cs
- SteamAccountPageTruncationTests.The_session_reports_completeness_from_what_it_watched_happen [pending at split]
- SteamAccountPageTruncationTests.A_cap_that_stopped_the_walk_is_not_completeness [pending at split]

tests/Winnow.Tests/SteamAccount/SteamAccountPagesContractTests.cs
- SteamAccountPagesContractTests.A_caller_that_ignores_the_licences_walk_still_compiles_and_reads_sensibly [partial at split]

tests/Winnow.Tests/SteamAccount/SteamPageHarvesterAvailabilityTests.cs
- SteamPageHarvesterAvailabilityTests.A_console_less_host_is_told_so_rather_than_left_waiting [pending at split]
- SteamPageHarvesterAvailabilityTests.Nothing_opens_without_consent_having_been_recorded [pending at split]
- SteamPageHarvesterAvailabilityTests.A_null_request_is_a_programming_error_not_an_outcome [pending at split]

tests/Winnow.Tests/SteamAccountPageProvenanceTests.cs
- SteamAccountPageProvenanceTests.Unknown_saved_pages_never_borrow_the_current_account_or_fill_its_projection [pending at split]
- SteamAccountPageProvenanceTests.Changed_or_lost_capture_identity_is_rejected_permanently [pending at split]
- SteamAccountPageProvenanceTests.Captured_pages_must_agree_on_account_and_parser_retains_it_without_reading_HTML_identity [pending at split]

tests/Winnow.Ui.Tests/SavedLicensePagesTests.cs
- SavedLicensePagesTests.Both_presentations_import_two_saved_pages_and_repeat_without_duplicate_facts [pending at split]

tests/Winnow.Ui.Tests/SteamReportedActivityTests.cs
- SteamReportedActivityTests.Scope_filters_other_accounts_hidden_games_and_covered_increases [partial at split]
- SteamReportedActivityTests.Both_presentations_label_estimates_and_bounds_without_changing_sessions [partial at split]
- SteamReportedActivityTests.Non_Steam_games_hide_the_section_on_both_surfaces_but_mixed_games_keep_it [partial at split]
- SteamReportedActivityTests.Fullscreen_activity_and_per_game_history_offer_separate_entry_points [partial at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original page capture, availability, truncation and provenance contracts preserve source boundaries, consent and partial/failure outcomes.
- [ ] #2 Saved licences, Steam-reported activity and existing acquisition CSV exports preserve source values and presentation on both surfaces without expanding into the separate portable-export project.
- [ ] #3 All 17 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
