---
id: TASK-322
title: Refresh merge suggestions after plugin imports and on demand
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 04:12'
updated_date: '2026-09-17 04:39'
labels: []
dependencies: []
ordinal: 364000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PlayStation and Xbox imports currently appear in the library without triggering the matching pass, leaving Merges stale until the regular library pipeline runs. Users also need an explicit way to refresh matching suggestions while reviewing their library.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Completed plugin library imports queue merge suggestion work without delaying initial library publication or waiting for optional artwork.
- [x] #2 Startup, plugin-triggered and manual matching use a shared serialized operation and preserve confirmed/rejected decisions.
- [x] #3 Desktop and fullscreen Merges expose an accessible manual refresh with busy, completion and recoverable failure states; new suggestions appear even when the pending count is unchanged.
- [x] #4 Focused coordinator, matching and real desktop/fullscreen interaction tests pass, with build and documentation updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a shared serialized suggestion-refresh service around the existing matcher and use it from the normal pipeline. 2. Queue independent matching after plugin imports and republish the merge queue. 3. Add a shared manual command and controls on desktop/fullscreen with completion/error feedback. 4. Verify orchestration, actual imported candidates, input behavior and build; document the operation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added one serialized background matching service for startup, plugin and manual requests. Plugin imports publish first, then request independent matching and enrichment; later imports and built-in startup can queue another pass. Focused orchestration/service checks: 11 passed. Actual SQLite imports: 12 passed across PSN/Xbox and Steam/GOG/Epic, including repeat imports and preserved decisions/source facts. Domain review found no actionable production defects. Desktop/fullscreen interaction and full solution verification are in progress.

Desktop and fullscreen now expose Refresh suggestions with accessible status and disabled duplicate requests. Revision-aware entry/publication reloads and load generations prevent stale same-count or out-of-order results. Inspected 950px desktop and 1280px fullscreen rendered states. Initial full run found a fullscreen header action/publication race and two test/copy issues; fixed and focused checks passed (106 VM/copy, 14 UI). Addressing the related active-Undo display after a refresh before final verification.

Final focused checks pass: 109 merge view-model/copy tests and 14 real desktop/fullscreen interaction tests. Added Undo-after-refresh coverage for rejected proposals, retained pending pairs and matcher-retired pairs; Undo reloads current rows when prior card references were replaced. Release solution builds with zero warnings/errors. Migration integrity verified all 43 hashes. Final full core/application and UI suites are running after the fixes.

User refinement: replace the desktop Refresh suggestions text button with an accent-colored refresh icon next to Merge selected, keeping tooltip/accessibility/status feedback. Fullscreen retains the labeled controller action. Updated isolated test app is running from C:\Temp\winnow-merge-full using the existing PSN test profile; startup log confirms the matching pass ran against 1,089 releases. Final main/application suite passed 4,844/4,844.

Full verification is green after fixes: 4,844 application/core and 813 UI tests; remaining suites passed 192 recommendation, 189 covers, 130 PSN, 104 Xbox, 89 plugin host, 42 SteamGridDB and 37 updater tests. Total 6,440 passed, with the two expected Linux-only tests skipped on Windows. Final icon-only desktop styling adjustment will receive focused interaction/render checks.

Final desktop refinement verified: accent refresh icon immediately before Merge selected, tooltip/automation label retained, idle status row collapsed. Release focused checks passed 10 UI and 17 copy tests; root inspected the rendered 950px pane. Fullscreen keeps its labeled controller action. Restarted the isolated PSN test session using C:\Temp\winnow-merge-icon\bin\Winnow.App\release\Winnow.exe and the existing C:\Temp\winnow-psn-test-20260916-210430 data profile.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Plugin imports now publish the library then queue a serialized merge suggestion pass independently of artwork. Desktop exposes an accent refresh icon beside Merge selected; fullscreen provides the same action with a controller-friendly label. Revision/generation checks keep both queues current, and Undo remains consistent after refresh. Verified with 6,440 passing Windows tests, two expected Linux-only skips, warning-free Release build and all 43 migration hashes; final icon layout passed focused UI/copy checks and rendered inspection. Updated isolated test session launched with existing data.
<!-- SECTION:FINAL_SUMMARY:END -->
