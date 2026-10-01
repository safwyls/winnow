---
id: TASK-381.21
title: 'Electron: finish cover selection, lifetime and conversion behavior'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
updated_date: '2026-10-01 16:20'
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
ordinal: 439000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Cover reuse, cancellation and conversion cross decoding lifetimes and can silently lose images or leak resources when many tiles are recycled.

Owns 20 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/CoverArtPreferenceTests.cs
- CoverArtPreferenceTests.Loads_mode_without_writing_back [pending at split]
- CoverArtPreferenceTests.Selector_persists_and_reloads_both_modes [pending at split]

tests/Winnow.Tests/CoverLeaseLifetimeTests.cs
- CoverLeaseLifetimeTests.Closing_the_detail_modal_releases_its_cover [pending at split]
- CoverLeaseLifetimeTests.Closing_the_modal_releases_every_screenshot_thumbnail [pending at split]
- CoverLeaseLifetimeTests.The_lightbox_holds_one_shot_at_a_time_and_none_when_closed [pending at split]
- CoverLeaseLifetimeTests.A_merge_row_leases_both_layers_and_releases_them [pending at split]

tests/Winnow.Tests/CoverSelectionPolicyTests.cs
- CoverSelectionPolicyTests.User_art_survives_pin_and_provider_availability [pending at split]
- CoverSelectionPolicyTests.Pin_uses_the_same_work_image_and_falls_back_when_its_source_is_unavailable [pending at split]
- CoverSelectionPolicyTests.Work_previews_use_typed_plugin_refs_only_while_the_provider_is_available [pending at split]

tests/Winnow.Ui.Tests/CoverCancellationFailureTests.cs
- CoverCancellationFailureTests.A_throwing_source_callback_does_not_replace_the_last_waiters_cancellation [pending at split]
- CoverCancellationFailureTests.A_throwing_source_cancellation_callback_cannot_skip_shutdown_cleanup [pending at split]

tests/Winnow.Ui.Tests/CoverConversionBoundaryTests.cs
- CoverConversionBoundaryTests.A_disk_hit_does_not_wait_for_an_unrelated_network_fetch [pending at split]
- CoverConversionBoundaryTests.A_failed_floor_conversion_releases_the_already_converted_vivid_layer [pending at split]
- CoverConversionBoundaryTests.The_decode_limit_includes_transient_bitmap_conversion [pending at split]

tests/Winnow.Ui.Tests/CoverPresentationTests.cs
- CoverPresentationTests.Inherited_fit_changes_existing_and_new_portrait_images_but_leaves_backgrounds_alone [pending at split]
- CoverPresentationTests.Desktop_cover_views_apply_fit_without_changing_their_bounds [pending at split]

tests/Winnow.Ui.Tests/CoverSelectionTests.cs
- CoverSelectionTests.Production_library_and_merge_composition_select_the_same_art_on_both_surfaces [pending at split]

tests/Winnow.Ui.Tests/DesktopTileFocusTests.cs
- DesktopTileFocusTests.Focus_restores_vivid_art_and_pointer_exit_preserves_only_keyboard_focus [pending at split]
- DesktopTileFocusTests.Library_selection_updates_rendered_art_without_moving_keyboard_focus [pending at split]

tests/Winnow.Ui.Tests/DormancyTokenTests.cs
- DormancyTokenTests.Xaml_resources_procedural_art_and_disk_renderer_share_the_transform_endpoint [partial at split]

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original selection, lease, cancellation/failure and conversion contracts preserve ownership, best available pixels and cleanup; framework-specific disposals require explicit per-method rationale.
- [x] #2 Desktop and fullscreen cover/focus/dormancy presentation retains source colors and geometry across size changes, recycling and preference updates.
- [x] #3 All 20 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit all twenty frozen cover preference, selection, lifetime, cancellation, conversion and presentation contracts and their complete fixtures. 2. Identify retained backend policies and framework-specific boundaries, then fix demonstrated Electron gaps while preserving both presentation paths. 3. Execute original and equivalent component/API/native tests with pixel, resource-lifetime and preference evidence; review visual output. 4. Record checkpoint76 and exact per-method mapping, commit the verified milestone, then continue to TASK-381.22.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started the authorized TASK-381.21–381.30 batch after checkpoint75 (fd9c3e7d; completion 22b2aa20). Baseline: 3,859 Electron cases across 190 files; 1,515 ported, 650 retained-backend, 32 framework-specific, 208 pending and 30 partial methods. Unrelated scc-report.html and local Claude settings remain untouched.

All twenty frozen methods expand to forty unchanged passing source cases (17 main, 23 UI); nine files match the frozen revision. Fixed screenshot/lightbox ownership and cancellation, separate bounded fetch/decode work, PNG conversion permit ownership, shared Fit/Fill normalization and desktop edge padding, detached backdrop source cleanup, and warm-image retention after a failed overflow reread. Fullscreen review remains the specified text/member flow; its actual Details backdrop is verified separately from desktop merge portraits.

Verification: complete Electron component/live API suite 3,904/3,904 across 194 files; 26 new native cases and 18 directly affected native regressions; 40 original cases; 194 cover-library cases; 21 HTTP cases. Build, changed-file formatting and diff checks pass. Independent review findings were corrected and rechecked. Native captures use test-only sRGB after diagnosing the monitor ICC profile; strict pixel tolerances remain. Controller input is simulated; physical devices remain a later validation task.

Checkpoint76 records source scope, actual desktop/fullscreen behavior, run provenance and limitations. All twenty mappings resolved: 16 ported, 3 retained-backend, 1 framework-specific. Inventory remains 2,435 methods: 1,531 ported, 653 retained-backend, 33 framework-specific, 189 pending and 29 partial. The complete migration gate remains incomplete. Continue with TASK-381.22 after this milestone commit.

Milestone committed as 70e38edf. All acceptance criteria and verification are complete. Continuing the user-authorized batch with TASK-381.22; review pause remains after TASK-381.30.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed cover selection, ownership, cancellation, conversion and Fit/Fill parity on both surfaces. All 20 assigned contracts resolved. Verified 3,904 Electron tests, 44 native cases, 40 unchanged originals, 194 cover-library cases and 21 HTTP cases. See checkpoint-seventy-six.md for exact evidence and simulated-device limitations.
<!-- SECTION:FINAL_SUMMARY:END -->
