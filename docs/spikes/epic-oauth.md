# Spike: Epic OAuth as an authenticated ownership source

> **Dated evidence.** Findings describe the builds and services observed on the dates below.
> Current implementation choices are in [the build spec](../../game-library-design.md);
> current interactions and layout are in [the visual spec](../../design-system.md).
> This record is optional background.

Date: 2026-08-26
Method: live unauthenticated probes from this machine against Epic's production hosts, plus
source reading at HEAD of `legendary-gl/legendary` (`master` last pushed 2026-08-24) and
`Heroic-Games-Launcher/HeroicGamesLauncher` (`main` last pushed 2026-08-10), plus live
GraphQL schema enumeration through Apollo validation errors.

The initial endpoint and schema probes were unauthenticated. Section 14 records a later
authenticated catalog capture on the same date. Unverified claims are labeled; section 7
describes the unresolved playtime-unit comparison.

The local-file study covers ownership available without authentication. This probe
examines acquisition dates, playtime and the sign-in flow.

---

## 1. Sign-in findings

Epic supports an embedded browser and a manual authorization-code flow. In both cases the
user signs in on Epic's domain. An embedded browser runs in Winnow's process; Winnow
captures the authorization code and does not read password fields. The console flow opens
the user's browser and accepts the pasted single-use code.

The WebView2 prototype needed 1.26 MB of DLLs. Epic's redirect allowlist rejected loopback
variants, so the probe did not find an automatic external-browser callback route. The
manual flow remains useful when WebView2 is unavailable or embedded sign-in fails.

## 2. The auth flow — CONFIRMED live

`https://legendary.gl/epiclogin` currently 302s to:

```
https://www.epicgames.com/id/login?redirectUrl=
  https%3A//www.epicgames.com/id/api/redirect%3FclientId%3D<clientId>%26responseType%3Dcode
```

So the `id/login?redirectUrl=…&clientId=…&responseType=code` form **wraps** the older
`id/api/redirect` endpoint rather than replacing it. Both are live. Probing
`id/api/redirect` unauthenticated returns HTTP 200 and exactly this:

```json
{"warning":"Do not share this code with any 3rd party service. It allows full access to your Epic account.",
 "redirectUrl":"https://localhost/launcher/authorized",
 "authorizationCode":null,"exchangeCode":null,"sid":null}
```

Signed in, `authorizationCode` is populated. That is the value the user pastes.

**Note the warning text.** Epic shows the user, at the moment of the copy, a message telling
them not to do what Winnow is about to ask them to do. The implementation reproduces that
warning verbatim in the console flow rather than hiding it. A user who is going to be
uncomfortable with this should be uncomfortable *before* they paste, not after.

**SID login is dead.** `legendary/webview_login.py` carries the comment
`# Update: Epic broke SID login, we'll also do this on Windows now`. Any guide describing an
`sid` exchange is stale.

**A device-code flow is not available.** `launcherAppClient2`'s grant-type allowlist is
`authorization_code, client_credentials, exchange_code, refresh_token`. There is no clean
headless path.

### Token exchange — CONFIRMED live

```
POST https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token
Authorization: Basic base64(client_id:client_secret)
Content-Type: application/x-www-form-urlencoded

grant_type=authorization_code&code=<code>&token_type=eg1
```

Probes:

| Probe | Result |
|---|---|
| `GET` the token endpoint | **405**, `errors.com.epicgames.common.method_not_allowed` — host and path are live |
| `POST` with a deliberately wrong Basic pair | **400**, `errors.com.epicgames.account.invalid_client_credentials`, numeric **18033**, plus OAuth-standard `"error":"invalid_client"` |

The second probe establishes something the implementation depends on: **Epic validates the
client pair before it looks at the grant.** A wrong client pair and a stale authorization code
are both 400, distinguishable only by `errorCode`. `EpicSignInFailure` splits them so the
message can say "get a fresh code" rather than "check your credentials", which are opposite
remedies.

