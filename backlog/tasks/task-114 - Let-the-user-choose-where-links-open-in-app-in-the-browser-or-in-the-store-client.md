---
id: TASK-114
title: >-
  Let the user choose where links open: in app, in the browser, or in the store
  client
status: To Do
assignee: []
created_date: '2026-09-05 02:50'
updated_date: '2026-09-11 14:05'
labels:
  - ui
dependencies: []
documentation:
  - design-system.md
  - game-library-design.md
priority: medium
type: feature
ordinal: 141000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Provide a persisted preference for opening supported links in Winnow, the system browser or an installed store client. Current routing chooses a permitted destination per link and falls back when embedded reading is unavailable. The preference selects among supported routes while retaining GameLink validation and the embedded reader's origin/scheme restrictions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A shared desktop/fullscreen preference chooses in-app, system browser or store client where supported.
- [ ] #2 If the chosen route is unavailable for a link, use a predictable documented fallback and communicate it clearly.
- [ ] #3 The preference cannot widen the embedded patch-note origin allowlist or bypass GameLink validation.
- [ ] #4 The preference persists across launches and applies consistently from both presentations.
- [ ] #5 An unavailable store client is not offered as a working destination; platform detection and provider-native boundaries remain explicit.
- [ ] #6 Verify route selection and fallback for supported web/store links, missing clients and unavailable embedded browsing.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: GameDetailsViewModel currently tries the embedded reader then falls back; StoreActions and GameLink validate and route targets. No persisted destination preference exists. The implementation must preserve these safeguards and document provider-specific route limits.
<!-- SECTION:NOTES:END -->
