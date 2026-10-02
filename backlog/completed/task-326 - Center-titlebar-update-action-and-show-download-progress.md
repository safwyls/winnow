---
id: TASK-326
title: Center titlebar update action and show download progress
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 15:18'
updated_date: '2026-09-17 15:23'
labels: []
dependencies: []
ordinal: 368000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The titlebar update action sits above the vertical center and remains a disabled button while downloading, obscuring progress.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop update action is vertically centered and becomes Updating above download progress in the same location.
- [x] #2 Fullscreen header reflects shared download progress and restores its notice after downloading.
- [x] #3 Updater UI checks cover transitions and rendered alignment.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Center the caption slot, add themed progress to desktop and fullscreen headers using shared state, update the visual specification, and verify updater UI transitions and renders.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Desktop: centered the caption slot and replaced the download-time disabled button with Updating above a themed determinate bar. Fullscreen: the header notice uses the same download state and progress; controller actions and Application settings retain shared commands. All 8 ApplicationUpdaterUiTests passed, including caption bounds, equal slot position/width, progress changes, cancellation, readiness and explicit restart. Inspected headless desktop ready/downloading and fullscreen downloading PNG captures in C:/Temp/winnow-update-captures. Verification built the app in a scratch output directory; no production host or real update was run.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Centered the titlebar update action and added live Updating progress in its place, with matching fullscreen header feedback. Updated the visual specification. All 8 updater UI tests pass and rendered captures were inspected.
<!-- SECTION:FINAL_SUMMARY:END -->
