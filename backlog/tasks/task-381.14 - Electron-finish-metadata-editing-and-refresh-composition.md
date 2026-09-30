---
id: TASK-381.14
title: 'Electron: finish metadata editing and refresh composition'
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

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original editor validation, save/cancel/conflict, metadata precedence and refresh contracts retain drafts and attribution on both surfaces.
- [ ] #2 Details, reception and image metadata update from the correct source/revision; timestamp boundaries and retired reads cannot publish stale data.
- [ ] #3 All 15 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
