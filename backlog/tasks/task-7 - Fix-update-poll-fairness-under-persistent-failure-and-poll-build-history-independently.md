---
id: TASK-7
title: >-
  Fix update-poll fairness under persistent failure and poll build history
  independently
status: To Do
assignee: []
created_date: '2026-08-29 21:52'
labels:
  - enrich
dependencies: []
priority: medium
ordinal: 7000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Under persistent failure, update polling can starve some titles indefinitely. Raw build history must be polled independently of announcement fetches so a failure in one does not block the other. Findings F11 and F12. Source: stabilization-2026-08-28.md Group 2. Trigger: next update-signal work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A persistently failing title does not prevent other titles from being polled
- [ ] #2 Build-history polling proceeds independently of announcement polling
- [ ] #3 A test demonstrates fair round-robin under simulated persistent failure
<!-- AC:END -->
