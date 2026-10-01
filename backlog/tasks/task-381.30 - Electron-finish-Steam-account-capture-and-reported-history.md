---
id: TASK-381.30
title: 'Electron: finish Steam account capture and reported history'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
updated_date: '2026-10-01 22:11'
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

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. This is the final task in that batch. Verify and commit the milestone, then pause for review without starting TASK-381.31.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original page capture, availability, truncation and provenance contracts preserve source boundaries, consent and partial/failure outcomes.
- [x] #2 Saved licences, Steam-reported activity and existing acquisition CSV exports preserve source values and presentation on both surfaces without expanding into the separate portable-export project.
- [x] #3 All 17 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, completing the authorized TASK-381.21 through TASK-381.30 batch. Stop for user review before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute all seventeen frozen source methods with exact provenance, truncation, capture, export and activity fixtures. 2. Restore shared acquisition export states/counts and fullscreen observation reading with controller hints and focus restoration. 3. Verify real API account boundaries, CSV values and repeated saved-page imports; preserve shared Core page/parser contracts. 4. Run focused source/API/renderer checks, the full component/live API gate and serialized native journeys on both surfaces. Review captures and computed layout, correct typography/overflow, update exact mapping and checkpoint85, commit and stop for review.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only preparation .tmp/task38130-ui-prep.md and .tmp/task38130-prep.md identifies demonstrated export feedback and fullscreen observation-reader gaps. Root owns main/preload bridge, official inventory, integration and documentation. Renderer, backend fixtures and native checks have separate bounded owners. Preserve the existing saved-file chooser and shared parser policies.

All 24 unchanged source cases pass: main17/UI7. Root changed only export bridge result to preserve saved and backend ownershipCount; byte writer remains unchanged. Host checks pass83/4files and final capture-policy82/2files (overlapping), including exact rejected identity matrix and real DOM complete12/9 versus capped100/50. Continuing capture remains pending. Backend/UI/native evidence is still in progress; no acceptance criteria checked yet.

Full component/live API gate passes 4,130 cases across216 files; source24 and backend19 pass with no skips. Review fixed immediate global Back, fullscreen Steam typography layering and Library paragraph overflow. Final production bundle index-Vknj1PPX.js builds and typechecks. Native fullscreen export now has zero horizontal overflow; exact Alpha/Beta second-pass screenshot shows2 retained facts and0 new. Final activity/regression verification remains active; acceptance criteria not yet checked.

Final verification: 24 unchanged original cases; 19 backend cases; 4,130 component/live API cases across 216 files; 93 overlapping focused cases after final CSS; 13 distinct native journeys (10 new, 3 existing). All pass without skips. Production build, final typecheck, formatting, diff and replacement-character checks pass. Screenshots and measured text/overflow verify desktop and fullscreen separately. Final bundle index-Vknj1PPX.js. Controller input is simulated; physical devices, live provider authentication and release packaging are not claimed.

All 17 assigned mappings resolved after independent source/assertion review: 15 ported, 1 retained exporter, 1 C# compatibility overload. Audit validates 1,626 ported / 705 retained-backend / 37 framework-specific / 54 pending / 13 partial. Complete migration gate still fails on the remaining 67 methods. Checkpoint85 and frontend/capture documentation updated. Milestone commit and final batch stop remain the last Definition of Done step.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored shared acquisition CSV feedback and record counts, explicit saved-page results, and separate fullscreen Steam activity reading with controller hints and safe Back/focus behavior. Preserved exact export bytes, capture consent/identity/truncation rules, unknown-account provenance, repeat-import facts and recorded-session totals. Fixed measured fullscreen text and paragraph overflow. Verified 24 unchanged source cases, 19 backend cases, the full 4,130-case component/API gate, 93 final focused cases and 13 distinct native journeys. All 17 assigned source methods have reviewed dispositions. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-eighty-five.md. The whole port still has 54 pending and 13 partial methods; this is the final milestone of the authorized TASK-381.21–381.30 batch.
<!-- SECTION:FINAL_SUMMARY:END -->
