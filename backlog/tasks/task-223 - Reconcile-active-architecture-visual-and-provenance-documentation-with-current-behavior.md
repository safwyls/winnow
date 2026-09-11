---
id: TASK-223
title: >-
  Reconcile active architecture visual and provenance documentation with current
  behavior
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'mock-library.html:11'
  - 'design-system.md:2442'
  - 'docs/facet-provenance.md:101'
  - ROADMAP.md
  - game-library-design.md
  - docs/recommendation-engine.md
documentation:
  - docs/architecture-review-2026-09-10.md
priority: low
type: docs
ordinal: 254000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R35. Evidence: Source verified. The active mock/charter target still uses rejected purple, top-right unread badges and a 0.60 floor. design-system says the merge queue has no dormancy ramp despite the active implementation and another spec section. Facet provenance describes GamesDb cache payload v4 while code uses v5 and invalidation. ROADMAP retains obsolete merge-execution debt, and the build spec requires Steam collections although no reader exists. Recommendation text describing best-copy collapse also needs alignment with the actual preselected grouped candidate path. New work can follow conflicting authorities or recreate retired behavior. Scope decisions must be explicit; stale requirements should not automatically become new features.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each cited active statement is checked against source and corrected in its owning document, or its still-required implementation/deferment is explicitly tracked.
- [ ] #2 The obsolete mock is updated or clearly retired as a fidelity target; paired Codex/Claude charters remain equivalent when changed.
- [ ] #3 Replaced historical wording is recorded in docs/decisions.md, delivery status stays in ROADMAP/Backlog, and both presentation paths are described accurately; TASK-27 remains the brightness implementation owner.
<!-- AC:END -->
