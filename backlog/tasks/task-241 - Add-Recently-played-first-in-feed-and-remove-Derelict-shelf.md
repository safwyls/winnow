---
id: TASK-241
title: Add Recently played first in feed and remove Derelict shelf
status: Done
assignee:
  - '@codex'
created_date: '2026-09-12 05:10'
updated_date: '2026-09-12 05:18'
labels: []
dependencies: []
ordinal: 282000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Show ten recently played games independent of recommendation feedback.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Recently played is first and ordered by last play with up to ten unique games unaffected by feedback.
- [x] #2 Desktop shows six and fullscreen accesses ten without recommendation feedback.
- [x] #3 Remove Derelict from both feeds and update docs and tests.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a chronological engine collection outside scoring and remove Derelict feed assembly. 2. Preserve six desktop cards and ten fullscreen cards while suppressing collection verdicts and surfacing writes. 3. Add focused engine and surface tests, update domain documentation, and run checks.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented chronological Recently played before recommendation shelves, independent of feedback and score. Derelict remains a library bucket but is absent from the feed. Desktop preserves six cards/four hidden; fullscreen uses the full set and scopes actions to the selected shelf. Full solution build passed with zero warnings/errors; regression verification in progress.

Focused verification passed: 192 recommendation tests, 75 app feed tests, 15 headless feed tests. Full-suite accessibility enforcement reports PluginSettingsView.axaml:33 setting AutomationProperties.Name on TextBlock; both source and enforcement test are unchanged from HEAD, outside this task. Initial combined full test run had a shared scratch-output xunit DLL copy lock, so App tests run separately with --no-build.

Final build passed with zero warnings/errors. Broader results: Recommend 192 passed; App 4624 passed/1 failed (unchanged PluginSettings TextBlock accessibility); UI 528 passed/1 failed (unchanged FullscreenPlatformTests expects arrow glyph but settings renders Open, reproduced in isolation). Covers 159, Plugins 87, SteamGridDB 42 passed; Linux-only 2 skipped on Windows. No feed-related failures. Physical controller/TV validation was not performed; desktop and fullscreen interactions verified headlessly.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a first Recently played collection of up to ten resolved games in recency order, with six desktop cards and all ten available fullscreen. Collection ignores recommendation feedback, has no verdict controls and records no impressions. Removed Derelict shelf while retaining the library bucket. Updated docs and regression coverage. Clean build and focused tests pass; broader suite has two unrelated failures in unchanged settings code, documented in notes.
<!-- SECTION:FINAL_SUMMARY:END -->
