---
id: TASK-152
title: Reduce Winnow's resident memory toward the Playnite baseline
status: Done
assignee:
  - '@claude'
created_date: '2026-09-07 22:19'
updated_date: '2026-09-08 01:31'
labels: []
dependencies: []
references:
  - docs/spikes/memory-footprint.ps1
documentation:
  - docs/spikes/memory-footprint.md
priority: high
ordinal: 179000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow sits at roughly 350 MB working set / 275 MB private bytes on a 1,039-game library right after startup, and around 500 MB once the grid has been scrolled, against about 175 MB for Playnite. docs/spikes/memory-footprint.md records where the memory goes, measured on 2026-09-07 with docs/spikes/memory-footprint.ps1: a bare Avalonia 11.3 window costs about 120-137 MB private on this machine, Winnow's shell with an empty library and --no-sync costs 142 MB, the startup sync/enrichment pipeline leaves about 100 MB behind that never returns, the real library adds about 100 MB more (GC heap growth, decoded covers on the native heap, JIT code), and the decoded-cover LRU can grow to its 128 MiB budget with two bitmaps per cover. This is the umbrella for the ranked opportunities in that document; each subtask is independently shippable and must re-measure with the same script before and after.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each subtask records before/after numbers from docs/spikes/memory-footprint.ps1 on the same machine and library in its final summary
- [x] #2 docs/spikes/memory-footprint.md is updated with the new measurements when a subtask changes them, and any rule that moves is edited in the document that owns it
- [x] #3 Steady-state private bytes on the 1,039-game library after startup are at or below 200 MB, or the remaining gap is explained and accepted in docs/decisions.md
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
All five subtasks integrated into the main working tree (uncommitted, alongside the TASK-153 work in progress) on 2026-09-07; build 0 warnings, 4,005 tests pass. Combined build measured with docs/spikes/memory-footprint.ps1 on the real library copy with the startup pipeline: private bytes 217.1 MB at 30 s and 205.7 MB at 90 s (before: 288.9 / 275.2), working set 332.8 / 323.7 (before: 361.2 / 354.7), GC committed 59.2 / 57.9 (before: 97.2 / 89.9). This is the framework-dependent Release build; the published win-x64 build adds ReadyToRun and ConserveMemory=9, which measured a further 20-30 MB. AC 3 (at or below 200 MB, or gap accepted in docs/decisions.md) is 6 MB short on this build and needs the owner's call. Branches mem/152-1..5 and mem/integration keep the per-task history.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
All five subtasks landed in the main working tree on 2026-09-07 (build 0 warnings, 4,005 tests pass). Verified with docs/spikes/memory-footprint.ps1 on the real library copy with the startup pipeline: private bytes 289/275 -> 217/206 MB at 30/90 s, working set 361/355 -> 333/324 MB, GC committed 97/90 -> 59/58 MB; scrolled-to-bottom 507 -> 328-368 MB (152.2's harness). The published win-x64 build adds ReadyToRun and ConserveMemory=9 for a further 20-30 MB. The remaining gap to the 200 MB target and to Playnite is the Avalonia/Skia/ANGLE floor (120-137 MB for a bare window), accepted by the owner as the cost of cross-platform support and recorded in docs/decisions.md. Spike sections 6-11 carry every measurement; branches mem/152-1..5 and mem/integration keep the per-task history.
<!-- SECTION:FINAL_SUMMARY:END -->