That verbatim response body is pinned at `tests/fixtures/epic-oauth/oauth-invalid-client.json`.

---

## 3. Token lifetimes — fields CONFIRMED, durations UNVERIFIED

Fields Legendary actually reads: `access_token`, `expires_in`, `expires_at`, `refresh_token`,
`account_id`, `displayName`. `expires_at` is ISO-8601 with a trailing `Z`.

**The widely-quoted numbers — 8 hours access, 23 days refresh — could not be confirmed from
any authoritative source.** Not from Epic, not from EpicResearch, not from Legendary.
`epic-gog-local-files.md` §21 states them as fact; it should not have.

More importantly: **Legendary never reads `refresh_expires` or `refresh_expires_at` at all**
(grep returns zero hits). It refreshes and handles failure. So a real token response is not
guaranteed to carry a refresh expiry.

Consequences, both implemented:

- **No lifetime is hardcoded.** `expires_at` is preferred, `expires_in` is the fallback, and
  only if neither is present does a deliberately short 30-minute floor apply — the error is
  made cheap in the direction it will actually be wrong.
- **A missing refresh expiry is carried as `null`, not as a guess and not as "expired".**
  `EpicOAuthToken.IsRefreshUsable` returns true for null. Treating "Epic did not say" as
  "expired" would refuse to refresh a live session and silently disable the module forever;
  treating it as usable costs at most one rejected request. The asymmetry is entirely
  one-sided. Covered by `A_token_response_with_no_refresh_expiry_is_still_usable`.

---

## 4. Library endpoint — CONFIRMED

```
GET https://library-service.live.use1a.on.epicgames.com/library/api/public/items?includeMetadata=true
```

Unauthenticated: **401** `errors.com.epicgames.common.authentication.authentication_failed`,
numeric 1032, `originatingService: library-service`. Pinned verbatim at
`tests/fixtures/epic-oauth/library-unauthenticated-401.json`.

Pagination is `responseMetadata.nextCursor`, re-sent as `&cursor=<value>` until absent
(Legendary `egs.py`). Records carry `namespace`, `catalogItemId`, `appName`, `acquisitionDate`.

**`catalogItemId` is the join key**, and it is the same value `catcache.bin` stores as `id`
and the `.item` manifest as `CatalogItemId`. That is what lets the API half and the local half
land on one ownership and be collapsed by `CandidateOwnershipMerge`. Using `appName` would
never join — "Bluebird" is Fez.

The ownership fetch supplies candidates without titles. Catalog `bulk/items` enrichment
provides names absent from the local cache. In the measured account, 99 distinct catalog
items were owned and only 70 had local catalog entries; section 14 records the missing
29 entries and their classification.

---

## 5. Playtime — IT EXISTS. Two independent confirmations

This is the finding that justified the work, and it is confirmed twice by different means.

### (a) REST routing discrimination

The library service 404s unknown paths and 401s real ones, so the status code reveals whether
a route exists without any credential:

| Path | Result |
|---|---|
| `/library/api/public/items` (known-good control) | **401** |
| `/library/api/public/playtime/account/{id}/all` | **401** ✅ |
| `/library/api/public/playtime/account/{id}/artifact/{artifactId}` | **401** ✅ |
| `/library/api/public/playtime/account/{id}` | **405**, `Allow: OPTIONS,PUT` — the launcher's write path |
| `/library/api/public/playtime/account/{id}/nonexistent-route-xyz` | 404 |
| `/library/api/public/playtime` | 404 |
| `/library/api/public/nonexistent-control-path` | 404 |

Both playtime read routes are real. The 405 on the bare account path additionally shows the
launcher **PUT**s playtime here, which explains section 6's caveat.

### (b) GraphQL schema, enumerated through validation errors

Introspection is disabled on `launcher.store.epicgames.com/graphql`, but Apollo's
field-validation errors leak the schema:

