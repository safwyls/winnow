---
id: TASK-381.2
title: 'Electron: restore the fullscreen clock and controller status'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:44'
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
ordinal: 420000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The original fullscreen header shows a live local clock and controller connection/battery status. Electron still lacks complete equivalent status reporting, and long status text must not move the centered navigation.

Owns 3 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/GamepadInputTests.cs
- GamepadInputTests.UnknownBatteryIsAbsentRatherThanAnError [pending at split]

tests/Winnow.Ui.Tests/FullscreenInteractionTests.cs
- FullscreenInteractionTests.Main_navigation_stays_centered_with_varying_controller_status_and_clock [pending at split]
- FullscreenInteractionTests.External_restore_hides_tv_and_controller_status_omits_unknown_battery [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Fullscreen root pages and Details show the local short-time clock, update while attached and release timers when detached; desktop retains its own header.
- [ ] #2 Controller connection, disconnect, wired and known battery states match the source; unknown or unsupported battery data never becomes a false level or an error. Native status follows the active device and survives reconnect/restore.
- [ ] #3 At original source sizes/scales, navigation remains centered with long status strings, and external restore returns to the correct desktop state. Automated native-boundary fixtures cover unavailable hardware; physical validation remains separately tracked.
- [ ] #4 All 3 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #5 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
