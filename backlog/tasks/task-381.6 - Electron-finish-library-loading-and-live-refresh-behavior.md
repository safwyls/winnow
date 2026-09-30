---
id: TASK-381.6
title: 'Electron: finish library loading and live refresh behavior'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:45'
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
- [ ] #1 Original API library load, ordered refresh, live-event replacement and ownership refresh assertions pass through the production Electron/API boundary.
- [ ] #2 Lazy panes and preparation retain responsive rendering and cancellation/disposal semantics; desktop and fullscreen publish the same authoritative library without stale completion or duplicate work.
- [ ] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
