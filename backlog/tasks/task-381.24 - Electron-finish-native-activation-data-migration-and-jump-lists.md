---
id: TASK-381.24
title: 'Electron: finish native activation, data migration and jump lists'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:47'
updated_date: '2026-10-01 19:22'
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
ordinal: 442000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Electron host must preserve installation identity, legacy user data and single-instance OS integrations independently of renderer behavior.

Owns 18 unresolved source contracts at migration snapshot d4b14152 (frozen Avalonia source cf45d9f1127243a987d3cf6e664a32fc767ecb67). Pending/partial is an evidence gap, not proof that the feature is absent: inspect existing implementation and replacement assertions before rewriting working behavior. Preserve the full original fixtures and edge cases; a matching test name alone does not establish parity.

tests/Winnow.Tests/ApplicationBuildInfoTests.cs
- ApplicationBuildInfoTests.Preserves_release_identity_and_handles_builds_without_git [pending at split]
- ApplicationBuildInfoTests.Settings_reads_the_application_assembly_identity [pending at split]

tests/Winnow.Tests/DataDirectoryOverrideTests.cs
- DataDirectoryOverrideTests.The_composition_root_puts_the_database_covers_and_themes_in_the_override [partial at split]

tests/Winnow.Tests/JumpListIconsTests.cs
- JumpListIconsTests.Icon_contains_six_square_png_frames_from_the_center_of_the_cover [pending at split]
- JumpListIconsTests.Icons_are_cached_in_the_selected_directory_and_missing_art_falls_back [pending at split]

tests/Winnow.Tests/SingleInstanceGuardTests.cs
- SingleInstanceGuardTests.A_first_copy_acquires_and_a_second_copy_on_the_same_directory_is_refused [pending at split]
- SingleInstanceGuardTests.Releasing_the_guard_lets_the_next_launch_through [pending at split]
- SingleInstanceGuardTests.A_second_copy_against_a_different_data_directory_is_not_the_failure [pending at split]
- SingleInstanceGuardTests.Two_spellings_of_one_directory_are_still_one_instance [pending at split]

tests/Winnow.Tests/WindowsActivationSecurityTests.cs
- WindowsActivationSecurityTests.Activation_objects_belong_to_user_and_exclude_other_accounts_and_network_logons [pending at split]

tests/Winnow.Tests/WindowsJumpListTests.cs
- WindowsJumpListTests.Isolated_directories_have_distinct_stable_taskbar_identities [pending at split]
- WindowsJumpListTests.Windows_arguments_preserve_spaces_quotes_and_trailing_backslashes [pending at split]
- WindowsJumpListTests.Every_destination_carries_its_isolated_data_directory [pending at split]
- WindowsJumpListTests.Native_publish_smoke_uses_only_an_isolated_app_id [pending at split]

tests/Winnow.Tests/WinnowDataLocationTests.cs
- LegacyThemeIdTests.The_pre_rename_theme_id_still_resolves_to_the_house_theme [pending at split]
- LegacyThemeIdTests.The_alias_resolves_through_the_service_catalogue_too [pending at split]
- LegacyThemeIdTests.A_user_theme_that_claims_the_old_id_wins_it [pending at split]
- LegacyThemeIdTests.An_id_that_is_neither_still_falls_through_to_the_default [pending at split]

