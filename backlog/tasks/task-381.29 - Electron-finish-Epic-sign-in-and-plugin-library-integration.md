---
id: TASK-381.29
title: 'Electron: finish Epic sign-in and plugin library integration'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:48'
updated_date: '2026-10-01 21:28'
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
ordinal: 447000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Embedded sign-in and plugin providers must preserve credential capture, validation and library attribution after replacing the frontend host.

Owns 14 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/EpicWeb/EpicInteractiveSignInTests.cs
- EpicInteractiveSignInTests.No_usable_prompt_is_a_reason_not_a_crash [pending at split]
- EpicInteractiveSignInTests.A_host_that_registers_no_prompt_at_all_is_also_a_reason [pending at split]
- InteractiveAuthPromptTests.The_console_prompt_is_available_when_output_is_redirected [pending at split]
- InteractiveAuthPromptTests.The_two_prompts_are_registered_in_fallback_order [pending at split]

tests/Winnow.Tests/PluginSyncIntegrationTests.cs
- PluginSyncIntegrationTests.Shipped_steamgriddb_package_loads_through_the_public_sdk_without_an_app_reference [pending at split]
- PluginSyncIntegrationTests.Separate_assembly_imports_library_and_applies_scoped_metadata_and_artwork [pending at split]
- PluginSyncIntegrationTests.Plugin_facets_refresh_only_their_source_and_leave_builtin_assignments [pending at split]

tests/Winnow.Tests/PluginSyncValidationTests.cs
- PluginSyncValidationTests.Every_steam_release_contributes_artwork_and_an_unavailable_release_preserves_the_previous_observation [pending at split]
- PluginSyncValidationTests.Null_shapes_and_broken_metadata_do_not_block_other_providers_or_erase_prior_artwork [pending at split]

tests/Winnow.Tests/XboxPluginPackageTests.cs
- XboxPluginPackageTests.Xbox_zip_loads_only_after_opt_in_with_the_shared_sdk_and_hides_managed_credentials [pending at split]

tests/Winnow.Ui.Tests/PluginLibrarySourcePresentationTests.cs
- PluginLibrarySourcePresentationTests.Grouped_history_retains_its_source_explanation_on_both_surfaces [pending at split]

tests/Winnow.Ui.Tests/PsnPluginPresentationTests.cs
- PsnPluginPresentationTests.Settings_mask_session_token_save_history_options_and_remove_the_saved_secret [pending at split]
- PsnPluginPresentationTests.Imported_history_shows_its_PlayStation_source_without_a_launch_or_install_action [pending at split]
- PsnPluginPresentationTests.PlayStation_filter_selects_imported_titles_on_both_surfaces [pending at split]

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original Epic sign-in success/cancel/failure and plugin source/sync validation cases pass without persisting secrets in renderer state or leaking account sessions.
- [x] #2 PSN/Xbox and provider library presentation retain plugin identity, source actions and error/empty behavior on desktop and fullscreen; packaged provider artifacts satisfy the original contract.
- [x] #3 All 14 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute the fourteen unchanged source methods and preserve plugin package, scoped metadata/artwork and no-prompt fixtures. 2. Restore grouped plugin provenance and exact PlayStation labels on desktop/fullscreen; verify the exact NPSSO settings matrix and filter. 3. Verify real API provider attribution plus existing Epic native/manual fallback and cancellation boundaries. 4. Run focused component/API and serialized native checks, review captures, record exact dispositions/checkpoint84, commit and continue to TASK-381.30.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Read-only preparation .tmp/task38129-prep.md identifies grouped secondary-provider provenance and PlayStation naming gaps. Do not replace shared plugin services or classify all Epic sign-in assertions as framework-specific. Root owns official mapping and integration; backend, renderer and native work have separate owned files.

Verified 18 unchanged source cases, 20 backend cases (18 HTTP and 2 direct registered legacy-policy cases), 272 focused renderer cases, 121 Epic component/main/preload cases and 11 distinct native journeys. All native passes use frozen index-CNBUuq-a.js; harness-only locator and completion fixes are recorded in checkpoint84. Reviewed desktop/fullscreen screenshots including masked keyboard glyphs. Mapping dispositions: 5 ported, 8 retained-backend, 1 narrowly framework-specific; inventory 1611/704/36/66/18. No physical-device or live-provider claim.

Milestone commit 55768005. Continue immediately to TASK-381.30, the final task of the authorized ten-task batch.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored grouped provider attribution, PlayStation naming and refresh acknowledgement while preserving source-specific history and NPSSO secret handling on both surfaces. All fourteen assigned contracts are resolved with executed source, API, component and native evidence. See docs/spikes/2026-09-28-electron-parity/checkpoint-eighty-four.md. Continue to TASK-381.30 within the authorized batch.
<!-- SECTION:FINAL_SUMMARY:END -->
