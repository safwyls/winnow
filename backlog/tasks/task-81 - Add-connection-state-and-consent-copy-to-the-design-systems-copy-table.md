---
id: TASK-81
title: Add connection-state and consent copy to the design system's copy table
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
labels: []
dependencies: []
priority: low
ordinal: 108000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Stores panel is almost entirely copy, and all of it was written from the two auth spikes' posture reasoning rather than from design-system.md section 7. That table should own these decisions:

- 'Not signed in' against 'Disconnected'
- 'Session expired' against 'Error'
- 'there is nothing to sign into' against a greyed-out button

Right now the next screen with a connection state re-decides them.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 design-system.md section 7's copy table carries rows for connection state and for credential consent
- [ ] #2 The Stores panel's strings match the table
<!-- AC:END -->
