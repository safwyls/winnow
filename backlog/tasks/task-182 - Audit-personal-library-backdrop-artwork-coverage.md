---
id: TASK-182
title: Audit personal-library backdrop artwork coverage
status: Done
assignee:
  - '@codex'
created_date: '2026-09-10 23:53'
updated_date: '2026-09-11 00:10'
labels: []
dependencies: []
type: spike
ordinal: 213000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Measure missing high-resolution backdrop coverage from a consistent read-only personal library snapshot and distinguish local metadata gaps from upstream asset limitations.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Audit defines library scope and resolution thresholds and reports per-game gaps in a private local artifact.
- [x] #2 Representative gaps are checked against IGDB and source-selection behavior to distinguish measured causes from assumptions.
- [x] #3 Findings include prioritized next steps without modifying the personal library.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Snapshot the database with read-only SQLite backup and inspect current cache versions and artwork metadata. 2. Classify owned games and validate representative upstream cases. 3. Deliver private per-game audit and summarized findings with measurement limitations.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Private artifacts: C:\Temp\winnow-art-audit-20260910\artwork-audit.html and artwork-audit.md, with source evidence and reproducible scratch scripts beside them. SQLite read-only backup; production GetSnapshotAsync with read-only factory reproduced current visibility/grouping. 975 visible groups from 1010 visible ownerships and 1037 owned works; snapshot hash unchanged during scope extraction. No personal-library writes, identity edits, app changes or cached credential updates.

Measured 379 display works with eligible native Full HD artwork; 711 with Full HD artwork or screenshot. First-choice sources provide Full HD for 674; rendition fitting reduces two further games, leaving 303 gaps. All 922 cached game payloads are v5; all 1764 image rows contain metadata; all 15 representative live IGDB image comparisons match local observations. Causes: root-only lookup misses cached images in20 confirmed groups (15 FullHD);720p tier prefers smaller art in37 cases; four FullHD alpha-flagged candidates verified fully opaque; two wide sources lose FullHD via CDN fitting. Probed288 Steam entries across283 affected groups:118 successful2x hero requests across116 games,170404s. Remedy union148 games,90 Steam opportunities additional to software-only remedies. No inferred new matches or automatic edits.

Verification: headless Edge exercised all report filters (303 current gaps,596 artwork gaps,148 opportunities,975 total) and search; screenshot visually reviewed. Native crop thresholds, CDN limits, near-threshold cases, partial sampling, missing-path limitations and unmeasured composition are documented. Full lists remain private outside Git; no follow-up implementation task started.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Completed read-only personal-library audit with searchable per-game report and verified upstream evidence. Found303 current FullHD selection gaps among975 visible groups and concrete candidate remedies for148 distinct games, combining existing linked art, selector corrections, opacity checks and larger Steam heroes. Personal library and application code unchanged.
<!-- SECTION:FINAL_SUMMARY:END -->
