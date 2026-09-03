---
id: TASK-63
title: Contribution explanation makes an ungated whole-library taste claim
status: To Do
assignee: []
created_date: '2026-09-01 03:07'
labels:
  - recommend
  - ui
dependencies: []
priority: medium
ordinal: 80000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
RecommendationScorer builds the taste contribution explanation as "<facet> is where your hours go, and this is one." This is the same defect class TASK-58 fixed in the card phrasebook: an implicit whole-library claim that fires at any affinity above zero, so two entries with different facets contradict each other. It appears in the why-this-was-recommended sheet rather than on the card, which is why TASK-58 left it: its exact text is asserted twice in FeedViewModelTests, in a project a concurrent agent held at the time. Reword it to a claim the scorer can prove, consistent with the phrasebook rule that a card may only claim what the engine knows, and update the two assertions with it.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The contribution explanation no longer asserts a whole-library rank or majority that the profile does not measure
- [ ] #2 Two contributions with different facets can be read together without contradiction
- [ ] #3 The FeedViewModelTests assertions are updated rather than deleted
<!-- AC:END -->
