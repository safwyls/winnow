---
id: TASK-42
title: Resolve the remaining single-entry ACCOUNT rail section
status: To Do
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 14:03'
labels:
  - ui
dependencies: []
documentation:
  - design-system.md
priority: low
ordinal: 92000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The desktop rail has Feed and Merges as screen destinations above All Games, with buckets and the single-entry ACCOUNT section below. REVIEW no longer exists as a section. Decide whether ACCOUNT should retain its heading or fit into a clearer grouping while keeping screen navigation, library subsets, lists and configuration understandable.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The remaining single-entry ACCOUNT heading is removed through a clearer layout or explicitly retained with concise current rationale.
- [ ] #2 All destinations remain reachable and their screen, bucket, list and configuration roles remain clear; document the actual resulting desktop order.
- [ ] #3 Assess fullscreen's separate navigation hierarchy and retain equivalent access without imposing desktop rail geometry.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: MainWindow.axaml places Merges beside Feed and keeps ACCOUNT around the statistics destination. The old task's REVIEW section and below-divider work-queue grammar do not describe current navigation. design-system.md section 12.1 records the current grouping; this task retains the unresolved ACCOUNT design choice.
<!-- SECTION:NOTES:END -->
