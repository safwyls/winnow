---
id: TASK-209
title: Resolve recommendation feedback identity independently of visible candidates
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Recommend/RecommendationEngine.cs:275'
  - 'src/Winnow.App/Services/PluginFeedService.cs:32'
  - src/Winnow.Data/Repositories/IdentityLinkRepository.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 240000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R21. Evidence: Reproduced. RecommendationEngine builds its work-resolution map from eligible bucket rows only, then resolves feedback for absent works as themselves. If a dismissed Steam child is hidden by own-account scope while its linked Epic parent remains visible, the parent is recommended again. PluginFeedService repeats the same mapping approach. Changing account visibility can undo a dismissal without changing the identity relationship. Eligibility is not a complete source of identity truth.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Feedback resolution uses complete relevant identity state independent of candidate visibility and account filtering in built-in and plugin feed paths.
- [ ] #2 Dismissals and other work-level feedback remain effective when their originating sibling is filtered, hidden or otherwise ineligible.
- [ ] #3 Tests cover all-account/own-account transitions, link creation/retraction and grouped release changes; undo and impressions remain coherent on both surfaces.
<!-- AC:END -->
