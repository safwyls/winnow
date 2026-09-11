---
id: TASK-196
title: Fence metadata observations when an IGDB mapping changes
status: To Do
assignee: []
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 05:06'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Data/Repositories/WorkIgdbPinRepository.cs:88'
  - 'src/Winnow.App/Services/FacetSyncService.cs:43'
  - 'src/Winnow.App/Services/ReceptionSyncService.cs:68'
  - 'src/Winnow.App/Services/GameRefetchService.cs:192'
  - 'src/Winnow.Enrich.Igdb/IgdbMaturitySync.cs:103'
  - 'src/Winnow.App/Services/LifecycleSyncService.cs:24'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 227000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R08. Evidence: Source verified. Changing a pin updates scalar work fields without coherently retiring IGDB-derived facets, maturity, artwork, reception and lifecycle observations. Independent sync passes capture a work/IGDB pair and later write against the captured work/release without validating its current IGDB mapping. An old in-flight response can overwrite a corrected mapping's projections, and an empty maturity result can preserve old ratings. A user correction can leave the game classified, filtered or illustrated as the old game. Maturity classification is especially sensitive to stale mapping data. Atomic pin persistence alone does not solve asynchronous observation races.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A mapping transition retires or versions all affected IGDB-derived projections without discarding independent user/store/plugin observations.
- [ ] #2 Every in-flight IGDB-derived write validates its expected mapping generation; late responses from the previous mapping are ignored.
- [ ] #3 Delayed-response and empty-result regressions cover facets, maturity, art, ratings and lifecycle, with coherent refresh and filter behavior on desktop and fullscreen.
<!-- AC:END -->
