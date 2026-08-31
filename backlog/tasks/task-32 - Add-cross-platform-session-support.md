---
id: TASK-32
title: Add cross-platform session support
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
labels:
  - infra
  - ingest
milestone: m-4
dependencies: []
priority: high
ordinal: 32000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Session detection is Windows-only. `GameExecutableIndexBuilder` matches `*.exe`, so off Windows the index is empty and nothing is ever recorded. Under Proton the resolved executable is the Wine loader, not the game binary, so the install-prefix join cannot work; attribution would need `STEAM_COMPAT_DATA_PATH` from `/proc/<pid>/environ`. The flywheel depends on sessions, so this blocks adoption beyond Windows. Finding F30. Sources: stabilization-2026-08-28.md Group 3; ROADMAP.md section 6.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Session detection works on at least one non-Windows platform (Linux with native games)
- [ ] #2 Proton games are attributed via `STEAM_COMPAT_DATA_PATH` or an equivalent mechanism
- [ ] #3 The executable index includes platform-appropriate binary patterns
<!-- AC:END -->
