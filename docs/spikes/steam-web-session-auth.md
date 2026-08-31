# Spike: Steam web session token as an alternative credential

Date: 2026-08-29; updated 2026-08-30 with live probe results
Evidence: public sources, anonymous probes, and one authenticated live session
(2026-08-30) inside Winnow's off-the-record WebView2 profile. All URLs and dates are
listed in the Sources section.

Question: can a Steam web session's minted access token replace the Web API key for the
endpoints Winnow uses? This gates a user-requested auth restructure where WebView
sign-in becomes a peer auth method beside the API key, as Playnite does.

### Confidence legend

- **VERIFIED**: primary source, or two independent sources that agree.
- **REPORTED**: one credible but unverified source.
- **UNKNOWN**: needs a real live session to determine.

---

## 1. The webapi_token: what it is, where it comes from, how it dies

**Format: VERIFIED.** The token is a JWT. Valve's own shipped script decodes it as
`JSON.parse(atob(g_wapit.split('.')[1]))` and reads `body.exp`. steam-session's README
shows the header as `eyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0`, which decodes to
`{"typ":"JWT","alg":"EdDSA"}`. Claims used in the wild: `exp`, `aud` (an array), `sub`
(the steamid64), `iss`.

**Where it is minted: VERIFIED, and there are three routes, not one.**

1. `https://store.steampowered.com/pointssummary/ajaxgetasyncconfig` returns JSON with a
   `webapi_token` field. This is the route xPaw's documentation site tells users to use.
2. `https://steamcommunity.com/my/edit/info` carries the token in
   `JSON.parse(application_config.dataset.loyalty_webapi_token)`.
3. **Any store page at all.** Fetched anonymously 2026-08-29,
   `https://store.steampowered.com/replay/` carries
   `<div id="application_config" ... data-store_user_config="{...&quot;webapi_token&quot;:&quot;&quot;,...}">`
   and `<script>window.g_wapit="";</script>`. Signed out both are empty strings; signed
   in they carry the JWT. This is the route Playnite uses (see section 4) and it is the
   cheapest: no extra navigation, the token is already in the document Winnow is looking
   at.

**Lifetime: VERIFIED, measured 2026-08-30.** xPaw (a
SteamKit maintainer, writing in SteamRE/SteamKit#1125, 2022-08-28): "Their tokens (and
cookies) are JWT tokens, you can base64 decode it and see its expiration. For web, the
jwt token expires in a day, and when it does it will redirect to login.steampowered.com
which has a separate JWT token cookie set (if remember password, it expires in 207
days)." Several marketplace API docs (market.csgo.com, market.dota2.net) independently
state 24 hours and "daily refreshes are necessary"; REPORTED, but agreeing with the
above. The 2022 figure was re-measured 2026-08-30: a store-minted token carried `exp`
2026-08-31 15:11:55Z, 24h 22m from mint. The "about a day" figure holds. This is one
reading from one mint; whether the delta varies is not established.

**Refresh path: VERIFIED, and Valve's own one requires live cookies.**
`https://store.akamai.steamstatic.com/public/shared/javascript/auth_refresh.js` is
loaded by every store page (fetched verbatim 2026-08-29, 1527 bytes). It reads
`window.g_wapit`, decodes the JWT payload and takes `exp`. It schedules a refresh at
`(exp - offset) * 1000 - Date.now()` where `offset = floor(random() * 600) + 1800`
seconds, between 30 and 40 minutes before expiry, jittered. It refreshes by POST to
`https://login.steampowered.com/jwt/ajaxrefresh` with `{redir: location.href}`,
`crossDomain: true`, `xhrFields: { withCredentials: true }`, then POSTs the response
back to `response.login_url` together with `prior: window.g_wapit`. On
`{result: 1, token, rtExpiry}` it replaces `g_wapit` and re-arms.
`withCredentials: true` against `login.steampowered.com` is the load-bearing detail:
**Valve's refresh path is cookie-based.** Without the login-domain cookie there is
nothing to refresh against.

