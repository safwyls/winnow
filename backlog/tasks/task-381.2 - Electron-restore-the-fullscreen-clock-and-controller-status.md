---
id: TASK-381.2
title: 'Electron: restore the fullscreen clock and controller status'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:44'
updated_date: '2026-09-30 19:23'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies: []
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
  - docs/spikes/2026-09-28-electron-parity/checkpoint-fifty-seven.md
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
- [x] #1 Fullscreen root pages and Details show the local short-time clock, update while attached and release timers when detached; desktop retains its own header.
- [x] #2 Controller connection, disconnect, wired and known battery states match the source; unknown or unsupported battery data never becomes a false level or an error. Native status follows the active device and survives reconnect/restore.
- [x] #3 At original source sizes/scales, navigation remains centered with long status strings, and external restore returns to the correct desktop state. Automated native-boundary fixtures cover unavailable hardware; physical validation remains separately tracked.
- [x] #4 All 3 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #5 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Restore the source local short-time clock and primary-ink controller status in the fullscreen root/Details header, with equal side regions and ellipsis that cannot move centered navigation. Preserve desktop controls. 2. Reuse the established trusted-main Windows PowerShell native-read pattern for XInput state/battery, with system-directory DLL loading, bounded named preload inputs, asynchronous reads, cached battery polling and helper cleanup. Chromium remains the controller selection authority; correlate current controls rather than assuming its index equals an XInput slot, and omit unknown, unsupported or ambiguous battery readings. 3. Verify all original battery rows plus native probe failures/reconnect/cache/lifetime, component clock/status cleanup and stale-reply behavior, and original native header/restore fixtures on both surfaces. 4. Run relevant shared regressions, inspect screenshots, document measured limits and update only the three assigned contract mappings. Commit, complete this child only, report the next task and pause for user review.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented fullscreen root/Details local clock, primary-ink controller status and centered navigation. The trusted-main Windows helper reads system XInput, caches battery for 30 seconds, uniquely matches the selected browser controller and omits unknown/unsupported/ambiguous readings. Cleanup and stale replies are covered. Desktop retains its header. Build/typecheck and 3351 component/live API cases across 164 files pass with no skips (59.03s). All 23 focused native controller/status/Details cases pass without retries or skips (1.7m); all 7 status cases additionally pass after explicit renderer preference-settlement waits (23.2s). Inspected fullscreen Library/Details and restored desktop screenshots. Only the three assigned source contracts moved to ported: inventory now 1267 ported, 625 retained, 30 framework-specific, 417 pending and 96 partial. The complete migration gate remains correctly failing for 513 unresolved methods. Physical validation stays in TASK-381.40. Removed two invalid Testing Library exact options without changing exact-name assertion semantics. Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-fifty-seven.md. Preparing the milestone commit; no next task started.
<!-- SECTION:NOTES:END -->
