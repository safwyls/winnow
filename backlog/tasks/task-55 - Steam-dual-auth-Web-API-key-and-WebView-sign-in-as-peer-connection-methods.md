---
id: TASK-55
title: 'Steam dual auth: Web API key and WebView sign-in as peer connection methods'
status: In Progress
assignee:
  - '@claude'
created_date: '2026-08-30 02:57'
updated_date: '2026-08-30 15:32'
labels:
  - auth
  - security
  - ingest
  - ui
dependencies: []
priority: high
ordinal: 55000
---

## Description

<!-- SECTION:DESCRIPTION:BEGIN -->
The Stores screen currently offers only a Web API key for Steam authentication. This task restructures the Steam connection to present two peer methods, following Playnite's model: the existing Web API key, and a new embedded WebView sign-in. The WebView path would give Winnow the user's Steam identity directly (enabling the account visibility toggle without waiting for a Year-in-Review disclosure call to confirm the account), support keyless operation, and allow purchase-history capture in the same session. A prerequisite research spike must determine whether the WebView session can mint a `webapi_token` usable against the two endpoints Winnow depends on (`GetUserYearInReview`, `ClientGetLastPlayedTimes`), or whether a durable session is required. The spike's findings gate the design and will require a recorded revision of the section 4.7 amendment's binding conditions either way. Winnow must prompt for explicit permission before capturing purchase history during a WebView sign-in; declining the prompt leaves the sign-in fully functional for identity and playtime backfill.
<!-- SECTION:DESCRIPTION:END -->

## Acceptance Criteria
<!-- AC:BEGIN -->
- [ ] #1 The Stores screen offers Web API key and WebView sign-in as peer connection methods, each with a plain-language explanation of what it can and cannot do
- [ ] #2 A WebView sign-in prompts for explicit permission before any purchase-history capture in the same session; declining leaves the sign-in fully functional for account identity and playtime backfill
- [ ] #3 Credential material from the WebView path is protected at least as strongly as Epic's tokens (DPAPI CurrentUser scope, no plaintext on disk); whatever the spike determines should persist is recorded in a revised section 4.7 amendment with updated binding conditions
- [ ] #4 Account identity obtained from a WebView sign-in enables the account visibility toggle without the Year-in-Review disclosure refetch that the Web API key path requires
- [ ] #5 The design lands only after the token-viability spike's findings are recorded in docs/spikes and its conclusions reviewed
<!-- AC:END -->

## Implementation Plan

<!-- SECTION:PLAN:BEGIN -->
1. Credential seam. Introduce SteamCredential, ISteamCredentialProvider, and SteamCredentialSelector; convert the three hand-concatenated key= call sites in SteamHistoryClient and SteamWebApiClient to a single AppendTo. No behaviour change, key-only path works exactly as today. Unblocks every later stage by giving credentials a uniform shape.

2. Session storage. DPAPI-protected, settings-backed session store mirroring the Epic precedent with distinct entropy. Persists access token, expiry, audience, issuer, steamid, refresh token, and renewal bookkeeping; stores no cookie, no password, no page content. A session provider handles load, expiry, and lapse but not renewal. The section 4.7 second amendment lands here because this is where a durable credential first touches disk. Unblocks sign-in by giving it somewhere to write.

3. Sign-in session. ISteamSignInSession contract in Core and WebView2SteamSignInSession promoted from the verified probe. The mint-scope predicate moves into SteamAccountPagePolicy as a third narrower tier beside the harvest predicate. Probe scaffolding and its command-line switch are deleted. Unblocks identity unification by producing a token with a subject claim.

4. Identity unification. Extract one shared account-confirmation writer used by both the key path and the sign-in path. Generalise the credential fingerprint without changing the existing key digest. Sign-in writes the owned account immediately from the token subject; the key path's disclosure refetch is kept unchanged for key-only users. Acceptance criterion 4 lands here. Unblocks the stores UI by making both paths converge on a single account record.