**Refresh WITHOUT live cookies: possible, but only if you keep the refresh token.
VERIFIED in source.** The refresh token lives in the `steamRefresh_steam` cookie on
`login.steampowered.com` (steam-session's `LoginSession.ts` has the comment "we want to
include steamRefresh_steam in our response"). Given that token and nothing else,
`LoginSession.getWebCookies()` for `EAuthTokenPlatformType.WebBrowser` POSTs
`{nonce: <refreshToken>, sessionid, redir}` to
`https://login.steampowered.com/jwt/finalizelogin` and then executes each returned
`transfer_info` POST, each of which replies with a fresh `steamLoginSecure` Set-Cookie.
steam-session's README: "As of 2025-04-30, this method works for EAuthTokenPlatformType
WebBrowser and MobileApp, but using SteamClient will fail with response `AccessDenied`
unless sent over an authenticated CM session."

**But the token-only refresh is closed to web tokens. VERIFIED.** steam-session's README
on both `refreshAccessToken()` and `renewRefreshToken()` (which call
`IAuthenticationService/GenerateAccessTokenForApp`): "As of 2025-04-30, this method
works only for EAuthTokenPlatformType MobileApp, but using WebBrowser will fail with
response `AccessDenied`, and SteamClient tokens will fail with the same response unless
sent over an authenticated CM session." Additionally, from the same README: "If a
refresh token is successfully renewed ... the old refresh token will become invalid,
even if it is not yet expired."

**One contrary data point, REPORTED and unresolved.** node-steam-session issue #56
(2026-05-20) reports `AccessDenied` on all three routes: `getWebCookies()` from a
refresh token, a direct `finalizelogin` POST, and `GenerateAccessTokenForApp`. No
maintainer reply. One report, environment-specific (the reporter is behind a network
accelerator), but it means the refresh-token route should not be assumed universally
reliable.

**Cookie and token are nearly the same object. VERIFIED.** steam-session's
`getWebCookies()` for SteamClient/MobileApp builds the cookie as
`steamLoginSecure = encodeURIComponent(steamid64 + '||' + accessToken)`, with the
source comment "our access token *is* our session cookie". The README elsewhere calls
web cookies "the same as an access token". Whether the JWT half of a *WebBrowser*
`steamLoginSecure` cookie can be handed to `api.steampowered.com` as `access_token=`
directly is UNKNOWN and needs a live test; the audience may differ from a
store-page-minted token.

**Refresh-token durability is conditional.** The 207-day figure applies when the user
chose "remember me" (steam-session models this as `ESessionPersistence`). A session the
user did not ask to be remembered is not a durable credential.

---

## 2. Which api.steampowered.com services accept access_token=

**First, a correction. The premise of this investigation expected steamapi.xpaw.me to
mark token-vs-key auth per method. As of 2026-08-29 it does not.**
`xPaw/SteamWebAPIDocumentation`'s `api.json` (1.8 MB, downloaded and parsed 2026-08-29)
has no per-method auth marking at all: every one of the ~1100 methods lists a `key`
parameter described "Access key", and the only `_type` values present are
`undocumented` (813), `publisher_only` (136) and absent (162). The site offers a single
global access-token field whose help text reads "Some APIs work with access tokens, if
you have one you can provide it here and it will be preferred over the webapi key", a
blanket preference, not a per-method fact. Its `src/App.ts` decodes the token and reads
`exp`, `aud` and `sub`, and populates the site's steamid field from `token.sub`. Useful,
but not the per-method evidence the question assumed.

**The real per-method marking exists, in Valve's own webui protobufs. VERIFIED.**
`SteamDatabase/SteamTracking`'s `ProtobufsWebui/*.proto` carry a generated comment
above each rpc with the web UI's call annotations:

```
// service_player.proto
// bConstMethod=true, ePrivilege=1, eWebAPIKeyRequirement=1
rpc ClientGetLastPlayedTimes (...)
// bConstMethod=true, ePrivilege=1, eWebAPIKeyRequirement=2
rpc GetOwnedGames (...)

// service_salefeature.proto
// bConstMethod=true, ePrivilege=2, eWebAPIKeyRequirement=1
rpc GetUserYearInReview (...)

// service_authentication.proto
// ePrivilege=1, eWebAPIKeyRequirement=1
rpc GenerateAccessTokenForApp (...)
```

Only the values 1 and 2 occur across the whole set. **The semantics of 1 vs 2 are
UNKNOWN**; the enum itself is not in the mirrored protos and does not appear by name in
the shipped bundle.

**How Valve's own web UI authenticates: VERIFIED, from the shipped store bundle.**
`https://store.akamai.steamstatic.com/public/javascript/applications/store/main.js`
(1.98 MB, downloaded 2026-08-29) contains the generic Steam web-API transport. It
builds URLs as `${WEBAPI_BASE_URL}I${Service}Service/${Method}/v${N}` from a
`Service.Method#version` string, with `WEBAPI_BASE_URL` =
`https://api.steampowered.com/` (taken from the page's `application_config`). It
appends `access_token=<m_webApiAccessToken>` to every call where the call site sets
`bSendAuth` and the method is not the "no auth required" combination of `ePrivilege`
and `eWebAPIKeyRequirement`. It also appends `spoof_steamid` when set (a Valve-internal
support facility). **It never holds an API key.** A browser has no key;
`access_token` is the store SPA's only credential. On HTTP 401 it invokes
`m_fnRequestNewAccessToken()` and retries the call. It refreshes proactively when the
JWT's `exp` is within roughly 900 seconds. It reads the `x-eresult` and
`x-error_message` response headers into the protobuf header.

`store.steampowered.com/replay/` is a route of this same SPA, as confirmed by the
bundle's route table (`YearInReview: (e,t) => \`/:prefix(yearinreview|replay)${...}\``),
and Year in Review is a `ProtobufsWebui` service. So Valve reaches
`GetUserYearInReview` from a browser that has only a token. The specific call is in a
lazily loaded chunk and was not read directly; the transport rule and the route table
were.

**Playnite ships both credentials against the same methods. VERIFIED, production code.**
`PlayniteExtensions/source/Libraries/SteamLibrary/Services/PlayerService.cs` has
literal key/token pairs:

```csharp
public IEnumerable<ClientPlaytime> GetClientLastPlayedTimesWeb(
    SteamUserToken userToken, ...) =>
    PlayerServiceGetClientLastPlayedTimes(
        "access_token", userToken.AccessToken, minLastPlayed);

public IEnumerable<ClientPlaytime> GetClientLastPlayedTimesApiKey(
    string apiKey, ...) =>
    PlayerServiceGetClientLastPlayedTimes(
        "key", apiKey, minLastPlayed);
```

The two differ only in the query-parameter name. The same pattern covers `GetOwnedGames`
(`GetOwnedGamesWeb` / `GetOwnedGamesApiKey`, both hitting
`https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/`). Both endpoints are the
two Winnow uses today. Playnite additionally calls, **token only, with no key path
offered**: `IFamilyGroupsService/GetFamilyGroupForUser`,
`IFamilyGroupsService/GetSharedLibraryApps`,
`IParentalService/GetParentalSettings`,
`IClientCommService/GetClientAppList`.

**Live anonymous probes, VERIFIED 2026-08-30 UTC.** A malformed token is rejected at
the auth layer with a hard 401; the absence of any credential, and a malformed key on
the `eWebAPIKeyRequirement=1` methods, are not.

| Request | Result |
| --- | --- |
| `ClientGetLastPlayedTimes`, no credential | 200, `x-eresult: 1`, 15-byte body |
| `ClientGetLastPlayedTimes`, `access_token=notatoken` | **401 Unauthorized** |
| `ClientGetLastPlayedTimes`, `key=<32 hex, invalid>` | 200, `x-eresult: 1`, 15-byte body |
| `GetOwnedGames`, `access_token=notatoken` | **401 Unauthorized** |
| `GetOwnedGames`, `key=<invalid>` | **401 Unauthorized** |
| `GetUserYearInReview`, no credential | 200, `x-eresult: 15`, `x-error_message: Caller does not have permission to view.` |
| `GetUserYearInReview`, `access_token=notatoken` | **401 Unauthorized** |
| `GetUserYearInReview`, `key=<invalid>` | 200, `x-eresult: 15`, same message |

Two conclusions:

1. **`access_token` is a recognised, validated auth parameter on all three endpoints
   Winnow cares about.** VERIFIED. It is parsed and checked before the method runs.
   The original anonymous probe (2026-08-30) proved the parameter is honoured but left
   the audience question open. **Now VERIFIED 2026-08-30**: a store-minted token
   (audience `web:store`) returned populated data from all three endpoints; see §7.1
   findings.
2. **Token auth fails louder than key auth, which is a real operational gain.** A wrong
   or expired token is a clean 401. A wrong key on `ClientGetLastPlayedTimes` or
   `GetUserYearInReview` is a silent 200 with an empty envelope, indistinguishable from
   "this account has no data". That is exactly the ambiguity `SteamHistoryClient`
   already documents in its `{"response":{}}` comment.

---

## 3. Does token auth change response shapes or visibility: VERIFIED (shape), partly UNKNOWN (visibility)

**Shape: unchanged. VERIFIED by code, not by a live diff.** Playnite deserializes the
token and key responses into the same `ClientPlaytimeResponse` and
`GetOwnedGamesResponse` types, through the same private method, with the same field
set. If the shapes diverged that code would not compile against both.

**Identity: partly implicit. VERIFIED.** `ClientGetLastPlayedTimes` takes no `steamid`
in either mode; the credential identifies the account. (This matches what the GDPR
spike already verified for the key on 2026-08-28.) The token additionally carries the
steamid in its `sub` claim, so a token is self-identifying; xPaw's site fills in its
steamid field from `token.sub`. Playnite still passes an explicit `steamid` to
`GetOwnedGames` under token auth; whether it *may* be omitted is UNKNOWN, as is the
same question for `GetUserYearInReview`.

**Visibility: narrower, not wider, except for the token-only services.** A key can
query any public profile; a token is bound to one account by `sub`. For Winnow this is
a non-issue, every endpoint it uses is own-account, but it should be stated because it
means a token cannot be a drop-in for any future friend-facing or public-profile call.

**The "web login gets more" claim: REPORTED, and partly explained.** Playnite's own
troubleshooting wiki says "Web login gets more of your Steam library, including
uninstalled and unplayed free games and demos, and Family Sharing games." The Family
Sharing half is fully explained by `IFamilyGroupsService` having no key path at all.
The free-games/demos half is **UNKNOWN**: it may be the `include_free_sub` /
`include_played_free_games` parameters rather than the credential, since Playnite
passes those on both paths. Only a live A/B on one account, same call with key then
token, settles whether `GetOwnedGames` itself discloses more under a token.

**Audience enforcement: partially VERIFIED 2026-08-30.** steam-session lists the
audiences issued per platform type: `SteamClient` gets `['web','client']`, `WebBrowser`
gets `['web']`, `MobileApp` gets `['web','mobile']`. xPaw's site displays a token's
`aud` badges, implying store-minted and community-minted tokens differ. The live probe
(2026-08-30) used a token with audience `web:store`, and all three endpoints returned
populated data, so `web:store` is accepted by `IPlayerService` and
`ISaleFeatureService`. Whether other audience values are rejected is still unknown.

---

## 4. What Playnite actually persists, and what breaks: VERIFIED

**It persists the steamid and nothing else.** From
`source/Libraries/SteamLibrary/Services/SteamStoreService.cs` and
`SteamLibrarySettingsViewModel.cs`:

- `Login()` opens a **visible** 600x720 WebView on
  `https://store.steampowered.com/explore/`, having first called `DeleteDomainCookies`
  on `.steamcommunity.com`, `steamcommunity.com`, `steampowered.com`,
  `store.steampowered.com`, `help.steampowered.com` and `login.steampowered.com`. On
  every `LoadingChanged` with `IsLoading == false` it re-parses the page and closes
  the window the moment a token appears.
- Token extraction, at login and at every later use: parse the page with AngleSharp,
  `getElementById("application_config")`, read `data-store_user_config` ->
  `webapi_token` and `data-userinfo` -> `steamid` + `logged_in`. It bails if the
  current URL contains `/login`.
- `GetAccessTokenAsync()` opens an **offscreen** WebView on the same store page and
  re-mints on demand.
- The settings view model's "am I authenticated" check *is* a call to
  `GetAccessTokenAsync()`, i.e. it probes whether the cookie jar can still mint a
  token.
- The only thing written to plugin settings is `Settings.UserId`.

So: **Playnite stores no token and no cookies of its own.** The durable credential is
the browser cookie jar in the shared Playnite CEF profile; the token is minted per use
and thrown away. That is a materially different design from "persist a token", and it is
the honest comparison for Winnow.

**What breaks when the session expires: a re-login prompt, and the whole web method goes
dark. VERIFIED from issue reports.** PlayniteExtensions issue #512 (open, filed
2026-02-12): "Every day I start my computer in the morning, there is a Playnite
notification, that Steam Integration needs authentication. I login and next day it's the
same... Using API key login instead fixes this, but then my Family Library is not
synced." The maintainer could not reproduce it and diagnosed cross-plugin interference:
"plugins currently all share one web view profile (this will be addressed in P11 where
every plugin will have its own profile), so they could in theory break each others login
sessions." The reporter later confirmed that disabling other Steam-touching plugins
largely stopped the logouts. Issue #507 (2025-12-13) is an infinite login loop; the
documented remedy is "clear web cache via Playnite's advanced settings."

