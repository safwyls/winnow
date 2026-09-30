---
id: TASK-381.39
title: 'Electron: switch primary build, CI and release entry points to Electron'
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
  - TASK-381.6
  - TASK-381.7
  - TASK-381.8
  - TASK-381.9
  - TASK-381.10
  - TASK-381.11
  - TASK-381.12
  - TASK-381.13
  - TASK-381.14
  - TASK-381.15
  - TASK-381.16
  - TASK-381.17
  - TASK-381.18
  - TASK-381.19
  - TASK-381.20
  - TASK-381.21
  - TASK-381.22
  - TASK-381.23
  - TASK-381.24
  - TASK-381.25
  - TASK-381.26
  - TASK-381.27
  - TASK-381.28
  - TASK-381.29
  - TASK-381.30
  - TASK-381.31
  - TASK-381.32
  - TASK-381.33
  - TASK-381.34
  - TASK-381.35
  - TASK-381.36
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
ordinal: 457000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Even after feature parity, the default run/build/release commands and required CI jobs must select the Electron frontend rather than silently shipping Avalonia.

This task owns a remaining delivery/validation gate beyond the source-method inventory. It does not reassign contracts from the feature tasks.

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Default documented run/build/package/release entry points produce Electron with the independent backend; legacy Avalonia reference code/tests cannot be mistaken for the delivered frontend.
- [ ] #2 Required Windows/Linux CI executes the appropriate Electron, backend, migration-integrity and packaging gates with accurate reusable-evidence provenance; missing evidence runs real checks.
- [ ] #3 Product, architecture, roadmap, agent/build and release documentation agree with the verified final composition and platform scope; no release is published as part of this task.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
