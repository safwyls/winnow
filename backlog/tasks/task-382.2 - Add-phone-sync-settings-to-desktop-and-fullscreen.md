---
id: TASK-382.2
title: Add phone sync settings to desktop and fullscreen
status: To Do
assignee:
  - '@claude'
created_date: '2026-10-06 20:45'
labels:
  - ui
  - sync
  - companion
dependencies:
  - TASK-382.1
parent_task_id: TASK-382
ordinal: 387000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Frontend half of TASK-382. Both presentation surfaces need the same control over phone sync.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Desktop and fullscreen settings can turn phone sync on and off and show its current state
- [ ] #2 Starting pairing shows a QR code and the code expiry; the code refreshes or closes after it expires
- [ ] #3 Paired phones are listed with name and last sync, and each can be revoked
- [ ] #4 UI tests cover both surfaces
<!-- AC:END -->
