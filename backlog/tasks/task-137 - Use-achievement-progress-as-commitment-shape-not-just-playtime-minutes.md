---
id: TASK-137
title: 'Use achievement progress as commitment shape, not just playtime minutes'
status: To Do
assignee: []
created_date: '2026-09-06 16:20'
labels:
  - recommend
dependencies:
  - TASK-15
priority: medium
ordinal: 164000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Commitment shape currently reads where playtime sits against the refund line. Achievement progress answers the same question better and is fully retroactive: three of forty achievements unlocked and thirty-five of forty are opposite verdicts that both present as "some hours, then stopped".

This bears on correctness, not only on ranking. Retired is a hard exclusion under section 4, so a game the user effectively finished but which does not clear the playtime threshold stays a candidate — and section 8 lists nagging about correctly-abandoned games as a failure mode the model is supposed to design against. Sharper retirement detection closes that gap from the evidence side.

Steam global achievement unlock percentages additionally give a progress reading normalised across games without knowing how long any game is, which is a cheaper partial answer to the genre-conditional threshold problem section 7 defers pending expected-commitment data.

TASK-15 owns the ingest (the tables exist since migration 0001; nothing populates them and there is no Steam fetch). This task owns only the scoring consumer.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 CandidateFacts carries achievement progress, keeping not-probed distinct from probed-and-zero, per the ReturnEpisodes precedent
- [ ] #2 RecommendationScorer uses achievement progress in commitment shape via a pure function, unit-tested to the decimal
- [ ] #3 A release with no achievement data scores exactly as it does today
- [ ] #4 A release with no achievements defined is distinguished from one with achievements and none unlocked
- [ ] #5 Retired classification is re-examined against achievement completion and the outcome recorded, whether or not it changes
- [ ] #6 The signal is documented in the section 3 inventory and carries a one-sentence explanation
<!-- AC:END -->
