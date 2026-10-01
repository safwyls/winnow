---
id: TASK-381.5
title: 'Electron: finish controller keyboard and accessible modal routes'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:44'
updated_date: '2026-10-01 00:51'
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
- [x] #1 Controller keyboard case changes, typing, backspace, inverted-T arrows, one-shot Back and original-field focus restoration pass the complete original interaction/geometry matrices.
- [x] #2 Settings, filters, combined prompts, journal fields and file pickers expose meaningful names, validation and reachable controller actions; dynamic modal close restores its invoking control on both applicable surfaces.
- [x] #3 All 10 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit all ten assigned source methods against current keyboard, controller dispatch, Settings/filter controls, list prompts, journal editors and file selection; preserve all original fixtures and edge cases.
2. Restore demonstrated keyboard and file-picker gaps using bounded domain-agent ownership. Coordinator owns accessibility/modal integration and shared controller dispatch. Serialize native Electron runs.
3. Verify accessible names/enabled states and directional reachability across all ten source screens, prompt empty/busy/error transitions, both journal entry points, dynamic modal return and keyboard return. Verify file filters, cancellation, overwrite consent and no picker writes with disposable fixtures.
4. Run focused tests, full component/live API checks and affected native regressions on desktop and fullscreen. Inspect screenshots, map only these ten source contracts, update documentation and commit.
5. Stop after TASK-381.5 for review; do not begin TASK-381.6 until the user prompts continuation.

6. User review correction: replace fullscreen keyboard text-only hints with the existing controller glyph strip for D-pad, A, X, RT and B. Preserve the desktop text treatment and accessible description. Verify the glyph sequence, layout and keyboard interactions at both fullscreen sizes, then commit the correction before beginning TASK-381.6 as requested.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the five-row 57-key controller keyboard, main-owned fullscreen file chooser with Cancel-first overwrite consent and pending-action cancellation, journal keyboard actions and return focus, disclosure navigation, settings save-time focus retention and age-limit row alignment. Desktop and fullscreen are independently exercised. Final build/typecheck, Prettier and diff checks pass. All 3,388 component/live API cases across 166 files pass with eight workers in 49.41 seconds. All 65 distinct native cases pass in serialized batches; the joined latest-results report has no skips or retries. Initial runs exposed a zoom-sensitive underline assertion and a Search test timeout; the corrected zoom reference passes and Search passed alone then in the full bounded run without changing its assertions or timeout. All ten assigned source methods are now ported: 1,313 ported, 625 retained backend, 30 framework-specific, 384 pending and 83 partial (467 unresolved). Evidence and limitations: docs/spikes/2026-09-28-electron-parity/checkpoint-sixty.md. Physical devices remain unverified; native desktop chooser options are checked at the Electron boundary. Existing TASK-381.7 records fullscreen Filters visual comparison. Stop after this milestone; TASK-381.6 is not started.

User-reviewed keyboard hint correction: fullscreen now uses the original bundled D-pad/A/X/RT/B vectors with Move/Type/Backspace/Enter/Close labels, source-sized visible geometry, live theme color and an accessible description. Desktop retains plain text hints. Build/typecheck, 19 focused component cases and all nine native keyboard cases pass; inspected both fullscreen screenshots. Evidence: .tmp/keyboard-hints-build.log, .tmp/keyboard-hints-components.log, .tmp/keyboard-hints-native.log and .tmp/keyboard-hints-results/. User explicitly authorized proceeding to TASK-381.6 after committing this correction.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed in milestone c355f48b. Restored five-row keyboard geometry and controller edits, accessible settings/modal routes, fullscreen file selection with explicit overwrite consent, and exact opener focus. Build/typecheck, Prettier, diff checks, all 3,388 component/live API cases across 166 files, and 65 distinct native cases pass. All ten assigned source contracts now have executed replacement evidence; 467 remain unresolved elsewhere. Checkpoint sixty records logs, screenshots, initial failures and validation limits. Stopped for user review; TASK-381.6 remains To Do until the next continuation prompt.
<!-- SECTION:FINAL_SUMMARY:END -->