5. Stores UI. Both connection methods presented with honest copy, sign-in first and key as a genuine alternative including in-app key input. Sign-in and sign-out commands, session health rendering, and the purchase-history permission control whose decline leaves sign-in fully functional. Acceptance criteria 1 and 2 land here. Unblocks renewal by surfacing the health state it will maintain.

6. Renewal. The finalizelogin and transfer_info exchange plus token mint, single-flight with rotation handling, failure classification separating hard lapse from transient, health transitions surfaced before the token dies, and scheduler integration. This is the riskiest stage and rests on untested refresh assumptions, which is why it is last: without it a signed-in user still has roughly a day of access and one-click re-sign-in, and keyed users are unaffected.

S2 execution detail (2026-08-30). New files under src/Winnow.Enrich.SteamWeb/Credentials/: ISteamSecretProtector + DpapiSteamSecretProtector/UnavailableSteamSecretProtector (DPAPI CurrentUser, entropy Winnow.Steam.Session.v1, ZeroMemory on both paths, no exception detail logged); SteamSession record + SteamSessionHealth + SteamSessionFailureKind; SteamSessionTokenClaims (unvalidated JWT exp/sub/aud/iss reader, promoted from the probe so SteamSignInProbeFacts delegates rather than duplicates); ISteamSessionStore + SettingsSteamSessionStore (one blob at steam.session.v1) + InMemorySteamSessionStore; ISteamSessionProvider + SteamSessionProvider (load, expiry, lapse; no renewal); SteamSessionCredentialSource implementing the S1 ISteamSessionCredentialSource. Register all of them in AddSteamWebApi so the S1 selector starts seeing sessions. No DB migration. Amendment prose delegated to docs-writer.
<!-- SECTION:PLAN:END -->

## Implementation Notes

<!-- SECTION:NOTES:BEGIN -->
Spike complete (docs/spikes/steam-web-session-auth.md, 2026-08-30). Verdict: a session-minted access token works for all three endpoints Winnow uses and is better behaved on failure than the API key (a bad token returns a hard 401; a bad key returns a silent 200 with an empty envelope). Evidence includes Valve's own shipped store bundle reaching GetUserYearInReview with access_token and no key, and Playnite's source carrying literal key/token pairs for the same methods. The token is a JWT living about a day; renewal needs live cookies or the steamRefresh_steam refresh token (~207 days with remember-me). User decisions 2026-08-30: (a) persist the refresh token under DPAPI so WebView sign-in becomes a true peer for unattended scheduled work, which requires its own recorded section 4.7 amendment covering a long-lived credential; (b) verify live before building the full design, specifically that a real store-minted token returns populated data for the user's account and that Steam sign-in completes inside the off-the-record WebView2 (Steam Guard, possible hCaptcha). A probe path lands first; the design follows the probe's findings.

Decision note, 2026-08-30, recorded after the TASK-56 live probe proved a WebView sign-in mints a working webapi_token, that the token returns populated data from all three endpoints Winnow uses, and that it resolves the signed-in account exactly (page steamid and JWT sub agree). Evidence: docs/spikes/steam-web-session-auth.md section 7.1.

1. Presentation order. WebView sign-in is presented as the fuller path: account identity at mint time (no Year in Review disclosure refetch), purchase history capture in the same consented session, and no key registration outside the app. The Web API key is the alternative, not a fallback, offered for users who prefer not to sign into Steam inside a third party app. Both remain genuinely usable and the UI must state plainly what each gives up. Sign-in gives up unattended durability: the minted token lives about a day and scheduled work depends on refresh renewal succeeding silently. The key gives up immediate identity, purchase history capture, and the token-only services, and requires registering a key on Steam's site. The presentation must be honest rather than burying the key.

2. Credential assignment for unattended schedulers. When both exist, the key drives the 15 minute local pass and the 6 hour remote pass; the session is the fallback for keyless users. Reason: the key does not expire and needs no renewal. The token lives about a day (24h 22m measured 2026-08-30) and renewal depends on the persisted refresh token and Valve's jwt/finalizelogin endpoint. A renewal failure overnight costs a sync cycle the user cannot intervene in; the key has no such failure mode.

