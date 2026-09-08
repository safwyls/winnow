---
id: TASK-155
title: Place unread-update badges consistently in the top-left corner
status: Done
assignee: []
created_date: '2026-09-08 02:45'
updated_date: '2026-09-08 02:51'
labels: []
dependencies: []
references:
  - design-system.md
modified_files:
  - design-system.md
  - src/Winnow.App/Views/GameTileView.axaml
  - src/Winnow.App/Views/FeedCardView.axaml
  - tests/Winnow.Ui.Tests/CardDetailsInteractionTests.cs
  - tests/Winnow.Tests/Enforcement/VisualDisciplineTests.cs
priority: medium
type: bug
ordinal: 187000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The unread-update badge drifts between the library tile and feed card. Every cover-level patched indicator must use the design-system top-left placement so the same signal appears in the same location across views.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Library tiles place the unread-update badge 8px from the cover's top and left edges.
- [x] #2 Feed cards place the unread-update badge 8px from the cover's top and left edges.
- [x] #3 Automated tests guard the shared corner placement against drift.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Inventory Flare unread markers and distinguish cover badges from inline rail, list, detail, and merge pips. 2. Align the feed cover badge with the existing top-left library tile placement. 3. Add regression coverage for both views and run focused UI and unit tests.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Distinguished cover-level BadgeGlow indicators from inline Flare pips in the rail, list, details, and merge surfaces; only cover badges have a corner-placement rule. Verification passed: Winnow.Tests 3,680/3,680 and Winnow.Ui.Tests 63/63 using isolated BaseOutputPath directories.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Placed the feed card's unread-update badge 8px from the top-left corner to match library tiles, and added a markup-wide regression test covering every BadgeGlow cover indicator. The existing rendered tile test verifies the same position at both supported tile widths. Full unit and UI suites pass.
<!-- SECTION:FINAL_SUMMARY:END -->
