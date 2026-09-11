---
id: TASK-213
title: Reconcile process sessions across Winnow restarts without duplicate history
status: Done
assignee:
  - '@data-layer'
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 08:00'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Monitor/SessionWatcher.cs:531'
  - 'src/Winnow.Monitor/SessionWatcher.cs:718'
  - 'src/Winnow.Data/Repositories/SessionRepository.cs:27'
  - 'src/Winnow.Recommend/RecommendationEngine.cs:690'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: medium
type: bug
ordinal: 244000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R25. Evidence: Source verified. SessionWatcher keeps live sessions in memory, inserts a null-ended session at shutdown, and starts a fresh in-memory session from the process's original start time when reattached after restart. SessionRepository inserts a new row when that process later exits; there is no persisted session identity/reconciliation path. History readers count session rows, including open rows. A crash before shutdown can lose the observation unless the game is rediscovered. Restarting Winnow during one game sitting can produce an open and a completed row for the same sitting, affecting session counts and engagement signals. This is not a claim that play duration is necessarily doubled.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A monitored sitting has a durable idempotent lifecycle that can reconcile shutdown/restart and rediscovery without duplicate logical sessions.
- [x] #2 Unknown exits remain explicit; recovery does not invent duration or join a reused PID to the wrong session, and notes/launch attribution retain their identity.
- [x] #3 Tests cover restart while the game runs, crash/recovery, PID reuse and persistence retry; Activity, journal and recommendation history agree on both surfaces.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add migration0038 with nullable monitored observation keys and an exact process-member ledger; leave legacy and manual sessions unchanged. 2. Add repository recovery/idempotent monitored save operations using operation-local transactions/savepoints; preserve notes, original attribution and known start when adopting a running sitting. 3. Recover exact ownership/PID/creation-time/name matches before applying debounce, checkpoint qualifying live sessions and new members, and complete the same row; keep missing exits null and reject PID reuse. 4. Preserve journal prompts for completed sessions only, retry checkpoint/final writes safely, and avoid writes on unchanged live ticks. 5. Add restart, crash, child-process continuation, ambiguous/unknown exit, PID reuse and failure/rollback regressions plus Activity/journal/recommendation parity on both surfaces; update contracts, docs and measured evidence and run focused checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented nullable monitor identity plus process ledger and durable recovery aliases in migration0038. Repository saves are atomic locally and inside caller savepoints; exact process recovery retains the original start, launch attribution, session ID and notes. Watcher checkpoints qualifying live sittings and newly joined processes, restores before debounce, retries uncertain writes, and announces only confirmed completion. Unknown exits and ambiguous or legacy identities remain explicit. Validation:77 main,19 desktop/fullscreen headless and4 recommendation Release tests passed; Linux consumer compiled with2 explicit Windows skips;38 migration hashes verified. Evidence and reproduction methods:docs/spikes/architecture-fixes-2026-09-10.md Durable monitored sittings section. No real-library or real-game data used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
One monitored sitting now survives normal shutdown, crash recovery and rediscovery without duplicate history. Confirmed exit completes the original row; notes and launch attribution retain their identity, and unknown exits stay null.100 focused Windows tests passed across data/watcher/history, desktop/fullscreen and recommendation consumers;2 Linux-only tests compiled and skipped on Windows. No speculative repair of ambiguous legacy sessions.
<!-- SECTION:FINAL_SUMMARY:END -->
