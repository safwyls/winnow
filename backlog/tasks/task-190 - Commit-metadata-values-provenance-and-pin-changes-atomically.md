---
id: TASK-190
title: Commit metadata values provenance and pin changes atomically
status: Done
assignee:
  - '@data-layer'
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 06:42'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Data/Repositories/WorkFieldSourceRepository.cs:132'
  - 'src/Winnow.Data/Repositories/WorkIgdbPinRepository.cs:32'
  - src/Winnow.Data/Repositories/WorkRepository.cs
  - 'src/Winnow.App/Services/WorkMetadataEditService.cs:69'
  - src/Winnow.App/Services/PluginSyncService.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 221000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R02. Evidence: Reproduced. WorkFieldSourceRepository writes a field and its provenance in separate statements without a local transaction. Rejecting the provenance INSERT leaves the new value committed without user ownership. WorkIgdbPinRepository clears/inserts pins before updating works; rejecting the works UPDATE leaves live pin222 while works still maps to111. WorkRepository enrichment also depends on caller transaction coverage, while PluginSyncService calls it directly. A failed edit can leave requested user ownership unrecorded or leave contradictory IGDB identity authorities. RepositoryWriteBatch already supplies a suitable local transaction/savepoint pattern.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Public field set/reset, pin and enrichment operations atomically persist values, provenance and history when called alone or inside an ambient unit of work.
- [x] #2 Fault injection at each dependent write and cancellation cannot leave partial changes, including when an ambient caller catches the error and commits other work.
- [x] #3 Built-in enrichment, plugin enrichment and metadata editing on both surfaces use the same atomic contract and preserve user-owned fields.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Wrap public field set/reset, IGDB pin and enrichment value/provenance operations in RepositoryWriteBatch so standalone calls use transactions and ambient callers use savepoints. 2. Add dependent-write failure and cancellation regressions with standalone and outer-commit scenarios, retaining existing user-field and pin semantics. 3. Document the expanded atomic repository contract and run focused metadata, pin, plugin and identity tests through the shared build helper.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented standalone transactions and ambient savepoints for field set/reset, IGDB pin and enrichment operations. The shared Release helper passed 133 focused tests with zero failures/skips, including 40 new dependent-write, cancellation-after-value-write and outer-commit cases plus existing field ownership, pin and plugin sync regressions. Final closure left to coordinator integration.

Final verification: shared Release helper passed 217 focused data/domain/form tests and 26 headless desktop/fullscreen presentation tests. Atomicity coverage includes 40 dedicated dependent-write, cancellation and ambient-commit cases; existing field ownership/pin/plugin tests and real manual-form submission preserve user ownership on both surfaces. Public pin-clear revision changes also have standalone/ambient rollback regressions. Governing atomic-call contract documented. Root retains full solution integration under TASK-227.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Field edits/resets, IGDB pins and enrichment now own atomic local transactions or ambient savepoints. Values, provenance and history survive failures and cancellation together. Verified with 40 atomicity regressions inside a passing 217-test focused run and 26 passing headless presentation tests.
<!-- SECTION:FINAL_SUMMARY:END -->
