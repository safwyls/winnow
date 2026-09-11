---
id: TASK-138
title: Evaluate expected-completion data for recommendation research
status: To Do
assignee: []
created_date: '2026-09-06 16:20'
updated_date: '2026-09-11 14:07'
labels:
  - recommend
  - spike
dependencies: []
documentation:
  - docs/recommendation-engine.md
priority: medium
ordinal: 165000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Evaluate legally usable expected-completion data and identify which deferred recommendation uses it can support. Measure library and edition coverage, uncertainty and refresh behavior. Whole-game completion duration differs from a playable session length; session-fit recommendations also need suitable cadence evidence. This task produces research findings, not an implementation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Evaluate candidate sources for licensing and terms, update cadence, library coverage, edition matching and uncertainty.
- [ ] #2 Measure coverage against a copied Steam/Epic/GOG library and report results per store, including unmatched and inaccessible records.
- [ ] #3 Record dated methods and evidence in docs/spikes/.
- [ ] #4 Update the open questions in docs/recommendation-engine.md with the actual finding and which uses it supports; lack of a viable completion source does not rule out every future session-fit method.
- [ ] #5 If a source is viable, create a separately scoped follow-up implementation task describing the supported uses and evidence limits; do not implement it as part of this research.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: docs/recommendation-engine.md section 7 still lists expected-commitment questions and no completion-time provider exists. The task remains useful research, but a source would not automatically establish short-enough-for-tonight or session-fit accuracy.
<!-- SECTION:NOTES:END -->
