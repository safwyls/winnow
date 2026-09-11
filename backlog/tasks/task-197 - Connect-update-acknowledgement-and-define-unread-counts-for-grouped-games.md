---
id: TASK-197
title: Connect update acknowledgement and define unread counts for grouped games
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 06:24'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Program.cs:1089'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:1367'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:1398'
  - 'src/Winnow.App/ViewModels/GameDetailsViewModel.cs:504'
  - 'src/Winnow.App/ViewModels/GameDetailsViewModel.cs:545'
  - 'src/Winnow.Data/Repositories/LibraryQueryRepository.cs:265'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 228000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R09. Evidence: Reproduced count; source-verified missing production wiring. Program registers IUpdateFlagService, but the sole production GameDetailsViewModel construction omits it, raw update events, acknowledgement state and the reload callback. Both surfaces hide mark-read/restore controls. Direct-constructor tests supply the missing dependencies and pass. Separately, library update_count includes pre-play pushes: last play in 2025 with correlated pushes in 2024 and 2026 returns 2. Details unions grouped releases while existing acknowledgement commands target only Tile.ReleaseId. The core unread-mail interaction is unavailable, its displayed count disagrees with 'updates since you played', and simply wiring a single watermark would mishandle grouped releases.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Production library-to-details composition provides working mark-read/restore behavior on desktop and fullscreen, with regression tests through that construction path.
- [x] #2 One documented unread definition uses effective last play and contributing release watermarks consistently for badges, counts, details and derived buckets.
- [x] #3 Tests cover pre-play/post-play updates, acknowledgement boundaries, never-played games and multiple grouped releases; grouped actions acknowledge exactly their intended observations.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Wire the production library-to-details update service and retain per-release event identity/watermarks. Define unread counts from correlated unacknowledged pushes strictly after effective grouped last play, retaining the existing maximum-per-release group count. Mark read only the displayed contributing release pushes and restore their own standing acknowledgements, with truthful partial-failure handling. Add production-construction, grouped/account/never-played/boundary and desktop/fullscreen regressions, update the specific unread documentation, then run focused serialized tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented production IUpdateFlagService injection, per-release event identity and watermarks, correlated-push counts after effective grouped last play, and displayed-evidence-only grouped read/restore with partial-failure feedback. Added real Program.ConfigureServices and FullscreenContext.Create keyboard regressions: 7 passed in ui197-composition.trx. Existing/new update, bucket, accessible-copy and details regressions: 123 passed in ui197-regression.trx. Updated visual spec section 5.2 and decisions; no production host launched. Awaiting coordinator integration/finalization.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Production desktop and fullscreen feed/library paths now inject update acknowledgement. Shared update-reading semantics count correlated build pushes since the grouped effective last-play time; acknowledgement stores per-release watermarks and preserves later unseen updates. Passed 123 focused unit/repository tests and 7 production-composition headless UI regressions, including keyboard read/restore on both surfaces.
<!-- SECTION:FINAL_SUMMARY:END -->
