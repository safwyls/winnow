---
id: TASK-189
title: Preserve identity invariants when undoing interleaved link history
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:10'
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
- [ ] #1 Single-link and whole-act undo preserve all relationship invariants and explicitly handle later superseding acts without overwriting them or failing midway.
- [ ] #2 Interleaved reparenting, partial undo and later membership changes are covered for each supported relationship kind; failure leaves the prior state intact.
- [ ] #3 Expansion/variant proposals use compatible admission rules so every offered operation is acceptable to the repository; grouped results agree on desktop and fullscreen.
<!-- AC:END -->
