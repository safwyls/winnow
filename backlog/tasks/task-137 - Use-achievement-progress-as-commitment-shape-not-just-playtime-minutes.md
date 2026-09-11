---
id: TASK-137
title: Evaluate achievement progress as recommendation evidence
status: To Do
assignee: []
created_date: '2026-09-06 16:20'
updated_date: '2026-09-11 14:01'
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
- [ ] #1 Consume TASK-15's explicit availability, account identity and achievement-schema states, including unknown versus known zero unlocks.
- [ ] #2 Preserve separate release/platform evidence and define any grouped recommendation aggregation without blending percentages.
- [ ] #3 Missing, private and unsupported achievement data leaves baseline scoring unchanged.
- [ ] #4 Implement a bounded pure contribution only if evaluation supports one, with truthful one-sentence explanations that distinguish achievement completion from game completion.
- [ ] #5 Evaluate retirement separately: any bucket change updates the shared query contract and both presentation paths; otherwise retain existing retirement behavior.
- [ ] #6 Use captured-state replay to report evidence, coverage and limits, and document the resulting signal or supported decision not to add one.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: no achievement scoring facts or contribution exist. RecommendationEngine filters retired buckets before scoring, so retirement changes cannot be achieved by a scorer-only weight. Existing per-release summaries must remain distinct. TASK-15 remains the required data producer; TASK-135 provides evaluation tooling.
<!-- SECTION:NOTES:END -->
