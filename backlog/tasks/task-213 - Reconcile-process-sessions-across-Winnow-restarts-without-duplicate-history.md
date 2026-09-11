---
id: TASK-213
title: Reconcile process sessions across Winnow restarts without duplicate history
status: To Do
assignee: []
created_date: '2026-09-11 05:00'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 A monitored sitting has a durable idempotent lifecycle that can reconcile shutdown/restart and rediscovery without duplicate logical sessions.
- [ ] #2 Unknown exits remain explicit; recovery does not invent duration or join a reused PID to the wrong session, and notes/launch attribution retain their identity.
- [ ] #3 Tests cover restart while the game runs, crash/recovery, PID reuse and persistence retry; Activity, journal and recommendation history agree on both surfaces.
<!-- AC:END -->
