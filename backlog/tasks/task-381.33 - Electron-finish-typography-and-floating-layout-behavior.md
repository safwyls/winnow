---
id: TASK-381.33
title: 'Electron: finish typography and floating layout behavior'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-02 00:16'
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
ordinal: 451000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Font packages, live typography changes and floating chrome can alter measured controls and clipping even when token values match.

Owns 13 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/FloatingLayoutTests.cs
- FloatingLayoutTests.The_layout_moves_only_the_ground_and_the_caption [partial at split]
- FloatingLayoutTests.A_field_is_one_step_above_the_pane_in_either_layout [pending at split]
- FloatingLayoutTests.An_unknown_stored_layout_reads_as_unset [pending at split]
- FloatingLayoutTests.A_stored_layout_comes_back [partial at split]
- FloatingLayoutTests.An_overridden_session_never_writes_a_layout [pending at split]

tests/Winnow.Ui.Tests/SegmentedSelectorContainmentTests.cs
- SegmentedSelectorContainmentTests.Segment_states_preserve_the_rounded_group_boundary [pending at split]

tests/Winnow.Ui.Tests/ThemeTypographyControlsTests.cs
- ThemeTypographyControlsTests.Desktop_font_pickers_and_size_follow_the_selected_theme [partial at split]
- ThemeTypographyControlsTests.Desktop_maximum_size_keeps_typography_controls_reachable_and_reset_restores_authored_values [partial at split]

tests/Winnow.Ui.Tests/ThemeTypographyRuntimeTests.cs
- ThemeTypographyRuntimeTests.Desktop_text_and_reading_line_height_follow_live_resources_without_scaling_geometry [partial at split]
- ThemeTypographyRuntimeTests.Missing_font_falls_back_by_role_and_installed_fonts_are_available [partial at split]
- ThemeTypographyRuntimeTests.Plain_inputs_follow_theme_face_and_size [pending at split]
- ThemeTypographyRuntimeTests.Native_fullscreen_popout_sizes_consent_hints_and_keyboard_from_stable_baselines [pending at split]
- ThemeTypographyRuntimeTests.Fullscreen_combines_theme_and_page_scale_without_accumulation_or_scaling_icons [partial at split]

Batch boundary: the user authorized TASK-381.31 through TASK-381.35 in order. Keep one implementation task active and commit each milestone. After completing this task, continue to TASK-381.34; pause for user review after TASK-381.35.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original runtime font/typography control matrices update actual rendered text and preserve source metrics, measure and keyboard/controller targets.
- [x] #2 Floating layout and segmented selectors remain contained at the original window/scaling boundaries on both applicable presentation paths.
- [x] #3 All 13 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, then continue to TASK-381.34 within the user-authorized TASK-381.31 through TASK-381.35 batch.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Compare all thirteen frozen typography and floating-layout contracts with existing coverage and execute their unchanged source fixtures. 2. Correct demonstrated font, text-scale, containment and persistence gaps, including separate fullscreen Theme Studio and Settings Appearance paths. 3. Run focused renderer tests, then freeze the build for serialized native measurements of real controls, popouts and floating geometry at original boundaries. 4. Record exact source mappings and captures, review the result, update documentation and commit the milestone before continuing to TASK-381.34.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Visual observation from TASK-381.32 to assess in this typography task: the actual fullscreen Theme Studio capture at1920x1080 retains compact desktop-sized labels and authored/typography buttons while the fullscreen shell headings/footer use their own scale. Capture: .tmp/task38132-native-final2-results/appearance-contracts-fulls-c2643-stored-scroll-across-reload/fullscreen-dawn-default-collapsed-notes.png. Verify both Theme Studio and the separate Settings/Appearance path against the fullscreen typography rules and original runtime/control contracts; do not infer coverage for one from the other.

TASK-381.32 completed at 1d85c480. Starting task three of the five-task batch. Root owns official mappings, documentation, Backlog and commits. Source and native test lanes remain serialized; broad component/live API gate is reserved for TASK-381.35.

All 30 unchanged source cases across the thirteen methods pass; twenty original captures and frozen-source provenance are recorded in .tmp/task38133-source-evidence.json. Demonstrated gaps include desktop-sized fullscreen Theme Studio labels, theme-sized keyboard hint icons, and fixed-font trusted browser popouts. The implementation adds bounded theme typography updates only to local toolbar/composer documents, preserving drafts and focus; provider pages remain isolated. Native verification will follow renderer freeze.

Renderer/main changes are frozen for native verification. All 212 focused cases across sixteen files pass; typecheck passes after native fixture props were corrected. Review found a fixed toolbar inset could cover enlarged hints; account and reference browser views now follow font-ready measured local toolbar height and remeasure on size, status and font changes. Native scope is sixteen cases, including separate original 28px and current Home 24px fullscreen roles, real consent and trusted popouts.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed typography and floating layout parity. Fullscreen Studio uses TV sizing; fonts and size changes preserve actual controls, role fallback and geometry. Trusted browser typography updates retain drafts and focus. Native checks corrected segmented focus cascade, header growth that changed cover capacity, and narrow toolbar clipping. Verification: 30 unchanged source cases, 212 focused cases across sixteen files, 16 distinct native cases, one final Home screenshot replay, TypeScript and build pass. All thirteen methods ported; audit validates 1659 ported, 705 retained-backend, 40 framework-specific, 31 pending and zero partial. Complete gate still fails for remaining tasks. See checkpoint-eighty-eight.md and .tmp/task38133-native-evidence.json. Physical devices and live provider sign-in remain outside this verification. Continue to TASK-381.34 after commit.
<!-- SECTION:FINAL_SUMMARY:END -->