Read that as: there is no silent refresh anywhere in this design, the failure is
user-visible and recurrent, and cookie-jar sharing is a real fragility.

**Heroic is not a Steam precedent**, it covers Epic, GOG and Amazon only. It is still
the right shape reference, and Winnow's Epic path already matches it: sign-in happens
entirely inside a webview, no credentials are stored, and access token + refresh token +
user id are persisted and refreshed on expiry.

---

## 5. Steam sign-in inside WebView2: partially VERIFIED

**Partially VERIFIED 2026-08-30; previously REPORTED. This section was originally the
weakest in the spike; the live runs strengthen it, though the QR route and hCaptcha
remain untested.**

Positive signals:

- Playnite ships exactly this flow against CEF, and the 2025-2026 issue traffic is about
  session *lifetime*, never about being unable to complete a sign-in. If embedded
  Chromium were blocked, #512 would read differently.
- Steam's login page is an ordinary Chromium-rendered React SPA on
  `login.steampowered.com`; Steam's own desktop client renders it in CEF (the
  community-known `-noreactlogin` switch reverts to the pre-webview window).
- QR sign-in needs only that the QR render and a phone scan it; device confirmation
  and Steam Guard codes are in-page. Nothing in the flow requires a browser feature
  WebView2 lacks.

Risk:

