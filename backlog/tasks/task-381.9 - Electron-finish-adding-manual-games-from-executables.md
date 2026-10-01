---
id: TASK-381.9
title: 'Electron: finish adding manual games from executables'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:45'
updated_date: '2026-10-01 03:47'
labels:
  - electron
  - parity
  - review-checkpoint
dependencies: []
references:
  - docs/spikes/2026-09-28-electron-parity/test-inventory.json
documentation:
  - src/Winnow.Electron/README.md
  - design-system.md
parent_task_id: TASK-381
priority: high
type: task
ordinal: 427000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Executable-based manual entries require validated paths, metadata and identity handling before they can be safely edited or launched.

Owns 8 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ManualGameFromExecutableTests.cs
- ManualGameFromExecutableTests.Browsing_fills_the_executable_proposes_the_title_and_searches [partial at split]
- ManualGameFromExecutableTests.A_cancelled_dialog_changes_nothing [partial at split]
- ManualGameFromExecutableTests.Choosing_a_candidate_fills_the_form_and_writes_nothing [partial at split]
- ManualGameFromExecutableTests.Correcting_the_title_and_searching_again_overrides_the_proposal [partial at split]
- ManualGameFromExecutableTests.Dismissing_the_proposal_leaves_the_form_as_typed [partial at split]
- ManualGameFromExecutableTests.An_executable_that_yields_nothing_still_saves_by_hand [partial at split]
- ManualGameFromExecutableTests.The_chosen_executable_is_the_one_stored [partial at split]
- ManualGameFromExecutableTests.A_second_browse_replaces_its_own_guess_but_not_the_users [partial at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 All original executable/manual-game success, validation, duplicate/conflict and failure cases pass through the production UI/API boundary.
- [x] #2 Desktop and fullscreen preserve field drafts, deliberate save/cancel actions and invoking focus; isolated fixtures never execute real games or mutate launcher files.
- [x] #3 All 8 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Compare the eight frozen executable/manual-game source contracts with current component, API and native coverage; preserve the original assertions.
2. Close any scoped draft, picker, metadata or persistence gaps in the shared manual editor and cover desktop and fullscreen independently.
3. Execute focused component/live API and isolated native tests, inspect visual evidence, update the eight inventory mappings and current documentation.
4. Run the relevant regression gates, record the milestone commit and verification summary, then stop for user review.

Native visual review also covers the executable chooser itself: restore visible controller glyph hints where its full-screen layer hides the shell hints, then rerun the shared chooser regressions.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Source audit found two executable-flow defects: late inspection could restore a discarded draft, and a typed title matching new file facts could be reclassified as a guess and overwritten on a later browse. Shared editor now guards picker lifecycle, preserves title ownership, invalidates stale metadata searches and waits for inspection before saving. Existing real HTTP/SQLite ManualGameParityTests: 6 passed, no skips; detailed component/native verification remains in progress.

Native executable flows passed on both surfaces, with direct SQLite assertions before and after Save. Visual review found the fullscreen chooser hides the shell hints without its own hints; added D-pad/A/B glyphs and a filename-keyboard Y hint. Existing shared chooser tests will verify the same change for open, save and replacement states. Exact source fixtures and 26 additional async/ownership/note regressions pass in 42 component cases; final broad gate follows native verification.

Review milestone: 0fe78fe6 (Finish Electron manual executable flows and chooser hints). All four acceptance criteria and the review checkpoint definition of done are verified. Stopping at TASK-381.9; TASK-381.10 remains To Do until the user prompts continuation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed all eight executable/manual-game source contracts on desktop and fullscreen. Fixed late picker responses restoring discarded drafts, stale metadata results, save during inspection and accidental ownership of a typed title matching file facts. Added visible fullscreen chooser controller glyphs, including filename keyboard and replacement Back hints.

Verification: build/typecheck and formatting pass; 3,519 component/live API tests across 177 files pass in 83.16 seconds; 22 distinct native Electron cases pass; six existing real HTTP/SQLite ManualGameParityTests pass. Native SQL proves no rows before Save and exact executable/install/platform persistence after Save. Controller input and external metadata replies are simulated; physical devices and complete migration gates remain outside this checkpoint.

Evidence: docs/spikes/2026-09-28-electron-parity/checkpoint-sixty-four.md. Inventory: 1,372 ported, 628 retained backend, 32 framework-specific, 338 pending and 65 partial; 403 unresolved remain. TASK-381.10 remains unstarted. Pause here for user review.
<!-- SECTION:FINAL_SUMMARY:END -->
