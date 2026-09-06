---
id: TASK-21
title: Add accessibility names and correct control hierarchy
status: To Do
assignee: []
created_date: '2026-08-29 21:53'
updated_date: '2026-09-06 00:18'
labels:
  - accessibility
  - ui
dependencies: []
priority: medium
ordinal: 74000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Views lack accessibility names and the control hierarchy does not correctly express the semantic structure to screen readers and automation. Finding F35. Source: stabilization-2026-08-28.md Group 2. Trigger: next view-authoring pass.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Every interactive control has an accessibility name
- [ ] #2 The control hierarchy expresses the semantic structure (headings, groups, lists)
- [ ] #3 A screen reader can navigate the primary views meaningfully
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
<!-- SECTION:NOTES:END -->
