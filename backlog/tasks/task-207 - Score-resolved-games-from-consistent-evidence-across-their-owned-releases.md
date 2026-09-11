---
id: TASK-207
title: Score resolved games from consistent evidence across their owned releases
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 06:44'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Recommend/RecommendationEngine.cs:275'
  - 'src/Winnow.Recommend/RecommendationEngine.cs:486'
  - 'src/Winnow.Recommend/RecommendationEngine.cs:514'
  - 'src/Winnow.App/Services/PluginFeedService.cs:80'
  - 'src/Winnow.App/ViewModels/FeedViewModel.cs:350'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 238000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R19. Evidence: Reproduced installed-sibling failure; other grain conflicts source verified. RecommendationEngine chooses one primary row before scoring, then derives Installed, sessions and updates chiefly from that ownership/release while grouped playtime/buckets include siblings. A never-played Steam parent with an installed Epic sibling is a candidate but gets no Ready to play card. PluginFeedService uses group.Any(Installed), so built-in and plugin eligibility differ. Taste/prevalence inputs also retain ownership/facet-row grain rather than a clearly defined resolved-game population. The chosen header can change recommendation eligibility despite an equivalent playable game. Explanation, scoring, launch and feedback need a shared identity/evidence contract, including which release supplied each fact.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A shared resolved-game candidate projection supplies consistent installation, play/session/update evidence and eligible population semantics to built-in and plugin recommendations.
- [x] #2 Tests cover installed/history-bearing siblings, derelict copies, duplicate storefront observations, hidden/account-scoped copies and stable scoring under header changes without double-counting equivalent facts.
- [x] #3 Reasons, launch targets, impressions and feedback preserve explicit release provenance while presenting the same game consistently on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Introduce a shared resolved-game candidate projection for built-in and plugin feeds. Aggregate installation, facets and history across contributing entries while retaining an explicit action/feedback representative. Prove linked multi-store behavior, identity separation and both presentation paths with focused tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Shared Core RecommendationGame projection now supplies built-in and plugin feeds with visible group evidence, complete feedback resolution, explicit action identity and kept title. Taste/mode population is per resolved game, session intervals merge overlaps, patch counts use max per release and retain quoted-title provenance. Recommendation suite166passed; production composition4passed on desktop/fullscreen; plugin/feed snapshot focused21passed (inventory was subsequently updated by191).
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Resolved-game recommendations now share evidence and eligibility semantics. Installed siblings provide the action, all contributing visible copies supply history/descriptors, and duplicated store observations do not multiply session/patch/taste facts. Kept title, surfaced action release and explanation provenance remain explicit across both presentations.
<!-- SECTION:FINAL_SUMMARY:END -->
