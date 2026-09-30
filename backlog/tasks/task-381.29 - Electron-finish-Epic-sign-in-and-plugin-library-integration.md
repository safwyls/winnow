---
id: TASK-381.29
title: 'Electron: finish Epic sign-in and plugin library integration'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original Epic sign-in success/cancel/failure and plugin source/sync validation cases pass without persisting secrets in renderer state or leaking account sessions.
- [ ] #2 PSN/Xbox and provider library presentation retain plugin identity, source actions and error/empty behavior on desktop and fullscreen; packaged provider artifacts satisfy the original contract.
- [ ] #3 All 14 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
