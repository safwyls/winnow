---
id: TASK-78
title: Encrypt the Steam Web API key and IGDB client secret at rest
status: To Do
assignee: []
created_date: '2026-09-03 00:52'
labels: []
dependencies: []
priority: medium
ordinal: 105000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Epic refresh token and the Steam session secrets are stored as one DPAPI-encrypted blob under CurrentUser scope. The Steam Web API key and the IGDB client secret are still plaintext rows in the local database.

game-library-design.md section 4.7 condition 2 states the standard: a host that cannot encrypt refuses to store rather than degrading to plaintext. That condition binds the two Steam session secrets today, and the section records that the same standard is intended for every secret Winnow keeps. These two do not meet it.

README previously claimed this was already tracked as future work when no task existed. This task is that record.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The Steam Web API key is written through the same DPAPI-protected credential store the Steam session secrets use
- [ ] #2 The IGDB client secret is written through that same store
- [ ] #3 A host that cannot encrypt refuses to store either secret rather than falling back to a plaintext row
- [ ] #4 Existing plaintext rows are migrated on first run and the plaintext columns are left empty
- [ ] #5 README's statement about where credentials live matches the shipped behaviour
<!-- AC:END -->
