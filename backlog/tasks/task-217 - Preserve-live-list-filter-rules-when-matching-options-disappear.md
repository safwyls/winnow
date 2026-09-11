---
id: TASK-217
title: Preserve live-list filter rules when matching options disappear
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Saved/current filter criteria survive absent option rows and retain restrictive semantics, with unavailable selected values represented clearly.
- [ ] #2 Rail counts, grid results and edited-state detection use equivalent filter semantics; opening/saving an unchanged list cannot broaden it.
- [ ] #3 Tests cover absent stores/facets after account scope, hide/remove and reload operations on desktop and fullscreen.
<!-- AC:END -->
