---
id: TASK-193
title: Partition Epic ownership cache by account and fence credential changes
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Ingest.Epic/Web/EpicAccountClient.cs:26'
  - 'src/Winnow.Ingest.Epic/Web/EpicAccountClient.cs:97'
  - 'src/Winnow.App/Services/SqliteEpicLibraryCache.cs:22'
  - 'src/Winnow.Ingest.Epic/Web/Model/EpicOwnedLibrary.cs:77'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 224000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R05. Evidence: Source verified. EpicAccountClient reads the global epic:library entry before resolving the access token/account. Changing sign-in can return the previous account's library for six hours, or longer through stale fallback. The API knows accountId, but emitted ownership candidates have no AccountRef. Ownership from one Epic account can be presented or ingested under another session. This contradicts the build specification's account-keyed cache requirement.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Epic library caches are account-scoped and every emitted candidate retains the account identity captured for that fetch.
- [ ] #2 A credential change during cache lookup or pagination cannot publish the old account's result as current; stale fallback remains within the same account.
- [ ] #3 Tests cover A-to-B switching, restart, expired/offline caches, legacy unscoped entries and delayed pages; desktop/fullscreen connection and library state agree.
<!-- AC:END -->
