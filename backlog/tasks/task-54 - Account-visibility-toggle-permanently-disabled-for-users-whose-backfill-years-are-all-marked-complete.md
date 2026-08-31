---
id: TASK-54
title: >-
  Account visibility toggle permanently disabled for users whose backfill years
  are all marked complete
status: Done
assignee:
  - '@claude'
created_date: '2026-08-30 02:40'
updated_date: '2026-08-30 02:53'
labels:
  - enrich
  - data
  - ui
dependencies: []
priority: high
ordinal: 54000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The account visibility toggle added in TASK-53 enables only when `steam.owned_account_ref` is present. `SteamPlaytimeBackfillService` writes that ref exclusively during a same-pass Year-in-Review disclosure of the account id. An account whose backfill years are already marked complete refetches only the current year; an uncompiled current-year Replay returns empty with no account id, so the disclosure never fires again. Found live 2026-08-30. Fix direction: when the ref is absent, the key fingerprint matches or is unset, and confirmed markers exist for the account, refetch one known-populated marked year purely for its disclosure. The full-fact observation identity makes the re-import a no-op, and the key-change fingerprint clear still guards against inheritance.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 An account with all backfill years marked complete and no stored owned_account_ref gets its ref written on the next backfill pass via a disclosure refetch of a populated year, with zero new observation rows
- [x] #2 A freshly pasted different key never inherits the previous ref; fingerprint clear still fires first
- [x] #3 An account with no populated marked years (nothing to disclose from) leaves the toggle disabled with the existing explanatory copy
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Read what the year markers actually store: SteamPlaytimeBackfillService.RecordCompletionAsync writes '{ISO stamp};games={N};written={M}' per (account, year), so 'games=' is a recorded per-year populated count and is enough to pick a year that will disclose. No new storage needed.
2. Add a disclosure-refetch step to BackfillAccountAsync, placed after the pending-year fetch loop and before the !confirmed branch, so it runs only when the ordinary path already failed to disclose. Preconditions, all three required: this pass did not disclose; steam.owned_account_ref is absent (blank counts as absent); the ConfirmedPrefix marker exists for this account; and the stored key fingerprint either matches the key in force or is unset.
3. Choose the year from the markers: parse 'games=' out of each marked year from FirstYear..currentYear-1, newest first, keeping those with games>0. The current year is excluded because it was just fetched and did not disclose. Markers that cannot be parsed are kept as lower-priority candidates so a marker written by an older build still has a path. Attempts are bounded at 3.
4. Fetch that year purely for its disclosure and read AccountId. Nothing is imported and no anchor is fetched, so the pass structurally writes zero observation rows rather than relying on the full-fact identity to swallow a re-import. AccountMismatch abandons the account exactly as the main loop does.
5. Cache policy derived from the fingerprint state, because a cached body fetched with a PREVIOUS key would disclose the previous account and re-establish the ref the fingerprint clear had just removed. Fingerprint matches the key in force: use the ordinary 6h client cache. Fingerprint unset (never recorded, or just cleared by a key change): force a refetch with cacheTtl TimeSpan.Zero so the disclosure is made with the key actually in force.
6. On disclosure, mark the pass disclosed and let the existing ConfirmAccountAsync write the ref and the fingerprint. No second write path.
7. Keep ReconcileOwnedAccountWithKeyAsync where it is, at the top of BackfillAsync before any account is processed, so the key-change clear always precedes any disclosure attempt.
8. Tests for the three ACs: all-years-complete plus empty current year writes the ref with zero new play_records and playtime_snapshots rows; a changed key clears first and never inherits the previous ref, with the ordering pinned; an account whose marked years are all empty discloses nothing and leaves the toggle disabled. Plus a test that the chosen year is the newest populated one, and one that the cache is bypassed when the fingerprint is unset.
9. Full suite across all three test projects. No commits, no finalization.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Implemented 2026-08-29. Root cause confirmed as recorded: every year but the current one is asked about once per install, so a finished account refetches only the current year, and an uncompiled current-year Replay answers empty with no account_id — so the same-pass disclosure that writes steam.owned_account_ref could never fire again on exactly the accounts that had already backfilled.

