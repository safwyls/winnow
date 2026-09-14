---
id: TASK-280
title: Show ratings in desktop feed previews
status: Done
assignee:
  - '@codex'
created_date: '2026-09-14 01:55'
updated_date: '2026-09-14 02:01'
labels: []
dependencies: []
ordinal: 322000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Include attributed IGDB and Steam reception in the compact hover preview.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Preview shows available IGDB user/critic and Steam scores with counts using shared formatting; absent ratings add no empty space.
- [x] #2 Loads stored ratings only for open previews and cancels stale requests; interaction and bounds remain correct.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Expose a repository-backed ratings loader on library tiles, load it during preview lifetime, bind compact shared reception figures and verify presence/absence and cancellation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop preview loads work ratings through a library-owned repository delegate and renders shared reception figures with source, score and count. No enrichment/network fetch is triggered. Requests cancel with preview lifetime and stale results are ignored. Missing scores occupy no space. Fullscreen already renders shared reception on its details presentation and is unchanged. Build passed without warnings; 29 focused UI tests passed, including 5 new ratings cases and async growth remaining within window bounds.

User follow-up: moved desktop details reception from Overview to the identity header beside year/publisher. Visible figures now show source and value only; existing tooltips and accessible names retain counts. Fullscreen retains counts in its About reading page for controller access. Updated design guidance and verified 10 details refresh/mapping tests, including header visibility across tabs and tooltip-only counts.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added attributed IGDB user/critic scores and Steam review percentages with counts to feed previews. Verified clean build and 29 focused UI tests including missing data, cancellation and async placement.
<!-- SECTION:FINAL_SUMMARY:END -->
