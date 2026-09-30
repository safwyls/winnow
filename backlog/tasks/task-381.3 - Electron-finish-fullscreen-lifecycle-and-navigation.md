---
id: TASK-381.3
title: 'Electron: finish fullscreen lifecycle and navigation'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:44'
updated_date: '2026-09-30 21:40'
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
ordinal: 421000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Switching between the two presentation paths can lose page state, focus, cursor visibility or window state even when each page works in isolation.

Owns 17 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Ui.Tests/FullscreenContextLifetimeTests.cs
- FullscreenContextLifetimeTests.Disposal_releases_each_independent_presentation_model_and_preserves_borrowed_shared_models [pending at split]

tests/Winnow.Ui.Tests/FullscreenContextTests.cs
- FullscreenContextTests.Runtime_factory_isolates_library_state_but_shares_the_persisted_theme [partial at split]

tests/Winnow.Ui.Tests/FullscreenInteractionTests.cs
- FullscreenInteractionTests.Hover_does_not_underline_actions_but_selected_sections_keep_a_neutral_underline [pending at split]
- FullscreenInteractionTests.Details_backdrop_fills_canvas_and_back_header_restores_its_origin [pending at split]
- FullscreenInteractionTests.Controller_hides_cursor_until_mouse_moves_and_exit_restores_it [pending at split]
- FullscreenInteractionTests.Ultrawide_fit_expands_canvas_without_stretching_type_and_restores_reference_layout [pending at split]
- FullscreenInteractionTests.Fullscreen_hosts_a_separate_interface_and_search_keyboard_at_minimum_window_size [pending at split]
- FullscreenInteractionTests.Fullscreen_restores_each_window_state_and_desktop_visibility [pending at split]
- FullscreenInteractionTests.Entry_button_and_F11_toggle_without_a_library_context [pending at split]
- FullscreenInteractionTests.Controller_quick_menu_and_browse_navigation_leave_desktop_state_untouched [pending at split]
- FullscreenInteractionTests.Returning_to_desktop_covers_refresh_until_the_feed_is_ready [pending at split]
- FullscreenInteractionTests.Main_screens_render_at_reference_and_smaller_size [pending at split]

tests/Winnow.Ui.Tests/FullscreenNavigationLayoutTests.cs
- FullscreenNavigationLayoutTests.Details_tabs_keep_geometry_with_unread_update_badge [pending at split]
- FullscreenNavigationLayoutTests.Selection_and_focus_keep_every_tab_and_trigger_stationary [pending at split]

tests/Winnow.Ui.Tests/FullscreenQuickMenuTests.cs
- FullscreenQuickMenuTests.Repeated_start_keeps_one_menu_and_returns_to_the_original_focus [partial at split]

tests/Winnow.Ui.Tests/FullscreenSectionHintTests.cs
- FullscreenSectionHintTests.Trigger_hints_remain_visible_around_sections_when_navigating [pending at split]

tests/Winnow.Ui.Tests/FullscreenThemeNavigationTests.cs
- FullscreenThemeNavigationTests.Returning_home_after_each_theme_change_keeps_loaded_shelves_navigable [partial at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Entry button, F11, external restore, window states and cursor ownership preserve the original desktop/fullscreen transition contracts, including startup without a library context.
- [x] #2 Root navigation, quick menu, local section hints, theme switching and context disposal preserve selection and return focus without leaking state between surfaces.
- [x] #3 All 17 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Preserve the existing per-surface navigation/store and shared API/theme architecture; audit all 17 original assertion scopes and test runtime ownership/cleanup rather than reproducing Avalonia DI types. 2. Close demonstrated interaction gaps: clear controller cursor ownership on presentation switches, expose fullscreen entry while data is preparing, and render non-focusable LT/RT hints outside local section strips with stable selected/focused geometry. Keep desktop compositions unchanged. 3. Exercise real Electron transitions from normal/maximized windows, repeated root/nested controller menus, minimum-size search keyboard, delayed return preparation, origin focus/backdrops, ultrawide sizing and all original palette/size/scale matrices. Fix only failures within this task. 4. Verify shared theme/dormancy preferences, independent surface filters and released component/event/query subscriptions with retained shared cache. Run focused and complete component/API checks plus relevant native regressions; map only the 17 assigned contracts with exact evidence, update documentation, commit and pause before TASK-381.4.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop and fullscreen verified separately. Preparation supports entry before library context; cursor ownership clears on mode changes; native normal/maximized restoration, repeated root/nested quick menus, delayed desktop readiness, independent filters/lists/journal state, shared appearance and subscription disposal preserve their original contracts. LT/RT glyphs remain beside stable-width section labels; selected sections use neutral underlines, focus uses accent, and Details retains its unread count without shifting tabs. Settings entry selectors follow the new wrapper, compact Controller-guide spacing keeps all ten mappings bounded, plugin content retains its inset, and fullscreen fallback titles scale once and truncate within covers.

Verification: production build/typecheck pass; all 3358 component/live API cases in 164 files pass without skips (56.66s). Across the recorded native batches, all 87 distinct relevant cases have a passing latest executed result; the final 33-case repair/regression batch passes in 349.46s with no skips or retries. Native tests use temporary --data-dir, sample fixtures and --no-sync. Inspected desktop Details/plugin layouts and fullscreen section hints, compact Controller guide and long-title layout. Changed files pass formatting and git diff --check.

Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-fifty-eight.md, .tmp/fullscreen-lifecycle-integration-final.log, .tmp/fullscreen-lifecycle-native-final.log and .tmp/fullscreen-lifecycle-native-summary.json. Per-method mapping: tests/migration-fullscreen-lifecycle.json. Exactly the 17 assigned contracts change disposition; inventory is 1284 ported, 625 retained backend, 30 framework-specific, 403 pending and 93 partial. migration:report passes; test:migration remains correctly failing for 496 unresolved methods. No .NET implementation changed; the full .NET/native aggregate and physical-controller/display validation remain queued. Stop after this milestone for user review; do not begin TASK-381.4 without continuation.
<!-- SECTION:NOTES:END -->
