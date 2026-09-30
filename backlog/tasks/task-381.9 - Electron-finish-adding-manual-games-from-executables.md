---
id: TASK-381.9
title: 'Electron: finish adding manual games from executables'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:45'
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
- [ ] #1 All original executable/manual-game success, validation, duplicate/conflict and failure cases pass through the production UI/API boundary.
- [ ] #2 Desktop and fullscreen preserve field drafts, deliberate save/cancel actions and invoking focus; isolated fixtures never execute real games or mutate launcher files.
- [ ] #3 All 8 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
