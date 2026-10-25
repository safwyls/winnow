---
id: TASK-314
title: Rename Played out to Invested and explain built-in collections
status: Done
assignee:
  - '@codex'
created_date: '2026-09-16 23:32'
updated_date: '2026-09-16 23:35'
labels: []
dependencies: []
ordinal: 356000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Clarify built-in collections with short explanations and the requested Invested label.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Desktop and fullscreen show Invested with unchanged membership.
- [x] #2 Built-in collections have desktop rail tooltips and fullscreen explanations.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Share collection descriptions, bind tooltips and accessible help, update the copy spec and verify both views.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Renamed the shared high-playtime label to Invested; internal retired key and query semantics are unchanged. Added shared descriptions for All games and all five built-in collections, bound desktop rail tooltip/help, and exposed descriptions in fullscreen collection choice content, tooltip and help. Verified actual desktop tooltip opening and fullscreen rendered descriptions in CollectionExplanationTests (1 passed), plus 23 fullscreen accessibility and related UI checks. Build succeeded with warnings treated as errors.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Invested replaces Played out. Every built-in collection now has a concise rail tooltip, with shared visible and accessible explanations in fullscreen. Verified 24 headless UI checks across both surfaces.
<!-- SECTION:FINAL_SUMMARY:END -->
