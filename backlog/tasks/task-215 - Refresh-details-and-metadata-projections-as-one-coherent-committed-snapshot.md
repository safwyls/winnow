---
id: TASK-215
title: Refresh details and metadata projections as one coherent committed snapshot
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 07:07'
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
- [x] #1 Successful metadata changes invalidate every affected visible detail, sort/filter fact and list count, including summary, publisher and year.
- [x] #2 Open details refresh dependent update/history/art/rating projections coherently after background changes while preserving navigation, focus and active drafts.
- [x] #3 Desktop/fullscreen regressions cover non-name edits and new patches/sessions during open details; misleading governing text is corrected with its previous wording recorded in decisions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Keep the open details instance and its editing/navigation state while publishing refreshed tile facts, update history, tracker, reception, acquisition, artwork and journal projections as one completed detail refresh. Route every saved metadata field through the same library invalidation path and preserve journal/metadata drafts. Update fullscreen presentation on committed detail refresh while retaining focus. Cover non-name saves, new patch/session evidence, drafts and artwork with database and headless desktop/fullscreen regressions; correct the governing metadata refresh text and record displaced wording.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
All metadata fields now refresh tile/filter/list projections while preserving the details and editor instances. Open details gathers updates, acknowledgements, play history, ratings, artwork, acquisition and journal rows before applying them; journal entries and tracker retain draft/view state. Desktop and fullscreen preserve focus during row refresh; fullscreen updates its hero/current section and metadata Save/source labels now follow observable state. Added six actual metadata field-save cases and two background patch/session/draft/focus cases. Updated section 10.10 and recorded displaced wording in decisions. TASK-216 separately coordinates overlapping read generations.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Verified 40 headless tests in tests/Winnow.Ui.Tests/TestResults/ui215-details-parity.trx, including both surfaces and actual metadata Save input, plus 139 model/inventory regressions in tests/Winnow.Tests/TestResults/ui215-model-regression.trx. Summary, publisher and year edits immediately update details and year rules; new patches/sessions refresh open projections while unfinished notes remain. Source reads are gathered before presentation updates; cross-request ordering is handled by TASK-216. No physical-controller or pixel QA was performed.
<!-- SECTION:FINAL_SUMMARY:END -->
