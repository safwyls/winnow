---
id: TASK-221
title: Reuse session-note validation and save behavior across Activity and Details
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:07'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.App/Views/Fullscreen/FullscreenActivityPage.cs:251'
  - 'src/Winnow.App/ViewModels/GameJournalViewModel.cs:173'
  - 'tests/Winnow.Ui.Tests/FullscreenActivityTests.cs:93'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 252000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R33. Evidence: Source verified. FullscreenActivityPage writes notes directly to the repository, permits empty text/no rating, preserves whitespace and lacks the details editor's save-busy guard. GameJournalViewModel trims and rejects an empty edit. The Activity note textbox also has no explicit automation name, and the Activity editor does not display the selected rating consistently. The same journal operation behaves differently by entry point and can race or create empty content. Sharing application behavior does not require merging the separate layouts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Activity and Details use shared note validation/persistence semantics for trimming, empty text/rating and saving state.
- [ ] #2 Delayed or failed saves retain the draft and give consistent error/busy behavior without duplicate writes.
- [ ] #3 Applicable desktop/fullscreen tests cover empty/whitespace edits, selected ratings and delayed failures; the Activity editor has an accessible name and reachable save/back actions.
<!-- AC:END -->
