---
id: TASK-198
title: Distinguish complete account inventories from positive local play evidence
status: Done
assignee:
  - '@data-layer'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:35'
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
- [x] #1 Successful inventory completion and completeness are represented by source/account independently from individual positive observations.
- [x] #2 Own-account filtering infers absence only from suitable complete inventory evidence; local-only, failed, partial and unknown results remain conservative.
- [x] #3 Tests and the governing account-scoping specification distinguish these evidence classes and verify library/feed behavior on both surfaces.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add migration0034 and a source/account-scoped current inventory attempt contract/repository; durable attempt revisions fence completion and historical memberships remain positive-only. 2. Expose Steam completeness only for explicit counts matching all valid distinct app IDs; fresh cache retains original time/completeness while stale, failed and partial answers cannot establish absence. 3. Begin incomplete attempts before provider IO and complete only after resolver success, including empty inventories and confirmed accounts absent from the local scan. 4. Require suitable complete inventory evidence in the shared account query, retaining selected-account positives and games unknown at the original inventory time; document the conservative policy. 5. Verify parser/API evidence classes, ordering/rollback, remote completion/failure and desktop/fullscreen library/feed composition; update docs, decisions and measurement record.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Final verification: 164 focused Release tests passed across account scope/membership, inventory repository/completeness API, remote sync, confirmation/playtime, plugin feeds, account-page provenance and identity inventory. 34 headless tests passed across account/feed production composition, refresh ordering and store account context. Migration validation passed37 hashes including0034; scoped diff check passed. Complete inventory requires exact explicit game_count and valid distinct IDs; stale fallback remains positive-only. Beginning a newer source/account attempt retires old completeness before HTTP, and older completion is rejected by revision. Original response time bounds absence for newly discovered games. Local positives, unknown entries and legacy memberships are never erased. Remote confirmed-account targeting works with no local games. No UI production changes were required.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Separated inventory completeness from individual positive membership evidence with migration0034 and guarded attempt revisions. Account filtering now requires a suitable complete resolved inventory; partial, failed, stale and overlapping attempts stay conservative. Verified164 focused tests,34 desktop/fullscreen tests and37 migration hashes; measurement record and governing account specification updated.
<!-- SECTION:FINAL_SUMMARY:END -->