- The login page's hCaptcha is the fragile part. It is documented to fail on
  unsupported browsers, blocked cookies or JS, and IPs its provider distrusts
  (VPN/proxy). WebView2 is Edge/Chromium and should pass, but an off-the-record profile
  with an unusual user-agent is the surface where this would bite.

Winnow-specific: `src/Winnow.Auth.WebView/WebView2SteamPageHarvester.cs` already drives
a user-present, off-the-record WebView2 through a Steam sign-in, and its
`SteamAccountPagePolicy` allowlists Valve's login and support origins precisely so Steam
Guard, captcha and account recovery work. **Exercised end-to-end against a live
account** on three runs (twice 2026-08-29, once 2026-08-30). Sign-in completed on all
three; the third run minted a token and all three endpoints returned populated data.
See §7.1.

---

## 6. The cookie-vs-token tradeoff, honestly: VERIFIED (structure), policy UNKNOWN

Which capability needs what:

| Capability | Needs |
| --- | --- |
| `ISaleFeatureService/GetUserYearInReview` | token **or** key; no cookies |
| `IPlayerService/ClientGetLastPlayedTimes` | token **or** key; no cookies |
| `IPlayerService/GetOwnedGames` | token **or** key; no cookies |
| `IFamilyGroupsService` shared-library apps | token only; no key path exists |
| `store.steampowered.com/account/licenses` and `/account/history` HTML | live store session cookies; a token is useless |
| Minting a fresh `webapi_token` from a page | live store session cookies |
| Valve's own refresh (`/jwt/ajaxrefresh`) | live `login.steampowered.com` cookies |
| Minting a session from scratch, no cookies | the `steamRefresh_steam` refresh token, via `/jwt/finalizelogin` |

**If Winnow drops cookies after minting** it keeps roughly a day of API access and
loses the two-page harvest and the ability to re-mint. That is not a loss for the
harvest itself: the harvest is already a one-shot, user-present operation, so the token
can be minted at the end of the same visit from a page already loaded. It *is* a loss
for anything scheduled; the nightly snapshot service would find a dead token most
mornings, which is precisely the user experience in Playnite issue #512.

**If Winnow holds cookies**, and only one cookie matters (`steamRefresh_steam`), it
gains up to ~207 days of silent re-minting through `/jwt/finalizelogin`, and takes on a
durable full-account bearer credential at rest. That is the exact thing ROADMAP
section 4.7's amendment condition 1 forbids today ("Ephemeral session... Cookies are
never persisted to disk. The profile is torn down after harvest"). DPAPI CurrentUser is
the same protection Winnow already applies to the Epic refresh token via
`DpapiEpicSecretProtector`, so the machinery exists and this is a policy question, not
an engineering one. Two caveats belong in the same breath: `steamRefresh_steam` is only
long-lived if the user chose "remember me", and renewing a refresh token invalidates the
previous one, so a stored copy can be silently killed by the user logging in elsewhere.

---

## 7. What only a live session can confirm

The items below are ordered by what they unblock; the repo owner can run them when
ready.

1. That a store-minted `webapi_token` returns **populated** data from
   `GetUserYearInReview`, `ClientGetLastPlayedTimes` and `GetOwnedGames`. **VERIFIED
   2026-08-30.** All three returned HTTP 200, `x-eresult: 1`, POPULATED, under a
   store-minted token with audience `web:store`. See §7.1 findings.
2. The token's real `exp` delta and its `aud` values as minted by
   `store.steampowered.com` in 2026. **VERIFIED 2026-08-30.** Expiry 24h 22m from
   mint; audience `web:store`; issuer `r:0018_28B7BA66_D69F8`. The 2022 "about a day"
   figure holds.
3. Whether `steamid` may be omitted from `GetOwnedGames` and `GetUserYearInReview`
   under token auth, given the token's `sub` claim. **Still UNKNOWN; the probe sends
   a steamid to both.**
