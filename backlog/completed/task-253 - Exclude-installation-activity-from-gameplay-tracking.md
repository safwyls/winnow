---
id: TASK-253
title: Exclude installation activity from gameplay tracking
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 23:36'
updated_date: '2026-09-12 23:40'
labels: []
dependencies: []
ordinal: 294000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Ensure install actions and installer processes cannot create gameplay sessions or feed recently-played statistics. Audit reported Last played changes and preserve valid gameplay.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Install dispatch creates no launch intent; installer processes cannot create sessions through inference, launch fallback or compatibility paths
- [x] #2 Regression tests cover installers and genuine game launches
- [x] #3 Desktop and fullscreen share corrected tracking; existing suspect history is not blindly erased
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Audit install versus Play dispatch and imported dates. Centralize installer/runtime exclusion at process attribution, covering launch and compatibility fallbacks. Verify fake installer cannot create session while genuine games still do; inspect relevant local evidence read-only and document limits.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Install dispatch already used GameLinkKind.Install and declared no launch intent; expanded factory-to-dispatch check to Steam/Epic/GOG. Found executable-filter gap: generic setup/install helpers could be scanned and compatibility or launch fallback could bypass scan-only exclusions. Added central exact-name/known-prerequisite-directory predicate at scan, direct index matching, launch attribution and watcher discovery. Tests prove installer creates no session and real game launched afterward retains only its own590s runtime.82 focused tests pass after rebuild. Desktop/fullscreen share these service and monitor paths. Read-only default database inspection did not identify a current affected timestamp; requested game/store from user, no reply yet. This fixes a confirmed false-session path, not a verified diagnosis of that individual imported date. Existing sessions and store-imported play dates are unchanged; no historical cleanup or live data writes.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Blocked known installers and prerequisite tools from gameplay attribution across inference, launch and compatibility paths. Verified82 passing tests, including Install dispatch for all3 stores. Existing suspect history was not erased.
<!-- SECTION:FINAL_SUMMARY:END -->
