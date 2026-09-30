---
id: TASK-381.10
title: 'Electron: finish identity projections and expansion relationships'
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
ordinal: 428000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The displayed library depends on same-game, expansion and fuzzy-match rules that must survive moving composition out of Avalonia.

Owns 19 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ExpansionLinkTests.cs
- ExpansionLinkTests.An_expansion_link_moves_no_number_anywhere [pending at split]
- ExpansionLinkTests.An_expansion_never_enters_the_coverage_sum [pending at split]
- ExpansionLinkTests.The_pack_says_what_it_extends_and_the_two_sections_stay_apart [pending at split]
- ExpansionLinkTests.Re_parenting_a_base_game_keeps_its_expansions_expansions [pending at split]

tests/Winnow.Tests/IdentityReadInventoryTests.cs
- IdentityReadInventoryTests.Every_reader_of_works_or_ownerships_is_on_the_resolve_or_the_do_not_resolve_list [pending at split]
- IdentityReadInventoryTests.The_inventory_names_no_reader_that_no_longer_exists [pending at split]
- IdentityReadInventoryTests.A_new_reader_on_neither_list_is_caught_and_named [pending at split]
- IdentityReadInventoryTests.A_new_repository_read_is_caught_as_well_as_a_new_query [pending at split]
- IdentityReadInventoryTests.Shared_SQL_constants_and_bulk_snapshot_callers_are_caught_under_their_own_names [pending at split]

tests/Winnow.Tests/IdentityReadModelTests.cs
- IdentityReadModelTests.Linking_collapses_one_tile_and_leaves_the_store_counts_alone [pending at split]
- IdentityReadModelTests.A_linked_pair_is_one_tile_under_the_primary_title_and_cover [pending at split]
- IdentityReadModelTests.The_modal_lists_the_titles_this_game_covers_with_their_own_figures [pending at split]
- IdentityReadModelTests.The_modal_shows_per_release_achievement_rows_and_never_a_blended_percentage [pending at split]
- IdentityReadModelTests.An_unsupported_release_distinguishes_unknown_progress_from_zero [pending at split]
- IdentityReadModelTests.A_game_that_covers_nothing_draws_no_coverage_section [pending at split]
- IdentityReadModelTests.Separate_retracts_one_link_and_leaves_the_rest_of_the_act [pending at split]

tests/Winnow.Tests/SoftMatchRegistrationTests.cs
- SoftMatchRegistrationTests.The_sweep_resolves_from_the_container_and_runs [pending at split]
- SoftMatchRegistrationTests.The_matcher_and_the_resolver_are_reachable_and_shared [pending at split]
- SoftMatchRegistrationTests.Registrations_defer_to_anything_already_in_the_container [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original read-model/inventory, expansion linking and soft-match registration rules hold at the shared backend boundary; ambiguous matches never auto-merge.
- [ ] #2 Both surfaces display the resulting groups and expansions with correct visibility, independent play totals and child/parent actions, including account-scoped or hidden counterparts.
- [ ] #3 All 19 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