3. Standing decisions, unchanged, each needing recorded follow-through. (3a) The steamRefresh_steam refresh token is persisted under DPAPI CurrentUser scope, the same protection DpapiEpicSecretProtector applies to the Epic refresh token, so the access token can be re-minted silently and the session can serve scheduled work. This is a section 4.7 amendment: condition 1 currently forbids persisting session material, and a long-lived bearer credential at rest is exactly what it was written to prevent. The amendment must be recorded in ROADMAP.md with its own binding conditions before the implementation ships. (3b) Purchase history capture during a sign-in requires an explicit permission prompt; declining leaves the sign-in fully functional for identity and playtime backfill.

4. Consequence to design around: the keyless signed-in user. They get account identity and the account scope filter immediately, on demand syncs and backfills immediately, the token-only services immediately, and unattended scheduling only for as long as refresh renewal keeps working. That last point is fragile: a refresh token can be invalidated by signing in elsewhere, one contrary community report exists against the finalizelogin route, and the long lifetime applies only if the user chose remember me. The design must make that state legible rather than silently degrading. When renewal fails the UI surfaces it promptly with a one click re-sign-in and explains that adding an API key makes scheduled syncs unconditionally reliable. A silently dead credential that drops sync cycles without telling the user is the failure mode to avoid.

S1 (Steam credential seam) implemented 2026-08-30. No behaviour change; no DB migration; not finalized (five stages remain).

New in src/Winnow.Enrich.SteamWeb/Credentials/: SteamCredential (kind ApiKey|SessionToken, redacted ToString, ParameterName derived from kind - key vs access_token, optional ExpiresAt, Provenance, optional SteamId, IsUsableAt(now, skew), and AppendTo as the only method allowed to put a credential into a URI); SteamCredentialSelector.Choose - pure and total, implementing decision note 2 (Unattended prefers the key then a usable session; UserInitiated prefers a usable session then the key; an expired session is chosen for neither; null is a normal outcome); ISteamCredentialProvider with GetAsync(purpose, ct) and GetInventoryAsync (SteamCredentialInventory carries no secret - it is what the Stores screen reads in S5); SteamCredentialProvider composing the existing ChainedSteamApiKeyProvider with an optional ISteamSessionCredentialSource (nothing implements that source yet, so session inputs are null throughout S1). Registered via a factory lambda so a later stage only has to register a session source.

ISteamApiKeySource, ChainedSteamApiKeyProvider and ISteamApiKeyProvider are unchanged and still registered, so SteamPlaytimeBackfillService's key-fingerprint dependency and StoreConnections.IsSteamWebApiConfiguredAsync are untouched in this stage.

All three hand-concatenated key= call sites now go through AppendTo: SteamHistoryClient (last-played, Year in Review) and SteamWebApiClient (owned games). The emitted query strings are byte-identical to before. Client methods gained an optional purpose parameter defaulting to Unattended, placed before cacheTtl so no caller's cancellation token shifts; every existing caller is unchanged in behaviour.

Tests: SteamCredentialSelectorTests (both purposes x key-only / session-only / both / neither / expired session, plus the skew window) and SteamCredentialTests (AppendTo emits the right parameter once and escaped for each kind, opens the query when there is none, round-trips through the URI parser, ToString redacts both kinds, and a pin that SteamWebRedaction's allowlist redacts access_token by construction and that neither credential parameter name is on the allowlist). 29 new tests.

Verification: dotnet build clean under TreatWarningsAsErrors. Full suite green - Winnow.Tests 2174 passed, Winnow.Recommend.Tests 75 passed, Winnow.Covers.Tests 70 passed, 0 failed. The 224 existing Winnow.Tests.SteamWeb tests pass with no assertion or fixture changed, which is the no-behaviour-change proof; six test-double files needed only a mechanical signature update to keep implementing the two client interfaces.

Not committed.
<!-- SECTION:NOTES:END -->
