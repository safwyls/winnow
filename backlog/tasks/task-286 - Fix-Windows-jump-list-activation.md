---
id: TASK-286
title: Fix Windows jump list activation
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-14 03:47'
updated_date: '2026-09-14 03:54'
labels: []
dependencies: []
ordinal: 328000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Clicking either recent games or Switch to Fullscreen briefly shows a busy cursor but performs no action.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Generated shell launch arguments reach the existing session for games and fullscreen, verified with actual subprocess execution.
- [ ] #2 Cold startup and normal activation retain their behavior; regression tests cover shell argument ordering and launch context.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Inspect persisted shell links, reproduce their exact launch command under an isolated instance server, fix the failing boundary, and verify both game and fullscreen requests plus existing activation behavior.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Persisted Windows shortcut inspected read-only: correct executable, command arguments and working directory. Seven subprocess cases passed against the exact source Debug Winnow.exe, covering dotnet, direct apphost and actual Windows .lnk launches in disposable data directories. A real sample-data Winnow window accepted the fullscreen request and changed to 3440x1440 fullscreen. A retained prior pipe client did not break subsequent activation. Original user failure remains unreproduced; requested whether affected session was launched from IDE/debugger, normally, or was closed. No product fix claimed. Added opt-in native shortcut regression coverage using WINNOW_ACTIVATION_TEST_EXE.
<!-- SECTION:NOTES:END -->
