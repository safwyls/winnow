---
id: TASK-152.5
title: >-
  Remove duplicated startup work: repeated local scans, feed passes and
  full-library reads
status: Done
assignee:
  - '@claude'
created_date: '2026-09-07 22:21'
updated_date: '2026-09-08 00:27'
labels: []
dependencies: []
references:
  - src/Winnow.App/Program.cs
  - src/Winnow.App/Services/LibrarySyncService.cs
  - src/Winnow.App/ViewModels/FeedViewModel.cs
  - src/Winnow.App/ViewModels/MergeQueueViewModel.cs
  - src/Winnow.Monitor/SystemProcessSource.cs
documentation:
  - docs/spikes/memory-footprint.md
parent_task_id: TASK-152
priority: medium
ordinal: 185000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The diagnostic log of a 2026-09-07 launch on a 1,039-game library shows the local library sync (Steam, Epic and GOG scans plus resolution) running four times in the first five seconds, the feed scored four times, the merge queue loading its own full snapshot on window open and again after enrichment while the pane is hidden, the recommender re-reading the bucket query, identities, ownerships and facets on every feed pass, and the bucket query run twice for each hidden-count in the display settings. None of this is retained, but it is the allocation burst that sets the GC's committed size and LOH fragmentation for the rest of the session, and it is why non-concurrent GC peaked at 409 MB. Separately, SessionWatcher calls Process.GetProcesses() every 5 s, which materialises Process, ProcessInfo and ThreadInfo objects for every process on the machine (648 Process and 13,525 ThreadInfo objects were live in the dump); a pid-and-name enumeration would do. The outcome is one scan, one feed pass and one snapshot per trigger, with the same visible results.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 One launch performs the local scan once unless a later trigger has new candidates, and the log shows it
- [ ] #2 The feed is scored once per library change, not once per refresh call
- [x] #3 The merge queue loads its snapshot only when its pane is shown or its rail count changes, and the count comes from a COUNT query
- [x] #4 Process polling allocates no per-thread objects; the poll's allocation per tick is stated in the summary
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Find why the local scan runs four times at launch (startup task plus first-run scans of the scheduler and install-refresh services through LibrarySyncGate) and coalesce later triggers onto the shared first scan. 2. Coalesce TilesChanged bursts so the feed scores once per library change. 3. Give the merge-queue rail a COUNT query and load the full snapshot only when the pane is shown or the count changes. 4. Compute both hidden counts from one bucket query pass. 5. Replace Process.GetProcesses() in SystemProcessSource with a pid-and-name enumeration and measure allocation per tick. 6. Verify with the diagnostic log and the spike script before and after. Delegated to an Opus subagent in worktree C:\Temp\wt\152-5 (branch mem/152-5).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Subagent (Opus) finished on branch mem/152-5, commit 1ea8888. New Services/LibraryScanBaseline.cs (records launcher install fingerprints each completed pass covered; Expect()/PassExpected lets the install watchers wait for a pass in flight); StableInstallRefreshService holds its first stable read while a pass is expected and adopts a fingerprint a completed pass already covered; RefreshInstallStateAsync re-reads per store only when the fingerprint moved; Program.cs wraps the startup local sync in Expect() and replaces the post-sweep merge-queue reload with NoteQueueMayHaveMovedAsync (COUNT + sweep state); MergeQueueViewModel gains IsPaneVisible/EnsureLoadedAsync and MainWindowViewModel loads the screen when the pane is shown; MainWindow.LoadOnOpenAsync no longer builds the merge screen or scores the feed; IMergeCandidateRepository.CountPendingAsync (no migration); rating-cap and explicit-content hidden counts read the bucket rows once; SystemProcessSource on Windows walks one NtQuerySystemInformation snapshot into a reusable pinned array for pids and image names. Measured (real library copy, first 60 s): Local library sync 3 -> 1; Steam/Epic/GOG scans 4/4/2 -> 1/1/1; Expansion scan 2 -> 0; Feed scored 7 -> 3; private bytes 280/266 -> 261/242 MB (30 s / 90 s); native heaps committed 195 -> 171 MB; process poll 1,319,760 -> 84,320 bytes per tick at 702 processes (15.6x less) for a ~1.7 MB pinned buffer. Tests: 0 warnings; 3663+155+56+94 pass, 2 skip; 19 new tests. AC 1, 3, 4 met; AC 2 partial (3 feed passes, one per surviving RefreshLibraryAsync, because LibraryViewModel.LoadAsync raises TilesChanged unconditionally; absorbing duplicate LoadCommand calls in FeedViewModel was tried and reverted because AsyncRelayCommand cancels the previous execution and leaves the feed stuck — a latent bug worth its own task). game-library-design.md Tier 1 rule corrected with the old sentence in docs/decisions.md. Integration pending.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
LibraryScanBaseline records the launcher fingerprints each completed pass covered so the install watchers and the remote backfill adopt the startup scan instead of repeating it; the merge queue loads when its pane is shown and the pipeline asks whether the queue moved with IMergeCandidateRepository.CountPendingAsync; the window no longer scores the feed itself; rating-cap and explicit-content hidden counts read the bucket rows once; SystemProcessSource walks one NtQuerySystemInformation snapshot for pids and image names. Verified on the real library copy over the first 60 s of the diagnostic log: Local library sync 3 -> 1, store scans 4/4/2 -> 1/1/1, Expansion scan 2 -> 0, Feed scored 7 -> 3; private bytes 280/266 -> 261/242 MB; process poll allocation 1,319,760 -> 84,320 bytes per tick; 19 new tests, full suite passes; game-library-design.md Tier 1 rule corrected with the old sentence in docs/decisions.md. AC 2 left unchecked: three feed passes remain because LibraryViewModel.LoadAsync raises TilesChanged unconditionally; coalescing inside FeedViewModel was tried and reverted because AsyncRelayCommand cancels the previous pass and leaves the feed stuck (a latent bug worth its own task).
<!-- SECTION:FINAL_SUMMARY:END -->
