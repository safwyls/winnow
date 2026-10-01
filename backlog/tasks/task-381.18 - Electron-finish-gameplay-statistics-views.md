---
id: TASK-381.18
title: 'Electron: finish gameplay statistics views'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 11:59'
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
ordinal: 436000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Statistics must expose the same recorded-session and library composition facts with consistent filter and interaction semantics.

Owns 11 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/GameplayStatsViewModelTests.cs
- GameplayStatsViewModelTests.Hidden_scope_reload_cancels_and_rejects_an_obsolete_result [pending at split]
- GameplayStatsViewModelTests.Identity_reload_updates_game_mapping_and_store_removal_resets_selection [pending at split]
- GameplayStatsViewModelTests.Search_does_not_filter_statistics_and_deactivation_rejects_pending_data [pending at split]
- GameplayStatsViewModelTests.Custom_local_day_uses_inclusive_calendar_dates_and_real_DST_duration [pending at split]
- GameplayStatsViewModelTests.Invalid_custom_dates_do_not_query_or_retain_obsolete_results [pending at split]

tests/Winnow.Ui.Tests/GameplayStatsInteractionTests.cs
- GameplayStatsInteractionTests.Xbox_import_updates_open_gameplay_store_choices_and_library_counts [partial at split]
- GameplayStatsInteractionTests.Both_surfaces_scope_dates_switch_sections_and_render_four_charts [partial at split]
- GameplayStatsInteractionTests.Desktop_error_retry_and_custom_date_validation_remain_actionable [partial at split]
- GameplayStatsInteractionTests.Fullscreen_gameplay_retry_cancel_and_resume_are_controller_actions [partial at split]
- GameplayStatsInteractionTests.Fullscreen_shell_applies_native_section_style_and_real_text_scale [pending at split]
- GameplayStatsInteractionTests.Spending_header_keeps_figures_above_the_fold [pending at split]

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original statistics calculations, empty/missing data, ranges and selected-store rules pass against the production backend inputs.
- [x] #2 Desktop summary and fullscreen Gameplay paths preserve chart/selector interactions, meaningful labels and source layout boundaries.
- [x] #3 All 11 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the eleven frozen statistics contracts and their complete fixtures, including cancellation, library identity changes, local DST dates and native typography/layout. 2. Correct demonstrated gameplay scope, range, retry/cancel, store-selection and chart presentation gaps on desktop and fullscreen. 3. Run original and equivalent component/API/native cases, including both DST days, narrow layouts, 140% text and Spending header bounds; record checkpoint73 and per-method migration evidence. 4. Commit the verified milestone and continue to TASK-381.19.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after activity recovery milestone c7f3fbcb and completion record f2621af4. Eighth task in the authorized ten-task batch. Previous complete Electron gate: 3,789 cases across 187 files.

All 23 unchanged source cases pass (11 view-model and 12 UI). First renderer pass restores gameplay cancellation, ownership-scope invalidation, selected-store reset, full store counts and fullscreen typography; the production build and focused app navigation suite (57 cases) pass. HTTP verification exposed a real SQLite writer lock while an obsolete statistics read remains pending. The scoped fix adds an explicit deferred read unit of work for gameplay, retaining a coherent library/statistics snapshot while allowing writes and replacement reads; existing write transactions keep their behavior. Native verification waits for that fixed fixture.

Focused renderer coverage now passes 72 cases, including 33 new exact gameplay cases. It reproduced and fixed failed-refresh cached figures and invalid applied dates being lost on section return; selected fullscreen choices now have visible styling. The final HTTP gate passes 11 cases, plus 63 transaction/repository regressions and 23 original cases. Corrected native request classification excludes incidental History queries and preserves final store/ownership scope through canceled intermediate reads. Native final2 passes 24 of 26; only two shell hint assertions incorrectly searching for SVG glyph text remain to rerun. Production bundle is frozen at index-BIyID6oS.js.

Final verification: all 3,822 component/live API cases across 188 files pass in 95.16 seconds (task38118-components-final2.log). Restored fullscreen Spending transaction/licence counts after the first full gate exposed their omission; 59 focused cases and a final native changed-view recheck pass on index-CxNyF6F6.js. Native evidence records 28 distinct passing cases plus that recheck, exact scope ledgers and nine inspected original final captures plus the restored-count capture. TypeScript/build, formatting, whitespace and migration audit pass. All eleven assigned source methods are ported; inventory is 1,487 ported, 650 retained, 32 framework-specific, 231 pending and 35 partial. Checkpoint73 records methods, measured bounds and framework adaptations. Controller checks use simulated Gamepad frames; physical devices/Linux/release CI remain separate.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored gameplay scope/cancellation, source date rules, store selection/counts, fullscreen typography and compact Spending behavior. Gameplay now uses a coherent deferred read transaction so stale reads cannot block library changes. Verified 3,822 full Electron cases, 28 distinct native cases plus final Spending recheck, 23 original cases, 11 HTTP cases and 63 transaction/repository regressions. All eleven assigned methods are ported; see checkpoint-seventy-three.md. Eighth milestone in the authorized ten-task batch.
<!-- SECTION:FINAL_SUMMARY:END -->
