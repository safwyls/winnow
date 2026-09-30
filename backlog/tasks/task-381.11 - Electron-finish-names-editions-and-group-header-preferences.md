---
id: TASK-381.11
title: 'Electron: finish names, editions and group-header preferences'
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
ordinal: 429000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
User-authored names and preferred edition headers must retain precedence when grouping, metadata or store membership changes.

Owns 19 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/GroupHeaderPreferenceTests.cs
- GroupHeaderPreferenceTests.Saved_store_changes_header_and_primary_route_but_preserves_metadata_and_identity [pending at split]
- GroupHeaderPreferenceTests.Further_links_inherit_preference_and_undo_retains_original_identity_history [pending at split]
- GroupHeaderPreferenceTests.Unavailable_preference_falls_back_and_can_be_reset_without_losing_the_saved_choice [pending at split]
- GroupHeaderPreferenceTests.Latest_explicit_choice_wins_when_groups_join_and_pending_queue_default_is_independent [pending at split]
- GroupHeaderPreferenceTests.Expansion_relations_and_unlinked_works_cannot_receive_group_preferences [pending at split]

tests/Winnow.Tests/MergeMemberLabelTests.cs
- MergeMemberLabelTests.A_title_that_names_one_member_is_the_whole_label [pending at split]
- MergeMemberLabelTests.Two_members_with_one_title_take_their_stores [pending at split]
- MergeMemberLabelTests.Two_members_with_one_title_and_one_store_take_their_years [pending at split]
- MergeMemberLabelTests.Members_a_storefront_describes_identically_take_a_position [pending at split]
- MergeMemberLabelTests.No_label_carries_an_entry_number [pending at split]

tests/Winnow.Tests/UserSetNameTests.cs
- UserSetNameTests.A_user_set_name_reaches_the_grid_tile [pending at split]
- UserSetNameTests.A_user_set_name_reaches_the_details_modal_headline [pending at split]
- UserSetNameTests.A_user_set_name_reaches_search_and_the_title_sort [pending at split]
- UserSetNameTests.A_user_set_name_reaches_the_merges_queue [pending at split]
- UserSetNameTests.A_game_the_user_has_not_named_keeps_the_title_it_had [pending at split]
- UserSetNameTests.Handing_the_name_back_to_automatic_marks_it_provisional_again [pending at split]

tests/Winnow.Ui.Tests/EditionIdentityPresentationTests.cs
- EditionIdentityPresentationTests.Acquired_native_evidence_updates_library_queue_and_details_and_can_be_separated [partial at split]

tests/Winnow.Ui.Tests/GroupHeaderPreferenceUiTests.cs
- GroupHeaderPreferenceUiTests.Desktop_header_selector_saves_and_automatic_restores_without_relinking [pending at split]
- GroupHeaderPreferenceUiTests.Fullscreen_saved_group_offers_equivalent_store_choices_and_reset [pending at split]

Review boundary: work on this task only after the user prompts continuation from the previous checkpoint. Keep one implementation task active. On completion, report the changes, checks, limitations and next task, then stop until the user prompts continuation. Do not automatically begin a dependency or the next task.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Original user-name, edition/member-label and automatic/preferred-header rules survive reload, regrouping and metadata refresh without overwriting user intent.
- [ ] #2 Desktop and fullscreen group-header controls show the current choice, preserve unrelated records and restore the correct focus after save or refusal.
- [ ] #3 All 19 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [ ] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Stop at this task boundary and wait for the user to prompt continuation before beginning another task.
<!-- DOD:END -->
