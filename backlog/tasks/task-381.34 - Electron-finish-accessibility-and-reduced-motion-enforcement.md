---
id: TASK-381.34
title: 'Electron: finish accessibility and reduced-motion enforcement'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-02 01:11'
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
ordinal: 452000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Accessible names, live updates and reduced motion can be silently lost in a framework replacement and need app-wide enforcement beyond individual workflows.

Owns 13 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/Enforcement/AutomationNameReachabilityTests.cs
- AutomationNameReachabilityTests.Every_accessible_name_sits_on_an_element_that_reaches_the_control_view [pending at split]
- AutomationNameReachabilityTests.The_tile_and_the_card_name_themselves_on_their_outermost_control [pending at split]
- AutomationNameReachabilityTests.Avalonias_own_peers_hand_back_the_name_from_the_new_host_and_not_from_the_old [pending at split]

tests/Winnow.Tests/Enforcement/InteractiveControlNameTests.cs
- InteractiveControlNameTests.List_and_filter_names_follow_counts_and_list_renames [pending at split]
- InteractiveControlNameTests.Interactive_controls_have_a_name_or_plain_text_content [pending at split]

tests/Winnow.Tests/Enforcement/ReducedMotionCoverageTests.cs
- ReducedMotionCoverageTests.Every_AXAML_motion_surface_is_in_the_reduced_motion_inventory [pending at split]
- ReducedMotionCoverageTests.Every_motion_surface_has_a_style_that_removes_motion [pending at split]
- ReducedMotionCoverageTests.Transition_values_are_never_local_to_an_element [pending at split]

tests/Winnow.Tests/Enforcement/VisualDisciplineTests.cs
- VisualDisciplineTests.Flare_is_attached_to_the_unread_signal_and_to_nothing_else [pending at split]
- VisualDisciplineTests.Every_cover_badge_uses_the_same_top_left_corner [pending at split]
- VisualDisciplineTests.There_is_no_filter_group_for_games_with_updates [pending at split]
- VisualDisciplineTests.The_numeric_text_style_tracks_the_data_face_and_tabular_figures [pending at split]
- VisualDisciplineTests.The_mono_face_resolves_to_a_bundled_font [pending at split]

Batch boundary: the user authorized TASK-381.31 through TASK-381.35 in order. Keep one implementation task active and commit each milestone. After completing this task, continue to TASK-381.35; pause for user review after TASK-381.35.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each assigned source accessibility/visual rule has equivalent checks against meaningful Electron DOM/accessibility behavior or an exact framework-specific rationale.
- [x] #2 Desktop and fullscreen controls retain names, focus visibility, announced changes and reduced-motion behavior through authored themes and dynamic surfaces.
- [x] #3 All 13 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, then continue to TASK-381.35 within the user-authorized TASK-381.31 through TASK-381.35 batch.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect all thirteen frozen enforcement contracts and execute their unchanged source checks. 2. Compare meaningful Electron accessibility, dynamic naming, semantic color/badge rules and reduced-motion behavior; fix demonstrated gaps on desktop and fullscreen, including root controller/keyboard guidance. 3. Verify focused enforcement/component checks and serialized native accessibility-tree, focus, live-update and motion behavior with bundled/authored themes. 4. Record precise framework dispositions where source assertions are framework-bound, integrate exact evidence and screenshots, update documentation and commit before continuing to TASK-381.35.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Observation from TASK-381.31 native review to assess during accessibility work: Avalon fullscreen root footer always renders keyboard wording even while Controller connected and simulated Y/A operate correctly (avalon.tsx root footer). Existing LB/RB root hints remain visible. Verify the intended keyboard/controller guidance against the visual spec and existing input-hint components; do not describe the current footer as modality-sensitive before it is implemented and checked.

TASK-381.33 completed at c7689072. Starting task four of five. Root owns official mapping, documentation, Backlog and commits. Unchanged source/.NET lane precedes a frozen native build; broad component/live API gate remains reserved for TASK-381.35.

Original verification: all 18 cases across the 13 assigned methods pass unchanged, with scanned AXAML and facet inputs matching the frozen revision. Source/.NET lane is closed; ledger is .tmp/task38134-source-evidence.json. Renderer audit is correcting generic named hosts, dynamic count wording, unread token/badge alignment and root controller guidance. Root review also identified a possible conflict between profile and fullscreen reduced-motion class ownership, now under focused verification before the native freeze.

Renderer verification is complete: 379 distinct tests across 17 files pass, TypeScript passes, and independent source-scope review found no remaining material blocker. Native first run passed footer/cover and completed motion behavior checks, but exposed AX text-node/class-membership harness assumptions and teardown timeouts; affected cases are being rerun with readiness checks and process diagnostics. Production remains frozen except the reviewed removal of an unnecessary timer role from the native wall-clock element. Evidence drafts: .tmp/task38134-ui-evidence.json and .tmp/task38134-ui-mapping.json.

Final verification: 18 unchanged original cases, 379 distinct focused checks across 17 files, seven distinct native checks and TypeScript/build pass. Explicit CDP domain cleanup preserves the five-second shutdown bound with no owned survivors. Nominal 8 logical-unit badges are measured through actual Chromium border quantization. Actual generic AX nodes remain exposed, so the exact Avalonia negative peer mechanism is narrowly framework-specific. Inventory: 1670 ported, 706 retained-backend, 41 framework-specific, 18 pending, zero partial. Complete gate correctly remains incomplete. Details and qualified limits: checkpoint-eighty-nine.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered live accessible names, whole-renderer enforcement, authored-theme reduced motion, data-role tabular typography and fullscreen keyboard/controller guidance. Verified desktop and fullscreen with 18 source, 379 focused and seven native checks. Eleven methods ported, one retained facet contract and one exact framework peer disposition. Build/typecheck pass; see checkpoint-eighty-nine.md.
<!-- SECTION:FINAL_SUMMARY:END -->
