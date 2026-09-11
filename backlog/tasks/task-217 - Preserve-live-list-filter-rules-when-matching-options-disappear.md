---
id: TASK-217
title: Preserve live-list filter rules when matching options disappear
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 07:40'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/ViewModels/Filters/FilterGroupViewModel.cs:83'
  - 'src/Winnow.App/ViewModels/Filters/FilterPanelViewModel.cs:122'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:2044'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:2663'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 248000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R29. Evidence: Source verified. FilterGroupViewModel retains selections only when their keys occur in the current option inventory, which is derived from visible tiles. A saved Steam-only live list opened when only GOG tiles are visible loses the steam criterion. Its rail count still uses the original saved filter and can show zero while its grid shows GOG games. Saving can persist that accidental broadening. Filter definitions are incorrectly derived from available UI choices. Missing stores and facets must remain restrictive selections even when their current count is zero.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Saved/current filter criteria survive absent option rows and retain restrictive semantics, with unavailable selected values represented clearly.
- [x] #2 Rail counts, grid results and edited-state detection use equivalent filter semantics; opening/saving an unchanged list cannot broaden it.
- [x] #3 Tests cover absent stores/facets after account scope, hide/remove and reload operations on desktop and fullscreen.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Keep selected filter keys independent of currently available options by retaining selected missing rows with zero counts and clearable state; reconstruct saved absent labels from the facet vocabulary or explicit fallback. Recompute group/year visibility without dropping selected rules. Verify live-list counts, results, edited-state and unchanged persistence across account visibility, hide/remove, metadata loss and reload using both desktop/fullscreen filter controls.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Selected absent options now remain checked and clearable with zero residual counts; saved absent facet labels resolve from the full vocabulary with an explicit unavailable fallback. Selected groups and year ranges remain accessible without matching data. Fullscreen draft/group buttons now use the same residual counts, zero-count availability and selection semantics. Fixed year-chip removal to clear the applied range. Added10 headless cases for saved/current rules after account scope with complete inventory authority, hide, removal and lost facets, plus unknown vocabulary and year-chip removal. Both real desktop FilterPanelView and fullscreen filter/group pages are exercised; unchanged live-list save retains identical persisted rules and count/grid/edited-state agree.46 UI tests passed at tests/Winnow.Ui.Tests/TestResults/ui217-filter-parity.trx and42 filter model cases passed at tests/Winnow.Tests/TestResults/ui217-filter-model.trx. Visual spec11.2 updated; no live data or physical controller used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserved missing live-list filter rules and clearable zero-count choices on desktop/fullscreen; unchanged saves cannot broaden membership. Verified46 headless and42 filter model tests.
<!-- SECTION:FINAL_SUMMARY:END -->
