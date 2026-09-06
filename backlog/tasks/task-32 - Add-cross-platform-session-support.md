---
id: TASK-32
title: Add cross-platform session support
status: In Progress
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 23:09'
labels:
  - infra
  - ingest
milestone: m-4
dependencies: []
priority: medium
ordinal: 2800
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Session detection is Windows-only. `GameExecutableIndexBuilder` matches `*.exe`, so off Windows the index is empty and nothing is ever recorded. Under Proton the resolved executable is the Wine loader, not the game binary, so the install-prefix join cannot work; attribution would need `STEAM_COMPAT_DATA_PATH` from `/proc/<pid>/environ`. The flywheel depends on sessions, so this blocks adoption beyond Windows. Finding F30. Sources: stabilization-2026-08-28.md Group 3; ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Session detection works on at least one non-Windows platform (Linux with native games)
- [x] #2 Proton games are attributed via `STEAM_COMPAT_DATA_PATH` or an equivalent mechanism
- [x] #3 The executable index includes platform-appropriate binary patterns
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect process enumeration and executable indexing boundaries. 2. Add Linux-native executable patterns and read-only Proton compatibility-prefix attribution from proc environments. 3. Add focused tests and attempt a read-only Linux runtime verification without installing components.

Stabilize the real-process smoke assertion by waiting for the monitor-owned asynchronous exit callback and persistence, then rerun Ubuntu verification.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added execute-bit discovery for Unix-native launchers. A complete Proton compatdata join remains blocked by the monitor ownership projection lacking Steam appids; WSL runtime is unavailable on this host, so Linux runtime verification cannot be claimed.

Implemented Proton attribution without a schema change: GameExecutableIndexBuilder bulk-reads the existing ReleaseIdentity SteamAppId projection, SystemProcessSource reads only STEAM_COMPAT_DATA_PATH (capped at 64 KiB) from Linux proc environments, and the watcher promotes exact known prefixes. Added an isolated Linux smoke project with native and synthetic-compat scenarios; Windows explicitly skips those two facts.

Linux native name matching preserves extension-like suffixes and indexes a 15-character /proc/pid/comm alias; the Linux smoke uses a long suffixed executable so CI exercises that behavior.

Correction: Unix /proc comm truncation is 15 UTF-8 bytes, not 15 characters. The index now produces the matching UTF-8 byte-truncated alias and a multibyte regression test. LinuxFact explicitly skips the real-process smoke facts on all non-Linux hosts; no local Linux runtime result is claimed.

Ubuntu CI run 34065062873 at commit 9eb2ebe passed both real-process smoke tests on 2026-09-06. The native case discovers an executable with Unix mode bits inside its install root. The compatibility case runs a process outside that root and records its session using only the exact Steam appid from STEAM_COMPAT_DATA_PATH. This verifies the Proton attribution mechanism with a synthetic environment; it is not a compatibility matrix of real Wine/Proton games. The full Windows suite passed 3,932 tests, with these two Linux facts explicitly skipped; Release build and migration integrity checks also passed.

The second Ubuntu run discovered the compatibility process but asserted before its session was recorded. Investigating the smoke-test exit/persistence synchronization; first-run evidence remains valid but repeatability must be restored.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented Linux-native executable indexing and bounded proc-environment Steam appid attribution, including UTF-8 comm aliases and ambiguous-id rejection. Both real-process smoke checks passed on Ubuntu CI: https://github.com/safwyls/winnow/actions/runs/34065062873. Windows regression suite remains green.
<!-- SECTION:FINAL_SUMMARY:END -->
