---
id: TASK-329
title: Build the desktop and fullscreen artwork browser with Steam and IGDB
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 16:10'
updated_date: '2026-09-17 16:50'
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
- [x] #1 Change artwork opens Hero, Cover and Icon tabs from game details and existing artwork edit rows; All, Steam and IGDB source filters show only appropriate available candidates and honest setup or unsupported states.
- [x] #2 Candidate selection previews without saving, shows current/selected state, source and dimensions, and supports paging plus source-specific loading, empty and retry states.
- [x] #3 Use artwork validates and saves only the selected slot; Back cancels the preview; Use automatic resets the slot; existing file and URL import remain available.
- [x] #4 Hero previews expose desktop and fullscreen crops, covers respect Fit/Fill, and icons include small-size and transparency previews.
- [x] #5 Desktop supports keyboard navigation and focus return; fullscreen provides a TV-scale page with controller navigation, source selection and import, preserving focus as results arrive.
- [x] #6 Both surfaces refresh committed choices and pass focused interaction, scaling, reduced-motion and failure tests; visual verification uses throwaway data; relevant user and visual docs are updated.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Use the stable TASK-328 selection and browser contracts; finish foundation verification alongside integration. 2. Implement app-owned Steam/IGDB candidate discovery and guarded provider paging, validated download/save/reset and imports. 3. Add separate desktop focused browser and fullscreen controller page through a shared view model. 4. Wire both library instances and editor entry points. 5. Exercise source failure, cancellation, save/reset, focus and previews in headless UI tests; update visual and user documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Focused UI verification passed46 tests (11 new browser cases plus details/fullscreen regressions): preview/back without writes, source failure/retry/paging, rapid reopen, slot selection retention, stale-current refresh, commit ordering, focus return to More and editor rows without lost drafts, controller focus preservation, text scale1.4 and reduced motion. Real GameDetailsView and FullscreenView captures inspected using synthetic artwork and isolated headless data. Final per-slot scroll retention and full solution checks in progress.

Final full solution verification passed:6631 tests,0 failures,2 Linux-only skips on Windows, including all832 UI tests. Final13 browser regressions include per-slot scroll retention on both surfaces. Desktop/fullscreen styled captures inspected; physical controller hardware and live authenticated provider calls were not required or exercised. Build has0warnings/errors; diff check and44 migration hashes pass.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Implemented shared Hero/Cover/Icon browsing with Steam, IGDB and optional plugin sources, explicit previews/save/reset/import, crop and transparency previews, provenance links, source failure/paging states, per-slot state and scroll retention. Separate desktop and fullscreen views preserve keyboard/controller focus and metadata drafts. Verified13 focused browser tests and full832 UI tests, with styled real-host headless captures; full solution6631passed.
<!-- SECTION:FINAL_SUMMARY:END -->
