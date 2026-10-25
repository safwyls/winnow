---
id: TASK-381.13
title: 'Electron: finish IGDB matching and candidate presentation'
status: Done
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 08:27'
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

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The original matching modal and candidate-row matrices preserve geometry, keyboard/controller routes, search results and confirmation behavior.
- [x] #2 Observation isolation and mapping/GamesDB refresh maintain attribution, stale-write protection and immediate consistent library/Details updates on both surfaces.
- [x] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [x] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the eighteen frozen IGDB assignment, candidate layout, observation isolation and refresh contracts against production Electron/backend behavior and current tests. 2. Implement only demonstrated gaps and add equivalent tests preserving the original fixtures and assertions on both surfaces. 3. Run focused backend/component and native checks, including mapping/artwork and immediate Details/library refresh; record checkpoint 68 and per-method migration evidence. 4. Commit the verified milestone and continue to TASK-381.14.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after merge review milestone 61746e74 and completion 4bc01656. Batch tasks 381.11 and 381.12 are done; 381.13 is third of ten.

Audit found missing Settings candidate covers, wrapping platform lines and incorrect candidate/list sizing. Implementing a shared source-sized candidate row for Details and manual-game search. Existing assignment and claim flows are being tested with exact Prey fixtures. The optional backend assignment seam lacked availability in its response; adding an optional available flag to IGDB state, defaulting to supported for older responses. Missing credentials do not remove the matching control. Native coverage will use actual HTTP, repositories and publication with external-service substitutes.

All 42 original source cases and 10 new HTTP cases pass. Native matching matrix passes 30/30; final structural-refusal copy rerun passes 2/2 with the exact source sentence and conflict recovery retained. Desktop/fullscreen candidate screenshots reviewed. Full component/live API gate currently 3663 passed, 2 failed: shared Details restores an active journal editor after navigation but saves back to Overview. Correcting that lifecycle distinction before finalizing.

Final gate passes 3667/3667 component/live API cases across181 files (94.06s), with no skips; build/typecheck, formatting and migration audit pass. Draft regression fixed with original assertions retained and History-origin coverage added. Checkpoint68 records source42, HTTP10, native30 plus final refusal2 reruns and inspected desktop/fullscreen screenshots. Inventory:15 ported and3 retained backend for this task;1427 ported647 retained32 framework-specific,269 pending60 partial overall.

Milestone committed as 43649dc3. All acceptance criteria and verification are complete; proceeding to TASK-381.14, fourth of the ten authorized tasks.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed IGDB candidate layout and matching parity on desktop/fullscreen, optional-service visibility, persisted assignment/claim/artwork behavior and live mapping/GamesDB refresh. All 18 source contracts accounted for. Verified 3,667 component/API, 42 original, 10 new HTTP and 30 native cases; both final affected refusal reruns passed. See checkpoint-sixty-eight.md. Continue to TASK-381.14 within the authorized batch.
<!-- SECTION:FINAL_SUMMARY:END -->