4. Whether token responses differ in *content* from key responses for the same own
   account, the "web login gets more" claim, run as a same-account A/B. **Still
   UNKNOWN; the probe uses a token only.** However, the 592 first-played dates from
   token auth match exactly the 592 rows the M5 backfill wrote under key auth, which
   is partial evidence of equivalence for `ClientGetLastPlayedTimes`.
5. Whether Steam's full login flow (password + Steam Guard, and QR) completes inside
   Winnow's off-the-record WebView2 profile, hCaptcha included. **Partially VERIFIED
   2026-08-30.** Sign-in completed on all three runs. The password-plus-Steam-Guard
   path completed on the first two; the third used a route the probe's heuristic did
   not recognise. QR route untested; hCaptcha not presented on any run.
6. Whether `steamRefresh_steam` is present in that profile after sign-in, and whether
   `/jwt/finalizelogin` still re-mints from it days later. **Still UNKNOWN; the
   profile is private and deleted after each run.**
7. Whether the JWT half of a WebBrowser `steamLoginSecure` cookie works directly as
   `access_token=`, which would remove the page-scraping mint step entirely. **Still
   UNKNOWN; the probe mints from `application_config`.**

---

## 7.1. Verification probe (TASK-56)

**Status: run three times; third run successful 2026-08-30.** The first two
runs (2026-08-29) stalled on instrument defects, both since fixed. The third
run minted a token and all three endpoints returned populated data. The
findings table below records the combined results.

**The report file is `%LOCALAPPDATA%\Winnow\steam-signin-probe.txt`**, written
on every path including failure, cancellation and timeout. The console is a
second copy, not the channel the findings depend on. Winnow is a `WinExe` and
may have no console at all.

**First run, 2026-08-29.** The sign-in itself worked. Steam's login page
rendered inside the off-the-record WebView2 window, the user entered their
password, completed Steam Guard, and Steam's post-login redirect landed on the
store home page. This is the first end-to-end exercise of that flow against a
live account recorded in this repository. §5 called itself the spike's weakest
section and predicted embedded Chromium would be fine; that prediction is borne
out for the password-plus-Steam-Guard path. The QR route was not exercised. No
hCaptcha was presented, so "hCaptcha passes in this profile" is untested, not
confirmed. Mark item 5 partially VERIFIED 2026-08-29.

No token was minted, so item 1 is entirely open. Nothing was learned about
whether a store-minted token returns populated data from the three endpoints.

The probe then stalled for its full ten-minute timeout. The cause was an
instrument bug: the probe's scope predicate was copied from the harvest
session's `IsSignInJourney`, which counts the store root as a sign-in page, so
the once-a-second poll refused to read the page Steam landed on. Fixed by
giving the probe its own mint-scope predicate (`IsMintScope`); the shipped
`SteamAccountPagePolicy` was not changed.

**Second run, 2026-08-29.** The browser window opened, sign-in completed, and
the browser window closed immediately afterwards. The immediate close is
consistent with the mint path reaching its end (the probe closes the window
the moment it reads a token), but nothing was printed to the terminal and no
report file was written. The terminal appeared frozen and had to be
force-closed. Because no output was captured, the immediate close is not
evidence that a token was obtained. Item 1 remains entirely open.

**Instrument bugs, both fixed.**

1. **First run: scope predicate.** The probe's scope predicate was copied from
   the harvest session's `IsSignInJourney`, which counts the store root as a
   sign-in page. Steam lands the user on the store root after Steam Guard, so
   the one page the probe was shown was the one it refused to read. Fixed by
   giving the probe its own mint-scope predicate (`IsMintScope`); the shipped
   `SteamAccountPagePolicy` was not changed.

2. **Second run: no console, then deadlock.** Two compounding causes.
   `Winnow.App.csproj` declares `<OutputType>WinExe</OutputType>`, so the
   Windows GUI subsystem gives the process no console and every
   `Console.WriteLine` goes nowhere (code review finding F41). The existing
   helper `ConsoleAuthPrompt.AttachConsoleIfNeeded` did not help: its guard
   returns early when `Console.IsOutputRedirected`, and a `WinExe` with no
   console has a null stdout handle that .NET reports as redirected, so the
   guard meant to protect a piped run also skipped the attach in the one case
   the method was written for. Separately, after sign-in completed the process
   deadlocked: `ReportAsync` was called via `.GetAwaiter().GetResult()` on the
   main thread after `Dispatcher.UIThread.MainLoop` had returned, but
   Avalonia's `SynchronizationContext` was still installed, so every `await`
   captured a context whose dispatcher was no longer pumping. The blocking wait
   on the main thread could never complete. Output ordering and buffering were
   not the explanation; there was no console to buffer to.

   Both fixed. The report now writes to a file (`SteamProbeLog`, `AutoFlush =
   true`) and the console is attached by a probe-specific opener
   (`TryOpenConsole`). The report runs inside a dispatcher-posted lambda while
   the loop is still pumping; the loop is cancelled only in that lambda's
   `finally`.

   **Latent:** the `AttachConsoleIfNeeded` bug also affects the shipped
   `--epic-login` and `--epic-signin` console flows, which use that same
   method. It is noted here, not fixed, because the probe was given its own
   opener rather than changing a method three shipped flows depend on.

**Third run, 2026-08-30.** Sign-in completed inside the off-the-record
WebView2 window (one page load). Outcome TokenMinted. The token was minted
from the store root (`https://store.steampowered.com/`) via route
`application_config/data-store_user_config`, on the 13th poll second,
immediately after the sign-in redirect. All three endpoints returned HTTP
200, `x-eresult: 1`, POPULATED, under `access_token` with no API key in
any request. Details in the findings table below.

Instrument note: the probe reported "password field: never seen" even though
the user signed in. Steam took a route the probe's sign-in-form detection
does not recognise (QR, mobile confirmation, or a JS-rendered field the
heuristic missed). This does not affect the result but should not be
mistaken for evidence about which login route was used. QR route and hCaptcha
remain untested.