Batch boundary: the user authorized TASK-381.21 through TASK-381.30 in order. Keep one implementation task active, verify and commit each milestone, then continue. Pause for review after TASK-381.30.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Original data-directory override/legacy migration, activation security and single-instance rules preserve the existing library and refuse invalid startup paths without fallback.
- [x] #2 Build identity and Windows jump-list entries/icons preserve source behavior in development and packaged fixtures; secondary processes and shutdown leave no duplicate backend or stale native state.
- [x] #3 All 18 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary. Continue within the authorized batch through TASK-381.30, then stop for review.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Execute and audit the 18 frozen source methods (24 cases), preserving exact icon pixels, ACLs, activation identity, argument fixtures, build metadata and legacy-theme precedence. 2. Restore explicit secure Windows frontend ownership through an Avalonia-free helper boundary, with parent-bound lifetime and separate backend ownership; preserve the existing Electron installation namespace. 3. Add content-addressed six-frame cover ICOs and stable isolated Jump List identity/arguments; expose the actual frontend build identity on desktop and fullscreen. 4. Verify source, focused component/API, isolated process/ACL and native Jump List cases, including both surfaces and no real library mutations. 5. Record checkpoint79 and per-method evidence, commit the verified milestone, then continue to TASK-381.25.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Started after TASK-381.23 implementation 6d8186d1 and completion 4d6829e5. Baseline full component/live API gate: 3,958 cases across 198 files, plus later focused picker checks; 24 distinct startup/setup native cases pass. Inventory: 1,553 ported, 663 retained-backend, 34 framework-specific, 162 pending and 23 partial. Read-only source audit identifies missing cover ICO cache, commit/build metadata, and the explicit current-user/deny-network activation ACL boundary. Preserve Afterglow installation/prototype profile compatibility; adapt source AppUserModelId names to the deliberately independent Electron namespace. Preserve unrelated scc-report.html and any existing .claude/settings.local.json.

The 24 unchanged original cases pass with WINNOW_JUMP_LIST_SMOKE=1, including actual isolated Windows publication (task38124-source.log/TRX). Bounded ownership is split between a shared BCL/Api.Contracts activation assembly and a parent-bound helper branch in the backend executable; that branch executes before HTTP hosting or database locking. Preserve the established Winnow.Electron profile-hash taskbar identity and the separate app.winnow.afterglow installer identity. The frontend package version remains independent until the release-entrypoint migration.

Focused identity/legacy-theme gate passes 53 cases, Jump List gate passes 11 cases, and the Node host gate passes 59 cases before final integration corrections. Ten real helper/protocol cases pass, including protected current-SID kernel ACLs, exact Int64 forwarding, independent Avalonia ownership, secondary exit and EOF release. The backend composition HTTP test passes with database/WAL/cache containment and the authenticated selected folder root. Root added the native local-art/legacy-theme fixture and actual helper ACL inspector. Integration review is preserving explicit override versus resolved root across profiles, login startup, updates and Jump List arguments; activation ownership must survive a pending quit drain.

Final focused gates: helper/API13, main60, identity53 and Jump List12. Production build passes with index-GqYybSk9.js. Full component/live API run executes3992 cases/203 files:3989 pass; three stale expectations in dependency traversal and Settings capture are corrected without production changes. Both affected files then pass10/10, covering all3992 cases with final source. Native31 execution is active against that frozen bundle; two standalone wrappers require nonblocking Electron readiness adapters before accepting their evidence.

Final verification:31 distinct native cases pass with complete JSON evidence and no skips or final errors (task38124-native-evidence.json). Source24 assigned cases fully execute; supplemental24 reported passes contain20 executed bodies and4 explicitly conditional early returns, so44 source bodies total. All3992 component/live API cases pass across the full run and corrected two-file rerun. Helper/API13 pass; production build, TypeScript, formatting and visual inspection pass. Native test-only adapters fix ESM readiness, bounded pipe connection retry and actual package metadata loading. Real isolated Jump List publication/empty/clear each return ok. Installed packages, non-Windows runtime and physical controllers retain their later milestones. Checkpoint79 records exact boundaries. Inventory:1571 ported,665 retained-backend,32 framework-specific,145 pending,22 partial.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Restored protected Windows frontend activation through a parent-bound shared helper, exact cached cover ICOs and stable isolated Jump Lists, legacy-theme precedence and frontend build identity on desktop/fullscreen. Verified3992 component/live API cases,44 fully executed source bodies,13 helper/API cases and31 distinct native cases; inspected both About layouts. All18 assigned source methods are ported; two prior activation waivers now identify retained shared native policy. Continue the authorized batch with TASK-381.25 after the milestone commit.
<!-- SECTION:FINAL_SUMMARY:END -->
