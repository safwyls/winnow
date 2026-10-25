---
id: TASK-381.23
title: 'Electron: finish startup boundaries and first-run setup'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
updated_date: '2026-10-01 18:30'
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

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original process/startup boundaries, single-read behavior, off-thread work and distinct startup failure exits pass through Electron with the external backend.
- [x] #2 Desktop and fullscreen first-run/setup shells preserve consent, progress, retry/cancel and completed-entry behavior using temporary data directories.
- [x] #3 All 17 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute and audit the seventeen frozen startup/setup methods against existing Electron behavior, preserving exact process exits, input sequences and fixture scope. 2. Fix demonstrated startup lifecycle and setup presentation gaps, retaining single backend ownership and coherent first-load publication. 3. Verify focused component/API and isolated native startup/setup cases on both surfaces, including short windows, theme corners, saved presentation, masked keyboard and consent confinement. 4. Record checkpoint78 and per-method mappings, commit the verified milestone, then continue to TASK-381.24.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after TASK-381.22 implementation 84c4eca4 and completion 90a5b780. Baseline: all 3,918 Electron component/live API cases pass; inventory has 1,537 ported, 663 retained-backend, 33 framework-specific, 173 pending and 29 partial methods. Preliminary audit identifies fatal companion-startup handling, short setup action placement and fullscreen provider navigation gaps; exact controller checks may require evidence only. Preserve unrelated scc-report.html and .claude/settings.local.json.

Source review also confirmed fullscreen setup still used a centered dialog with smaller text and no local controller hints. Complete the full-canvas setup presentation, saved safe margins and text scales, A/B/Y hints, and provider/settings entry-return flow within this task, preserving the desktop overlay.

Independent review found three additional startup/setup gaps: Application and Library provider pages still embed desktop controls, Appearance only offers the desktop design selector, and primary window.loadURL rejects outside the startup error boundary. Reuse the existing fullscreen settings controls with setup persistence/focus/layer handling and add reported renderer-load failure coverage before finalizing.

Restored ordinary desktop palette, layout, transparency and font choices alongside shared fullscreen settings controls. Review caught a profile-save gate lost when editors unmounted during a presentation switch; App now passes persistent runtime write state into Setup. The final 202 focused renderer cases, 54 focused main cases, TypeScript and production build pass. Native verification is running against frozen source and bundle with prebuilt isolated backends.

The final native matrix now has 24 distinct passing cases, including source consent busy state on both surfaces. Generic dialog width, padding and scrolling were corrected after native measurement; exact full-viewport, inner-corner and fixed-action assertions pass. One early refused-renderer process timed out after reporting exit 3; minimal/application/unchanged-case reproductions and the final full sequence exited correctly, with no speculative workaround. Source review confirms all 17 mappings after the busy assertion addition. The full component/live API gate is running. Visual review identified one remaining nested font-picker keyboard-hint gap; add a local hint and verify Y/A/B within that layer before finalization.

Full component/live API regression gate passes: 3,958 cases across 198 files in 356.13 seconds (.tmp/task38123-components-final2.log). Native evidence is consolidated in .tmp/task38123-native-evidence.json. The remaining change is limited to local controller hints and readable field labels inside the nested fullscreen font picker; verify it with focused tests and the two existing fullscreen native cases before committing.

Final verification complete: 25 unchanged original cases, 5 authenticated HTTP cases, 3,958 component/live API cases, and 24 distinct native cases pass. The final picker delta passes 46 focused cases, then 15 CSS-focused cases and both fullscreen native cases with measured unclipped font choices and real Y/A/B keyboard focus restoration. Final build index-DJJXOwe4.js, TypeScript, formatting and diff checks pass. All 17 assigned methods are resolved: 16 ported and 1 framework-specific C# scanner with Electron error-boundary coverage. Inventory: 1,553 ported, 663 retained-backend, 34 framework-specific, 162 pending, 23 partial. Checkpoint78 records fixtures, adapters, screenshots, the non-reproduced initial process timeout and simulated-input limitation.

Reviewable implementation milestone: 6d8186d1 (Complete Electron startup boundaries and first-run setup). All acceptance criteria are verified; continuing within the authorized batch to TASK-381.24.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored fatal startup reporting before window creation, saved initial presentation and single-read backend ownership. Desktop and fullscreen setup retain their own layout, fixed navigation, provider return, consent busy state, masked keyboard, real appearance/settings controls and local controller hints. Pending or failed appearance saves survive presentation changes. Verification and exact per-method evidence are recorded in checkpoint-seventy-eight.md; all 17 assigned source methods are resolved. Continue to TASK-381.24 after recording the milestone commit; batch review remains after TASK-381.30.
<!-- SECTION:FINAL_SUMMARY:END -->
