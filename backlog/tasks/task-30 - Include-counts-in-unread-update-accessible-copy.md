---
id: TASK-30
title: Include counts in unread-update accessible copy
status: Done
assignee: []
created_date: '2026-08-29 21:54'
updated_date: '2026-09-06 00:55'
labels:
  - accessibility
  - ui
milestone: m-4
dependencies: []
priority: medium
ordinal: 81000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Flare-marked unread-update counts are currently visual only; screen readers cannot access the count. Finding F48. Source: stabilization-2026-08-28.md Group 3.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Every flare-marked count has an accessible text equivalent (e.g., AutomationProperties.Name)
- [x] #2 A screen reader announces the count, not just the presence of updates
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
DESIGN DECISIONS, 2026-09-05, from the combined details-modal design pass (mock at mock-details.html). Approved by the user:
- Band 4 order becomes: corrections, updates, ABOUT+screenshots, ALSO COVERS, EXTENDS, EXPANSIONS, LISTS. This reverses the recorded reason at GameDetailsView.axaml:899 that ALSO COVERS leads because it is a fact about identity; the superseded sentence goes to docs/decisions.md.
- Ratings become a reception line in Band 1 under year and publisher, NOT a new section. Three figures, each attributed with its count: IGDB users, IGDB aggregated critics, Steam.
- Steam shows its own label ("Very Positive") with the percentage and count on hover.
- Acquisition facts: acquired_at and license_type in the left column under ON DISK. Price paid NEVER appears in this modal — section 7 never be smug; "$59.99 / never opened" is the sentence the product must not write. Price goes to export and account stats.
- Screenshots go inside ABOUT as a thumbnail strip that expands one shot to a hero above it, inline in the modal tree, no popup.
- Refetch is a More menu row with its status on a Band 3 TextBlock outside the scroll region.
- The update list is renamed so it stops colliding with the Band 2 rail; the rail keeps SINCE YOU PLAYED. Mock placeholder is "What landed" and a better name is welcome.
- TASK-115 ships BOTH halves in one pass: the release-to-today axis AND the backfilled monthly bars.
- The rule that governs future additions: a label section heading in Band 4 is earned by a list of rows the user can act on, ABOUT being the single prose exception. A fact about the game goes in Band 1; a fact about this copy goes in the left column; a picture goes inside ABOUT; an act goes in the More menu with its status on the strip.

Closed by TASK-129. GameTileViewModel.AutomationName now states the badge in words with its count (UnreadCopy.TileBadge: "Patched since you played: 3 updates."), and BucketViewModel.AutomationName gives the rail's Flare-marked Patched row its count and meaning instead of the "Avalonia.Controls.Grid" its ContentControl peer was falling back to. The count is spelled into the string because Avalonia's PositionInSet and SizeOfSet are wired to nothing - verified against the 11.3.20 source. It is carried out of the same major_update aggregate that gives the badge its timestamp, under the same acknowledgement watermark, so the words and the dot cannot disagree. Pinned by UnreadAccessibleCopyTests.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Delivered as part of TASK-129. The unread badge and the rail's Patched count now state themselves, with the count, in the accessible name; the count comes from the same query aggregate as the badge itself. Verified by UnreadAccessibleCopyTests (singular and plural wording, the zero-count branch, a tile with no badge gaining no words, the rail row, and the query counting only the pushes the badge stands for) and by AutomationNameReachabilityTests, which proves the names actually reach the UIA control view. Full suite green: 3331 / 152 / 82.
<!-- SECTION:FINAL_SUMMARY:END -->
