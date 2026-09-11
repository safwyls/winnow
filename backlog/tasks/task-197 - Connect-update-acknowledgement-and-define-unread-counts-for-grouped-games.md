---
id: TASK-197
title: Connect update acknowledgement and define unread counts for grouped games
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Production library-to-details composition provides working mark-read/restore behavior on desktop and fullscreen, with regression tests through that construction path.
- [ ] #2 One documented unread definition uses effective last play and contributing release watermarks consistently for badges, counts, details and derived buckets.
- [ ] #3 Tests cover pre-play/post-play updates, acknowledgement boundaries, never-played games and multiple grouped releases; grouped actions acknowledge exactly their intended observations.
<!-- AC:END -->
