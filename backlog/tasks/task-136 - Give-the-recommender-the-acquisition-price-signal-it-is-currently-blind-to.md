---
id: TASK-136
title: Give the recommender the acquisition price signal it is currently blind to
status: To Do
assignee: []
created_date: '2026-09-06 16:19'
labels:
  - recommend
dependencies: []
priority: high
ordinal: 163000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The columns `price_paid_cents`, `list_price_cents`, `price_source` and `acquired_at` are stored (migration 0014), populated by the M5 account-page importer, and reach the domain on `Ownership`. `CandidateFacts` carries none of them, so the scoring model cannot see any of it.

This matters more than its size suggests. The measured candidate pool is roughly 1,018 rows and 754 of them are `never_played`. Across that pile the bucket, commitment shape and dormancy signals are all constant, patch-after-dormancy never fires, and the effective ranking reduces to taste affinity (0.10), installed (0.05) and deterministic jitter (0.03). Section 3 itself calls taste affinity a tiebreaker and genre similarity the commodity that loses to incumbents, so three quarters of the library is currently ordered by a commodity signal plus noise — and that pile is exactly what the product exists to surface.

Price paid is intent measured in money, it is retroactive, and unlike every other signal it varies across never-opened games. A full-price purchase never opened is a different fact from a bundle leftover, which is different again from an Epic giveaway that was never chosen — and there are 99 Epic rows, mostly giveaways, sitting undifferentiated in that pile today. `list_price_cents` additionally makes discount depth available.

TASK-38 covers the same columns for the UI and export and states explicitly that `price_paid_cents` is never read into the details modal, so the recommender consumer is untracked.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 CandidateFacts carries the acquisition facts the scorer needs, with null meaning absent evidence rather than a zero match
- [ ] #2 RecommendationScorer derives an acquisition-intent value from a pure function, unit-tested to the decimal
- [ ] #3 A missing price contributes zero and is absent from the breakdown, per the section 4 degradation rule
- [ ] #4 Free acquisitions are not penalised as if they were evidence against a game; they carry no intent either way
- [ ] #5 The signal has at least one phrasing in ReasonPhrasebook and passes the one-sentence explanation contract test
- [ ] #6 The signal is recorded in the docs/recommendation-engine.md section 3 inventory with its weight and tier
- [ ] #7 Measured against the real library, the never-played pile is ranked by something other than taste affinity plus jitter
<!-- AC:END -->
