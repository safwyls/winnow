---
id: TASK-196
title: Fence metadata observations when an IGDB mapping changes
status: Done
assignee:
  - '@data-layer'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 07:16'
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
- [x] #1 A mapping transition retires or versions all affected IGDB-derived projections without discarding independent user/store/plugin observations.
- [x] #2 Every in-flight IGDB-derived write validates its expected mapping generation; late responses from the previous mapping are ignored.
- [x] #3 Delayed-response and empty-result regressions cover facets, maturity, art, ratings and lifecycle, with coherent refresh and filter behavior on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a captured IGDB mapping/revision token and an atomic persistence guard that shares one ambient transaction with repository callbacks; provider requests stay outside it. 2. Retire old IGDB-only scalar, facet, maturity, artwork and reception projections during mapping changes, and filter retained lifecycle history by the current source identity. 3. Fence automatic metadata, assignment, refetch, facet, maturity, reception and lifecycle writes against captured revisions; make successful empty maturity responses distinct from unavailable results. 4. Carry actionable correction and stale-choice outcomes through both shared presentation paths. 5. Add delayed-response, explicit-empty, rollback and provider-preservation regressions, update governing docs/inventory, and run focused Release and headless checks through the shared build helper.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Validated 281 focused Release tests across IGDB observation isolation/writer, automatic enrichment, refetch, facets, maturity, lifecycle, art/reception, assignment and identity-read inventory; 18 Avalonia headless tests covered desktop/fullscreen mapping refresh, manual correction and details refresh. Added 14 delayed-response cases across seven paths including A-to-B-to-A, ambient/local rollback and cancellation, concurrent writer exclusion, four projection-delete failures, explicit-empty versus unavailable ratings, independent-provider retention, immediate corrected-mapping schedules and refetch. Migration verification passed 34 hashes; scoped diff check passed. No new migration. Lifecycle rows remain raw history but only matching non-null IGDB source IDs are applicable. Full pin intentionally replaces chosen scalar fields; typed correction preserves user/unknown-source fields.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Mapping changes atomically retire IGDB projections, and seven asynchronous paths fence writes against captured mapping revisions. Known mappings now fetch their own metadata; successful empty ratings retire stale IGDB evidence, and corrected identities can refetch immediately. Verified 281 focused and 18 desktop/fullscreen tests, plus 34 migration hashes. Root owns full integration under TASK-227.
<!-- SECTION:FINAL_SUMMARY:END -->
