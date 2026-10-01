---
id: TASK-381.14
title: 'Electron: finish metadata editing and refresh composition'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:46'
updated_date: '2026-10-01 08:54'
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
ordinal: 432000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Metadata edits can replace Details data while an editor remains open; saved overrides and refreshed image/reception evidence must keep their correct ownership.

Owns 15 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/CacheTimestampBoundaryTests.cs
- CacheTimestampBoundaryTests.Cache_rejects_unspecified_timestamp_without_inserting_or_overwriting [pending at split]

tests/Winnow.Tests/GameDetailsViewModelTests.cs
- GameDetailsViewModelTests.Reduced_motion_reaches_the_panel [pending at split]

tests/Winnow.Tests/GameMetadataEditorViewModelTests.cs
- GameMetadataEditorViewModelTests.The_menu_row_keeps_one_face [pending at split]
- GameMetadataEditorViewModelTests.The_section_closes_itself [pending at split]
- GameMetadataEditorViewModelTests.Choosing_an_open_row_again_keeps_the_section_and_the_drafts_in_it [pending at split]

tests/Winnow.Tests/MetadataEditorModalTests.cs
- MetadataEditorModalTests.The_editor_writes_the_work_the_tile_resolves_to [pending at split]
- MetadataEditorModalTests.A_field_can_be_handed_back_to_automatic_from_the_modal [pending at split]
- MetadataEditorModalTests.A_saved_name_reaches_the_tile_and_the_headline_without_touching_the_drafts [pending at split]
- MetadataEditorModalTests.A_saved_name_moves_the_game_in_the_sort_order [pending at split]
- MetadataEditorModalTests.No_service_means_no_editor [pending at split]
- MetadataEditorModalTests.No_picker_means_the_url_route_only [pending at split]

tests/Winnow.Tests/ReceptionImageRefreshTests.cs
- ReceptionImageRefreshTests.Existing_enriched_library_refreshes_old_image_payload_and_warm_sync_is_a_noop [pending at split]

tests/Winnow.Tests/WorkImageMetadataWriterTests.cs
- WorkImageMetadataWriterTests.Metadata_only_changes_are_saved_and_unchanged_runs_are_noops [pending at split]

tests/Winnow.Ui.Tests/DetailsRefreshParityTests.cs
- DetailsRefreshParityTests.Saving_metadata_refreshes_visible_facts_and_year_rules_without_replacing_other_drafts [partial at split]
- DetailsRefreshParityTests.Background_reads_refresh_updates_history_art_and_reception_while_journal_drafts_survive [pending at split]

Batch boundary: the user authorized TASK-381.11 through TASK-381.20 in order. Keep one implementation task active, verify and commit each milestone, then continue to the next task. Pause for review after TASK-381.20.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original editor validation, save/cancel/conflict, metadata precedence and refresh contracts retain drafts and attribution on both surfaces.
- [x] #2 Details, reception and image metadata update from the correct source/revision; timestamp boundaries and retired reads cannot publish stale data.
- [x] #3 All 15 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the user-authorized batch through TASK-381.20, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Audit the fifteen frozen metadata editing, refresh, timestamp and image-writer contracts against current backend and Electron tests. 2. Correct demonstrated gaps and add exact equivalent coverage, preserving draft ownership and refresh on desktop/fullscreen. 3. Run source/backend, component/live API and isolated native checks; inspect changed presentation and record checkpoint 69 with per-method migration evidence. 4. Commit the verified milestone and continue to TASK-381.15.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after IGDB matching milestone 43649dc3 and completion 775e0bcc. This is fourth of the ten authorized sequential tasks.

Audit confirmed a missing optional metadata-service seam: Details construction required the editor service. Added availability to metadata state and safe unavailable mutations while preserving ordinary Details reads. Renderer is hiding only explicit unavailability, restoring the constant Edit details tooltip and gating native file selection on its picker capability. Existing save/draft logic is retained. Native verification is being expanded from year-only to the original summary/publisher/year matrix and background refresh with focused drafts and update rows.

Checkpoint 69: all 3699 component/live API cases pass across 182 files in 92.29s; 27 original source and 9 new HTTP cases pass; 16 distinct native cases pass with two final accessibility reruns. Final build/typecheck, formatting and migration audit pass. Fixed a real compact reception accessibility gap; adjusted the older header locator to its aggregate name while preserving the ordering assertion. Screenshots inspected on both surfaces. Inventory now 1439 ported,650 retained-backend,32 framework-specific,255 pending,59 partial. Native controller input is simulated.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Preserved metadata drafts, field ownership, sorted and filtered library projections, and focused Details during real background publication on desktop/fullscreen. Added optional metadata-service availability, URL-only picker capability handling, stable editor copy and reception accessibility. Twelve source methods ported and three retained with executed evidence. Verification and limits: docs/spikes/2026-09-28-electron-parity/checkpoint-sixty-nine.md.
<!-- SECTION:FINAL_SUMMARY:END -->
