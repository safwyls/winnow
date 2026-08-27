# Spike: Epic OAuth as an authenticated ownership source

Date: 2026-08-26
Method: live unauthenticated probes from this machine against Epic's production hosts, plus
source reading at HEAD of `legendary-gl/legendary` (`master` last pushed 2026-08-24) and
`Heroic-Games-Launcher/HeroicGamesLauncher` (`main` last pushed 2026-08-10), plus live
GraphQL schema enumeration through Apollo validation errors.

**No authenticated call was made during this spike.** Everything below is either CONFIRMED by
an unauthenticated probe or by source at HEAD, or is explicitly marked UNVERIFIED. The one
thing that requires a real token to settle is named in section 7, and the implementation
prints exactly what is needed to settle it.

This supersedes sections 21–22 of `epic-gog-local-files.md`, which reached its conclusions
from source and community documentation without probing. Where the two disagree, this
document wins — and section 9 records where they disagree, because one of the disagreements
matters.

---

## 1. The §4.6 question, answered directly

§4.6 excludes PSN and Xbox and gives three reasons. The claim under test is that Epic OAuth
trips only the first. **It trips one and a half of the three, and the half is the
interesting part.**

| §4.6 reason | PSN | Epic | Verdict |
|---|---|---|---|
| No consumer API; every wrapper is reverse-engineered | Yes | **Yes** | **Trips.** Identical in kind |
| User must manually extract a credential by hand, and repeat it when it lapses | `npsso` cookie from a browser session, redone ~every 2 months | **An `authorizationCode` pasted from a JSON page.** Redone only when the refresh token lapses, which rolls forward indefinitely while the app is used | **Trips, but materially weaker.** See below |
| Documented account-ban risk | PSNAWP's own docs warn of temporary or permanent bans and recommend a throwaway account | **Nothing comparable exists.** Heroic's FAQ says the opposite; no report of an Epic ban for Legendary/Heroic/Rare was found | **Does not trip** |

### The manual-extraction row, honestly

The task framing that prompted this work held that Epic "needs no manual cookie extraction".
**That is not quite right and the difference should be recorded rather than glossed.** Epic's
interactive login has two viable shapes and neither is credential-free:

- **Embedded webview** (what Heroic does). The user types their Epic password and 2FA into a
  browser Hoard hosts. Avalonia has no webview, so this means WebView2 on Windows and
  something else everywhere else. **Rejected** — the cost is large and hosting someone's
  password entry inside Hoard is a worse posture than not touching it at all.
- **Manual copy-paste** (what Legendary falls back to, and what Hoard implements). The user
  signs in on Epic's own page, in their own browser, and pastes back one code.

So there *is* a manual step. What makes it weaker than PSN's is not its absence but three
properties, each verified:

1. **It is not a session cookie.** The user copies a single-use authorization code that is
   dead within minutes, not a live credential with two months of life in it.
2. **It is not repeated on a schedule.** PSN's `npsso` expires roughly every two months by
   construction. Epic's refresh token is *rolling* — each refresh returns a new one — so a
   session that is exercised renews indefinitely. Re-login is an exception, not a cadence.
3. **Hoard never sees the password.** The user authenticates to Epic, on Epic's domain.

The honest summary is therefore: **Epic trips one of §4.6's three reasons outright, trips a
weaker version of the second, and does not trip the third.** That is a materially different
risk profile from PSN, and it is a real difference rather than a rhetorical one — but it is
not the clean "only the first" the premise assumed.

### AMENDED 2026-08-26: the embedded webview is no longer rejected

The "Rejected" verdict above was reversed by the user after
`docs/spikes/embedded-auth.md`. Recorded here rather than left to contradict that document,
because a reader arriving at this section would otherwise act on a decision that no longer
holds.

**What changed, and it is only half of the objection.** The verdict rested on two claims and
the spike falsified the first one:

