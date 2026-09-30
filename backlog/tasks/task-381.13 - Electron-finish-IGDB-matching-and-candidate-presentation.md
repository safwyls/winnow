---
id: TASK-381.13
title: 'Electron: finish IGDB matching and candidate presentation'
status: To Do
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
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
ordinal: 431000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Manual matching must show enough candidate evidence while keeping observations and saved mappings isolated from unsaved choices.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/IgdbAssignmentModalTests.cs
- IgdbAssignmentModalTests.Choosing_a_candidate_rewrites_the_metadata_and_the_cover_and_preserves_the_tab [pending at split]
- IgdbAssignmentModalTests.A_second_game_cannot_claim_an_entry_another_game_already_holds [pending at split]
- IgdbAssignmentModalTests.The_assignment_can_be_cleared_and_the_pin_goes_with_it [pending at split]
- IgdbAssignmentModalTests.No_service_means_no_control [pending at split]
- IgdbAssignmentModalTests.Accepting_the_offer_writes_a_same_game_link_and_pins_nothing [pending at split]
- IgdbAssignmentModalTests.Declining_the_offer_leaves_both_games_as_they_were [pending at split]
- IgdbAssignmentModalTests.A_refused_details_link_stays_in_the_modal [pending at split]
- IgdbAssignmentModalTests.A_steam_owned_game_takes_the_pinned_art_and_gives_the_capsule_back [pending at split]
- IgdbAssignmentModalTests.A_pinned_entry_with_no_cover_leaves_the_capsule_in_place [pending at split]

tests/Winnow.Tests/IgdbCandidateRowLayoutTests.cs
- IgdbCandidateRowLayoutTests.Every_child_of_the_candidate_row_declares_its_column [pending at split]
- IgdbCandidateRowLayoutTests.The_candidate_row_gives_the_text_the_star_column_and_the_button_the_last [pending at split]
- IgdbCandidateRowLayoutTests.The_detail_line_is_a_grid_and_not_a_horizontal_stack [pending at split]
- IgdbCandidateRowLayoutTests.The_platforms_trim_inside_the_star_column_and_keep_their_full_value [pending at split]

tests/Winnow.Tests/IgdbObservationIsolationTests.cs
- IgdbObservationIsolationTests.An_old_response_cannot_write_after_reassignment_even_when_the_same_id_returns [pending at split]
- IgdbObservationIsolationTests.An_explicit_empty_rating_response_retires_igdb_evidence_but_an_unavailable_response_preserves_it [pending at split]
- IgdbObservationIsolationTests.A_mapping_transition_retires_every_igdb_projection_and_keeps_independent_observations [pending at split]

tests/Winnow.Ui.Tests/GamesDbRefreshCompositionTests.cs
- GamesDbRefreshCompositionTests.Production_refresh_pipeline_publishes_qualified_identity_links_on_both_surfaces [pending at split]

tests/Winnow.Ui.Tests/IgdbMappingRefreshTests.cs
- IgdbMappingRefreshTests.Correcting_the_mapping_refreshes_open_details_and_removes_old_visibility_evidence [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The original matching modal and candidate-row matrices preserve geometry, keyboard/controller routes, search results and confirmation behavior.
- [ ] #2 Observation isolation and mapping/GamesDB refresh maintain attribution, stale-write protection and immediate consistent library/Details updates on both surfaces.
- [ ] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
