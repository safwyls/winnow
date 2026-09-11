---
id: TASK-136
title: Evaluate acquisition evidence for owned-game recommendations
status: To Do
assignee: []
created_date: '2026-09-06 16:19'
updated_date: '2026-09-11 14:01'
labels:
  - recommend
dependencies:
  - TASK-135
documentation:
  - docs/recommendation-engine.md
  - docs/spikes/feed-replay.md
priority: high
ordinal: 163000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Evaluate whether attributable acquisition evidence improves owned-game recommendations. Price paid, acquisition date and price provenance exist, but the recommender does not consume them. List price is transaction-level data and may cover bundles; ownership prices have no currency. Define safe treatment of account scope, missing/conflicting facts and unsupported comparisons before introducing a bounded signal.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Specify usable acquisition evidence and unsupported comparisons; do not infer per-game discount depth from an unmatched or multi-item transaction, or compare raw prices across unknown currencies.
- [ ] #2 Respect account scope and resolved-game grouping without double-counting linked copies.
- [ ] #3 Missing, conflicting and free acquisitions introduce no negative judgement and leave unsupported comparisons out of the breakdown.
- [ ] #4 If evidence supports a signal, implement a bounded pure contribution with truthful one-sentence explanations and unchanged behavior when evidence is absent.
- [ ] #5 Compare against the baseline using captured-state replay and report cohort coverage and limitations; a changed ranking alone is not proof of improvement.
- [ ] #6 Document the supported signal, weight and tier, or the evidence-based conclusion that a signal is not yet justified.
- [ ] #7 Verify explanations and visible recommendation behavior on desktop and fullscreen.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: Ownership carries paid price/source/date but not list_price_cents. AccountFacts holds transaction-level list price; OwnershipAcquisitionObservation and AccountAcquisitionReader preserve account scope and suppress conflicting prices. TASK-38 already delivered acquisition display/export. CandidateFacts has no acquisition signal; TASK-135 provides completed replay tooling. Prior library counts are dated measurements, not current eligibility.
<!-- SECTION:NOTES:END -->
