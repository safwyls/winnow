---
id: TASK-136
title: Evaluate acquisition evidence for owned-game recommendations
status: Done
assignee:
  - '@recommendation-engine'
created_date: '2026-09-06 16:19'
updated_date: '2026-09-11 19:37'
labels:
  - recommend
dependencies:
  - TASK-135
documentation:
  - docs/recommendation-engine.md
  - docs/spikes/feed-replay.md
priority: high
ordinal: 176000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Evaluate whether attributable acquisition evidence improves owned-game recommendations. Price paid, acquisition date and price provenance exist, but the recommender does not consume them. List price is transaction-level data and may cover bundles; ownership prices have no currency. Define safe treatment of account scope, missing/conflicting facts and unsupported comparisons before introducing a bounded signal.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Specify usable acquisition evidence and unsupported comparisons; do not infer per-game discount depth from an unmatched or multi-item transaction, or compare raw prices across unknown currencies.
- [x] #2 Respect account scope and resolved-game grouping without double-counting linked copies.
- [x] #3 Missing, conflicting and free acquisitions introduce no negative judgement and leave unsupported comparisons out of the breakdown.
- [x] #4 If evidence supports a signal, implement a bounded pure contribution with truthful one-sentence explanations and unchanged behavior when evidence is absent.
- [x] #5 Compare against the baseline using captured-state replay and report cohort coverage and limitations; a changed ranking alone is not proof of improvement.
- [x] #6 Document the supported signal, weight and tier, or the evidence-based conclusion that a signal is not yet justified.
- [x] #7 Verify explanations and visible recommendation behavior on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit acquisition contracts and available copied library evidence. 2. Capture and replay the baseline, reporting coverage and outcome limitations. 3. Document safe evidence handling and add a bounded contribution only if evaluation supports it. 4. Verify shared desktop/fullscreen explanations and commit the task separately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: Ownership carries paid price/source/date but not list_price_cents. AccountFacts holds transaction-level list price; OwnershipAcquisitionObservation and AccountAcquisitionReader preserve account scope and suppress conflicting prices. TASK-38 already delivered acquisition display/export. CandidateFacts has no acquisition signal; TASK-135 provides completed replay tooling. Prior library counts are dated measurements, not current eligibility.

Source audit documented safe acquisition comparisons and account/grouping requirements in docs/recommendation-engine.md and docs/spikes/acquisition-evidence-2026-09-11.md. Fixed ownership-only reasons that falsely claimed payment or bundles; all weights and scores remain unchanged. 186/186 recommendation tests pass, including phrase/contribution honesty and sanitized replay cases. Automatic approval review rejected creating a full private library capture because it includes account/settings data; no capture created and no bypass attempted. Real per-store/account/group coverage and later-outcome baseline comparison remain unmeasured, so task stays In Progress. Desktop/fullscreen composition verification pending concurrent UI compilation fix.

Desktop and fullscreen production composition verification now passes 4/4 cases after the concurrent UI compile correction. Together with 186/186 recommendation cases, this verifies the explanation correction on both surfaces. TRX: acquisition-evidence.trx and acquisition-ui.trx. Remaining acceptance criteria stay unchecked because captured cohort/outcome evaluation is unavailable; no completion claim.

Final safe evaluation: acquisition-specific temporary captures now cover seven native works/releases/ownerships (six Steam, one Epic), reversible linking to six resolved games, two fake accounts, unknown legacy account, known zero, missing amounts, same-account price/provenance conflicts and two unmatched multi-item transactions with different currency symbols. Production AccountAcquisitionReader tests verify selected-account projection and unknown/conflict suppression. Before/after/account-switched replay ranks six games exactly once with identical scores and reasons; two synthetic judged outcomes give coverage one third. No raw-price comparison, currency inference or transaction allocation enters scoring. AC4/6 supported no-add decision: available semantics and unvalidated outcomes justify retaining baseline weight/tier/retirement; synthetic labels establish no real quality or lift. Real per-store coverage/outcome efficacy remains explicitly unmeasured, not an unmet live-data requirement inferred beyond AC5. Release verification passed30/30 replay/reason/resolved-game tests,11/11 account provenance tests and4/4 desktop/fullscreen production composition tests. Source and dated study updated. No production records captured; earlier full-backup auto-review rejection was not bypassed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed acquisition evaluation through the supported no-signal option. Captured synthetic evidence preserves account scope, one recommendation per linked game, known-zero/missing/conflicting distinctions, and baseline scores/reasons.30 recommendation tests,11 account tests and4 desktop/fullscreen cases pass. No acquisition weight added; real coverage and quality remain unmeasured.
<!-- SECTION:FINAL_SUMMARY:END -->
