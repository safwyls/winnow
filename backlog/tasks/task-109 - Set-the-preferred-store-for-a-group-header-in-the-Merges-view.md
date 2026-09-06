---
id: TASK-109
title: Set the preferred store for a group header in the Merges view
status: To Do
assignee: []
created_date: '2026-09-05 02:49'
labels:
  - ui
dependencies: []
priority: medium
type: feature
ordinal: 136000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
A merged/linked group in the Merges view shows one entry as its header. The user wants to choose which store that header comes from, rather than accepting whatever the survivor rule picked. Relates to how the header is currently chosen (SurvivorLadder and the identity-link resolution).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The user can set which store provides the header for a linked group
- [ ] #2 The choice persists and survives re-ingest and further links
- [ ] #3 The choice is visible without opening anything, and reversible
- [ ] #4 A group whose preferred store is later removed falls back to the automatic rule rather than showing nothing
<!-- AC:END -->
