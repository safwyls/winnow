---
id: TASK-381.35
title: 'Electron: finish repository boundaries and preview coverage'
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-30 18:49'
updated_date: '2026-10-02 01:49'
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
- [x] #1 Original hygiene/boundary contracts have equivalent Electron/backend enforcement, retaining external API composition and all rename compatibility shims.
- [x] #2 Each original design-time preview contract has a working isolated Electron preview/test equivalent or a justified framework-only disposition; preview validation never starts a production data host.
- [x] #3 All 12 source contracts listed in this task leave pending/partial only after executed equivalent Electron/API tests or a per-method justified retained-backend/framework-specific disposition. Preserve source assertion scope and record exact evidence in the migration inventory.
- [x] #4 Relevant component/API and isolated native tests pass, with desktop and fullscreen assessed separately. Record visual evidence for presentation changes and distinguish simulated checks from unverified devices; update affected documentation in place.
<!-- AC:END -->

## Definition of Done
<!-- DOD:BEGIN -->
- [ ] #1 Record a reviewable milestone commit and verification summary, completing the authorized TASK-381.31 through TASK-381.35 batch. Stop for user review before beginning another task.
<!-- DOD:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect all 12 frozen source contracts and existing Electron preview/hygiene equivalents; verify unchanged source evidence. 2. Preserve repository safeguards and external API/rename boundaries with meaningful Electron or retained enforcement. 3. Supply isolated preview equivalents for both surfaces, exercising real production components without a production data host. 4. Run focused checks, serialized native screenshots and the complete component/live API gate; record exact migration evidence and limitations. 5. Commit the milestone, mark the task complete and stop for user review without starting TASK-381.36.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Repository gate: nine new hygiene cases plus 24 existing dependency-boundary/legacy-theme/session-runtime cases pass. Preserves original four noun sites, three compiler switches, fake SteamID whitelist and hook config; executes configured Codex hook without file writes. Original hygiene6/8 pass; two whole-tree scans fail before assertions at an old ignored .tmp denied-path fixture. ACLs and originals untouched. Replacement authored-file token/name checks pass with scan-boundary adaptation explicit in checkpoint-ninety.md. Preview implementation uses a shared isolated exact fixture and production components; native execution waits for source/unit lanes to close.

Source lane closed: original preview20/20, original hygiene6/8 with two previously recorded pre-assertion traversal failures, supplemental rename regressions33/33. Frozen test/helper/preview implementation and assembly provenance is in .tmp/task38135-source-evidence.json. New shared isolated previews pass33/33 components and typecheck; independent read-only review found no blocking gap. Native fourjourney matrix now runs against frozen test-only source without production main/preload/backend; complete component/API gate follows after it closes.

First complete component/live API run finished: 4284 passed and two failed of4286 across226 files,209.84s, no integration backend processes remain. Both failures reviewed: singular list-name helper still matched plural games only; compact ReceptionLine added a second named group after .34. Fixes preserve expected names/order and split compact plain span from full named group without weakening enforcement. Native4 functional journeys passed, but capture review caught auto-height standalone fullscreen FeedView clipping; preview host needs definite height and native guard now checks both active source cards, near-full intersection, aspect, hit test and useful size. Fixing these within the batch regression gate, then fresh native and full component/API verification.

Final gates: all4286 Electron/component/liveAPI cases across226files pass with zero skips in207.84s, including12liveAPI cases; temporary backend closed and no owned integration processes remain. Build/typecheck pass (index-CEqesFQd.js). Final native4/4 in8.7s with whole rawreport/captures in task38135-native-final4-results; both15surface matrices, source shell/card/detail journeys, corrected fully visible Feed and decoded/nonrectangular dragon masks pass. Native SVG loader now matches Vite file assets; production logo unchanged. Root reviewed final desktop/fullscreen captures; independent review found no blockers. All12assignedmethodsported. Inventory1682ported/706retained/41framework/6pending/0partial; completiongate correctly stillfails. Detailed limitations and original two traversal failures remain in checkpoint-ninety.md.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed repository safeguards and isolated Electron previews on desktop and fullscreen. Added the source fixtures and all 15 preview targets per mode; fixed compact reception accessibility and standalone Feed geometry. All 4,286 component/live API tests, build/typecheck and four native journeys pass. Twelve source methods are ported, completing this five-task batch and 61 resolved contracts. Two original hygiene scans hit temporary-folder permissions; their authored-file replacements pass. Six source contracts and full release verification remain. See checkpoint-ninety.md.
<!-- SECTION:FINAL_SUMMARY:END -->
