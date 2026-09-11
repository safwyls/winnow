---
id: TASK-216
title: Prevent older library reloads from publishing stale settings
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 08:31'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:822'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:1231'
  - 'src/Winnow.App/ViewModels/DisplaySettingsViewModel.cs:239'
  - 'src/Winnow.App/ViewModels/MainWindowViewModel.cs:112'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 247000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R28. Evidence: Source-verified race; not runtime reproduced. LoadLibraryAsync captures preferences and reads on Task.Run, then publishes without serialization or a generation check. Display preference changes, background refresh and internal operations can initiate overlapping loads. A slower earlier permissive load can replace a newer restrictive result, including maturity/non-game visibility. The displayed library and counts can disagree with current preferences. Disabling one generated command does not cover direct internal calls.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All library refresh triggers use one coordinated publication policy that prevents superseded requests from publishing older settings or data.
- [x] #2 Controlled reverse-completion tests cover maturity/account/non-game preferences and background/manual reload overlap, including cancellation/disposal.
- [x] #3 Desktop/fullscreen tiles, counts and open context agree with the winning snapshot, with focus/selection preserved where valid.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Coordinate all library loads through one request generation and cancellation scope, capturing presentation settings before IO and rejecting superseded publication. Retire pending loads and detail opens on disposal or close; close context that the winning visibility snapshot excludes. Preserve selected ownership and viewport where valid. Add controlled reverse-completion tests for settings/account changes, background/manual overlap, ignored cancellation and disposed fullscreen libraries, then verify both presentation paths and document the shared refresh policy.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Library generation guards apply to public and internal reloads; all caches and an open detail snapshot stay local until a current, uncancelled publication. Explicit close, newer detail selections and disposal retire late opens; a concurrently published library triggers a reread or suppresses a now-hidden ownership. Added 14 controlled headless reverse-completion cases using real temporary SQLite plus delayed readers that ignore cancellation. Desktop uses the production CoverWall/GameTileView and fullscreen uses FullscreenBrowsePage/FullscreenDetailsPage. Existing details focus/draft/install regressions remain green. Verified 50 UI tests in tests/Winnow.Ui.Tests/TestResults/ui216-refresh-parity.trx and 57 model/inventory tests in tests/Winnow.Tests/TestResults/ui216-model-regression.trx. Architecture 5.1 and visual spec 10.10 record the shared policy. No production host or physical controller was used.

Integration follow-up corrected FullscreenContext.Dispose: determine feed ownership independently from library ownership so an isolated library cannot dispose a borrowed shared feed, and an independent feed is released even when the library is shared. New FullscreenContextLifetimeTests cover all three mixed/independent combinations and assert the desktop shared library and feed continue publishing afterward. This resolves process-wide preview contamination exposed by the full suite. Focused UI suite passed 51/51 in tests/Winnow.Ui.Tests/TestResults/ui204-lifetime-publication.trx, including all design preview, ordering, production publication and Activity recovery cases.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Prevented stale library reloads and detail opens from replacing current visibility or user intent. Verified 50 headless desktop/fullscreen tests and 57 model/inventory tests, including maturity, account, non-game, cancellation and disposal races.
<!-- SECTION:FINAL_SUMMARY:END -->
