---
id: TASK-381.1
title: Split the remaining Electron migration into reviewable tasks
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:38'
updated_date: '2026-09-30 18:52'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies: []
parent_task_id: TASK-381
priority: high
type: task
ordinal: 418000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The migration has continued across a large parent task without useful review boundaries. The user now requires separate bounded tasks, one active implementation task at a time, and an explicit pause after every completed task until they prompt continuation. This planning task is the first review checkpoint; it changes task organization only.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every one of the 516 pending or partial contracts at commit d4b14152 is assigned to exactly one child task, with its source identifier and source file recorded.
- [x] #2 Known functionality gaps, full regression checks, platform packaging, release/CI cutover and device-validation limits have explicit tasks and sensible dependencies.
- [x] #3 The parent records an ordered task list and the mandatory stop-after-each-task workflow; queued tasks have outcomes and acceptance criteria without speculative implementation plans.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Read the current inventory and existing open tasks. 2. Partition unresolved source contracts by bounded behavior and add explicit remaining release/device gates. 3. Create all tasks using the Backlog CLI, verify exact coverage and dependencies, and update the parent. 4. Commit the breakdown, report the next task, and stop for user review before changing code.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified the persisted tasks through CLI JSON readback: all 516 current pending/partial method IDs and source paths occur in exactly one of 35 feature/test tasks; five additional tasks cover Windows packages, Linux packages, release/CI cutover, physical devices and final complete regression. All 40 queued tasks retain unchecked acceptance criteria, no speculative implementation plan, valid acyclic dependency references and an explicit stop/wait-for-user review boundary. Parent queue readback contains every task ID and the new cadence; parent acceptance criteria remain unchecked. No app code, test mappings or user data changed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Created TASK-381.2 through TASK-381.41 and an ordered queue on TASK-381. Exact-coverage and dependency audits pass for all 516 unresolved contracts. This breakdown is the completed review checkpoint. Pause here; begin TASK-381.2 (fullscreen clock and controller status) only after the user prompts continuation.
<!-- SECTION:FINAL_SUMMARY:END -->
