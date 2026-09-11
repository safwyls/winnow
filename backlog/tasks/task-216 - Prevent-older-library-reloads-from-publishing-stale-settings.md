---
id: TASK-216
title: Prevent older library reloads from publishing stale settings
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 All library refresh triggers use one coordinated publication policy that prevents superseded requests from publishing older settings or data.
- [ ] #2 Controlled reverse-completion tests cover maturity/account/non-game preferences and background/manual reload overlap, including cancellation/disposal.
- [ ] #3 Desktop/fullscreen tiles, counts and open context agree with the winning snapshot, with focus/selection preserved where valid.
<!-- AC:END -->
