---
id: TASK-192
title: Bind Steam history caches and account confirmation to captured credentials
status: In Progress
assignee:
  - '@enrichment-api'
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 06:17'
labels:
  - architecture
  - review
dependencies: []
references:
  - 'src/Winnow.Enrich.SteamWeb/SteamHistoryClient.cs:45'
  - 'src/Winnow.Enrich.SteamWeb/SteamHistoryClient.cs:146'
  - 'src/Winnow.App/Services/SteamPlaytimeBackfillService.cs:443'
  - 'src/Winnow.App/Services/SteamPlaytimeBackfillService.cs:713'
  - 'src/Winnow.App/Services/StoreConnections.cs:119'
documentation:
  - docs/architecture-review-2026-09-10.md
priority: high
type: bug
ordinal: 223000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Architecture review 2026-09-10, R04. Evidence: Source verified. SteamHistoryClient reads the global steam-web/lastplayed cache before credentials, and Replay cache entries are keyed by account/year without credential provenance. Credential changes invalidate memoized credentials and confirmation but not these payloads. Backfill can combine cached data from account A with account B's credential generation, treat cached Replay as newly disclosed, and confirm A against B's fingerprint. Play history, first-play anchoring and account confirmation can cross account boundaries after credential replacement or restart. Partitioning data and validating which credentials actually disclosed it are separate requirements.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 Each cached history payload is tied to its captured account and credential/disclosure provenance; unscoped legacy data cannot attest a newly selected account.
- [x] #2 Account confirmation requires suitable evidence from the current credential generation; stale or cached responses cannot manufacture fresh disclosure.
- [ ] #3 Tests cover credential replacement, restart with warm caches, differing API-key/session accounts and delayed responses during sign-out/sign-in; both surfaces display consistent account state.
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Carry captured nonsecret credential identity through history responses and partition durable caches by that identity and account/year, rejecting legacy unscoped entries. 2. Require compatible current provenance for Replay disclosure and anchors; confirmation records the captured credential rather than the current key. 3. Add deterministic account-switch, warm-restart, session/key disagreement and delayed-response regressions through real clients and repositories. 4. Run serialized focused tests and update narrow build-spec account/cache rules plus decision history; verify shared desktop/fullscreen account state.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented captured SteamCredentialIdentity across authorized history responses, versioned credential-scoped caches, backfill joins and confirmation. Legacy unscoped entries are ignored; response publication rechecks current credentials; cached Replay never supplies fresh confirmation. Confirmation settings write atomically in production and use the captured fingerprint. Release focused test suite passed 94/94, wider SteamWeb/account/renewal suite passed 391/391 with no warnings. New tests cover durable restart, legacy caches, differing key/session accounts, delayed replacement and a key change between Replay and anchors; shared AccountVisibility state is verified. Root integration will run desktop/fullscreen Stores headless checks before AC3 is checked. Build spec section 4.2 and decision record updated; no live APIs or user launcher data touched.
<!-- SECTION:NOTES:END -->
