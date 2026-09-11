---
id: TASK-189
title: Preserve identity invariants when undoing interleaved link history
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
  - 'src/Winnow.Data/Repositories/IdentityLinkRepository.cs:224'
  - 'src/Winnow.Data/Repositories/IdentityLinkRepository.cs:299'
  - 'src/Winnow.Data/Repositories/LibraryQueryRepository.cs:463'
  - 'src/Winnow.Resolve/LibraryExpansionScan.cs:224'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 220000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R01. Evidence: Reproduced. Creating A→B and then B→C correctly flattens A→C. Retracting only A's link restores A→B while B→C remains live: the resulting depth-two graph violates the one-hop resolution contract. A second history, X,Y→P; X,Y→Q; X→R; retract the middle act, fails the unique live-parent constraint. Undo restores displaced rows without checking later acts or the same admission rules as link creation. Expansion scanning also emits a proposal under a base that is itself an expansion child; the repository rejects that proposal. Identity groups, aggregate playtime, counts and list membership can become inconsistent. Undo must respect later user decisions and preserve the depth-one relationship contract.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Single-link and whole-act undo preserve all relationship invariants and explicitly handle later superseding acts without overwriting them or failing midway.
- [x] #2 Interleaved reparenting, partial undo and later membership changes are covered for each supported relationship kind; failure leaves the prior state intact.
- [x] #3 Expansion/variant proposals use compatible admission rules so every offered operation is acceptable to the repository; grouped results agree on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Define a shared structural admission rule for every link kind and use it for repository creation and expansion/variant proposals. 2. Make whole-act and single-link undo supersession-aware: retract only standing effects, restore only still-valid prior links, otherwise separate the affected child without moving later decisions. 3. Add interleaved-history, proposal and failure-rollback regressions across all link kinds. 4. Document the depth-one and conservative undo policy, then run focused tests through the shared dotnet helper.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented shared structural admission, supersession-aware restoration, and 21 interleaving/proposal/failure cases. Production projects compile through the shared Release helper. Focused test compilation is temporarily blocked by concurrent Steam interface fixture updates; no unrelated fixtures changed. Both desktop and fullscreen consume the same repository contract; pending integration verification remains.

Shared Release helper verification passed: 133 focused identity, expansion, variant, read-model, metadata, pin and plugin-sync tests, zero failures/skips. This includes 21 new identity regressions covering all link kinds, nested proposal refusal, and restoration failure rollback inside/outside an ambient unit of work. Final closure left to coordinator integration.

Final verification: shared Release helper passed 217 focused repository, identity inventory, schema and form tests; headless Release helper passed 26 desktop/fullscreen presentation tests, including merge platform/dormancy and fullscreen details behavior. The 21 new identity regressions cover all relationship kinds, supersession, partial undo, proposal refusal and failure rollback. Governing depth-one and conservative restoration policy documented in game-library-design.md section6 and docs/decisions.md. Root retains complete solution integration under TASK-227.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Undo preserves later decisions and restores prior membership only when the one-hop invariants remain valid. Proposal and repository admission share the same rules. Verified by 217 focused data/domain tests and 26 headless presentation tests, all passing.
<!-- SECTION:FINAL_SUMMARY:END -->
