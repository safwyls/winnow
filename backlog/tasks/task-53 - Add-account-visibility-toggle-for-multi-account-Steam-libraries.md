---
id: TASK-53
title: Add account visibility toggle for multi-account Steam libraries
status: Done
assignee:
  - '@claude'
created_date: '2026-08-30 00:36'
updated_date: '2026-08-30 01:51'
labels:
  - ingest
  - resolve
  - ui
  - data
dependencies: []
priority: high
ordinal: 53000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
Winnow ingests games from every Steam account found on the system and collapses them to a single ownership per (release, store) via `ResolvePlaytimeWinner`. The user wants a toggle between two modes: show everything from all local accounts (current behavior), or show only games belonging to the account that matches the configured Web API key. The implementer must resolve a structural tension: ownerships are keyed `(release_id, store)` with `account_ref` outside the identity, and `account_ref` stores only the winning account's reference, so a game owned by two accounts carries one `account_ref` that may not be the API account even when that account also owns the game. An honest per-account filter therefore needs per-account ownership observation data, which was previously deferred as too invasive (see review finding F10's account-identity discussion, ROADMAP section 6).
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [x] #1 A toggle in the UI switches between all-local-accounts and this-account-only (identified by the stored Web API key's Steam ID), and its label states plainly what is hidden when filtered
- [x] #2 In single-account mode, a game the API account owns is never hidden because another account played it more and won the account_ref
- [x] #3 The filter is honest: per-account ownership observation data supports the toggle rather than relying solely on the current account_ref column, or the task documents and accepts any residual inaccuracy before shipping
- [x] #4 The toggle default is all-accounts so existing behavior is unchanged until the user acts
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Migration 0015 creates `ownership_accounts(ownership_id, account_ref, playtime_minutes, last_played_at, source, first_seen_at, last_seen_at)`, PK `(ownership_id, account_ref)`. Seed rows from existing non-null `ownerships.account_ref`. Rows are append/update only, never deleted.
2. Add an `Accounts` list to `CandidateOwnership` carrying per-account ref, latest playtime and last-played. `SteamLibrarySource` emits one entry per account with localconfig evidence; single-account games where the account has never played receive sole-account attribution. `SteamOwnedGame` emits the queried account's entry. `GogLibrarySource` mirrors its ref. Epic is unchanged.
3. `CandidateOwnershipMerge.Coalesce` unions the `Accounts` lists by ref, merging within a single account only (max minutes, later date). Cross-account merge is never performed.
4. `ExternalIdResolver` upserts membership rows in the same unit of work as the ownership write. `first_seen_at` is write-once; `playtime_minutes` takes the max; `last_played_at` takes the later date.
5. `RemoteOwnershipSyncService` enumerates accounts from the union of `Accounts` refs, closing the gap where the never-wins-account query returns no results.
6. `SteamPlaytimeBackfillService.ConfirmAccountAsync` also writes the setting `steam.owned_account_ref`. Changing the API key clears this setting.
7. `GetOwnershipBucketsAsync` reads `library.account_scope` (values: `all`, `own`). In `own` mode, hide a game only on positive evidence that no `ownership_accounts` row names the owned account; games with no evidence stay visible. Where a membership row exists for the owned account, substitute that account's playtime and last-played for the household-level figures, and derive buckets from the substituted values. In `all` mode, the query is unchanged.
8. Toggle UI in the Stores section beside the Steam key state. Disabled until the key's account is confirmed. Label states the count of hidden games. Changing the toggle persists the setting, reloads the library, and invalidates the feed.
9. Tests per the design's 14-item list, plus cases verifying per-account figure substitution and bucket derivation from substituted figures.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Decision note, 2026-08-30. Four decisions signed off by the user. (a) Filtered mode displays the owned account's own playtime and derives buckets from it: where an `ownership_accounts` row exists for the owned account, the library grid and bucket queries use that row's `playtime_minutes` and `last_played_at` rather than the ownership-level household figures. This overrides the plan's original recommendation to use household figures in all modes. Residual divergence: the recommender's episode signal still reads the ownership-level playtime series, so scoring and display can diverge for shared games. Accepted as a known gap until a yours-vs-household episode distinction is built as a follow-up. (b) Games without account evidence stay visible in filtered mode: a game is hidden only when positive evidence exists that no membership row names the owned account. (c) The toggle lives in the Stores section beside the Steam API key state, disabled until `steam.owned_account_ref` is confirmed. (d) Family Sharing play under the owned account counts as mine for visibility: the filter is account-identity, not purchase-identity; play under your account is yours regardless of who bought the license.

Implementation started 2026-08-29. Research pass complete: read LibraryQueryRepository (acknowledgement-watermark precedent), CandidateOwnership/Merge, ExternalIdResolver (incl. the new PlaytimeToleranceMinutes band), SteamLibrarySource, LibrarySyncService, SteamPlaytimeBackfillService, OwnershipRepository, StoresView/StoresViewModel, DisplaySettingsViewModel. Confirmed 0015 is the next migration number.

