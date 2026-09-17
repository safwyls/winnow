---
id: TASK-329
title: Build the desktop and fullscreen artwork browser with Steam and IGDB
status: To Do
assignee: []
created_date: '2026-09-17 16:10'
labels: []
dependencies:
  - TASK-328
documentation:
  - doc-1
type: feature
ordinal: 371000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users currently have to import image files or URLs and cannot compare available artwork inside Winnow. Provide an app-owned browser with built-in Steam and IGDB sources and room for plugin sources, following the proposed interaction design in doc-1.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 Change artwork opens Hero, Cover and Icon tabs from game details and existing artwork edit rows; All, Steam and IGDB source filters show only appropriate available candidates and honest setup or unsupported states.
- [ ] #2 Candidate selection previews without saving, shows current/selected state, source and dimensions, and supports paging plus source-specific loading, empty and retry states.
- [ ] #3 Use artwork validates and saves only the selected slot; Back cancels the preview; Use automatic resets the slot; existing file and URL import remain available.
- [ ] #4 Hero previews expose desktop and fullscreen crops, covers respect Fit/Fill, and icons include small-size and transparency previews.
- [ ] #5 Desktop supports keyboard navigation and focus return; fullscreen provides a TV-scale page with controller navigation, source selection and import, preserving focus as results arrive.
- [ ] #6 Both surfaces refresh committed choices and pass focused interaction, scaling, reduced-motion and failure tests; visual verification uses throwaway data; relevant user and visual docs are updated.
<!-- AC:END -->
