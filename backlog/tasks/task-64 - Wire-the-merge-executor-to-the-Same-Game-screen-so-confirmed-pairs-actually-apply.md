---
id: TASK-64
title: >-
  Wire the merge executor to the Same Game screen so confirmed pairs actually
  apply
status: To Do
assignee: []
created_date: '2026-09-01 03:09'
labels:
  - resolve
  - ui
dependencies:
  - TASK-62
priority: high
ordinal: 81000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
MergeExecutor and IMergeExecutionRepository are registered in Program.cs and resolved nowhere outside tests, so nothing in the running app has ever applied a merge. Confirmed pairs stay confirmed and unapplied, and merge_applications is empty. TASK-5 built the engine and recorded that wiring the screen was a later stage; no task covered it until now. The Same Game screen should offer applying a confirmed pair, showing the plan preview and any blocker before the user commits, and the batch path should apply outstanding confirmed pairs. Sequencing note: the undo journal from TASK-62 should land BEFORE or WITH this, so the first merge the user ever applies is already reversible.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The Same Game screen can apply a confirmed pair and reports what changed
- [ ] #2 The preview shows the surviving identity and any blocker before the user commits
- [ ] #3 Applying is refused with a visible reason when the plan reports a blocker
- [ ] #4 A merge applied through the UI writes an undo journal entry, so it is reversible from the moment the feature ships
<!-- AC:END -->
