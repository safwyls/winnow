---
id: TASK-152.4
title: Measure ReadyToRun publish and GC conserve settings for the shipped build
status: Done
assignee:
  - '@claude'
created_date: '2026-09-07 22:21'
updated_date: '2026-09-08 00:27'
labels: []
dependencies: []
references:
  - packaging/Publish.ps1
  - src/Winnow.App/Winnow.App.csproj
documentation:
  - docs/spikes/memory-footprint.md
  - docs/releases.md
parent_task_id: TASK-152
priority: medium
ordinal: 184000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The published win-x64 build (packaging/Publish.ps1) is self-contained, untrimmed and not ReadyToRun, and Winnow.runtimeconfig.json carries no System.GC settings. On 2026-09-07 the runtime's double-mapped JIT code and loader heaps showed as about 60 MB of resident pagefile-backed sections on the real library versus 16 MB for a bare Avalonia window, and the loader heaps alone were 44 MB in the heap dump. DOTNET_GCConserveMemory=9 cut GC committed from 90 MB to 73 MB and private bytes by about 14 MB in a quick trial; DOTNET_GCgen0size and a 128 MB hard limit changed nothing useful; non-concurrent GC made things worse (409 MB private at 20 s). This task measures PublishReadyToRun (with and without composite), System.GC.ConserveMemory and TieredPGO off against the same library with docs/spikes/memory-footprint.ps1, and adopts what pays for itself. Trimming is out of scope: Avalonia and Dapper are not trim-safe without a separate effort.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A table in docs/spikes/memory-footprint.md compares private bytes, working set, GC committed, startup time and package size for baseline, R2R and R2R plus ConserveMemory on the same library
- [x] #2 packaging/Publish.ps1 and the runtimeconfig carry only settings that measured a gain, each with a comment stating the number
- [ ] #3 Release smoke checks still pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Publish baseline with packaging/Publish.ps1, then variants: ReadyToRun, ReadyToRun composite, ConserveMemory 5 and 9, TieredPGO off. 2. Measure each twice with docs/spikes/memory-footprint.ps1 on the same library copy with --no-sync, plus the adopted variant with sync; record private bytes, working set, GC committed, mapped resident, startup time and package size. 3. Adopt only settings that measured a gain, each with a comment stating the number; update docs/releases.md. 4. Run the smoke checks that can run locally. Delegated to a Sonnet subagent in worktree C:\Temp\wt\152-4 (branch mem/152-4).
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Subagent (Sonnet) finished on branch mem/152-4, commit 5ba2814. Measured baseline / ReadyToRun / R2R composite / R2R+ConserveMemory (env) / adopted R2R+ConserveMemory=9, two runs each with --no-sync on a copy of the real library. R2R: Mapped resident 47.7 -> 36 MB, warm startup ~2.0 s -> ~1.0 s, package 115.8 -> 139.3 MB. ConserveMemory=9 on top: private bytes about 20-30 MB lower, GC committed about 25 MB lower. Composite (+28 MB package for ~2 MB) and TieredPGO off (no gain) rejected. Adopted: PublishReadyToRun for win-x64 in packaging/Publish.ps1 (plus an -ExtraProperties passthrough) and System.GC.ConserveMemory=9 as a RuntimeHostConfigurationOption scoped to a win-x64 RuntimeIdentifier in Winnow.App.csproj. Tests: build 0 warnings, Winnow.Tests 3644 and Winnow.Ui.Tests 56 pass in an isolated worktree at the same base commit. Installer smoke checks need Inno Setup and a disposable runner; not run locally. Integration into main pending.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Measured self-contained win-x64 publishes on the real library copy (two runs each, --no-sync): ReadyToRun cut Mapped resident (JIT code and loader heaps) 47.7 -> 36 MB and warm startup ~2.0 -> ~1.0 s for +23 MB package; System.GC.ConserveMemory=9 on top cut private bytes 20-30 MB and GC committed ~25 MB; composite R2R (+28 MB package for ~2 MB) and TieredPGO off (no gain) rejected. Adopted PublishReadyToRun for win-x64 in packaging/Publish.ps1 (with an -ExtraProperties passthrough) and ConserveMemory=9 as a RuntimeHostConfigurationOption scoped to a win-x64 RuntimeIdentifier; docs/releases.md updated; table and commands in spike section 7. Publish and launch verified for every variant; build and full suite pass. AC 3 left unchecked: installer smoke checks need Inno Setup on a disposable runner and were not run locally.
<!-- SECTION:FINAL_SUMMARY:END -->
