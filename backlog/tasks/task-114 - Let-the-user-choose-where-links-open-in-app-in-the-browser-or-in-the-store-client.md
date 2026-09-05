---
id: TASK-114
title: >-
  Let the user choose where links open: in app, in the browser, or in the store
  client
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
labels:
  - ui
dependencies: []
priority: medium
type: feature
ordinal: 141000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow opens outbound links inconsistently by necessity: TASK-93 added a contained webview for patch notes, store pages go to the system browser, and steam:// URIs hand off to the client. The user wants that to be a preference rather than a per-link decision made for them.

Note the constraints already established: GameLink.Create validates every outbound target and renders no button for one that fails (design-system.md §10.3), and the patch-notes panel is gated to four Steam origins with non-http(s) schemes refused outright (§10.8). A preference chooses among permitted destinations; it must not become a way to widen what is permitted.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A single preference chooses in-app, system browser, or the store client where each is possible
- [ ] #2 A link whose target is not available in the chosen mode falls back predictably, and the fallback is stated rather than silent
- [ ] #3 The preference does not widen the patch-notes origin allowlist or bypass GameLink validation
- [ ] #4 The preference persists across launches
- [ ] #5 Where a store client is not installed, that option is not offered rather than failing on click
<!-- AC:END -->
