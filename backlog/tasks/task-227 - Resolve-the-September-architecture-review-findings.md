---
id: TASK-227
title: Resolve the September architecture review findings
status: In Progress
assignee:
  - '@codex'
created_date: '2026-09-11 06:02'
updated_date: '2026-09-11 08:29'
labels:
  - architecture
  - hardening
dependencies: []
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: task
ordinal: 258000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the concerns recorded in docs/architecture-review-2026-09-10.md: TASK-189 through TASK-226 plus the directly reused temporal replay, dormancy authority and fullscreen verification concerns in TASK-135, TASK-27 and TASK-4. Preserve unrelated edits and intentionally deferred feature scope. Work autonomously through routine implementation decisions; only genuine user-required decisions or unavailable physical/provider validation may remain open, moved to the end of the backlog with a precise decision/evidence request.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Each of the 38 review findings has a verified correction or an explicitly justified user-dependent disposition, with task evidence and both presentation paths assessed.
- [ ] #2 Directly reused replay, dormancy and fullscreen concerns are resolved or clearly separated into remaining user/hardware-dependent verification.
- [ ] #3 Integrated Release builds, relevant Windows/Linux tests and migration/packaging checks pass where executable; limitations are documented honestly.
- [ ] #4 Milestone commits preserve unrelated work, governing documents reflect the implementation, and a final delivery record identifies anything still needed from the user.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Correct identity/metadata integrity, account cache isolation and unread composition in parallel with runtime/schema guards. 2. Integrate and test each bounded batch, then address feed, refresh/list behavior, provider contracts, artwork/plugin lifetime and session recovery. 3. Reconcile active documentation, replay and dormancy concerns; perform full integration and available platform checks. 4. Move only genuinely user-dependent items to the end with explicit reasons and summarize delivery.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Foundation checkpoint verified: solution Release build zero warnings/errors; all4838 runnable Windows tests covered, with2 nativeLinux skips. Complete run found3 stale naming/count expectations, corrected and rechecked via46 LibraryViewModel+8 hygiene tests. Shared BaseOutputPath requires build before test--no-build to avoid simultaneoustesthost copy locks.33migrationhashes and isolatedWindows/Linux pluginpublishes pass. Evidence in docs/spikes/architecture-fixes-2026-09-10.md.192/193/195 backend fixes land now; explicitStores/action presentation checks remainassigned toUI. Remaining review tasks continue.

Progress2026-09-11: TASK189-221 and223-226 are Done; TASK27 is Done. TASK222 measured a real long-history bottleneck and its paged/background-read fixes are under final interaction review. TASK135 is implementing honest frozen-snapshot replay and dated-input fences. TASK4 was moved to ordinal259000 at the end with needs-user for physical controller/TV validation. Independent final scrutiny of cover lifetime found an interleaved lease retry race and is correcting it before the consolidated gate. No product-policy decision is waiting on the user.

Consolidated verification is in progress. Fresh audited restore and Release solution build pass with zero warnings/errors; all 38 migration hashes, migration mutation tests, test-summary checks and bundled-plugin verifier mutation checks pass. Native Linux session smoke passes both cases on Fedora 44 WSL. The first full test pass exposed two feed edge cases and two UI lifecycle/awaiting issues; each is being corrected with focused regression evidence before the gate is repeated.
<!-- SECTION:NOTES:END -->
