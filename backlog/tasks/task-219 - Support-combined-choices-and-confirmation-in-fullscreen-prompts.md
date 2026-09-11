---
id: TASK-219
title: Support combined choices and confirmation in fullscreen prompts
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Views/Fullscreen/FullscreenContext.cs:258'
  - 'src/Winnow.App/ViewModels/LibraryViewModel.cs:2128'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 250000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R31. Evidence: Source verified. Add-to-list prompts expose existing list choices plus a new-list input/confirm action. FullscreenContext renders Confirm only when HasChoices is false. Once a list exists, fullscreen shows the input but no submit action. It also navigates Back unconditionally after callbacks, even when a callback retains the prompt after failure. Fullscreen cannot create another list from library/details/feed Add to list, and failed operations can lose their correction context.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Fullscreen renders every applicable prompt action, including choices together with editable input and confirmation.
- [ ] #2 Busy, validation, error and success state determine dismissal and focus restoration; a failed save retains actionable context.
- [ ] #3 Headless pointer/controller tests cover existing-list plus new-list creation from library/details/feed and a failed save; desktop behavior remains consistent.
<!-- AC:END -->
