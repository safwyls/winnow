---
id: TASK-381.34
title: 'Electron: finish accessibility and reduced-motion enforcement'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each assigned source accessibility/visual rule has equivalent checks against meaningful Electron DOM/accessibility behavior or an exact framework-specific rationale.
- [ ] #2 Desktop and fullscreen controls retain names, focus visibility, announced changes and reduced-motion behavior through authored themes and dynamic surfaces.
- [ ] #3 All 13 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
