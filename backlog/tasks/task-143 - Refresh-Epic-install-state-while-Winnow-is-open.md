---
id: TASK-143
title: Refresh Epic install state while Winnow is open
status: Done
assignee:
  - codex
created_date: '2026-09-06 18:52'
updated_date: '2026-09-06 19:04'
labels: []
dependencies: []
priority: high
ordinal: 170000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
When Epic finishes an installation, Winnow should update Install to Play in the library and already-open Details. This is the user-requested Epic completion slice of TASK-3; downloading remains delegated to Epic.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Completed Epic manifest changes refresh persisted install state and visible library/actions without restarting Winnow.
- [x] #2 Queued, incomplete, malformed or actively changing manifests never cause a premature Play action.
- [x] #3 Already-open Details refreshes actions while preserving user edits and selection.
- [x] #4 Temporary-file and deterministic service tests cover completion, unchanged files, transient read failure and shutdown; documentation states refresh timing.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Poll Epic top-level manifest content and debounce readable changes using a focused local Epic scan/resolve with the shared sync gate and a UI reload callback. 2. Refresh open Details actions in place on library reload (coordinator). 3. Verify with temporary manifests and focused tests; document timing and limitations.

4. Reread Epic candidates inside the shared sync gate for both local and remote passes so a queued or slow backfill cannot restore stale install state; test a stale reusable scan against completed manifests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented two-second Epic manifest polling with two consecutive valid snapshots, retry on read/sync failure, post-scan change check and cancellation. Focused Epic sync shares LibrarySyncGate and persists launch keys; it never scans Steam/GOG or calls the network. Parser requires explicit false completion bit. Coordinator wired normal library reload and in-place Details action refresh. Focused build passed 99 tests (Epic refresh, Details refresh, Epic reader/source, local-sync contract and identity guards); one additional regression for returning to a previously applied fingerprint is awaiting final integration run. All install-state tests use temporary manifests and SQLite; no real launcher files or library were changed.

Closed the concurrent-sync race: both local and remote ownership passes reread Epic candidates after acquiring LibrarySyncGate, so an old startup scan cannot undo a completed installation. Steam/GOG scans and network calls remain outside the gate. Final focused run passed 31/31, including both queued local/remote regression cases, stale reusable scan, unavailable manifests retaining installed state, service retries/shutdown, real resolver transitions, open Details preservation, and existing remote convergence/install-state contracts. Previous 99-test run covered Epic parser/source and identity guards. Final full-suite integration pending coordinator.

Coordinator verified actual open Details Install-to-Play command binding and retained Store page in the 20-test UI run. Final selection/flip restoration preserves the visible card by ownership; 185 focused tests pass after this last change, including real SQLite install/uninstall action refresh, unsaved editor draft retention, selected/flipped state, Library filtering/grain, metadata editor, Details, and Epic completion/order regressions. Full main suite remains running.

Final verification: full main suite 3496/3496, UI suite 20/20, and final selection-refresh focused suite 185/185 passed. Solution build is clean. No real download or launcher files were changed during verification.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Epic manifest completion now refreshes Install to Play within two to four seconds plus local scan time, including an open Details panel. It preserves editor drafts and the selected/flipped visible card. Stable explicit completion, retry, cancellation and fresh reads under the resolver gate protect against partial manifests and stale startup scans. Verified temporary manifests through real SQLite resolution, actual UI command binding, and 3496 main, 20 UI and 185 final focused passing tests.
<!-- SECTION:FINAL_SUMMARY:END -->
