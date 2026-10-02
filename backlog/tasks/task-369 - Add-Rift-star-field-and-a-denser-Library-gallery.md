---
id: TASK-369
title: Add Rift star field and a denser Library gallery
status: Done
assignee:
  - '@codex'
created_date: '2026-09-28 20:39'
updated_date: '2026-09-28 20:54'
labels: []
dependencies: []
type: spike
ordinal: 405000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The focused deck works for Discover, but Library needs to expose more games at once and differ clearly from Discover. The user also requested a star field behind the app.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Both views have a subtle decorative star field that preserves readability and has no continuous render loop.
- [x] #2 Library presents multiple rows of selectable covers with reusable materials and a details portal; Discover retains its focused deck.
- [x] #3 Verify desktop, fullscreen, keyboard selection, filters, constrained layouts and appearance controls; save updated mock captures without changing production or Afterglow.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Add a reusable static star field with a visibility/intensity control. 2. Introduce a Library gallery and compact persistent portal, with an option to expand the gallery. 3. Verify both surfaces and navigation, update the study documentation and capture the result.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a seeded static Canvas2D star field with visibility and brightness controls. Library now uses a multi-row cover gallery, compact captions, selectable artwork portal, collapsible details and keyboard row navigation; Discover retains the focused deck. Both presentation modes keep the viewport frame. Verified all 12 sample covers at 2025 px, 10 complete covers at 1280x720 desktop and 12 at fullscreen, plus the 390 px dismissible-portal fallback. Checked filter/search recovery, missing art, keyboard selection, active material canvas, hidden renderer suspension, star controls/reset and no portal/body overflow. All JavaScript syntax checks, HTML unique-ID/local-reference checks and git diff whitespace checks passed. Browser warning/error log was empty. Captures 05 and 06 plus README record the review. Production and Afterglow files unchanged. Hardware controller/touch and GPU performance were not measured.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Rift now has a quiet configurable star field and a Library gallery that exposes multiple rows beside a compact optional details portal. Discover remains a focused deck. Desktop and fullscreen previews verified with saved captures; this is still a design mock for review.
<!-- SECTION:FINAL_SUMMARY:END -->
