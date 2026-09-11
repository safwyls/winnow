---
id: TASK-81
title: Standardize provider connection-state and credential-consent copy
status: Done
assignee:
  - '@codex-ui'
created_date: '2026-09-03 00:58'
updated_date: '2026-09-11 18:49'
labels: []
dependencies: []
documentation:
  - design-system.md
priority: low
ordinal: 108000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Add concrete connection-state and credential-consent entries to the visual copy table so screens do not choose wording independently. Cover no stored connection, session renewal/expiry, local-only discovery and optional consent while preserving provider-specific capabilities and failure meanings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 The design-system copy table includes concrete wording and usage conditions for no stored connection, renewal/expiry, local-only providers and credential consent.
- [x] #2 Desktop platform strings match the approved wording while retaining factual provider-specific differences.
- [x] #3 Fullscreen platform and consent flows use the same meanings and appropriately concise wording, including actionable error and optional-state distinctions.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Record exact current state and credential-consent wording with usage conditions in the visual copy table. 2. Align local-only and optional-state recovery messages on desktop/fullscreen while preserving provider-specific capabilities. 3. Verify rendered copy and consent paths in headless tests and commit.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SteamConnectionCopy, StoresViewModel and FullscreenPlatformTools hold current provider text. design-system.md section 7 has general guidance but lacks the concrete state/consent rows requested here. The task remains useful documentation and copy alignment.

Added concrete copy-table entries for stored connection absence, renewal due/failing, expired sessions, local-only GOG, credential lifetime and optional purchase consent. Both consent views display the shared scope/lifetime text; fullscreen purchase consent announces Off/On. Fullscreen Epic now shows the shared capability gap and preconnection promise. GOG wording names the local source without promising complete data. StoresAccountContextTests and FullscreenPlatformTests: 25 passed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Documented shared connection and consent wording, aligned fullscreen Epic explanations, clarified GOG local discovery and fixed consent state announcements. Verified 25 desktop/fullscreen UI cases.
<!-- SECTION:FINAL_SUMMARY:END -->
