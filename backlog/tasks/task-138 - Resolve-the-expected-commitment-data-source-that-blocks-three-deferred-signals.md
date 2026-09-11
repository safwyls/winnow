---
id: TASK-138
title: Evaluate expected-completion data for recommendation research
status: In Progress
assignee:
  - '@recommendation-engine'
created_date: '2026-09-06 16:20'
updated_date: '2026-09-11 18:45'
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
- [x] #3 Record dated methods and evidence in docs/spikes/.
- [x] #4 Update the open questions in docs/recommendation-engine.md with the actual finding and which uses it supports; lack of a viable completion source does not rule out every future session-fit method.
- [ ] #5 If a source is viable, create a separately scoped follow-up implementation task describing the supported uses and evidence limits; do not implement it as part of this research.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect documented completion sources and terms using current primary sources. 2. Distinguish completion duration from session cadence and define edition/account coverage measurements. 3. Measure copied-library coverage only with authorized evidence; record inaccessible or unmeasured records honestly. 4. Record dated findings and update deferred model questions; create an implementation follow-up only if source viability is established.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: docs/recommendation-engine.md section 7 still lists expected-commitment questions and no completion-time provider exists. The task remains useful research, but a source would not automatically establish short-enough-for-tonight or session-fit accuracy.

Recorded dated primary-source findings in docs/spikes/expected-completion-2026-09-11.md and corrected recommendation-engine section 7. IGDB documents game_time_to_beats; RAWG average playtime is not completion; HLTB was inaccessible in this run. No authenticated provider queries or copied-library coverage measurements were performed. Full library capture was rejected by automatic approval review due private account/settings payload; rejection not bypassed. Steam/Epic/GOG denominators, exact edition matches, unmatched/inaccessible record counts remain unmeasured. Current English Twitch agreement content did not render, so complete terms validation remains outstanding. Task stays In Progress with AC1/2/5 unchecked; no provider is yet validated and no implementation follow-up created. Research changes no desktop/fullscreen behavior.
<!-- SECTION:NOTES:END -->
