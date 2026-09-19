---
id: TASK-42
title: Resolve the remaining single-entry ACCOUNT rail section
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-08-29 21:54'
updated_date: '2026-09-11 18:53'
labels:
  - ui
dependencies: []
documentation:
  - design-system.md
priority: low
ordinal: 105000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The desktop rail has Feed and Merges as screen destinations above All Games, with buckets and the single-entry ACCOUNT section below. REVIEW no longer exists as a section. Decide whether ACCOUNT should retain its heading or fit into a clearer grouping while keeping screen navigation, library subsets, lists and configuration understandable.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The remaining single-entry ACCOUNT heading is removed through a clearer layout or explicitly retained with concise current rationale.
- [x] #2 All destinations remain reachable and their screen, bucket, list and configuration roles remain clear; document the actual resulting desktop order.
- [x] #3 Assess fullscreen's separate navigation hierarchy and retain equivalent access without imposing desktop rail geometry.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Place Steam stats alongside Feed and Merges as a screen destination above All games, removing the single-entry Account heading. 2. Preserve buckets, lists and footer configuration actions and document the exact order. 3. Exercise desktop statistics navigation and fullscreen Activity account-summary access with headless tests; commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: MainWindow.axaml places Merges beside Feed and keeps ACCOUNT around the statistics destination. The old task's REVIEW section and below-divider work-queue grammar do not describe current navigation. design-system.md section 12.1 records the current grouping; this task retains the unresolved ACCOUNT design choice.

Moved STEAM STATS beside FEED and MERGES before ALL GAMES, removed the single-entry ACCOUNT heading, and documented the exact screen/bucket/list/footer order. Three RailListControlsTests passed: position and keyboard activation of statistics/all-games, existing list grouping/creation actions, and fullscreen Activity to Library summary activation.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Grouped Steam stats with other screen destinations and removed ACCOUNT. Desktop navigation and fullscreen Library summary access verified by three headless rail tests.
<!-- SECTION:FINAL_SUMMARY:END -->
