---
id: TASK-219
title: Support combined choices and confirmation in fullscreen prompts
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
- [x] #1 Fullscreen renders every applicable prompt action, including choices together with editable input and confirmation.
- [x] #2 Busy, validation, error and success state determine dismissal and focus restoration; a failed save retains actionable context.
- [x] #3 Headless pointer/controller tests cover existing-list plus new-list creation from library/details/feed and a failed save; desktop behavior remains consistent.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Make ActionPromptViewModel expose explicit busy, failure and completion state shared by both surfaces. Render combined choices, input and confirmation in fullscreen; retain failed/invalid prompts and restore navigation only on successful completion, with safe disposal. Exercise library/details/feed prompt creation with an existing list, delayed/failing writes and desktop parity through headless input tests. Update the narrow prompt interaction documentation and run focused serialized tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented explicit shared prompt busy/error/completion state, combined fullscreen choices/input/confirmation, retained failed drafts, and suppression of late navigation after page disposal. Headless keyboard/controller parity covers all three library/details/feed entry points plus delayed/disposed operations: 8 passed, ui219-prompts.trx. Existing list/feed regressions: 58 passed, ui219-list-regression.trx. Section 12.3 documents both surfaces. Awaiting coordinator integration/finalization.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Shared prompt state now supports existing choices together with new-list confirmation, pending-operation guards, retryable errors and safe fullscreen dismissal. Verified keyboard/controller creation from library, details and feed plus failure, retry and disposed completion: 8 headless tests passed in tests/Winnow.Ui.Tests/TestResults/ui219-prompts.trx and 58 list/feed regressions passed in tests/Winnow.Tests/TestResults/ui219-list-regression.trx. Updated design-system section 12.3; physical-controller and pixel QA were not performed.
<!-- SECTION:FINAL_SUMMARY:END -->
