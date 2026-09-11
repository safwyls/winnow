---
id: TASK-201
title: Preserve account identity through Steam account-page imports
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Core/Ingest/SteamAccountPages.cs:50'
  - 'src/Winnow.App/Services/SteamAccountPageImportService.cs:226'
  - 'src/Winnow.App/Services/SteamAccountPageImportService.cs:472'
  - 'src/Winnow.App/Services/SteamAccountPageImportService.cs:559'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 232000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R13. Evidence: Source verified. The Steam account-page capture contract carries HTML, time and route but no account identity. Imports resolve against a global Steam title index and store source=steam facts without account scope. Embedded sign-in knows the captured account but discards it at this boundary. Two accounts' purchases/licenses can mix or deduplicate identical transactions. Account statistics and acquisition evidence can be attributed or collapsed incorrectly. Saved files may have genuinely unknown identity and need an explicit representation rather than a guessed account.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Known account identity survives capture, parsing, resolution and persistence, including observation deduplication keys.
- [ ] #2 Unknown saved-file/legacy identity is represented explicitly with a documented compatibility policy; no current account is guessed.
- [ ] #3 Tests import the same game and identical transaction facts under two accounts and verify account statistics/acquisition provenance on both surfaces.
<!-- AC:END -->
