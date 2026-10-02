---
id: TASK-381.38
title: 'Electron: verify the supported Linux Electron release packages'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:50'
updated_date: '2026-10-02 03:30'
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
ordinal: 456000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Existing users have supported Linux desktop packages; replacing the frontend must preserve those installation paths and session behavior.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Supported Linux package/portable outputs contain the Electron UI and bundled backend/runtime with correct desktop integration and required native dependencies.
- [ ] #2 Disposable Linux install/start/upgrade/uninstall and session-tracking checks pass, preserving data and package-manager update behavior; unsupported environments are explicitly distinguished.
- [ ] #3 Existing optional macOS packaging is audited for build claims; any support expansion beyond current Winnow release scope remains explicitly out of scope.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Preparation during Windows packaging identified a Linux startup risk to verify: BackendInstallationLease currently creates its adjacent lock for every release manifest, including /opt/winnow package-managed installs. Debian non-root launch cannot assume parent /opt is writable. Assess managed and read-only backend lease behavior before Linux smoke; do not bypass Chromium sandbox to make startup pass.
<!-- SECTION:NOTES:END -->
