---
id: TASK-243
title: Rename Stats navigation and investigate broader store and gameplay statistics
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 15:25'
updated_date: '2026-09-12 15:32'
labels: []
dependencies: []
ordinal: 275000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Rename the Steam Stats rail item to Stats and investigate feasible store breakdowns and a separate non-monetary gameplay statistics view using current data. Deliver a source-grounded recon spike rather than implementing the proposed analytics expansion.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop rail reads Stats and its tooltip remains accurate; fullscreen naming and current Steam-only data scope assessed.
- [x] #2 Spike maps feasible monetary and non-monetary statistics to actual sources and queries, with store coverage, identity/account rules, missing-data limitations and implementation cost.
- [x] #3 Spike recommends desktop/fullscreen information structure and a bounded first implementation; navigation change checked and relevant docs updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Rename the rail label and tooltip while retaining accurate Steam-only content labeling. 2. Inspect current store importers, scoped library/session/activity queries and stats presentation for feasible analytics. 3. Write a dated source-grounded spike with metric feasibility, store/account/identity semantics, proposed Spending and Gameplay views, and a bounded implementation recommendation. 4. Verify navigation and review spike evidence; update docs and close task.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Renamed the desktop rail and accessibility label to STATS; tooltip is Spending and licence statistics. Kept current Steam account content source labeling and existing neutral fullscreen Activity → Library summary entry. Full solution build succeeded with zero warnings/errors; 12 AccountStatsViewModelTests and the desktop/fullscreen stats navigation interaction test passed. Recon inspected source only, not live accounts or APIs. Spike includes 26 validated source links; domain reviews confirmed capability claims and clarified that only snapshot Buckets supplies scoped population, while Works/Ownerships are unfiltered. No new analytics or store selector implemented; no follow-up tasks created.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Renamed Stats navigation and delivered docs/spikes/stats-store-and-gameplay-recon.md. Recommends Gameplay/Spending sections with explicit store filters; first Gameplay slice can use existing scoped sessions and library facts. Monetary sources remain Steam-only; documented Epic unit uncertainty, account-history limitations, identity aggregation, missing data and deferred metrics. Build and 13 focused tests pass.
<!-- SECTION:FINAL_SUMMARY:END -->