Steps 1-8 implemented, solution builds clean (0 warnings, TreatWarningsAsErrors on). Migration 0015 ownership_accounts(ownership_id, account_ref, playtime_minutes, last_played_at, source, first_seen_at, last_seen_at) PK (ownership_id, account_ref), seeded from non-blank ownerships.account_ref joined to the newest play record, stamped source='ownerships.account_ref'. CandidateOwnership gains an Accounts init-property (IReadOnlyList<CandidateAccount>, defaults empty) rather than a 12th positional parameter; CandidateOwnershipMerge.Fill unions the lists by ref, merging within one account only. ExternalIdResolver upserts membership rows in the same unit of work, falling back to the candidate's own AccountRef when a source enumerated none. LibraryQueryRepository gains owned_account/mine/hidden/effective_play CTEs and CountHiddenByAccountScopeAsync. Toggle seam is IAccountVisibility/AccountVisibilityService; StoresViewModel carries the copy and a ReloadLibrary callback wired by MainWindowViewModel to reload both library and feed. Tests next.

All 9 plan steps complete. Full suite green: Winnow.Tests 2072/2072, Winnow.Recommend.Tests 75/75, Winnow.Covers.Tests 70/70 (2217 total, 0 failures), build 0 warnings under TreatWarningsAsErrors. New tests: AccountScopeTests (15), AccountMembershipTests (13), OwnedAccountConfirmationTests (6), AccountScopeFeedTests (1, Recommend), plus 3 added to SteamLibrarySourceTests and MigrationTests.Migration_0015_seeds_memberships_from_the_winning_account_ref. Two pre-existing tests needed mechanical updates for the new migration and DI registration: DatabaseBackupTests.Rewind (its comment already says 'a new migration adds a line') and LocalLibrarySyncContractTests' minimal container. One deviation from the recorded plan, in the honest direction: the filter refuses to hide on the strength of a 0015 SEED row alone, because a seed inherits the single-winner ambiguity the table replaces and would reproduce AC#2's bug for the window between migration and first sync; a seed naming the owned account IS enough to keep a game (presence and absence are different claims). Two extensions beyond the literal plan: the hide predicate is scoped to store='steam' so GOG/Epic ownerships can never be hidden by a Steam account reference, and SteamPlaytimeBackfillService.SteamAccountsAsync also unions ownership_accounts refs so a user whose account never wins account_ref can still be confirmed. steam.owned_account_ref is now written only on a disclosure in the same pass, not on a persisted marker, so a changed key cannot inherit the previous owner's identity. ROADMAP section 6 carries a new debt bullet recording the accepted recommender divergence and the positive-evidence under-reach. Not finalized; nothing committed.

Batch-review findings F1, F2, F4 fixed 2026-08-29. F1: added an owned_account_attested CTE to the bucket query — the hidden predicate now fires only when the owned account has at least one NON-SEED row somewhere in store='steam', which is proof its evidence pass (GetOwnedGames, whose failure is caught and logged) has actually run. Without it, a confirmed account whose owned-list pass had not succeeded would have had every owns-but-never-launched game a housemate played hidden, since the local scan attests only to games that were PLAYED. Two tests added; three existing filter tests now seed an attesting row as a precondition. F2: replaced the max ratchet on ownership_accounts.playtime_minutes with the TASK-50 err-low discipline — within PlaytimeTolerance.Minutes either direction the LOWER figure wins (so a filtered library can no longer read a minute above an unfiltered one on the same sawtooth ownerships), a rise beyond the band records as play, and a fall beyond the band records only when the incoming last_played is at least as current as the stored one. That last rule is the correction path the reviewer asked to be decided coherently: membership rows are refreshed current facts rather than an append-only series, so a correction has somewhere to land, but a lower figure from a reader that is behind (older or absent date) is a blind spot and is ignored. Reasoning is in the repository doc comment and the interface contract. The tolerance literal moved to Winnow.Core.Domain.PlaytimeTolerance.Minutes and ExternalIdResolver.PlaytimeToleranceMinutes now delegates to it, so the two layers cannot drift; pinned by a test. CandidateOwnershipMerge.UnionAccounts deliberately keeps max — erring low in-pass would put the first membership write a minute BELOW the first ownership write, creating the divergence the band removes; noted in its comment. Six tests added. F4: the hidden count left the ToggleSwitch face and now renders beside it in the data face (Plex Mono tnum) as [DATA figure] + prose, the same split the card header uses for per-store title counts; the switch face is now the plain 'Show only your account'. Copy via docs-writer: the count line is state-neutral ('N games from other accounts') and is suppressed entirely at zero. Full suite green: 2080 + 75 + 70 = 2225, 0 failures, build 0 warnings. Not finalized; nothing committed.
<!-- SECTION:NOTES:END -->

## Final Summary

<!-- SECTION:FINAL_SUMMARY:BEGIN -->
Migration 0015 added the `ownership_accounts` table recording per-account membership for each ownership, seeded from the existing `account_ref` winner; seed rows are marked as presence evidence only and never treated as absence evidence. Ingest now emits per-account entries and the resolver upserts membership in the same unit of work, with err-low banding and a date-corroborated correction path; the bucket query hides a Steam game in own-account mode only when a non-seed evidence pass has attestably run, positive evidence exists for the game, and none of it names the owned account, substituting that account's playtime and last-played where a membership row exists. The Stores-section toggle disables until its account is confirmed via same-pass disclosure with a SHA-256 key fingerprint guarding key changes, and the hidden count renders in the data face. Verified by batch review (go verdict, three findings fixed and re-tested) and the full suite at 2,225 passing with 39 new tests across ingest, merge, resolver, migration, query, feed, and view-model layers; two accepted divergences (recommender episode signal and details-modal history) are recorded in ROADMAP with the fast-follow, and live exercise of the toggle on the user's real library happens on the next launch.
<!-- SECTION:FINAL_SUMMARY:END -->
