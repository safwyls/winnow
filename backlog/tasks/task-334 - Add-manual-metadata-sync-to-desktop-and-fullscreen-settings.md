---
id: TASK-334
title: Add manual metadata sync to desktop and fullscreen settings
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 17:14'
updated_date: '2026-09-17 17:21'
labels: []
dependencies: []
ordinal: 376000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Users need to match and enrich the existing library on demand, including demo runs with automatic sync disabled, without enabling launcher imports.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Settings Metadata and artwork offers Sync metadata now on desktop and fullscreen, including no-sync runs.
- [x] #2 Explicit sync refreshes metadata for existing library entries and publishes changes without invoking library imports or changing automatic sync settings; saved matches and manual artwork remain authoritative.
- [x] #3 Both surfaces share running/progress/completion/error state, prevent duplicate requests, and provide actionable missing-credential feedback.
- [x] #4 Focused service and keyboard/controller tests verify scope and failure behavior; build passes and user documentation describes the action and cache behavior.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Reuse the serialized IGDB-relevant library refresh stages through an explicit manual service, with progress/result reporting and credential preflight. 2. Add a shared command view model and settings actions on desktop and fullscreen outside automatic-sync suppression. 3. Verify isolated service scope and both presentation paths; update documentation.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented explicit manual service through the existing serialized IGDB-relevant pipeline; service and ownership regression tests pass15/15. Running state and stage/result messages live in a singleton shared view model, registered regardless of no-sync/sample flags. Desktop and fullscreen actions bind the same command. Normal caches, pins and overrides remain unchanged; completion does not claim every game was matched.

Final verification: zero-warning solution build;510 metadata/IGDB/enrichment/ownership/artwork-preservation/accessibility-name tests pass;24 UI tests pass including6 new manual-sync cases, IGDB settings interactions and fullscreen accessibility. New UI cases cover Enter and controller activation, shared progress, duplicate prevention, retry, settings reopen, late progress and navigation while disabled. Inspected styled MainWindow1280x900 and FullscreenView1920x1080 captures under C:\Temp\winnow-manual-metadata-captures using isolated fixtures; no live library/network run. Composition registers manual service/view model outside automatic suppression, so no-sync/sample flags do not gate explicit invocation. No cache flush or new import path introduced.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added Sync metadata now to desktop/fullscreen Metadata and artwork settings. Explicit cache-aware metadata pass works independently of automatic sync and excludes ownership imports. Shared progress/result state, credential guidance, duplicate prevention and soft-failure reporting preserve existing pins/overrides. Verified534 targeted tests and styled captures; solution builds with zero warnings.
<!-- SECTION:FINAL_SUMMARY:END -->
