---
id: TASK-245
title: Compact Stats headers to bring charts above the fold
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 18:35'
updated_date: '2026-09-12 18:38'
labels: []
dependencies: []
ordinal: 286000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Reduce stacked header and empty status space in Stats, especially Steam Spending, while preserving coverage and usable desktop/fullscreen controls.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop spending figures begin substantially higher with source and coverage preserved and no empty status rows
- [x] #2 Fullscreen header is compact without reducing readable control sizes or navigation
- [x] #3 Rendered desktop narrow/wide and fullscreen checks plus relevant regressions pass
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Combine title, sections and refresh; suppress duplicate embedded account header and collapse absent statuses. 2. Compact fullscreen heading and actions using existing sizes. 3. Render both surfaces, run focused regressions, update visual spec and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Combined desktop title/tabs/refresh, hid the duplicate embedded account header, removed empty status row height and allowed full-width source/coverage. Fullscreen heading/tabs/refresh wrap together with existing font and target sizes; redundant library count removed. Rendered desktop1024/600 spending cards begin at about119px for seeded coverage; fixtures assert upper bounds170/220px. Inspected fullscreen including actual shell140%. Build clean,48 focused UI tests pass, diff check clean.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Compacted Stats headers on desktop and fullscreen while preserving source/coverage and actions. Removed duplicate embedded heading and empty status space. Verified wide/narrow captures, real fullscreen140% sizing and48 passing UI tests; build has zero warnings.
<!-- SECTION:FINAL_SUMMARY:END -->
