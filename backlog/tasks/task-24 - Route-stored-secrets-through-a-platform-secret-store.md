---
id: TASK-24
title: Route stored secrets through a platform secret store
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
labels:
  - security
  - auth
dependencies: []
priority: high
ordinal: 24000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Stored credentials (Epic tokens, API keys) are not routed through a platform secret store. The README's blanket DPAPI claim was corrected by F03/F40 (Epic tokens are protected, but keys are plaintext). The remainder of F40: migrate plaintext rows to a platform secret store on first read. Source: stabilization-2026-08-28.md Group 2, finding F40 (remainder). Trigger: next credential or settings work.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 All stored secrets are read from and written to the platform secret store (DPAPI/Credential Manager on Windows)
- [ ] #2 Existing plaintext rows are migrated on first read
- [ ] #3 A test confirms that no secret is stored in plaintext after migration
<!-- AC:END -->
