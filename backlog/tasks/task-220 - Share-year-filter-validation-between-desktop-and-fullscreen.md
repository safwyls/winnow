---
id: TASK-220
title: Share year-filter validation between desktop and fullscreen
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
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
- [ ] #1 One validation/parser contract governs year filters on desktop and fullscreen, including the accepted range and reversed bounds.
- [ ] #2 Invalid input cannot be silently accepted as an absent bound; each surface provides consistent actionable feedback.
- [ ] #3 Tests cover 999, 1000, 9999, invalid text, empty bounds and reversed ranges through both application paths.
<!-- AC:END -->
