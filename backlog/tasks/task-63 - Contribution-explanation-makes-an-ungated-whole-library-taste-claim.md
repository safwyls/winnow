---
id: TASK-63
title: Contribution explanation makes an ungated whole-library taste claim
status: Done
assignee:
  - '@codex'
created_date: '2026-09-01 03:07'
updated_date: '2026-09-06 22:40'
labels:
  - recommend
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 3000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
RecommendationScorer builds the taste contribution explanation as "<facet> is where your hours go, and this is one." This is the same defect class TASK-58 fixed in the card phrasebook: an implicit whole-library claim that fires at any affinity above zero, so two entries with different facets contradict each other. It appears in the why-this-was-recommended sheet rather than on the card, which is why TASK-58 left it: its exact text is asserted twice in FeedViewModelTests, in a project a concurrent agent held at the time. Reword it to a claim the scorer can prove, consistent with the phrasebook rule that a card may only claim what the engine knows, and update the two assertions with it.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The contribution explanation no longer asserts a whole-library rank or majority that the profile does not measure
- [x] #2 Two contributions with different facets can be read together without contradiction
- [x] #3 The FeedViewModelTests assertions are updated rather than deleted
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect RecommendationScorer's taste contribution and the existing FeedViewModelTests assertions. 2. Replace the unsupported whole-library phrasing with a facet-scoped explanation that remains true for multiple facets. 3. Add focused scorer coverage and update both FeedViewModelTests expectations in place. 4. Record evidence and finalize after the focused tests pass.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Replaced the taste contribution's unsupported whole-library wording with facet-scoped copy: 'This matches your taste in {facet} games.' Added scorer assertions for the copy and two distinct facets, and updated both FeedViewModelTests expectations in place. Verification awaits the parent's serialized test slot.

Changed taste contribution to This matches your taste in {facet} games. Distinct-facet scorer tests and both retained FeedViewModel assertions pass in Release.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Taste explanations describe a supported match without claiming a whole-library majority. Distinct-facet and feed assertions pass.
<!-- SECTION:FINAL_SUMMARY:END -->