```graphql
PlaytimeTracking: PlaytimeTrackingQuery
type PlaytimeTrackingQuery {
  total(accountId: String!): [Playtime]
  artifact(accountId: String!, artifactId: String!): Playtime
}
type Playtime { accountId: String!  artifactId: String!  totalTime: Int! }
```

The resolver proxies the same REST service — unauthenticated it returns
`"Failed to get playtime for game y: Error: Request failed with status code 401"`.

**No open-source launcher implements this.** Legendary has zero `playtime` references; Heroic
times the child process and its server sync is hard-gated to GOG. Heroic issue #1240 is still
open. So absence-in-launchers is not absence-of-endpoint — it is simply unbuilt.

### What the schema proves is ABSENT

Each of these was individually rejected as a non-existent field on `Playtime`:

**`lastPlayed`, `firstPlayed`, `updatedAt`, `lastModified`, `platform`, `sandboxId`, `id`,
`seconds`, `minutes`.**

**Epic exposes no last-played timestamp anywhere.** This confirms — now by schema rather than
by inference — what `epic-gog-local-files.md` §8 concluded from the filesystem. Winnow's
staleness buckets (§6.1) are a recency model, so **Epic titles still cannot enter a
dormancy-based bucket from API data alone.** The only mechanism that gives an Epic game a real
last-played date remains §5.2's process monitor.

---

## 6. What the playtime figure is worth — three limits

1. **No dates.** `totalTime` is a running total and nothing else. See above.
2. **It only counts sessions Epic's own launcher started.** The launcher PUTs to the write
   route; a user who plays through Heroic — or through Winnow's process monitor — accrues
   **zero** Epic-side playtime. For an app about forgotten games, undercounting play is the
   wrong direction of error. This is why an artifact absent from the playtime list must arrive
   as `null` and never as `0`: absence means "Epic has no figure", not "never played".
3. **`artifactId` is `appName`**, not the catalog item id — so the client joins the playtime
   list onto the library by `appName` and keys the resulting candidate by `catalogItemId`.

## 7. The unit of `totalTime` — UNVERIFIED, and the one thing verification must settle

The schema declares a bare `Int!`. There is no unit anywhere in the type, no Epic
documentation, and no launcher reads the field. **Seconds is the plausible reading and it is
the default, but it is a reading.**

The implementation does not hardcode it: `EpicWebOptions.PlaytimeUnit` is a setting, and the
sign-in flow prints raw `totalTime` beside the hours Winnow derives from it so the user can
compare against the launcher's own "You've Played" display in one look.

**The blast radius is smaller than it appears**, and this is worth stating because it is why
shipping on an unverified reading is defensible. Epic exposes no last-played date, so the only
bucket-relevant thing its playtime can say is *whether a game has ever been played* — and that
bit is unit-independent. A positive `totalTime` is positive in any unit. **Getting the unit
wrong misstates a displayed number; it cannot move a game into the wrong staleness bucket.**

---

## 8. Rate limits — 429 is real, the numbers are not published

- **CONFIRMED:** Legendary issue #486 — `429 Too Many Requests` on the launcher assets
  endpoint, crashing Legendary with an unhandled `HTTPError` because it handles 503 and not
  429. That crash is the cautionary tale.
- **Epic sends no `Retry-After` and no `X-RateLimit-*`.** None of the probes returned either.
  It returns `x-epic-error-code`, `x-epic-error-name`, `x-epic-correlation-id`, and throttles
  as `errors.com.epicgames.common.throttled`.
- **Thresholds: UNKNOWN.** No published figures.

Consequences, implemented: the resilience handler cannot lean on a server-stated delay the way
the Steam one does, so the exponential schedule is the only thing between a throttled sync and
a hot loop. `Retry-After` is still parsed, in case Epic ever starts sending it. The shared
token-bucket limiter is set to 4 req/s — matching the IGDB ceiling as a deliberately
conservative figure, since a full library paginates in a handful of requests and nothing here
is throughput-sensitive.

---

## 9. Data added by authentication

