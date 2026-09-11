---
id: TASK-208
title: Provide an honest cold-start shelf for eligible uninstalled games
status: Done
assignee:
  - '@codex'
created_date: '2026-09-11 04:59'
updated_date: '2026-09-11 06:44'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Recommend/ShelfBuilder.cs:30'
  - 'src/Winnow.App/Services/FeedService.cs:341'
  - tests/Winnow.Recommend.Tests/ColdStartFeedTests.cs
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 239000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R20. Evidence: Reproduced. ShelfBuilder's five fixed shelf predicates omit a library containing only never-played, uninstalled games without a taste profile. The flat GetFeed API returns a candidate, but GetShelves returns no shelves; production FeedService uses the latter. Existing cold-start tests exercise the flat path. A valid imported library can show an empty For you page on day one, contrary to the recommendation model's explicit cold-start intent.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The production shelf path surfaces suitable eligible owned games without installation, play history or a taste profile, using an honest cold-start reason.
- [x] #2 Existing maturity/non-game/account filters, dismissals, feedback suppression and deduplication still apply; genuinely ineligible libraries retain truthful empty states.
- [x] #3 Integration regressions use GetShelves through the production feed contract and verify usable desktop/fullscreen output for cold and partially enriched libraries.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add an honest deterministic cold-start shelf for eligible never-opened games without taste evidence. Keep exclusions, diversity and visible/reserve semantics. Verify first-run feeds and explanation copy on desktop and fullscreen.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added Waiting to be opened as the final non-overlapping shelf for uninstalled never-opened games without a qualifying taste match. Uses existing shelfware scores/reasons and unchanged thresholds. Complete recommendation suite166passed; four actual Program.ConfigureServices desktop/fullscreen composition cases verify cold/installed-sibling rendering and feedback.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Fresh libraries now produce an honest shelf from eligible uninstalled games without requiring taste or play history. Existing exclusions, feedback, diversity and reserves remain in force; both presentation paths verified through production composition.
<!-- SECTION:FINAL_SUMMARY:END -->
