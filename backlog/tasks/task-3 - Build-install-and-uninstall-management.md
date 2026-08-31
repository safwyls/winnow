---
id: TASK-3
title: Build install and uninstall management
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - ui
  - ingest
  - infra
milestone: m-2
dependencies:
  - TASK-2
priority: medium
ordinal: 33000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the M9 deliverable. Winnow delegates installation and uninstallation to the owning store client and reflects state back. Never reimplements download, patching, or CDN auth (ROADMAP.md section 4, "M9 delegates, never reimplements"). Source: ROADMAP.md section 4, M9 row.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Install command for a Steam game issues `steam://install/<appid>` and monitors state change
- [ ] #2 Install command for an Epic game delegates to the Epic launcher
- [ ] #3 Uninstall delegates similarly and reflects the new state
- [ ] #4 Library view reflects installed/uninstalled state after the operation
<!-- AC:END -->
