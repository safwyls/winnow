---
id: TASK-381.5
title: 'Electron: finish controller keyboard and accessible modal routes'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:44'
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
ordinal: 423000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Controller text entry and dynamic dialogs must remain usable without a physical keyboard, including error and busy states.

Owns 10 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Ui.Tests/FullscreenAccessibilityTests.cs
- FullscreenAccessibilityTests.Rendered_settings_and_filter_controls_expose_names_and_controller_routes [pending at split]
- FullscreenAccessibilityTests.A_combined_prompt_keeps_names_and_focus_routes_through_empty_busy_and_error_states [pending at split]
- FullscreenAccessibilityTests.Journal_editors_name_the_field_expose_validation_and_reach_the_keyboard_action [pending at split]
- FullscreenAccessibilityTests.Back_from_a_dynamic_modal_restores_the_invoking_control_in_the_real_shell [pending at split]
- FullscreenAccessibilityTests.Controller_keyboard_exposes_its_keys_and_returns_focus_to_the_original_field [pending at split]

tests/Winnow.Ui.Tests/FullscreenFilePickerTests.cs
- FullscreenFilePickerTests.Saving_an_existing_file_requires_confirmation_and_picker_never_writes_it [pending at split]
- FullscreenFilePickerTests.Controller_selects_only_allowed_files_and_back_completes_cancellation [pending at split]

tests/Winnow.Ui.Tests/GamepadKeyboardTests.cs
- GamepadKeyboardTests.Controller_can_change_case_type_and_backspace_without_a_physical_keyboard [partial at split]
- GamepadKeyboardTests.Back_closes_once_restores_focus_and_stops_editing [partial at split]
- GamepadKeyboardTests.Standard_keys_fit_and_arrows_form_an_inverted_T [partial at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Controller keyboard case changes, typing, backspace, inverted-T arrows, one-shot Back and original-field focus restoration pass the complete original interaction/geometry matrices.
- [ ] #2 Settings, filters, combined prompts, journal fields and file pickers expose meaningful names, validation and reachable controller actions; dynamic modal close restores its invoking control on both applicable surfaces.
- [ ] #3 All 10 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