Corroboration: the 613 apps and 592 first-played dates from
`ClientGetLastPlayedTimes` match exactly the 592 `steam_first_played` rows
the M5 backfill wrote to this user's database under key auth, so token auth
and key auth agree on this account's data.

**Guaranteed termination.** Three layers: a report budget (2 minutes) around
the HTTP calls only, a hard-budget watchdog (sign-in timeout + report + 30s)
that cancels the loop, and a background thread that calls
`Environment.Exit(3)` if the process is still alive past that. Exit codes:
0 = all three endpoints populated, 1 = a conclusion short of that, 2 =
stopped or failed, 3 = hard exit.

**Why the probe was not moved to its own console project.** The Avalonia +
STA + `app.manifest` host inside Winnow.App is proven working: the browser
window opened and sign-in completed on both live runs, so the host was never
the broken part. Making the file the guaranteed channel removes the only
benefit a console-subsystem project would have brought, and a new project
would have had to re-derive the manifest and Avalonia bootstrap that
`NativeControlHost` requires.

The probe is a hidden command-line switch the repository owner runs once by
hand:

```
dotnet run --project src/Winnow.App -- --steam-signin-probe
```

A private browser window opens on Steam's login page. The user signs in there;
Steam Guard, captcha or QR, whatever Steam requires, is theirs to complete
inside the window. Once a store page hands over a token the window closes
itself. No token, refresh token or cookie is ever logged, printed or persisted,
and nothing is written to the database: the probe returns from `Program.Main`
before `DatabaseInitializer` runs.

**What the report prints.** A heartbeat stream followed by three blocks.

The heartbeat is one line per second while the probe runs, carrying elapsed
time, the current origin and path (never the query string), and what the probe
did that cycle. Once the user is signed in, the probe walks a bounded list of
store pages (`/explore/`, `/replay/`, `/points/shop/`), giving each four polls
before steering to the next. Which of those pages reliably carries a populated
token is an open question the walk is designed to answer; the third run's
token was minted from the store root before the walk began. If no page carries a token the
probe concludes about fifteen seconds after sign-in rather than burning the
rest of a ten-minute timeout.

1. **SIGN-IN.** Outcome (token minted / signed in without token / not signed
   in / window closed / timed out), whether a password field was ever rendered,
   page loads on approved origins, and which mint route produced the token
   (`application_config/data-store_user_config` or `window.g_wapit`).
2. **TOKEN.** Whether a token was acquired, whether the JWT payload decoded,
   expiry with remaining lifetime, `aud`, `iss`, the resolved SteamID64 and
   steam3 account id, and whether the page's steamid and the token's `sub`
   agree.
3. **ENDPOINTS.** One line per endpoint (`ClientGetLastPlayedTimes`,
   `GetOwnedGames`, `GetUserYearInReview`), each carrying HTTP status,
   `x-eresult`, populated-or-empty, body size, and a shape fact or two (app/game
   counts, first-played date coverage, Year in Review games and monthly points).

**Which items this settles.**

