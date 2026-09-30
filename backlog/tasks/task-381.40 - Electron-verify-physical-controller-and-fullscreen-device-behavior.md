---
id: TASK-381.40
title: 'Electron: verify physical controller and fullscreen device behavior'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies:
  - TASK-381.2
  - TASK-381.3
  - TASK-381.4
  - TASK-381.5
  - TASK-381.37
  - TASK-381.38
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 458000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Synthetic gamepads and fixed-size windows do not establish physical controllers, native fullscreen switching or TV-distance legibility. These limits must be reviewed explicitly before completion claims.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Available supported physical controller/native fullscreen configurations are exercised for input mapping, held-button suppression, reconnect, battery status, focus and cursor recovery.
- [ ] #2 TV/small/ultrawide legibility, native window restoration and platform material/accessibility fallbacks have recorded device/environment-specific evidence.
- [ ] #3 Any unavailable hardware or OS matrix is named with a concrete unverified behavior; completion requires user agreement on each exception rather than treating simulated tests as physical proof.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
