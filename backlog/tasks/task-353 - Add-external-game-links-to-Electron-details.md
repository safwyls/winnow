---
id: TASK-353
title: Add external game links to Electron details
status: Done
assignee:
  - codex
created_date: '2026-09-27 03:45'
updated_date: '2026-09-27 03:53'
labels: []
dependencies: []
type: feature
ordinal: 389000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Details need direct access to launcher and reference pages, and the update acknowledgement button currently touches the final update divider.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The update acknowledgement button has clear space above it.
- [x] #2 Details offer Steam viewing, store, patch notes, SteamDB and IGDB links when their verified identities or URLs are available, including non-Steam store pages.
- [x] #3 External navigation failures are shown; Steam viewing is allowed without permitting game launch protocols through the link bridge.
- [x] #4 Desktop and fullscreen layout and link routing are verified with focused tests and isolated native inspection.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Build game links from existing workspace IDs and cached storefront URLs; add a compact links sidebar panel and update-button spacing; permit only the Steam game-details navigation route in the main-process validator; test ID resolution, unavailable links, errors and both modes, then inspect and rebuild the packaged app.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Added a 20px top margin to the update acknowledgement action. Explore the game appears beneath Your editions and derives links from the selected game releases, workspace IGDB identity and cached Epic/GOG store URLs; duplicate destinations are removed, distinct Steam editions are labeled, and unavailable or unsafe provider destinations are omitted. IGDB reuses the base-36 short-link convention already used by the Avalonia frontend. Steam-owned copies use steam://nav/games/details; other mapped copies use the Steam store view. The external bridge accepts only that additional navigation route and still rejects launch/install/uninstall commands. Navigation failures render a retryable message. Validation: 17 focused tests passed across game-links, shell and app; production TypeScript/Vite build and unpacked Windows packaging passed. Native packaged inspection at 1440x980 desktop and 3440x1440 fullscreen verified all five link rows, API ID resolution, scrolling, and update-button spacing. Used a temporary manual fixture in C:\Temp\winnow-electron-20260926 and removed it after inspection. External handlers were mocked in tests; no game or real Steam session was launched.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added spaced update acknowledgement and an Explore the game sidebar with Steam viewing, store, patch notes, SteamDB and IGDB destinations when known. Verified 17 focused tests, production build, packaged launch and native desktop/fullscreen rendering. Updated the frontend guide and kept external navigation separate from game launch actions.
<!-- SECTION:FINAL_SUMMARY:END -->