- **Item 5** (sign-in inside Winnow's off-the-record WebView2 profile):
  partially VERIFIED, observed on all three runs. The
  password-plus-Steam-Guard path completed end to end on the first two runs;
  the third run completed sign-in but the probe's heuristic did not see a
  password field, so the exact login route is undetermined. QR route
  untested; hCaptcha not presented on any run and therefore untested.
- **Item 1** (store-minted token returns populated data from the three
  endpoints): **VERIFIED 2026-08-30.** All three returned HTTP 200,
  `x-eresult: 1`, POPULATED, under a store-minted `access_token`.
- **Item 2** (token lifetime and audience): **VERIFIED 2026-08-30.** Token
  expires 2026-08-31 15:11:55Z, 24h 22m from mint. Audience `web:store`.
  The 2022 "about a day" figure holds.

**What this run does not settle, stated plainly.**

- Item 3 (whether `steamid` may be omitted from `GetOwnedGames` and
  `GetUserYearInReview` under token auth). The probe sends a steamid to both
  calls that accept one.
- Item 4 (the same-account key-vs-token A/B, the "web login gets more" claim).
  The probe uses a token only.
- Item 6 (whether `steamRefresh_steam` survives and re-mints days later). The
  browser profile is private and is deleted at the end of the run.
- Item 7 (whether the JWT half of a WebBrowser `steamLoginSecure` cookie works
  directly as `access_token=`). The probe mints from `application_config`, not
  from the cookie.
- Refresh-token behaviour over days and scheduler-driven unattended renewal.
  Nothing is persisted.

**The instrument.** Three files and one guard in `Program.cs`, all marked
THROWAWAY VERIFICATION SCAFFOLDING and to be deleted once this section carries
its findings:

- `src/Winnow.Auth.WebView/SteamSignInProbeSession.cs`
- `src/Winnow.App/Services/SteamSignInProbe.cs`
- `tests/Winnow.Tests/SteamWeb/SteamSignInProbeTests.cs`
- The `--steam-signin-probe` guard in `src/Winnow.App/Program.cs`

All four were deleted in TASK-55 stage S3 (2026-08-31); this section is now the
only surviving record of the run. What was promoted rather than deleted: the mint
script and the poll into `WebView2SteamSignInSession`, the JWT claim reader into
`Winnow.Core.Auth.SteamJwtClaims`, and the mint-scope predicate into
`SteamAccountPagePolicy.AllowsMint`.

**Findings.**

| Question | Answer |
| --- | --- |
| Sign-in completed (item 5) | Partially VERIFIED. Observed on all three runs: twice 2026-08-29, once 2026-08-30. Password entry and Steam Guard completed on the first two runs; the third run completed sign-in but the probe reported "password field: never seen", so Steam took a route the heuristic does not recognise. QR route untested; hCaptcha not presented on any run. |
| Password field rendered | Yes on both 2026-08-29 runs. Not seen by the probe on 2026-08-30, though the user signed in; the detection is heuristic and does not cover all login routes. |
| Mint route | `application_config/data-store_user_config` from the store root (`https://store.steampowered.com/`). Token appeared on the 13th poll second, immediately after the sign-in redirect. VERIFIED 2026-08-30. |
| Token acquired | Yes. Outcome TokenMinted. VERIFIED 2026-08-30. |
| Token expiry (delta from now) | 2026-08-31 15:11:55Z; 24h 22m remaining at mint. Consistent with the 2022 "about a day" figure. |
| Token aud | `web:store` |
| Token iss | `r:0018_28B7BA66_D69F8` |
| SteamID64 / steam3 account id | 76561198009290480 / 49024752 |
| Page steamid vs token sub | Agree. The page's `steamid` and the token's `sub` are the same value. This settles that a WebView sign-in yields exact account identity at mint time, which is what TASK-53's visibility toggle needs and what TASK-54's disclosure refetch exists to obtain on the API key path. |
| ClientGetLastPlayedTimes: status, populated, shape | HTTP 200, `x-eresult: 1`, POPULATED, 277895 bytes. 613 apps, 592 carrying a first-played date. The 592 first-played dates match exactly the 592 `steam_first_played` rows the M5 backfill wrote under key auth. |
| GetOwnedGames: status, populated, shape | HTTP 200, `x-eresult: 1`, POPULATED, 269707 bytes. 841 games, all carrying names. |
| GetUserYearInReview: status, populated, shape | HTTP 200, `x-eresult: 1`, POPULATED, 92779 bytes. 2025 year: 43 games, 52 monthly points across 11 distinct months. Echoes account id 49024752. |

---

## 7.2. Refresh-token capture in the shipped sign-in (TASK-55 S3)

**Status: NOT VERIFIED LIVE.** TASK-55 stage S3 promoted the §7.1 probe into the
shipped `WebView2SteamSignInSession` and implemented refresh-token capture, but no
live Steam sign-in was performed in that stage. Section 7.1's run remains the only
live evidence, and it did not attempt a cookie read.

**Mechanism.** The `steamRefresh_steam` cookie is read through
`CoreWebView2.CookieManager.GetCookiesAsync("https://login.steampowered.com/")`,
the browser-process cookie API, rather than from any page script. The cookie is
`httpOnly` and scoped to `login.steampowered.com`, so `document.cookie` cannot see
it. The mint script is never asked to try; a test
(`SteamSignInSessionTests.The_mint_script_never_asks_for_a_cookie`) pins that the
script contains no `document.cookie`. Microsoft documents
`CoreWebView2CookieManager` as returning cookies irrespective of the `httpOnly`
flag; that is documentation, not a measurement taken here.

**Three things remain UNKNOWN until a live run, each with a different consequence.**

1. **Whether an off-the-record (InPrivate) WebView2 profile's cookie manager
   returns the session's in-memory cookies at all.** The shipped session is
   in-private by construction (amendment condition 1), so if it does not, no
   refresh token can ever be captured by this route.
2. **Whether Steam issues `steamRefresh_steam` for this sign-in at all.** It does
   so only when the user ticks "remember me" on Steam's own login form, and Winnow
   never scripts that form, so nothing in Winnow can tick it for them. Absence is
   therefore a normal outcome, not a bug.
3. **The cookie's exact value shape.** The session code treats it as opaque.
   `SteamSession` already reads the `steamid64||jwt` form when parsing a refresh
   expiry and treats an unreadable value as an unknown expiry rather than guessing.

**What the implementation does about not knowing.** It never fakes a capture.
`SteamSignInResult.RefreshTokenCaptured` is set from what was actually found in
the jar (a whitespace-only value counts as nothing). The failure to read the jar
is caught and logged by exception type only. A sign-in with no refresh token still
succeeds as a working session.

**The consequence S6's renewal design must plan for.** S2 defines a stored
`SteamSession` as BOTH secrets: `SteamSession.TryCreate` returns null without a
refresh token, so a sign-in that captures no refresh token currently persists
nothing at all. The user gets a session that lasts about a day within the running
process, and unattended scheduled work is unaffected only because it was never
given the session. If a live run shows the cookie cannot be captured, the choice
in front of S6 is explicit: relax the session record so the refresh token is
optional (a short-lived, non-renewable stored session that the health enum can
already describe), or accept that WebView sign-in is a user-initiated credential
only and that the Web API key remains the sole unattended one. Neither is decided.

**What would settle it.** One live sign-in with "remember me" ticked, reporting
only whether a non-empty `steamRefresh_steam` was present. No value, ever, in any
report or log.

---

## 8. Recommended architecture sketch

**Premises verified 2026-08-30.** The §7.1 probe confirmed that a store-minted token
returns populated data from all three endpoints, that sign-in completes inside Winnow's
WebView2 profile, and that the token lifetime is about a day (24h 22m measured). The
recommendation below is unchanged; the refresh question (items 6, 7) that would enable
unattended token use remains open.

Treat the token as a second credential behind the seam that already exists, not as a
second auth system. `ISteamApiKeyProvider` / `SteamApiKey` in
`src/Winnow.Enrich.SteamWeb/Credentials/` already isolate "what goes in the query
string" from the three call sites that build it (`SteamHistoryClient` lines 102 and 154,
`SteamWebApiClient` line 184, each of which hand-concatenates `&key=`); widening that
record into a credential that knows its own parameter name (`key=` or `access_token=`)
and its own expiry is a small change, and it is the right place for a WebView sign-in to
plug in. Mint the token the way Playnite does, read `application_config`'s
`data-store_user_config.webapi_token` and `data-userinfo.steamid` off a store page, but
do it as the last step of the harvest session ROADMAP section 4.7 already sanctions, so
no new browser visit and no new trust condition is introduced. Persist the token, its
`exp` and the steamid under DPAPI and nothing else: no cookies, which keeps amendment
condition 1 intact. Then be honest about what that buys, roughly a day, so the token
should be the credential for user-initiated work (a sign-in-then-backfill run, plus the
Family Sharing calls the key cannot make at all) while the API key stays the credential
for the unattended snapshot scheduler; a 401 should mark the token dead and surface a
one-click re-sign-in rather than retry. Persisting `steamRefresh_steam` to get silent
months-long re-minting is a real option and the only one that would make the token a true
peer of the key for scheduled work, but it puts a durable full-account bearer credential
at rest and is therefore a section 4.7 amendment in its own right, not an implementation
detail; it should be deferred to its own decision.