Fix, in SteamPlaytimeBackfillService: after the pending-year fetch loop and before the !confirmed branch, if the pass did not disclose and NeedsDisclosureRefetchAsync agrees, DiscloseFromCompletedYearAsync re-reads one already-imported year purely for the account id in it. Preconditions are all four of: this pass did not disclose; steam.owned_account_ref is absent; the ConfirmedPrefix marker exists for the account; and the stored key fingerprint matches the key in force or is unset.

Year selection reads the completion markers, which already record 'games=N' (RecordCompletionAsync writes '{stamp};games={N};written={M}'), so no new storage was needed. Candidates are marked years from FirstYear..currentYear-1, newest first, games>0 only; markers that cannot be parsed are kept as lower-priority candidates for an older build's shape; attempts bounded at 3. Years recorded as games=0 are never asked about — an empty Replay is what the current year already answered.

Nothing is imported: the year's games are read and dropped, no anchor is fetched and no write transaction is opened, so zero observation rows by construction rather than by the identity indexes swallowing a re-import. Completion markers are not rewritten. AccountMismatch abandons the account exactly as the main loop does.

Cache: the ordinary 6h client TTL when the stored fingerprint matches the key in force (the live case). Forced fresh (cacheTtl TimeSpan.Zero) when nothing records which key the cached bodies were fetched with, because a cached body from a PREVIOUS key would disclose the previous account and hand back the identity the fingerprint clear had just removed. ReconcileOwnedAccountWithKeyAsync still runs at the top of BackfillAsync, before any account is touched; pinned by a test.

One change beyond the recorded plan, in the honest direction: ISteamApiKeyProvider is now a REQUIRED constructor dependency rather than an optional one. With it optional, a missing registration silently disabled the fingerprint guard, and the reconcile then cleared the ref on every pass (no fingerprint could ever match), turning the repair into a per-launch refetch loop — which is what two existing SteamPlaytimeBackfillTests caught. AddSteamPlaytimeBackfill already documents AddSteamWebApi() as a prerequisite and that is what registers the provider, so requiring it is safe. Winnow.App gained InternalsVisibleTo(Winnow.Tests) so tests use the real marker-key constants instead of reproducing the format by hand — the way this bug would otherwise have escaped a test again. A shared FakeSteamApiKeyProvider test double replaces two local copies.

13 new tests in OwnedAccountDisclosureRefetchTests covering the three ACs, including the zero-new-rows assertion (the re-read year carries real months and the anchor endpoint answers for the same appid, so an importing implementation would leave rows), the newest-populated-year selection, the games=0 skip, the fingerprint-clear ordering pin, and both cache policies. Full suite green: 2093 + 75 + 70 = 2238, 0 failures, build 0 warnings. Built via --artifacts-path; the user's Winnow.exe was running (PID 58356) and was not touched. Not finalized; nothing committed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
The backfill now refetches disclosure data when the owned-account ref is absent, confirmed markers exist, and the key fingerprint matches or is unset; it selects the newest marked year with a nonzero game count (bounded at 3 attempts, zero-game years skipped) and imports nothing by construction since no anchor fetch or write transaction runs. A missing fingerprint forces an uncached fetch so a cleared identity cannot be re-disclosed from stale cache bodies. The key provider is a required constructor dependency after tests showed an absent registration silently disabled the fingerprint guard and produced a per-launch refetch loop. Verified 2026-08-30 by 13 new OwnedAccountDisclosureRefetchTests covering all three acceptance criteria including zero-new-rows and fingerprint-clear ordering, full suite 2,238 passing. Live confirmation happens on the user's next relaunch: the toggle should enable after one uncached Year-in-Review request.
<!-- SECTION:FINAL_SUMMARY:END -->
