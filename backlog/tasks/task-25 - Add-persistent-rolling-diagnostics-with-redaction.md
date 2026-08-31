---
id: TASK-25
title: Add persistent rolling diagnostics with redaction
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - infra
dependencies: []
priority: medium
ordinal: 25000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
There is no persistent diagnostic log. Post-hoc diagnosis of soft-failed paths (enrichment timeouts, cover fetch failures, malformed VDF) requires reproducing the conditions. Finding F41. Source: stabilization-2026-08-28.md Group 2. Trigger: next work that needs post-hoc diagnosis of a soft-failed path.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A rolling diagnostic log persists under the data directory
- [ ] #2 The log redacts user-identifying information (steam ids, account names, file paths)
- [ ] #3 Redaction is covered by tests
- [ ] #4 Log rotation bounds total size
<!-- AC:END -->
