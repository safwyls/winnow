---
id: TASK-137
title: Evaluate achievement progress as recommendation evidence
status: Done
assignee:
  - '@recommendation-engine'
created_date: '2026-09-06 16:20'
updated_date: '2026-09-11 19:20'
labels:
  - recommend
dependencies:
  - TASK-15
  - TASK-135
documentation:
  - docs/recommendation-engine.md
  - game-library-design.md
priority: medium
ordinal: 164000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Evaluate whether available achievement progress adds reliable commitment evidence while retaining per-release and account distinctions. TASK-15 supplies the missing producer and availability contract. Achievement completion does not inherently mean game completion, and global unlock percentages are not a universal progress scale. Preserve baseline behavior without usable evidence.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Consume TASK-15's explicit availability, account identity and achievement-schema states, including unknown versus known zero unlocks.
- [x] #2 Preserve separate release/platform evidence and define any grouped recommendation aggregation without blending percentages.
- [x] #3 Missing, private and unsupported achievement data leaves baseline scoring unchanged.
- [x] #4 Implement a bounded pure contribution only if evaluation supports one, with truthful one-sentence explanations that distinguish achievement completion from game completion.
- [x] #5 Evaluate retirement separately: any bucket change updates the shared query contract and both presentation paths; otherwise retain existing retirement behavior.
- [x] #6 Use captured-state replay to report evidence, coverage and limits, and document the resulting signal or supported decision not to add one.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Evaluate TASK-15 account-aware availability and schema states using sanitized captured databases. 2. Replay identical baseline tuning with and without achievement evidence; keep per-release/account states separate and test retirement independently. 3. Report fixture cohort coverage and no-signal limits, without claiming live quality improvement. 4. Document supported decision and outstanding real captured-outcome evaluation; commit separately.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: no achievement scoring facts or contribution exist. RecommendationEngine filters retired buckets before scoring, so retirement changes cannot be achieved by a scorer-only weight. Existing per-release summaries must remain distinct. TASK-15 remains the required data producer; TASK-135 provides evaluation tooling.

2026-09-11: TASK-15 now supplies account-aware Steam schema/progress/global observations. Captured temporary fixture before and after evidence verifies AC1-3: eight releases across seven works, four known-progress rows, unknown versus zero, private/unavailable and empty schema, separate 50%/100% accounts, and unsupported Epic sibling without inherited percentage. Baseline ranked entries, scores and reasons remain equal. AC4 decision: no contribution is supported by available semantics or validated outcomes, so no weight or achievement explanation is added. AC5: 100% achievements at 300 minutes stays eligible; existing 6000-minute retirement stays excluded, preserving shared desktop/fullscreen buckets. AC6: docs/spikes/achievement-evidence-2026-09-11.md records six ranked works, two judged synthetic outcomes (coverage one third), no live coverage or quality/lift claims, and the supported no-add decision. ReplayTests, ReasonContractTests and ResolvedGameEvidenceTests passed 29/29 Release tests. TASK-15 separately verified desktop/fullscreen summaries. No real database capture or live API requests occurred; future outcome research needs separately authorized evidence.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Evaluated account and platform boundaries with captured-state replay and retained baseline scoring and retirement. All 29 selected tests pass; the dated study documents fixture coverage and explains why no achievement contribution is justified. Real provider coverage and recommendation efficacy remain unmeasured.
<!-- SECTION:FINAL_SUMMARY:END -->
