---
id: TASK-381.23
title: 'Electron: finish startup boundaries and first-run setup'
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
ordinal: 441000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
First-run and startup failures must not initialize the backend twice, block rendering or leave an unusable shell.

Owns 17 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/Enforcement/StartupBoundaryTests.cs
- StartupBoundaryTests.Frontend_subscriptions_wait_for_native_platform_services_and_no_backend_workers_are_started [pending at split]
- StartupBoundaryTests.Every_async_void_lifecycle_override_carries_an_error_boundary [pending at split]
- StartupBoundaryTests.The_startup_spine_is_bounded [pending at split]

tests/Winnow.Tests/StartupProcessTests.cs
- StartupProcessTests.Windows_startup_opens_a_responsive_native_window [pending at split]
- StartupProcessTests.Malformed_configuration_is_reported_before_the_library_is_opened [pending at split]
- StartupProcessTests.Invalid_host_logging_configuration_is_reported_without_starting_the_ui [pending at split]
- StartupProcessTests.Unsupported_schema_is_refused_before_services_or_either_surface_start [pending at split]
- StartupProcessTests.Unusable_data_directory_still_returns_exit_two [pending at split]

tests/Winnow.Tests/StartupReadsOnceTests.cs
- StartupReadsOnceTests.The_window_does_not_build_the_merge_screen_on_open [pending at split]
- StartupReadsOnceTests.The_window_scores_the_feed_only_when_no_library_load_will [pending at split]

tests/Winnow.Ui.Tests/FirstRunSetupDesktopTests.cs
- FirstRunSetupDesktopTests.Header_and_footer_fills_stay_inside_the_rounded_frame [partial at split]
- FirstRunSetupDesktopTests.Every_step_keeps_navigation_inside_a_short_desktop_window [partial at split]

tests/Winnow.Ui.Tests/FirstRunSetupFullscreenTests.cs
- FirstRunSetupFullscreenTests.Controller_setup_blocks_root_navigation_and_returns_from_provider_to_same_step [partial at split]

tests/Winnow.Ui.Tests/FirstRunSetupShellTests.cs
- FirstRunSetupShellTests.New_install_opens_setup_after_startup_in_the_selected_presentation [partial at split]
- FirstRunSetupShellTests.Setup_keyboard_is_above_enabled_overlay_and_types_into_masked_field [partial at split]
- FirstRunSetupShellTests.Controller_stays_in_steam_consent_and_back_closes_only_that_layer [partial at split]

tests/Winnow.Ui.Tests/StartupReadThreadTests.cs
- StartupReadThreadTests.Library_and_startup_models_read_off_UI_thread_and_publish_on_UI_thread [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original process/startup boundaries, single-read behavior, off-thread work and distinct startup failure exits pass through Electron with the external backend.
- [ ] #2 Desktop and fullscreen first-run/setup shells preserve consent, progress, retry/cancel and completed-entry behavior using temporary data directories.
- [ ] #3 All 17 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
