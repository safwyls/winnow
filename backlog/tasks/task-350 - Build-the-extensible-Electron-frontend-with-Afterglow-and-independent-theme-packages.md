---
id: TASK-350
title: >-
  Build the extensible Electron frontend with Afterglow and independent theme
  packages
status: Done
assignee: []
created_date: '2026-09-27 00:11'
updated_date: '2026-09-27 00:59'
labels: []
dependencies: []
references:
  - docs/spikes/2026-09-26-electron-design/README.md
  - docs/frontend-api.md
  - docs/spikes/2026-09-26-electron-implementation/README.md
priority: high
type: feature
ordinal: 386000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Implement the approved Afterglow design as an independent Electron and TypeScript frontend over the public backend API. The user approved the recommendation to support appearance presets, configurable layouts, and versioned developer themes with an alternative composition proving the shared contracts.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Electron securely discovers and connects to the public backend with main-owned credentials, authenticated artwork, live invalidation, reconnect and explicit error states.
- [x] #2 Afterglow provides usable discovery, library, game details, lists, activity and session journals, settings and supported game actions on desktop and fullscreen.
- [x] #3 Users can persist and import/export appearance and layout profiles; versioned developer theme packages can replace screens through documented public data and action contracts, with compatibility checks and recovery.
- [x] #4 Afterglow and a distinctly different second composition use the same public theme interface without app-core changes; keyboard, reduced-motion and constrained-window behavior are verified.
- [x] #5 Focused automated tests, live temporary-backend checks, production build/package validation, and user/developer documentation are complete; remaining API limitations are stated accurately.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
The user approved implementation after reviewing the mock and the Playnite comparison. Establish a typed Electron bridge and frontend theme SDK. Delegate the secure shell, API-backed screens, and theme runtime while integrating the Afterglow and second composition locally. Verify the integrated frontend against a throwaway backend, test theme compatibility/recovery and concurrency, build a local package, and document the supported workflow.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented the approved independent Electron frontend in src/Winnow.Electron: secure main/preload API/SSE/art transport; Afterglow and Catalogue; desktop/fullscreen navigation; live library, details, lists/manual games, journals, metadata/artwork, connections and feedback; appearance/layout profiles and versioned trusted developer theme packages. Reading room is an independently installed example. No backend or Avalonia source changes.

Verification: npm run typecheck passes; opt-in full frontend suite 96 passed, 2 skipped across 13 files. Skips: sample has no recorded sessions; metadata sync would use configured IGDB credentials. Real backend tests covered DTOs, list/manual CRUD and revision conflicts, Epic challenge begin/cancel, health and initial SSE resync. Reviewer findings fixed: initial-read invalidation race, Guid N client IDs, persistent draft/revision/pending state across remount, uncertain create reconciliation, and feed error reporting.

Native Windows inspection covered the production custom protocol, authenticated art, Afterglow/Catalogue and light palette, details, responsive library at about 760px, Theme Studio, installed Reading room through the public SDK, keyboard recovery, fullscreen and a packaged fresh-library companion startup. npm run package passes with self-contained backend and 44 dependency notices; production npm audit reports zero vulnerabilities. New frontend CI workflow parses and runs npm tests/build; remote CI has not run. Evidence and screenshots: docs/spikes/2026-09-26-electron-implementation/README.md.

Remaining limits are documented in src/Winnow.Electron/README.md: first frontend has narrower features than Avalonia; actual game launch/account-auth completion, live journal roundtrip, physical controllers/TV distance and macOS/Linux packages are not validated. Test runs used throwaway data directories only. Full .NET tests were not repeated because backend source was unchanged; Release companion publish and live API checks passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Built Winnow Afterglow as an independent Electron/TypeScript frontend with secure API transport, desktop/fullscreen feature screens, configurable appearance and layouts, and a public developer-theme SDK demonstrated by Catalogue and an installed Reading room package. Verified with 96 passing frontend tests, live isolated-backend checks, native visual/keyboard/narrow-window inspection and a Windows x64 packaged-companion startup. Two credential/session-dependent checks skipped; feature and device-validation limits documented.
<!-- SECTION:FINAL_SUMMARY:END -->
