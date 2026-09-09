---
id: TASK-171
title: Give Journal and Updates dedicated game details tabs
status: Done
assignee:
  - '@codex'
created_date: '2026-09-09 04:54'
updated_date: '2026-09-09 04:58'
labels: []
dependencies: []
ordinal: 203000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Separate journal editing and update reading from the activity timeline while preserving existing actions and navigation.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Five independent bounded tabs
- [x] #2 Updates shortcut and journal editing remain functional
- [x] #3 Tests and visual spec reflect new layout
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Move existing panels; update shortcut and keyboard navigation; verify tests and update visual spec.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Verified rendered Updates and Journal tabs at 1200x640 and bounded navigation at 1280x820. Full assemblies pass: 108 Covers, 160 Recommend, 3802 core tests, 105 UI; 2 Linux-only skipped. Corrected stale tab-index assertions and detached-control visibility assertion, then reran all UI checks.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added dedicated Updates and Journal tabs, moved unread navigation to Updates, and preserved journal editing, patch links, and tab-local scrolling. Visual spec updated. 4175 tests passed, two platform skips.
<!-- SECTION:FINAL_SUMMARY:END -->
