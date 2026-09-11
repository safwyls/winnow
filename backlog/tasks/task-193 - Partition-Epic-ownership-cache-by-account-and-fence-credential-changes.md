---
id: TASK-193
title: Partition Epic ownership cache by account and fence credential changes
status: In Progress
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 06:27'
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
- [x] #1 Epic library caches are account-scoped and every emitted candidate retains the account identity captured for that fetch.
- [x] #2 A credential change during cache lookup or pagination cannot publish the old account's result as current; stale fallback remains within the same account.
- [ ] #3 Tests cover A-to-B switching, restart, expired/offline caches, legacy unscoped entries and delayed pages; desktop/fullscreen connection and library state agree.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Capture Epic account identity before cache reads and partition durable library payloads by validated account with a new versioned key. 2. Fence pagination, optional playtime, stale fallback and candidate publication against sign-out/account replacement while preserving same-account token renewal. 3. Retain captured AccountRef on ownership candidates and ignore legacy unscoped entries. 4. Add warm-restart, A-to-B switch, offline/expiry, legacy and delayed-page tests; run serialized Epic and sync suites and update build-spec section 4.8 and decisions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented version-2 account-scoped durable payloads, nonsecret captured sign-in generations, request context checks across authentication/pagination/playtime, and AccountRef propagation. Normal token renewal preserves context and cannot change account. Legacy or misfiled entries cannot supply ownership. Release Epic suite passed 255/255, including warm restart, A-to-B switch, signed-out cache refusal, offline expired-token fallback, delayed cache reads, delayed pages and same-account re-sign-in. Build-spec section 4.8 and decisions updated. AC3 remains pending root desktop/fullscreen Stores integration checks; backend candidate/account-state coverage passed. No migrations, live APIs, launcher data or UI layouts changed.
<!-- SECTION:NOTES:END -->