- *"The cost is large."* Measured: WebView2 hosts inside Avalonia 11.3.20 for **1.26 MB** of
  DLLs against CEF's 123 MB, on a runtime Windows 11 preinstalls. Roughly forty lines. Not
  large.
- Separately, the alternative got worse rather than better. **Loopback is impossible for
  Epic** — `redirectUrl` is validated against an exact allowlist, and same-host-different-port
  and same-path-different-scheme are both refused — so there is no third option where the
  user signs in on Epic's own page *and* the code is captured automatically. Playnite and
  Heroic both embed a browser for this reason.
- And the manual flow's fragility stopped being theoretical. The code is single-use and dies
  within minutes, so every misstep between issuing and spending it burns it. In practice that
  cost several rounds of debugging on one machine, for a step that has to work first time on
  someone else's.

**What did NOT change: the posture objection stands, and point 3 above is now weaker.** That
list claims *"Hoard never sees the password — the user authenticates to Epic, on Epic's own
domain."* Under an embedded webview the domain is still Epic's, but the **host process is
Hoard's**, and a host process can read what is typed into the page it renders. Hoard does not
do that. The point is that the user's protection changes from *structural* to *promised*,
and no amount of care on our side converts it back.

That is a real cost, accepted deliberately rather than argued away:

- The manual flow stays a **peer**, not a legacy path — `IInteractiveAuthPrompt` has a console
  implementation beside the WebView2 one, so a user who declines to type their password into
  Hoard keeps a first-class route.
- Nothing is injected into, read from, or logged around the credential fields. The capture
  hooks are the ones Epic's own page offers (`window.ue`) or its redirect.
- The consent moment survives the change. The console flow showed Epic's warning before
  opening a browser; an embedded flow makes the code invisible and removes the moment the
  user could reconsider, so the warning has to be stated before the browser opens instead of
  being allowed to disappear with the copy-paste step.

§10's principle is unchanged and now carries more weight, not less: the decision to
impersonate Epic's launcher belongs to the person doing it.

### The reason §4.6 does not cover, and it is the biggest one

**There is no third-party registration path that reaches the storefront library.** Epic
Account Services will issue anyone a real OAuth client, but its consent scopes stop at
`basic_profile` / `friends_list` / `presence` / `country` — none of which read entitlements.
`library:public:items` and the playtime permission live only on Epic's own launcher client,
`launcherAppClient2`, whose id and secret were extracted from the launcher binary and have
circulated publicly since 2020. Epic has never rotated them. Every tool in this space —
Legendary, Heroic, Rare — authenticates as that client.

Epic staff have said plainly, on the Epic Developer Community forums, that this is not
offered and not supported: *"we do not offer or expose an API for these specific items, and
it is not something we would be able to support."*

**So using this at all means impersonating Epic's launcher with a credential taken from their
binary.** §4.6 does not have a row for that because PSN does not have this problem. It is the
single largest thing a user is accepting, and section 10 sets out how the implementation
responds to it.

**Verdict: no ban risk was found, so the stop condition in the brief is not met.** The work
proceeds. But it proceeds with the client credentials treated as the user's to supply, not
Hoard's to ship — see section 10.

---

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
them not to do what Hoard is about to ask them to do. The implementation reproduces that
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

**The catalog `bulk/items` endpoint is deliberately NOT called.** It is the only way to get
titles from the API, it requires auth, and it costs a request per namespace — and
`catcache.bin` already has the title for every owned game, locally and for free. So API
candidates carry `Title: null`, which the ingest contract reads as "this source has no title"
and which leaves the local name in charge.

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
by inference — what `epic-gog-local-files.md` §8 concluded from the filesystem. Hoard's
staleness buckets (§6.1) are a recency model, so **Epic titles still cannot enter a
dormancy-based bucket from API data alone.** The only mechanism that gives an Epic game a real
last-played date remains §5.2's process monitor.

---

## 6. What the playtime figure is worth — three limits

