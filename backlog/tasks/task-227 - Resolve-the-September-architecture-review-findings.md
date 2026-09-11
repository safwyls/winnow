---
id: TASK-227
title: Resolve the September architecture review findings
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 06:02'
updated_date: '2026-09-11 08:37'
labels:
  - architecture
  - hardening
dependencies: []
documentation:
  - docs/architecture-review-2026-09-10.md
  - docs/architecture-review-resolution-2026-09-11.md
  - docs/spikes/architecture-fixes-2026-09-10.md
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
- [x] #1 Each of the 38 review findings has a verified correction or an explicitly justified user-dependent disposition, with task evidence and both presentation paths assessed.
- [x] #2 Directly reused replay, dormancy and fullscreen concerns are resolved or clearly separated into remaining user/hardware-dependent verification.
- [x] #3 Integrated Release builds, relevant Windows/Linux tests and migration/packaging checks pass where executable; limitations are documented honestly.
- [x] #4 Milestone commits preserve unrelated work, governing documents reflect the implementation, and a final delivery record identifies anything still needed from the user.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Correct identity/metadata integrity, account cache isolation and unread composition in parallel with runtime/schema guards. 2. Integrate and test each bounded batch, then address feed, refresh/list behavior, provider contracts, artwork/plugin lifetime and session recovery. 3. Reconcile active documentation, replay and dormancy concerns; perform full integration and available platform checks. 4. Move only genuinely user-dependent items to the end with explicit reasons and summarize delivery.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
All 38 findings are resolved through TASK-189-226; directly reused TASK-135 and TASK-27 are also Done. Implementation milestones bde7ca4 and e84ba077504d96aa18a95f7b4018efd61e97da23 preserve the unrelated ApplicationSettingsView edit, Yaak task, local settings and docs/api work. Fresh audited restore and Release solution build pass with zero warnings/errors. The final complete Windows run passes 5,285 tests: 4,402 main, 462 UI, 185 recommendation, 159 covers, 35 plugins and 42 SteamGridDB. Two platform-specific tests skip there and separately pass on real Fedora 44 WSL Linux processes using SDK 10.0.400/runtime 10.0.11. All 38 migration hashes and mutation checks, test-result-summary checks, release-version checks and bundled-plugin mutation checks pass. Clean git-archive publishes of e84ba07 succeed for win-x64 with normal ReadyToRun and linux-x64, including source identity and bundled-plugin verification. Earlier integrated failures led to controlled regressions for optional deadlines, queued backfills, asynchronous Activity assertions and independent fullscreen-model lifetime; the final whole-suite pass is clean. Per-domain corrections, user-visible effects, measurements and limits are documented in the linked resolution/evidence records. TASK-4 is last at ordinal 259000 with needs-user for physical controller and TV-distance completion/focus/readability checks; no product-policy decision is waiting. Steam collections remain explicitly deferred in DRAFT-1, alongside the separately scoped pre-existing feature/research backlog. No installer, live sign-in or production-library interaction is claimed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed and documented all 38 review findings plus replay and dormancy. Verified 5,285 Windows tests, two real Linux process tests, clean Release build/audit, 38 migration hashes and Windows/Linux publishes. Milestone commits preserve unrelated work. Only TASK-4 physical controller/TV validation remains user-dependent at the end of the backlog; the resolution record describes the exact evidence needed.
<!-- SECTION:FINAL_SUMMARY:END -->
