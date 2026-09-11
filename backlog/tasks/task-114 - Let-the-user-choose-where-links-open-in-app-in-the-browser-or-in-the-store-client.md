---
id: TASK-114
title: >-
  Let the user choose where links open: in app, in the browser, or in the store
  client
status: Done
assignee:
  - '@codex'
created_date: '2026-09-05 02:50'
updated_date: '2026-09-11 19:17'
labels:
  - ui
dependencies: []
documentation:
  - design-system.md
  - game-library-design.md
priority: medium
type: feature
ordinal: 141000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Provide a persisted preference for opening supported links in Winnow, the system browser or an installed store client. Current routing chooses a permitted destination per link and falls back when embedded reading is unavailable. The preference selects among supported routes while retaining GameLink validation and the embedded reader's origin/scheme restrictions.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A shared desktop/fullscreen preference chooses in-app, system browser or store client where supported.
- [x] #2 If the chosen route is unavailable for a link, use a predictable documented fallback and communicate it clearly.
- [x] #3 The preference cannot widen the embedded patch-note origin allowlist or bypass GameLink validation.
- [x] #4 The preference persists across launches and applies consistently from both presentations.
- [x] #5 An unavailable store client is not offered as a working destination; platform detection and provider-native boundaries remain explicit.
- [x] #6 Verify route selection and fallback for supported web/store links, missing clients and unavailable embedded browsing.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
Add a shared GameLink router and persisted destination preference. In-app opens only existing allowlisted patch notes; browser opens web links; store-client routing supports validated Steam store pages when a registered executable exists. Unsupported/refused web routes fall back to the browser with visible status. Native Play/Install/Uninstall and explicit client management stay native. Add desktop and fullscreen application controls and shared routing tests, preserve validation and document the support matrix.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: GameDetailsViewModel currently tries the embedded reader then falls back; StoreActions and GameLink validate and route targets. No persisted destination preference exists. The implementation must preserve these safeguards and document provider-specific route limits.

Implemented persisted link destination controls in Application settings on desktop and fullscreen, strict shared routing, Windows registered-client detection, native-action preservation and visible fallback/failure status. Initial validation: 66 route/policy/settings tests and 3 headless UI tests pass; desktop test activates the actual More-menu item. Added 2 further refused/throwing-reader cases. Production LibraryViewModel injection is the remaining integration step, sequenced after TASK109 to keep commits isolated. Support matrix is recorded in design-system section10.8 and game-library-design section5.1.

Final integration injects IGameLinkRouter through the production LibraryViewModel and registers client detection/router in Program. Verification passed: 68 backend route/policy/settings tests and 6 Avalonia headless tests, covering persisted controller/desktop choices, actual desktop More-menu activation, library-created details, visible fallback on both surfaces and preference-write failures on both settings surfaces. Read-only UI review found the missing fullscreen save-error display; fixed and regression-tested. Store-client routing is limited to canonical Steam store pages on Windows with a registered executable. Explicit native actions and reader allowlists are preserved.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added a persisted shared link destination preference with strict route support and visible browser fallback. Desktop and fullscreen settings/details use the same router; Steam client availability gates its option. Verified with 68 backend and 6 headless UI tests, including production library composition and failure feedback.
<!-- SECTION:FINAL_SUMMARY:END -->