The API adds acquisition dates, total playtime and a fresh entitlement list. It exposes no
last-played timestamp. Local ownership remains available without sign-in. The unit of
`totalTime` was unverified in this probe; section 7 records the comparison procedure.

## 10. Credentials

Winnow supplies the launcher client pair as its fallback credential source. User-supplied
configuration can replace that pair. This is distinct from the user's private session tokens.

### Storage

The session is stored **DPAPI-encrypted, `CurrentUser` scope**, in the §6 `settings` table as
one blob. Never plaintext, anywhere, under any failure. When encryption is unavailable the
store **refuses to write** rather than falling back — the failure mode of a plaintext fallback
is silent and permanent, while the failure mode of refusing is a login repeated after a
restart.

`CurrentUser` rather than `LocalMachine` because `LocalMachine` ciphertext can be decrypted by
any account on the box. DPAPI does not defend against malicious code already running as this
user; that is its documented boundary and it is recorded in the source so nobody mistakes the
guarantee for a stronger one.

---

## 11. The verification step

Both verification flows use the built-in client pair unless configuration supplies another.

**The embedded browser** — this is the one to run:

```powershell
dotnet run --project src/Winnow.App -- --epic-signin --data-dir C:/Temp/winnow-epic-probe
```

A window opens showing what Winnow is about to hold; accepting it loads Epic's own sign-in
page inside that window, and the code is captured the instant Epic issues it. On success it
prints **which of the three capture routes actually fired**, which is the one thing
`embedded-auth.md` §9 could not settle without a real account.

**The console peer**, for a headless machine, a missing WebView2 runtime, or the day Epic
breaks the embedded page:

```powershell
dotnet run --project src/Winnow.App -- --epic-login --data-dir C:/Temp/winnow-epic-probe
```

It prints the Epic sign-in URL together with Epic's own warning about the code, waits for a
keystroke before opening the browser (the page issues a full-account credential, so the
browser is not opened until the user has had the chance to read why), takes the pasted
`authorizationCode`, exchanges it, stores the session encrypted, then fetches the library once
and prints:

- owned title count, and how many carry an `acquisitionDate`
- whether the playtime endpoint answered, and how many titles carry a figure
- **raw `totalTime` beside the hours Winnow derives**, for the eight most-played titles

Compare that last table against the launcher's own "You've Played". If Winnow's column is ~60×
off, flip `EpicWebOptions.PlaytimeUnit` to `Minutes`. That is the whole of section 7's open
question, settled by looking.

A user-supplied pair still overrides the built-in one, via a git-ignored
`appsettings.local.json`, the `Epic__ClientId` / `Epic__ClientSecret` environment variables,
or the app's settings table under `epic.oauth.client_id` / `epic.oauth.client_secret`. That
is also the workaround the day Epic rotates the built-in pair.

**Neither flow asks for, reads, or stores an Epic password.** The console flow sends the user
to their own browser. The embedded flow hosts Epic's page in a Chromium surface — the user
types into Epic's form, which posts to Epic over TLS, and Winnow reads only the code Epic hands
back. That is a weaker statement than the console flow's and it is made deliberately: the
password is typed into a window Winnow opened.

---

## 12. Risks the user should weigh before enabling this

1. **It impersonates Epic's launcher with a credential taken from their binary.** This is
   reverse engineering, which Epic's ToS section 3 prohibits, with section 8.b's penalty being
   suspension "a year or longer" or termination. No enforcement against Legendary/Heroic/Rare
   users has ever been reported, and six years of an unrotated credential is de facto
   tolerance — but tolerance is not permission.
2. **No ban risk was found, and that is an absence of evidence.** It is a genuinely different
   posture from PSN, where the risk is documented by the tooling's own authors. It is not a
   guarantee.
3. **Breakage, not bans, is the realistic failure mode.** Legendary ships a remote
   `webview_killswitch` precisely because Epic's login page breaks these flows periodically.
   Expect this to stop working at some point. Everything degrades to the local readers when it
   does.
