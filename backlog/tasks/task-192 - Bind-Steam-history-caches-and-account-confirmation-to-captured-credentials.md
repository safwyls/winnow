---
id: TASK-192
title: Bind Steam history caches and account confirmation to captured credentials
status: To Do
assignee: []
created_date: '2026-09-11 04:58'
updated_date: '2026-09-11 05:06'
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
- [ ] #1 Each cached history payload is tied to its captured account and credential/disclosure provenance; unscoped legacy data cannot attest a newly selected account.
- [ ] #2 Account confirmation requires suitable evidence from the current credential generation; stale or cached responses cannot manufacture fresh disclosure.
- [ ] #3 Tests cover credential replacement, restart with warm caches, differing API-key/session accounts and delayed responses during sign-out/sign-in; both surfaces display consistent account state.
<!-- AC:END -->
