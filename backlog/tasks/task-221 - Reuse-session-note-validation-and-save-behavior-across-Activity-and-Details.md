---
id: TASK-221
title: Reuse session-note validation and save behavior across Activity and Details
status: Done
assignee:
  - '@avalonia-ui'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 06:41'
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
- [x] #1 Activity and Details use shared note validation/persistence semantics for trimming, empty text/rating and saving state.
- [x] #2 Delayed or failed saves retain the draft and give consistent error/busy behavior without duplicate writes.
- [x] #3 Applicable desktop/fullscreen tests cover empty/whitespace edits, selected ratings and delayed failures; the Activity editor has an accessible name and reachable save/back actions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Reuse JournalEntryViewModel for fullscreen Activity session-note drafts instead of direct repository writes. Preserve shared trimming, nonempty validation, rating and busy/error semantics; show current rating and an accessible editor name. Guard both fullscreen editor pages against conflicting input and late navigation after disposal. Add parity, delayed-failure, empty-edit and persistence tests across desktop Details/fullscreen Details/Activity, update the narrow journal documentation and run focused serialized tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
The Activity editor now uses JournalEntryViewModel; all three presentation paths share whitespace trimming, nonempty note/rating validation, pending-write guards and retryable errors. Fullscreen shows the selected rating and explicitly names the journal textbox; disposed editors cannot navigate after late completion. Added 11 keyboard/controller parity cases for new/existing notes, empty drafts, failures, retries, duplicate attempts and disposal. Updated design-system section 10.1.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented shared journal behavior across desktop Details, fullscreen Details and fullscreen Activity. Verified 30 headless interaction/regression tests in tests/Winnow.Ui.Tests/TestResults/ui221-journal-parity.trx and 2 journal model regressions in tests/Winnow.Tests/TestResults/ui221-journal-regression.trx. Drafts survive failures; pending saves disable conflicts; successful writes trim and retain ratings. No physical-controller or pixel QA was performed.
<!-- SECTION:FINAL_SUMMARY:END -->
