---
id: TASK-220
title: Share year-filter validation between desktop and fullscreen
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 06:37'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Views/Fullscreen/FullscreenBrowsePage.cs:990'
  - 'src/Winnow.App/ViewModels/Filters/FilterPanelViewModel.cs:465'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: low
type: bug
ordinal: 251000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R32. Evidence: Source verified. FullscreenBrowsePage accepts years 1..9999, whereas FilterPanelViewModel accepts only four-digit years 1000..9999. Fullscreen accepts 999 and applies it, but the shared parser turns it into no bound. An accepted filter silently has no effect. The same filter semantics should not have separate parsers in presentation code.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 One validation/parser contract governs year filters on desktop and fullscreen, including the accepted range and reversed bounds.
- [x] #2 Invalid input cannot be silently accepted as an absent bound; each surface provides consistent actionable feedback.
- [x] #3 Tests cover 999, 1000, 9999, invalid text, empty bounds and reversed ranges through both application paths.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Use one release-year range parser and validation message for both surfaces. Preserve the last valid desktop range while the user edits invalid or reversed text, show actionable feedback, and refuse applying an invalid fullscreen draft. Cover 999, 1000, 9999, invalid and empty input, reversed ranges and recovery through shared-model and headless desktop/fullscreen tests. Update the narrow year-filter documentation and run focused serialized tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented one ReleaseYearRange parser and shared validation message. Desktop retains its last valid range during invalid/reversed edits and shows feedback; fullscreen refuses invalid Apply without changing the library. Headless year/browse tests passed 36 cases including 999, 1000, 9999, whitespace, invalid/empty input and reversed ranges (ui220-year-parity.trx). Existing filter/list regressions passed 47 cases (ui220-filter-regression.trx). Updated the release-year paragraph in section 11. Awaiting coordinator integration/finalization.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
ReleaseYearRange now governs both surfaces, accepts only 1000 through 9999, rejects reversed bounds and preserves the previously applied range while invalid text is corrected. Visible feedback and fullscreen Apply gating were verified through actual fields and controller actions: 36 headless tests passed in tests/Winnow.Ui.Tests/TestResults/ui220-year-parity.trx and 47 filter/list regressions passed in tests/Winnow.Tests/TestResults/ui220-filter-regression.trx. Updated the section 11 release-year contract; physical-controller and pixel QA were not performed.
<!-- SECTION:FINAL_SUMMARY:END -->
