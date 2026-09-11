---
id: TASK-209
title: Resolve recommendation feedback identity independently of visible candidates
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
- [x] #1 Feedback resolution uses complete relevant identity state independent of candidate visibility and account filtering in built-in and plugin feed paths.
- [x] #2 Dismissals and other work-level feedback remain effective when their originating sibling is filtered, hidden or otherwise ineligible.
- [x] #3 Tests cover all-account/own-account transitions, link creation/retraction and grouped release changes; undo and impressions remain coherent on both surfaces.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Load complete identity resolution independently of visible candidate rows, inside the library snapshot where practical. Apply shared suppression to built-in and plugin feeds, including hidden child verdicts and undo. Verify persisted facts remain release-scoped.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
LibrarySnapshot now includes complete live identity links in its existing read transaction, independently of filtered bucket rows. RecommendationGame.ResolveFeedback is shared by built-in/plugin feeds. Hidden-child dismissal/snooze, own/all scope transitions, unlink and original-release undo/impression regressions pass.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Visibility changes no longer erase feedback on linked games. Complete identity state resolves release-scoped stored feedback for both feed providers, and UI cards record/revoke the same release they surfaced.
<!-- SECTION:FINAL_SUMMARY:END -->
