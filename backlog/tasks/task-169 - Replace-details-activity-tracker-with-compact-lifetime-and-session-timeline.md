---
id: TASK-169
title: Replace details activity tracker with compact lifetime and session timeline
status: Done
assignee:
  - '@codex'
created_date: '2026-09-09 04:29'
updated_date: '2026-09-09 04:45'
labels: []
dependencies: []
ordinal: 201000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the user-approved compact activity timeline with equal monthly widths, wider tracked-session bars, proportional heights, coverage and grouped updates. Preserve existing details tab edits.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Compact lifetime and tracked-session ranges have accessible selectable marks.
- [x] #2 Stored history is wired without double counting or invented coverage, with honest sparse fallbacks.
- [x] #3 Equal monthly widths, wider session bars, and live update acknowledgements match the approved design.
- [x] #4 Model and UI checks and build pass, with visual specification updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inspect history and details wiring. 2. Build timeline model with coverage and overlap handling. 3. Integrate themed accessible chart and session loading. 4. Update spec and decisions and verify.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the approved compact tracker with equal monthly widths and 10px tracked-session marks. Imported monthly history wins overlap; missing or inconsistent months remain unknown. The tracker explicitly scopes linked games to the primary copy. Review caught grouped-update empty-range and repeated-selected-range issues, both fixed and covered. dotnet build passed with zero warnings/errors. Full dotnet test passed: 108 covers, 160 recommendation, 3799 main and 105 UI; two Linux-only tests skipped on Windows. Inspected 620px/360px tracker captures and details modal captures. An initial capture-enabled full run exposed an unrelated preview capture timing failure; normal full suite and targeted tracker capture run pass.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Replaced the old Activity chart/rail with the compact Lifetime and Tracked sessions tracker. Added 11 history-model tests, 2 tracker-state tests and 8 chart/integration UI checks. Verified full build and 4172 passing tests, plus targeted visual captures. Updated design-system section 10.2 and retained superseded wording in decisions.
<!-- SECTION:FINAL_SUMMARY:END -->