1. **No dates.** `totalTime` is a running total and nothing else. See above.
2. **It only counts sessions Epic's own launcher started.** The launcher PUTs to the write
   route; a user who plays through Heroic — or through Hoard's process monitor — accrues
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
sign-in flow prints raw `totalTime` beside the hours Hoard derives from it so the user can
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

## 9. Where this contradicts the earlier spike

`epic-gog-local-files.md` §22 recommended **not building this**. Its reasoning was sound and
most of it still stands — the owned library really is already on disk, and the identity join
really is solved unauthenticated via gamesdb. Two of its points need correcting:

| §21–22 said | This spike found |
|---|---|
| Token lifetimes are 8 h / ~23 days | **Unverified.** No authoritative source states them. Do not hardcode; read the response |
| Playtime has "no last-played timestamp" (inferred) | **Confirmed by schema**, which is stronger. `lastPlayed`, `firstPlayed`, `updatedAt`, `lastModified` all individually rejected |
| "OAuth buys one thing: a playtime floor with no dates" | **It buys two.** Playtime, and **`acquisitionDate`** — when the user actually claimed a title. Nothing on disk records that; `releaseInfo[0].dateAdded` is the store release date. §22 missed this |
| The unit of `totalTime` is "seconds is the plausible reading, unverified" | Unchanged, and still the one open item |

**§22's core judgement was not wrong, and it should not be quietly overwritten.** The owned
library is free locally; this module is not how Epic ownership is discovered. What it adds is
two facts Epic writes nowhere on disk — acquisition dates and playtime — plus a true
entitlement list that does not go stale between launcher runs. Whether that is worth the
costs in section 10 is a judgement for the user, which is why the module is opt-in, off by
default, and requires a deliberate act to enable.

---

## 10. What the implementation does about the client-secret problem

The one unavoidable cost is that reading a storefront library requires Epic's own launcher
client. Hoard's response:

**Hoard does not ship Epic's client credentials, and this repository does not contain them.**
The pair is user-supplied and stored locally, exactly like the Steam Web API key and the IGDB
pair — the charter rule is "user-supplied, stored locally, never logged, never committed", and
baking a credential Hoard has no right to into every checkout would break the last clause.

This is a real trade, not a dodge. It costs the user a setup step they must resolve
themselves, and it means the module ships disabled and stays that way for anyone who does not
go looking. In exchange:

- No credential Hoard has no right to enters the repository or a shipped binary.
- The decision to impersonate Epic's launcher is made by the person doing it.
- An unconfigured install is a clean no-op that makes no requests at all
  (`Registering_the_module_without_credentials_makes_no_requests_on_any_call`).

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

One command, from the repo root:

```powershell
$env:Epic__ClientId     = "<client id>"
$env:Epic__ClientSecret = "<client secret>"
dotnet run --project src/Hoard.App -- --epic-login
```

It prints the Epic sign-in URL together with Epic's own warning about the code, waits for a
keystroke before opening the browser (the page issues a full-account credential, so the
browser is not opened until the user has had the chance to read why), takes the pasted
`authorizationCode`, exchanges it, stores the session encrypted, then fetches the library once
and prints:

- owned title count, and how many carry an `acquisitionDate`
- whether the playtime endpoint answered, and how many titles carry a figure
- **raw `totalTime` beside the hours Hoard derives**, for the eight most-played titles

Compare that last table against the launcher's own "You've Played". If Hoard's column is ~60×
off, flip `EpicWebOptions.PlaytimeUnit` to `Minutes`. That is the whole of section 7's open
question, settled by looking.

Credentials can equally go in a git-ignored `appsettings.local.json`, or the app's settings
table under `epic.oauth.client_id` / `epic.oauth.client_secret`.

**Nothing in that flow asks for, sees, or stores an Epic password.**

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
   issues the code. Hoard uses it for two GETs, but the credential is not narrower than that.
6. **`legendary auth --import` logs the user out of the real Epic Launcher**, because it steals
   the launcher's refresh token. Hoard does **not** do this — it mints its own session from a
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