---

## Sources

- `https://store.akamai.steamstatic.com/public/shared/javascript/auth_refresh.js`,
  Valve's shipped token-refresh script; fetched 2026-08-29. VERIFIED: `g_wapit` is a
  JWT with `exp`, refresh window of 30-40 min before expiry, `POST
  login.steampowered.com/jwt/ajaxrefresh` with `withCredentials: true`.
- `https://store.steampowered.com/replay/`, fetched anonymously 2026-08-29. VERIFIED:
  `application_config` carries `data-store_user_config` with a `webapi_token` field, and
  `window.g_wapit`, both empty when signed out.
- `https://store.akamai.steamstatic.com/public/javascript/applications/store/main.js`,
  Valve's store SPA bundle; downloaded 2026-08-29. VERIFIED: the web-API transport sends
  `access_token` and never a key; 401 triggers re-mint and retry; proactive refresh at
  `exp - ~900s`; reads `x-eresult` / `x-error_message`; `/replay` route table entry.
- `https://github.com/SteamDatabase/SteamTracking`,
  `ProtobufsWebui/service_player.proto`,
  `ProtobufsWebui/service_salefeature.proto`,
  `ProtobufsWebui/service_authentication.proto`. VERIFIED: per-rpc `bConstMethod` /
  `ePrivilege` / `eWebAPIKeyRequirement` annotations.
- `https://github.com/xPaw/SteamWebAPIDocumentation`, `api.json`, `src/App.vue`,
  `src/App.ts`. VERIFIED: no per-method token/key marking exists; the documented mint
  routes (`pointssummary/ajaxgetasyncconfig`, `steamcommunity.com/my/edit/info` ->
  `loyalty_webapi_token`); the token is a JWT with `exp`, `aud`, `sub`, preferred over
  the key.
- `https://github.com/SteamRE/SteamKit/issues/1125` (xPaw, 2022-08-28). VERIFIED: web
  JWT expires in a day; `login.` subdomain holds a separate JWT cookie lasting 207 days
  with "remember password".
- `https://github.com/DoctorMcKay/node-steam-session`, README and
  `src/LoginSession.ts`. VERIFIED: audiences per platform type; `getWebCookies()` works
  for WebBrowser via `/jwt/finalizelogin` from a refresh token alone;
  `refreshAccessToken()` / `renewRefreshToken()` are `AccessDenied` for WebBrowser as of
  2025-04-30; cookie value is `steamid64||accessToken`; renewing a refresh token
  invalidates the old one; `steamRefresh_steam` is returned by `finalizelogin`.
- `https://github.com/DoctorMcKay/node-steam-session/issues/56` (2026-05-20). REPORTED,
  unresolved, no maintainer reply: `AccessDenied` on every refresh-token-to-cookie
  route.
- `https://github.com/JosefNemec/PlayniteExtensions`, files
  `source/Libraries/SteamLibrary/Services/SteamStoreService.cs`,
  `.../Services/PlayerService.cs`, `.../Services/FamilyGroupsService.cs`,
  `.../Services/ParentalService.cs`, `.../Services/ClientCommService.cs`,
  `.../SteamLibrarySettingsViewModel.cs`. VERIFIED: `access_token` vs `key` pairs on
  `ClientGetLastPlayedTimes` and `GetOwnedGames`; token-only family/parental/clientcomm
  services; token minted per use from `application_config`; only the steamid is
  persisted.
- `https://github.com/JosefNemec/PlayniteExtensions/wiki/Steam-troubleshooting`.
  REPORTED: "Web login gets more of your Steam library, including uninstalled and
  unplayed free games and demos, and Family Sharing games"; login-loop remedy is clearing
  the web cache.
- `https://github.com/JosefNemec/PlayniteExtensions/issues/512` (open, 2026-02-12) and
  `.../issues/507` (2025-12-13). VERIFIED as reports: daily re-authentication prompts,
  the maintainer's shared-WebView-profile diagnosis, and the login loop.
- Live anonymous probes of `api.steampowered.com`, 2026-08-30 UTC; the section 2 table.
- `https://partner.steamgames.com/doc/webapi_overview/auth` (Valve). VERIFIED by
  absence: Valve's public Web API auth documentation describes user keys and publisher
  keys only, and says nothing about access tokens. The token path is entirely
  undocumented by Valve.
- `https://heroicgameslauncher.com/faq` and the Heroic wiki. REPORTED: webview sign-in,
  no credentials stored, access/refresh tokens persisted to a local JSON file. Not a
  Steam precedent; Heroic does not integrate Steam.
- `https://help.steampowered.com/en/faqs/view/1F7B-387B-A923-DE2E` and general hCaptcha
  troubleshooting coverage. REPORTED: browser-environment sensitivity of the login
  captcha.
