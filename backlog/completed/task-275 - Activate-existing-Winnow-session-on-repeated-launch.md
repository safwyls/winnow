---
id: TASK-275
title: Activate existing Winnow session on repeated launch
status: Done
assignee:
  - '@codex'
created_date: '2026-09-13 22:48'
updated_date: '2026-09-13 22:52'
labels: []
dependencies: []
ordinal: 317000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Repeated launch should return to the existing session instead of asking the user to close it.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Second launches activate the existing window without starting another host.
- [x] #2 Directory isolation, startup races and desktop/fullscreen restoration have regression coverage.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Keep the directory mutex. Add bounded local IPC with queued UI activation. Reuse and correct window restoration. Test IPC and window states.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a current-user per-directory activation pipe before host startup. Second processes grant Windows foreground permission, request activation and exit quietly. Requests coalesce until the Avalonia dispatcher handler is ready. Restore now preserves fullscreen/maximized state and restores minimized windows to their prior state. Verified desktop and fullscreen with seven headless tray/window tests; eight guard/IPC tests cover readiness, repeated requests, isolation, shutdown, and a real child process that activates the owner and exits without a database. Build succeeded through test runs using scratch outputs. Native OS foreground policy was not interactively tested; Linux window managers may restrict focus. Older owners without the channel time out quietly after three seconds.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Repeated launches restore the existing session instead of showing an error. Preserved desktop/fullscreen state and directory isolation. Eight guard/IPC tests and seven UI restoration tests passed, including a real second-process launch.
<!-- SECTION:FINAL_SUMMARY:END -->
