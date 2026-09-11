---
id: TASK-198
title: Distinguish complete account inventories from positive local play evidence
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Data/Repositories/LibraryQueryRepository.cs:317'
  - 'src/Winnow.Data/Repositories/LibraryQueryRepository.cs:352'
  - 'tests/Winnow.Tests/AccountScopeTests.cs:398'
  - game-library-design.md
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 229000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R10. Evidence: Reproduced. With a confirmed account identity but no completed owned-library inventory, the owned_account_attested query treats any non-legacy membership for that account as sufficient evidence, including steam_local play observations. With Mine playing A and a housemate playing B, selecting Mine hides B even if Mine owns B but has never played it. Sign-in can confirm the account independently of inventory completion. Existing tests call a local observation an attested owned-account pass. Unknown ownership is treated as proven absence, contradicting the conservative intent of account scoping. The documented non-seed test itself needs correction, not merely another condition in the UI.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Successful inventory completion and completeness are represented by source/account independently from individual positive observations.
- [ ] #2 Own-account filtering infers absence only from suitable complete inventory evidence; local-only, failed, partial and unknown results remain conservative.
- [ ] #3 Tests and the governing account-scoping specification distinguish these evidence classes and verify library/feed behavior on both surfaces.
<!-- AC:END -->
