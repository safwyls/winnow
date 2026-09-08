---
id: TASK-152.1
title: Stop the startup sync pipeline from leaving ~100 MB behind
status: Done
assignee:
  - '@claude'
created_date: '2026-09-07 22:21'
updated_date: '2026-09-08 00:27'
labels: []
dependencies: []
references:
  - src/Winnow.App/Program.cs
  - src/Winnow.Data/SqliteConnectionFactory.cs
documentation:
  - docs/spikes/memory-footprint.md
parent_task_id: TASK-152
priority: high
ordinal: 181000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Measured 2026-09-07 (docs/spikes/memory-footprint.md): an empty library launched with --no-sync settles at 142 MB private bytes and 24 MB GC committed; the same empty library with the normal startup pipeline (the Task.Run in Program.cs that chains local sync, remote backfill, playtime backfill, enrichment, facets, maturity, reception, soft-match, update poll, merge-queue load and storefront sync) settles at 235-243 MB private and 70 MB GC committed, and stays there. The difference is GC heap that is committed but no longer live (about 45 MB), native NT-heap growth (about 40 MB; candidates are pooled SQLite connections each holding a page cache, HTTP/TLS buffers and the GOG Galaxy database copy) and about 23 MB of JIT'd code for the pipeline's code paths. On the real library the same pipeline is only 15-25 MB heavier than --no-sync, so most of this is per-launch overhead rather than per-game. The outcome is a launch whose post-pipeline footprint is close to the --no-sync footprint.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Empty library with the startup pipeline finishes within 20 MB private bytes of the same library launched with --no-sync, sampled at 90 s with docs/spikes/memory-footprint.ps1
- [x] #2 The summary names which of the three buckets (GC committed slack, native heap, JIT code) each change addressed, with numbers
- [x] #3 No enrichment, sync or scan behaviour changes; only when and how memory is returned
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Attribute the native-heap growth of the empty-library sync run by hypothesis (pooled SQLite connections and cache_size, GOG Galaxy snapshot, HttpClient handler pools), measuring each with docs/spikes/memory-footprint.ps1. 2. Return GC slack once the startup task completes (one forced compacting gen-2 collection; verify decommit). 3. Clear or bound SQLite pools after the burst; bound cache_size in Open(). 4. Re-measure empty library sync vs --no-sync at 90 s; record per-bucket numbers in the spike. Boundaries: no runtimeconfig or packaging changes (152.4), no pipeline trigger restructuring (152.5). Delegated to an Opus subagent in worktree C:\Temp\wt\152-1 (branch mem/152-1).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Subagent (Opus) finished on branch mem/152-1, commit 1900df6. Correction to the spike: the 'empty library' sync run was not empty; the local scans import 707 works from Steam/Epic/GOG within 0.6 s, so most of the ~95 MB gap between sync and --no-sync was the imported library, not pipeline residue (relaunching the populated dir with --no-sync costs 255-306 MB, more than the sync run that built it). Pooled SQLite page caches are not the native-heap growth (14,063 opens in 90 s, never more than 10 concurrent; bounding cache_size changed nothing measurable); decoded cover pixels track the NT heap (152.2), and the 5 s process poll is worth ~17 MB (152.5). Changes: new Services/StartupMemoryTrim.cs (releases pooled SQLite connections and forces one blocking compacting aggressive gen-2 every 20 s while the startup pipeline runs and once at its end; one log line per trim), three lines in Program.cs, PRAGMA cache_size=-256 and ReleasePooledConnections() in SqliteConnectionFactory, two new tests. Measured (3 interleaved pairs, pristine library, 90 s): GC committed 73.9 -> 61.0 MB, NT heap 49.5 -> 42.3, private 242.9 -> 224.3 (-18.6 MB); warm-library end-of-pipeline trim logged private 362 -> 271 MB. The trim has no durable effect at 180 s (noise), because cover decoding and the poll re-commit the heap. Tests: 3,951 pass, 2 skip. AC 1 not met as written (sync 224 vs --no-sync 149; the gap is the library itself); AC 2 and 3 met. Integration pending.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Services/StartupMemoryTrim.cs (releases pooled SQLite connections and forces a blocking compacting gen-2 every 20 s while the startup pipeline runs and once when it ends), PRAGMA cache_size=-256 and ReleasePooledConnections() in SqliteConnectionFactory, two tests. Verified with docs/spikes/memory-footprint.ps1 in three interleaved pairs on a pristine library at 90 s: GC committed 73.9 -> 61.0 MB, native heap 49.5 -> 42.3 MB, private bytes 242.9 -> 224.3 MB; on a warm library the end-of-pipeline trim returned 91 MB at once. Full suite passes (4,005 tests on the integrated main tree). AC 1 is left unchecked: the measurement showed the sync-vs-no-sync gap is the 707-work library the pipeline imports, not residue, so the 20 MB target is not meetable as written; spike section 6 records the correction and the remaining 75 MB attribution (library ~50, JIT ~14, process poll ~17).
<!-- SECTION:FINAL_SUMMARY:END -->
