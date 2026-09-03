---
id: TASK-47
title: >-
  Re-check licenses paginator fetch against Content Security Policy on a live
  session
status: To Do
assignee: []
created_date: '2026-08-29 21:55'
labels:
  - auth
  - ingest
milestone: m-4
dependencies: []
priority: medium
ordinal: 56000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The embedded licenses-walk uses `fetch()` with `DOMParser` inside the WebView to follow Steam's paginator. This same-origin fetch with credentials works in the tested session, but Steam could tighten its Content Security Policy to restrict fetch or script execution. A future live session should re-verify that the approach still works, particularly after any observed Steam frontend update. Source: docs/spikes/steam-gdpr-export.md section 8 (harvest selector verdicts); code in `SteamHarvestScripts.LicensesWalkHelpers`.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 A live session on a real account confirms that `fetch()` to the paginator URL succeeds under the page's current CSP
- [ ] #2 Any CSP-related failure is documented and a fallback strategy is recorded
<!-- AC:END -->
