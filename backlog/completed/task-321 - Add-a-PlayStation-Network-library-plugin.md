---
id: TASK-321
title: Add a PlayStation Network library plugin
status: Done
assignee:
  - '@codex'
created_date: '2026-09-17 03:11'
updated_date: '2026-09-17 03:26'
labels: []
dependencies: []
references:
  - 'https://www.npmjs.com/package/psn-api'
  - 'https://andshrew.github.io/PlayStation-Trophies/'
ordinal: 363000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
PlayStation libraries are missing from Winnow. Use psn-api and PlayStation-Trophies protocols to offer a separately packaged provider alongside Xbox through existing plugin settings.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 SDK-only PSN ZIP imports available PS4/PS5 account library entries, optional played history and legacy trophy-title history with stable identities and source labels.
- [x] #2 Protected credentials, account isolation, bounded pagination, cancellation and offline fallback preserve prior observations.
- [x] #3 Available playtime, last played, metadata and artwork use existing contracts without claiming installs or local launch.
- [x] #4 Desktop and fullscreen settings and imported facts have recorded verification.
- [x] #5 Fixture tests, solution build and packaging pass, with setup and live-validation limits documented.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Verify Xbox SDK and PSN protocols. 2. Implement protected credentials and account-scoped bounded data client. 3. Add artwork, fixtures, package and desktop/fullscreen checks. 4. Build, test, package and document limits.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented SDK-only PSN provider with NPSSO secret editor, protected refresh state, account-scoped complete-page caches, active PS4/PS5 library and independent opt-in played/PS3-Vita trophy history. No SDK changes, account device-code UI, local installs, launch actions or fabricated dates. Exact Sony title IDs join purchases/history; exclusive legacy trophy sets remain separate. Both desktop/fullscreen use generated settings and PlayStation store naming.

Verification: Release solution build succeeded with zero warnings/errors. 130 final PSN tests passed covering auth, library parsing/cache, artwork and wrapper races; six new headless UI cases cover desktop/fullscreen credential input, save/removal, toggles, source labels, filters and no local launch. Existing focused UI suite passed 39/39. Package script produced ZIP; real PluginCatalog smoke imported it disabled, enabled/reloaded it and invoked the unconfigured provider without network. Migration verifier passed all 43 hashes. Full suite still running.

Independent review found an unrelated-release artwork null result suppressing grouped PSN covers. Fixed to return a complete empty result for unrelated stores and added a passing regression. Protocols checked against supplied upstream sources. Live Sony login/account inventory/play-duration accuracy not exercised; setup guide and roadmap explicitly retain that limitation.

Full Windows Release solution suite completed successfully: 6,397 passed and 2 Linux-only tests skipped, including 809 UI tests. This run contained the first 119 PSN cases; the final expanded PSN suite and grouped-artwork fix were separately rebuilt and passed all 130 cases. Final ZIP was rebuilt and its exact contents, disabled discovery, enable/restart loading and credential-free invocation were rechecked through PluginCatalog. ZIP SHA-256: 516E88D511B04885BA101FFD7087B4EF4B409EE4CC8EF56D69FF951B66A88DBE. TRX evidence: C:/Temp/winnow-psn-full/test-results and C:/Temp/winnow-psn-plugin-verify/test-results. No live account credentials or production library were used.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Added an optional PlayStation SDK provider and installable ZIP for the PS4/PS5 account library, opt-in played and PS3/Vita trophy history, protected NPSSO/refresh handling, metadata and measured icon covers. Desktop/fullscreen generated settings, source labels and filters have headless interaction coverage. Release build, 130 final PSN tests, full Windows suite, migration verification and real ZIP loader smoke passed. Documented setup and unvalidated live Sony behavior.
<!-- SECTION:FINAL_SUMMARY:END -->
