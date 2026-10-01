---
id: TASK-381.35
title: 'Electron: finish repository boundaries and preview coverage'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-01 22:27'
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
ordinal: 453000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Framework-specific design previews and repository composition rules need a deliberate Electron disposition so the migration neither drops safeguards nor preserves obsolete build assumptions.

Owns 12 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/Enforcement/RepositoryHygieneTests.cs
- RepositoryHygieneTests.Exactly_one_tokens_axaml_exists_and_it_is_the_one_that_compiles [pending at split]
- RepositoryHygieneTests.The_deliberate_uses_of_hoard_are_still_there [pending at split]
- RepositoryHygieneTests.Hoard_appears_nowhere_else_except_the_compatibility_shims [pending at split]
- RepositoryHygieneTests.Directory_build_props_still_sets [pending at split]
- RepositoryHygieneTests.A_hook_refuses_direct_writes_to_backlog_markdown [pending at split]
- RepositoryHygieneTests.No_fixture_carries_an_unsanitised_steam_account_id [pending at split]

tests/Winnow.Ui.Tests/DesignTimePreviewTests.cs
- DesignTimePreviewTests.Preview_library_loads_a_tile_for_every_fabricated_game [pending at split]
- DesignTimePreviewTests.Preview_library_opens_a_populated_details_modal [pending at split]
- DesignTimePreviewTests.Preview_feed_draws_cards_from_the_loaded_library [pending at split]
- DesignTimePreviewTests.Preview_surface_attaches_and_renders [pending at split]
- DesignTimePreviewTests.Shell_preview_opens_and_populates_the_wall [pending at split]
- DesignTimePreviewTests.Details_preview_renders_the_fabricated_game [pending at split]

Batch boundary: the user authorized TASK-381.31 through TASK-381.35 in order. Keep one implementation task active and commit each milestone. This is the final task in that batch; verify and commit it, then pause for user review without beginning TASK-381.36.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original hygiene/boundary contracts have equivalent Electron/backend enforcement, retaining external API composition and all rename compatibility shims.
- [ ] #2 Each original design-time preview contract has a working isolated Electron preview/test equivalent or a justified framework-only disposition; preview validation never starts a production data host.
- [ ] #3 All 12 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, completing the authorized TASK-381.31 through TASK-381.35 batch. Stop for user review before beginning another task.
<!-- DOD:END -->
