---
id: TASK-381.15
title: 'Electron: finish remaining Details and lightbox contracts'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 09:23'
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
ordinal: 433000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Remaining Details/lightbox contracts require complete source-equivalent evidence beyond the populated layouts already verified.

Owns 11 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/DetailsModalScaleTests.cs
- DetailsModalScaleTests.The_width_floor_stays_above_the_cards_own_minimum [pending at split]

tests/Winnow.Tests/Enforcement/ScreenshotLightboxStructureTests.cs
- ScreenshotLightboxStructureTests.The_overlay_is_not_a_popup [pending at split]
- ScreenshotLightboxStructureTests.It_is_declared_in_the_windows_grid_after_the_modal [pending at split]
- ScreenshotLightboxStructureTests.Control_fills_use_theme_tokens_without_deferred_runtime_bindings [pending at split]

tests/Winnow.Tests/ScreenshotLightboxTests.cs
- ScreenshotLightboxTests.No_string_the_overlay_speaks_is_ever_a_placeholder [pending at split]

tests/Winnow.Ui.Tests/AchievementPresentationTests.cs
- AchievementPresentationTests.Both_surfaces_render_ingested_account_states_without_inventing_zero [pending at split]

tests/Winnow.Ui.Tests/GameDetailsTabInteractionTests.cs
- GameDetailsTabInteractionTests.Keyboard_arrows_switch_tabs_and_leave_focus_on_the_selected_tab [partial at split]
- GameDetailsTabInteractionTests.Long_overview_keeps_its_scroll_position_when_activity_is_opened [partial at split]
- GameDetailsTabInteractionTests.Wrong_game_menu_focuses_search_and_returns_without_losing_query_or_results [partial at split]

tests/Winnow.Ui.Tests/ProseMeasureTests.cs
- ProseMeasureTests.Desktop_prose_keeps_its_measure_and_left_edge_across_reading_surfaces [pending at split]
- ProseMeasureTests.Fullscreen_prose_retains_the_TV_typography_and_available_width [pending at split]

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every assigned original Details tab, achievement, prose/scale and lightbox contract has complete equivalent assertions using the original data/geometry boundaries.
- [x] #2 Desktop modal and fullscreen reading/gallery paths keep complete images, readable text, correct tab ownership and keyboard/controller origin restoration.
- [x] #3 All 11 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the eleven frozen Details, achievement, prose/scale and lightbox methods against current source and replacement evidence. 2. Correct demonstrated gaps and preserve original fixture, geometry and input boundaries on both surfaces. 3. Execute original and equivalent component/API/native tests; inspect screenshots and record checkpoint 70 and exact per-method migration evidence. 4. Commit the verified milestone and continue to TASK-381.16.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after metadata composition milestone e2be2a53. Fifth of the ten authorized sequential tasks.

All21 original cases and5 HTTP achievement cases pass. Added2px tab focus, screenshot tooltips, accessible achievement heading/group, and shared desktop/fullscreen prose policy. Focused tests found fullscreen matching focus raced the closing action panel and initial Details focus; both paths now respect existing focus and modal release. Source audit includes Themes descriptions/typography and settings help, with exact empty PreviewMergeCandidateRepository and unloaded AccountStats state. Native lightbox checks retain strict token equality while awaiting image readiness and settled CSS transitions; final regression is pending.

Final gate: 3718 component/live API cases across183 files pass in93.97s. All21 native cases,21 unchanged original cases and5 HTTP cases pass. Visual review includes both lightboxes, achievements, matching, tab focus and desktop/fullscreen prose; original empty Merges and unloaded Spending fixtures preserved. Audit:1450 ported,650 retained,32 framework-specific,247 pending,56 partial. Checkpoint70 records exact evidence and simulated-controller limits.

Verified milestone committed as adef2a8e. Continue to TASK-381.16 under the authorized batch.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed all11 Details/lightbox contracts:2px tab focus, shared410px desktop prose and scaled28px fullscreen prose, screenshot tooltips, accessible achievement grouping and robust fullscreen matching focus. Verified3718 component/API,21 native,21 original and5 HTTP cases; build, formatting and migration audit pass. Evidence:docs/spikes/2026-09-28-electron-parity/checkpoint-seventy.md.
<!-- SECTION:FINAL_SUMMARY:END -->
