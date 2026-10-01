---
id: TASK-381.6
title: 'Electron: finish library loading and live refresh behavior'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:45'
updated_date: '2026-10-01 01:22'
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
ordinal: 424000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Independent API reads and live events must not replace newer data, block initial rendering or keep retired views alive.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ApiLibraryViewModelTests.cs
- ApiLibraryViewModelTests.MetadataConflictPreservesObservedRevisionForReview [pending at split]
- ApiLibraryViewModelTests.FrontendDependencyGraphContainsNoBackendImplementationAssemblies [pending at split]
- ApiLibraryViewModelTests.FrontendCompositionBuildsBothSurfacesWithoutStorageOrWorkers [pending at split]
- ApiLibraryViewModelTests.LibraryUsesBackendGroupingAndMetadataWithoutRepositories [pending at split]
- ApiLibraryViewModelTests.ListEditsUseDisplayedRevisionAndPreserveStateOnConflict [pending at split]
- ApiLibraryViewModelTests.SettingsLoadsRemoteHiddenAndManualEntriesWithoutRepositories [pending at split]
- ApiLibraryViewModelTests.RemoteManualIdentifierConflictRemainsAnInlineFieldError [pending at split]

tests/Winnow.Tests/LibraryViewModelTests.cs
- LibraryViewModelTests.The_toggle_works_without_a_settings_store [pending at split]

tests/Winnow.Ui.Tests/BackendLiveUpdatesTests.cs
- BackendLiveUpdatesTests.FullscreenActivityJournalAndSummaryUseApiOnlyFrontendServices [pending at split]
- BackendLiveUpdatesTests.IndependentDesktopAndFullscreenClientsRefreshCommittedEditsAndRetainTheirSearch [pending at split]
- BackendLiveUpdatesTests.FrontendResynchronizesAfterBackendRestart [pending at split]

tests/Winnow.Ui.Tests/LazyPaneTests.cs
- LazyPaneTests.Panes_are_absent_from_the_tree_until_they_are_first_shown [pending at split]
- LazyPaneTests.A_panes_visibility_follows_its_container_without_a_change_at_birth [pending at split]
- LazyPaneTests.The_detail_modal_is_built_with_its_data_when_a_game_is_opened [pending at split]

tests/Winnow.Ui.Tests/LibraryPreparationResponsivenessTests.cs
- LibraryPreparationResponsivenessTests.Input_runs_during_preparation_without_publishing_partial_or_stale_tiles [pending at split]

tests/Winnow.Ui.Tests/LibraryRefreshOrderingTests.cs
- LibraryRefreshOrderingTests.Slower_manual_reload_cannot_replace_newer_settings_and_metadata_refresh [partial at split]
- LibraryRefreshOrderingTests.Winning_visibility_snapshot_closes_hidden_details_and_keeps_valid_selection [partial at split]

tests/Winnow.Ui.Tests/OwnershipRefreshCompositionTests.cs
- OwnershipRefreshCompositionTests.Scheduled_acquisition_and_later_metadata_reach_the_open_surface_despite_partial_failure [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original API library load, ordered refresh, live-event replacement and ownership refresh assertions pass through the production Electron/API boundary.
- [x] #2 Lazy panes and preparation retain responsive rendering and cancellation/disposal semantics; desktop and fullscreen publish the same authoritative library without stale completion or duplicate work.
- [x] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the eighteen frozen source methods against the existing Electron renderer, named preload bridge, API transport and live-event tests; preserve full original fixtures and assertion scope.
2. Add bounded API composition/conflict and lazy/preparation coverage with domain-agent ownership. Coordinator owns independent-client live events, restart, ordered refresh and ownership-refresh integration. Repair only demonstrated production gaps, sharing renderer data and cancellation policies across both surfaces.
3. Verify authoritative projection, displayed revisions, inline errors, deferred panes, input responsiveness, cancellation, latest-wins refresh and independent client search retention through executable component/API and native tests with disposable data.
4. Run final component/live API suite, relevant native regressions and build/typecheck. Map only these eighteen methods when equivalence is demonstrated, update documentation and checkpoint evidence, and commit.
5. Stop for user review at TASK-381.6. Do not begin TASK-381.7 until an explicit continuation prompt.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified all eighteen assigned source methods against their frozen fixtures. Added cancellable 128-record library preparation with atomic publication; API grouping/conflict and dependency-boundary coverage; exact independent-selection/publication-count assertions; native independent-client, restart, journal/Spending, lazy-pane, ordered-refresh and real scheduled-ownership fixtures. Sixteen methods are ported. Two exact retired Avalonia mechanisms receive per-method framework rationale; their corresponding user behavior remains tested on both surfaces.

Build and typecheck pass. All 3,414 component/live API cases across 170 files pass in 87.25s at four workers; all 19 distinct native cases pass with no final skips/retries. An existing large merge test timed out during the first full run overlapping native work; both variants passed alone and the complete lower-concurrency run passed without assertion/timeout changes. Screenshots, fixture corrections and limitations are recorded in docs/spikes/2026-09-28-electron-parity/checkpoint-sixty-one.md. Inventory: 1329 ported, 625 retained-backend, 32 framework-specific, 368 pending, 81 partial; 449 unresolved. Complete migration gate still fails as expected.

Review milestone d0f1b84a records the implementation, tests, migration inventory and checkpoint evidence. The keyboard controller-glyph correction is milestone 172482f4. Task complete; paused at this boundary for user review. Do not begin TASK-381.7 until the user explicitly prompts continuation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Library loading and live refresh now have complete evidence for the eighteen assigned contracts, with shared cancellable preparation and source-equivalent desktop/fullscreen coverage. Verified 3,414 component/API and 19 native cases, build/typecheck, formatting and migration inventory. See checkpoint-sixty-one.md for exact evidence and framework dispositions. Stop here for user review; TASK-381.7 remains unstarted.
<!-- SECTION:FINAL_SUMMARY:END -->
