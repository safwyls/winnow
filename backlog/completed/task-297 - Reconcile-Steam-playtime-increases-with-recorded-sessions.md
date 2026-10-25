---
id: TASK-297
title: Reconcile Steam playtime increases with recorded sessions
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 00:10'
updated_date: '2026-09-16 00:26'
labels: []
dependencies: []
type: feature
ordinal: 339000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Process watching can miss play while Steam cumulative totals still advance. Preserve account-scoped Steam observations for installed games and surface unexplained increases as approximate Steam-reported activity without fabricating precise sessions or inflating gameplay totals.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Installed Steam games retain account and source scoped raw totals, Last played and observation times; unknown values and historical imports do not fabricate activity.
- [x] #2 Reconciliation waits for updates to settle, compares recorded sessions without double counting, handles repeated or delayed observations, account separation and counter decreases, and remains idempotent.
- [x] #3 Desktop and fullscreen expose approximate Steam-reported activity with observation bounds and clear uncertainty; exact sessions and gameplay totals remain authoritative.
- [x] #4 Migrations, reconciliation, import integration and both presentation paths have automated coverage and updated documentation.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add raw account/source Steam observation storage and a conservative, idempotent reconciliation read model with a settling delay and session coverage. 2. Capture live Steam observations atomically through existing resolver/sync paths before household clamping and historical replay. 3. Show a separate Steam-reported activity view on desktop and fullscreen with approximate duration and observation bounds, excluding it from exact session totals. 4. Test mixed accounts, source lag, counter decreases, delayed session writes and restart/repeat behavior; validate migration, both UIs and docs; commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented migration 0043 and immutable live Steam change points per installed ownership/account/source. Resolver captures original account facts atomically before household clamping; cached API candidates preserve their original measurement time. Read model uses a 30-minute settling delay, canonical highwater, source baselines, one-use session credits, one-minute tolerance, and explicit unavailable comparisons for multiple accounts/open checkpoints. Desktop per-game Activity and fullscreen global Activity/per-game Play history expose separate estimates; exact sessions/statistics are unchanged. Initial focused run: 183 tests pass; new UI paths and account/stale-read regressions pass, both rendered surfaces inspected. Full-suite run identified a required identity-read inventory entry for the original-ownership install gate; added the explicit DoNotResolve rationale.

Final validation: solution build passed with zero warnings/errors. Full solution tests passed all other assemblies (UI 718, Recommend 192, Covers 189, Plugins 87, SteamGridDB 42, Updates 37); core had 4761 passes and the single identity-inventory annotation failure, corrected and verified in a 188-test focused rerun. Final per-game Steam visibility and scope-loading fixes passed 33 focused UI tests. Two Linux-native smoke tests skip on Windows. All 43 migration hashes and git diff whitespace checks pass. Desktop and fullscreen renders inspected. Production app and database were not changed. Unrelated feed-card edits remain outside this task.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a persistent second layer of session evidence from installed Steam games, preserving account/source observations and reconciling settled increases against exact sessions. Separate desktop and fullscreen Steam-reported activity presents approximate unmatched minutes and observation windows without adding synthetic sessions or changing totals. Handles account ambiguity, cached timestamps, source baselines, counter corrections and late session writes. First live readings establish baselines; historical missing sessions are not reconstructed.
<!-- SECTION:FINAL_SUMMARY:END -->
