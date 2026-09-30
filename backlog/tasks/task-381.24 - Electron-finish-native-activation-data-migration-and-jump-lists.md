---
id: TASK-381.24
title: 'Electron: finish native activation, data migration and jump lists'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
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
ordinal: 442000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Electron host must preserve installation identity, legacy user data and single-instance OS integrations independently of renderer behavior.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ApplicationBuildInfoTests.cs
- ApplicationBuildInfoTests.Preserves_release_identity_and_handles_builds_without_git [pending at split]
- ApplicationBuildInfoTests.Settings_reads_the_application_assembly_identity [pending at split]

tests/Winnow.Tests/DataDirectoryOverrideTests.cs
- DataDirectoryOverrideTests.The_composition_root_puts_the_database_covers_and_themes_in_the_override [partial at split]

tests/Winnow.Tests/JumpListIconsTests.cs
- JumpListIconsTests.Icon_contains_six_square_png_frames_from_the_center_of_the_cover [pending at split]
- JumpListIconsTests.Icons_are_cached_in_the_selected_directory_and_missing_art_falls_back [pending at split]

tests/Winnow.Tests/SingleInstanceGuardTests.cs
- SingleInstanceGuardTests.A_first_copy_acquires_and_a_second_copy_on_the_same_directory_is_refused [pending at split]
- SingleInstanceGuardTests.Releasing_the_guard_lets_the_next_launch_through [pending at split]
- SingleInstanceGuardTests.A_second_copy_against_a_different_data_directory_is_not_the_failure [pending at split]
- SingleInstanceGuardTests.Two_spellings_of_one_directory_are_still_one_instance [pending at split]

tests/Winnow.Tests/WindowsActivationSecurityTests.cs
- WindowsActivationSecurityTests.Activation_objects_belong_to_user_and_exclude_other_accounts_and_network_logons [pending at split]

tests/Winnow.Tests/WindowsJumpListTests.cs
- WindowsJumpListTests.Isolated_directories_have_distinct_stable_taskbar_identities [pending at split]
- WindowsJumpListTests.Windows_arguments_preserve_spaces_quotes_and_trailing_backslashes [pending at split]
- WindowsJumpListTests.Every_destination_carries_its_isolated_data_directory [pending at split]
- WindowsJumpListTests.Native_publish_smoke_uses_only_an_isolated_app_id [pending at split]

tests/Winnow.Tests/WinnowDataLocationTests.cs
- LegacyThemeIdTests.The_pre_rename_theme_id_still_resolves_to_the_house_theme [pending at split]
- LegacyThemeIdTests.The_alias_resolves_through_the_service_catalogue_too [pending at split]
- LegacyThemeIdTests.A_user_theme_that_claims_the_old_id_wins_it [pending at split]
- LegacyThemeIdTests.An_id_that_is_neither_still_falls_through_to_the_default [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original data-directory override/legacy migration, activation security and single-instance rules preserve the existing library and refuse invalid startup paths without fallback.
- [ ] #2 Build identity and Windows jump-list entries/icons preserve source behavior in development and packaged fixtures; secondary processes and shutdown leave no duplicate backend or stale native state.
- [ ] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
