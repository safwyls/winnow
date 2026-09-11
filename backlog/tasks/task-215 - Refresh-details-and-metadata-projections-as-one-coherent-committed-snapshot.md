---
id: TASK-215
title: Refresh details and metadata projections as one coherent committed snapshot
status: In Progress
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 06:40'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:1550'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:1269'
  - 'src/Winnow.App/ViewModels/GameDetailsViewModel.cs:203'
  - 'src/Winnow.App/ViewModels/GameDetailsViewModel.cs:298'
  - 'design-system.md:1737'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 246000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R27. Evidence: Source verified. After metadata save, LibraryViewModel propagates only Name. Summary, publisher and year remain stale in cached tiles, details and year-based filters/lists until reload. Background reload replaces a details Tile and action links but leaves updates, tracker/history, ratings, images and journal captured when details opened. A new patch can update the badge while the Updates section still omits it. The UI combines facts from different data generations and can immediately contradict a successful edit. The code/spec justification that other edited fields are not displayed elsewhere is false.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Successful metadata changes invalidate every affected visible detail, sort/filter fact and list count, including summary, publisher and year.
- [ ] #2 Open details refresh dependent update/history/art/rating projections coherently after background changes while preserving navigation, focus and active drafts.
- [ ] #3 Desktop/fullscreen regressions cover non-name edits and new patches/sessions during open details; misleading governing text is corrected with its previous wording recorded in decisions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Keep the open details instance and its editing/navigation state while publishing refreshed tile facts, update history, tracker, reception, acquisition, artwork and journal projections as one completed detail refresh. Route every saved metadata field through the same library invalidation path and preserve journal/metadata drafts. Update fullscreen presentation on committed detail refresh while retaining focus. Cover non-name saves, new patch/session evidence, drafts and artwork with database and headless desktop/fullscreen regressions; correct the governing metadata refresh text and record displaced wording.
<!-- SECTION:PLAN:END -->
