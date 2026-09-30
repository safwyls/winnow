---
id: TASK-381.37
title: 'Electron: verify the Windows Electron installer and portable build'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies:
  - TASK-381.24
  - TASK-381.23
  - TASK-381.36
  - TASK-381.29
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 455000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The primary Windows release still ships Avalonia; a working development Electron window does not establish installation or upgrade behavior.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Windows installer and portable packages contain the correct Electron UI, bundled backend/runtime, product identity, assets, provider files and notices, with no runtime Avalonia UI dependency.
- [ ] #2 Disposable install/launch/upgrade/uninstall smoke checks pass, preserving existing/legacy data, protocol activation, shortcuts, single-instance behavior and update recovery.
- [ ] #3 Build and smoke commands, artifacts and any signing limits are documented with reproducible evidence.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