4. **The endpoints are explicitly unsupported by Epic**, on the record. Zero stability
   guarantee and no recourse.
5. **Signing in creates a session with full account scope.** Epic says so on the page that
   issues the code. Winnow uses it for two GETs, but the credential is not narrower than that.
6. **`legendary auth --import` logs the user out of the real Epic Launcher**, because it steals
   the launcher's refresh token. Winnow does **not** do this — it mints its own session from a
   fresh authorization code — but anyone who has used that Legendary path should know why their
   launcher signed out.
7. **The playtime unit is unverified** until section 11 is run. A wrong unit misstates a
   displayed number and cannot misplace a bucket.

---

## 13. Summary

| Question | Answer | Confidence |
|---|---|---|
| Does the auth flow still work? | Yes, `authorization_code` via a pasted code | **CONFIRMED** live |
| Token/refresh lifetimes? | Read from the response; refresh rolls | Fields **CONFIRMED**; durations **UNVERIFIED**, so not hardcoded |
| Library endpoint shape? | `library-service…/items`, cursor-paged, keyed by `catalogItemId` | **CONFIRMED** |
| **Does Epic expose per-game playtime?** | **Yes** — `/playtime/account/{id}/all`, per `artifactId`, `totalTime: Int!` | **CONFIRMED** twice, by REST routing and by GraphQL schema |
| Does it expose last-played? | **No.** Not on any endpoint | **CONFIRMED** by schema field rejection |
| Unit of `totalTime`? | Seconds, assumed | **UNVERIFIED** — section 11 settles it |
| Ban risk? | None documented; Heroic's FAQ says the opposite | **CONFIRMED** absent from the record, which is not the same as absent |
| Rate limits? | 429 is real, no `Retry-After`, thresholds unpublished | 429 **CONFIRMED**; numbers **UNKNOWN** |
| Does this replace the local readers? | **No.** Union, per §4.2's rule | By construction |

---

## 14. The catalog service — VERIFIED LIVE 2026-08-26, and why it had to be

Added after 29 of 99 Epic ownership rows on the author's library turned out to have no title
from any source. Everything here was confirmed against a real session before any code was
written against it.

### The gap, stated exactly

`/library/api/public/items` returns **entitlements**: `namespace`, `catalogItemId`, `appName`,
`acquisitionDate`. **No title and no categories.** Two consequences, both measured:

1. Nothing could name an entitlement the launcher's `catcache.bin` has never cached. 29 rows.
2. Nothing could apply `EpicGameFilter` to one either — the local scan drops non-games before
   they become candidates, and the API half had no categories to judge by. So Unreal Engine
   builds, Infinity Blade asset packs and Fortnite cosmetic entitlements went into the games
   grid alongside the games.

The same 29 rows also could not enrich, for a third reason with the same root: the
`catalogItemId` to `AppName` alias that routes an Epic title through gamesdb was built from
`catcache.bin` too, so they had no alias, no gamesdb hop and no IGDB record.

### The endpoint

```
GET https://catalog-public-service-prod.ol.epicgames.com
    /catalog/api/shared/namespace/{namespace}/bulk/items
    ?id={catalogItemId}&id={...}&country=US&locale=en
Authorization: Bearer <access token>
```

| Probe | Result |
|---|---|
| Unauthenticated, real path | **401** `errors.com.epicgames.common.authentication.authentication_failed`, `originatingService: com.epicgames.catalog.public` |
| Unauthenticated, bogus sibling `/catalog/api/shared/nonexistent-control-path` | **404** `errors.com.epicgames.common.not_found` |
| Authenticated, real session | **200**, and it answered for **all 99** distinct catalog item ids the account owns |

The same routing discrimination that established the playtime routes in section 5. The numbered
mirror `catalog-public-service-prod06` (which Legendary hardcodes) answers identically; the
unnumbered alias is what ships.

### The response — CONFIRMED

**An object keyed by catalog item id**, not an array. Ids the service does not recognise are
simply absent from it, which is a real answer and is cacheable as a miss. Per entry:

