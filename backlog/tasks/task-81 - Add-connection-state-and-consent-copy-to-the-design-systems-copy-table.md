---
id: TASK-81
title: Standardize provider connection-state and credential-consent copy
status: To Do
assignee: []
created_date: '2026-09-03 00:58'
updated_date: '2026-09-11 14:04'
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
- [ ] #1 The design-system copy table includes concrete wording and usage conditions for no stored connection, renewal/expiry, local-only providers and credential consent.
- [ ] #2 Desktop platform strings match the approved wording while retaining factual provider-specific differences.
- [ ] #3 Fullscreen platform and consent flows use the same meanings and appropriately concise wording, including actionable error and optional-state distinctions.
<!-- AC:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Audit evidence: SteamConnectionCopy, StoresViewModel and FullscreenPlatformTools hold current provider text. design-system.md section 7 has general guidance but lacks the concrete state/consent rows requested here. The task remains useful documentation and copy alignment.
<!-- SECTION:NOTES:END -->
