---
id: TASK-223
title: >-
  Reconcile active architecture visual and provenance documentation with current
  behavior
status: Done
assignee:
  - '@data-layer'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 08:10'
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
Architecture review 2026-09-10, R35. Evidence: Source verified. The active mock/charter target still uses rejected purple, top-right unread badges and a 0.60 floor. design-system says the merge queue has no dormancy ramp despite the active implementation and another spec section. Facet provenance describes IGDB game cache payload v4 while code uses v5 and invalidation. ROADMAP retains obsolete merge-execution debt, and the build spec requires Steam collections although no reader exists. Recommendation text describing best-copy collapse also needs alignment with the actual preselected grouped candidate path. New work can follow conflicting authorities or recreate retired behavior. Scope decisions must be explicit; stale requirements should not automatically become new features.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each cited active statement is checked against source and corrected in its owning document, or its still-required implementation/deferment is explicitly tracked.
- [x] #2 The obsolete mock is updated or clearly retired as a fidelity target; paired Codex/Claude charters remain equivalent when changed.
- [x] #3 Replaced historical wording is recorded in docs/decisions.md, delivery status stays in ROADMAP/Backlog, and both presentation paths are described accurately; TASK-27 remains the brightness implementation owner.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify the cited visual, facet-cache, identity, recommendation and Steam collection statements against current source and completed tasks. 2. Mark the obsolete HTML mock as historical and remove it as a fidelity target in paired agent charters; preserve TASK27 rendering ownership. 3. Correct active owning documents and remove shipped merge debt; explicitly defer unimplemented Steam collections with a tracked task if none exists. 4. Record replaced wording in decisions, verify paired charter equivalence and document links/searches, and close only after all acceptance criteria are evidenced.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Source correction: the stale version4/version5 statement concerns IGDB GamePayloadVersion, not GamesDb (which currently uses projectionVersion1). SteamLibrarySource has no collection reader; DRAFT-1 now records a deferred post-beta scope decision without scheduling implementation. TASK70 is Done and TASK64 was superseded by immediate reversible link application. Paired Avalonia charters now point to the visual spec/tokens and mark the mock historical.

Verified current source for desktop merge floor/vivid layers, fullscreen text proposals, IGDB game payload v5 and legacy fallback, shared refresh scheduling, immediate reversible identity links and pregrouped recommendation candidates. Corrected owning docs, dated historical cache measurements and removed obsolete manual cache deletion advice. Retired mock via visible banner/title and both Avalonia charters; static checks confirm matching charter descriptions/bodies and valid banner links. Replaced wording is preserved in decisions. DRAFT-1 explicitly defers Steam collections outside beta/repair scope; TASK92 has a historical correction note. Scoped diff check passed. Evidence:docs/spikes/architecture-fixes-2026-09-10.md Active-document reconciliation section. No rendering constants or product implementation changed; TASK27 retains brightness ownership.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Active architecture, visual, provenance, recommendation and roadmap statements now match verified source. The obsolete library mock is clearly historical and paired Avalonia charters agree. Steam collection import remains deferred as DRAFT-1; existing user-authored lists are unchanged. Historical wording and verification methods are preserved. Static consistency and scoped diff checks passed; no new runtime test was needed for this documentation-only change.
<!-- SECTION:FINAL_SUMMARY:END -->