```json
"16a66a9f5630407d923429470bd5c967": {
  "id": "16a66a9f5630407d923429470bd5c967",
  "title": "Infinity Blade: Effects",
  "categories": [ {"path": "asset-format/game-engine"}, {"path": "type"} ],
  "namespace": "89efe5924d3d467c839449ab6ab52e7f",
  "releaseInfo": [ { "appId": "InfinityBladeEffects", "compatibleApps": ["UE_4.9"] } ],
  "entitlementType": "AUDIENCE", "itemType": "DURABLE",
  "developer": "Epic Games", "mainGameItemList": []
}
```

`title`, `categories[].path` and `releaseInfo[].appId` are the three fields that matter, and
they are the same fields `catcache.bin` stores — this is the launcher's own cache, served.

Three findings worth recording:

- **Both `mainGameItem` (an object) and `mainGameItemList` (an array) occur**, and which one
  appears varies per entry. Asset packs carry only the empty list; the Borderlands 3 and ARK
  add-ons carry both. A reader that knows one spelling classifies half a library's DLC as base
  games.
- **The service returns the real trademark characters that `catcache.bin` transliterates.**
  Trap 3 of `epic-gog-local-files.md` records the local file storing a literal `?` where a
  registered-trademark sign belongs; over the network the same title arrives as
  `LEGO(R) Fortnite: Odyssey` with a genuine U+00AE. Neither is "corrected" anywhere — both are
  stored as sent, and the soft matcher's normaliser already absorbs the difference.
- **The library endpoint returns one record per ARTIFACT, not per catalog item.** 144 records
  collapsed to 99 distinct `catalogItemId`s here. Ownership is keyed by the catalog item so the
  duplicates merge — but a count of "owned titles" taken from the record count is 45% too high.

### What the account's 29 unnamed rows actually are

| Count | What |
|---|---|
| 3 | **Games.** LEGO Fortnite: Odyssey (**408 minutes played**), Borderlands 3 Bounty of Blood, ARK Ragnarok |
| 3 | Unreal Engine builds — 4.0 (**320 minutes played**) and two "Chaos" preview builds |
| 18 | Engine sample content: the Infinity Blade packs, Soul: Cave, the Kite open-world demo, the Action RPG sample project — several entitled twice under different catalog item ids |
| 2 | `hidden` Fortnite / LEGO Fortnite content entitlements |
| 1 | Civilization VI : Aztec DLC (`addons` only) |

**Two of the 26 non-games have recorded playtime**, which settles how they are handled: they
are classified and hidden from the games grid by the existing `library.show_non_game_entries`
toggle, and **nothing is deleted**. The user owns them, and Epic has recorded 320 minutes in
the Unreal Editor.

### What the three games can and cannot reach

All three now carry an alias and resolve on gamesdb. **None of them has a Steam or GOG
release**, so the two-hop route in section 20 of the local-files spike cannot reach IGDB for
any of them: they get real titles, and no cover, year or summary.

- LEGO Fortnite: Odyssey resolves to Fortnite's `game_id`, whose releases are
  `epic / psn / xboxone / winstore / humble / beamdog / psx / psvita` and **no Steam or GOG**.
- Borderlands 3 Bounty of Blood (`CatnipDLC3`) and ARK Ragnarok resolve to their own game ids
  with no cross-store twin.

That is a fact about those three products rather than a failure of the route — and it is why
the route's counters are worth reading. The run now reports "0 of 19 bridged, 19 have no
cross-store release", which is a different sentence from "19 had no AppName on disk" and used
to be the one printed.

### Rate limiting and caching

The catalog client shares the module's single `EpicRateLimiter`, so catalog and library traffic
spend one 4 req/s budget between them rather than two. Answers and definite misses are cached
in `metadata_cache` under provider `epic-catalog` for 30 days; transport failures are not
cached at all. Measured on the author's library: 59 rows written on the first pass, **zero
requests on the second**.
