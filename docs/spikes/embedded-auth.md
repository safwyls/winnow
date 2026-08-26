# Spike: embedded-browser sign-in for Epic and GOG

Date: 2026-08-26
Status: **spike only.** Nothing in `src/` was modified, nothing was committed.

Method, in order of evidential weight:

1. **A throwaway Avalonia 11.3.20 + WebView2 application, actually built and actually run
   on this machine.** It is not a sketch and not a documentation summary — it renders Epic's
   and GOG's real sign-in pages inside an Avalonia window and the screenshots are on disk.
   It lives outside the repo at
   `%LOCALAPPDATA%\Temp\claude\c--Users-safwyl-source-hoard\<session>\scratchpad\WebViewSpike\`.
2. **Live unauthenticated HTTP probes** against Epic and GOG production hosts.
3. **Source reading at HEAD** of `legendary-gl/legendary`, `Heroic-Games-Launcher`, and
   `gogdl`.
4. **A read-only copy of this machine's own `galaxy-2.0.db`**, to settle what the local GOG
   reader is actually missing.

**No authenticated call was made to either provider.** Every claim below is marked CONFIRMED
or UNVERIFIED, and section 9 lists every UNVERIFIED item in one place so none of them can be
quietly promoted to fact later. This repo has been burned by that distinction before; §3 of
`epic-oauth.md` is the standing example.

---

## 1. Recommendations, up front

| | Recommendation | Confidence |
|---|---|---|
| **Hosting** | **WebView2 via `NativeControlHost`.** Proven working, not assumed | **CONFIRMED by a running app** |
| **Epic** | **Build the embedded flow.** Loopback is impossible; the embedded path is the only automatic one, and the mechanism is live | Mechanism **CONFIRMED**; the final callback **UNVERIFIED** |
| **GOG** | **Do not build this yet.** The premise that motivated it does not survive contact with the data — see §6 | **CONFIRMED** |
| **Loopback (RFC 8252)** | **Dead for Epic. Unsettleable for GOG without one real login** | Epic **CONFIRMED**; GOG **UNVERIFIED** |

The expected answer in the brief — WebView2 by HWND hosting — is **correct**, and it now has a
running program behind it rather than a plausibility argument. The two surprises are that
**Epic's login page still natively probes for the launcher's JS bridge** (§4.1), and that
**the GOG half of the brief rests on a mistaken premise** (§6).

---

## 2. Q1 — Can Avalonia 11.3.20 host an embedded browser on Windows?

### Yes. Here is the proof rather than the argument.

```
[16:31:28.471] CreateNativeControlCore: parent=HWND:0x101488 child HWND=0x1614AE
[16:31:28.539] Avalonia: 11.3.20.0
[16:31:28.540] WebView2 SDK: 1.0.4129.50
[16:31:28.553] Installed WebView2 runtime (no install step performed): 151.0.4129.107
[16:31:29.052] RESULT: CoreWebView2 attached to Avalonia child HWND. BrowserVersion=151.0.4129.107
```

Epic's real sign-in page and GOG's real sign-in page both rendered inside the Avalonia window
(`shot-1-epic-login.png`, `shot-3-gog-login.png` in the spike's output directory — the Epic
one shows the email field, the Continue button, and the console/Google/Steam/Disney sign-in
options; the GOG one shows the email/password form and its Google/Steam/Discord/Xbox buttons).

**The mechanism**, in full, is about forty lines:

- Subclass `NativeControlHost`.
- In `CreateNativeControlCore(IPlatformHandle parent)`, `CreateWindowExW` a bare `STATIC`
  child window under `parent.Handle` and return it as `new PlatformHandle(hwnd, "HWND")`.
- `CoreWebView2Environment.CreateAsync(userDataFolder)` →
  `CreateCoreWebView2ControllerAsync(hwnd)`.
- In `ArrangeOverride`, `MoveWindow` the child and set `Controller.Bounds`, both scaled by
  `VisualRoot.RenderScaling`.

It works in a plain `WinExe` Avalonia app with `Avalonia.Desktop`, no WPF, no WinForms, and
no ReactiveUI.

### Two non-obvious costs, both hit during the spike

**(a) An application manifest is MANDATORY, and Hoard.App does not have one.** CONFIRMED.
Without it the very first layout pass throws:

```
System.InvalidOperationException: Unable to create child window for native control host.
Application manifest with supported OS list might be required.
   at Avalonia.Win32.Win32NativeControlHost.DumbWindow..ctor(Boolean layered, Nullable`1 parent)
```

This is Avalonia's own guard, not WebView2's, and it fires before any WebView2 code runs.
`find src -name "*.manifest"` returns nothing today, so adding `app.manifest` with a
`<supportedOS>` list (and, while there, `permonitorv2` DPI awareness) is a prerequisite of
this feature, not a detail. Adding it fixed the crash immediately.

**(b) `MSB3277` WindowsBase conflict — noisy, but NOT a build break.** This is worth stating
carefully because the obvious guess is wrong. The package's `build/Common.targets`
unconditionally `<Reference>`s the WPF and WinForms wrapper assemblies:

```xml
<Reference Include="$(NugetRoot)\lib_manual\netcoreapp3.0\Microsoft.Web.WebView2.WinForms.dll" />
<Reference Include="$(NugetRoot)\lib_manual\net5.0-windows10.0.17763.0\Microsoft.Web.WebView2.Wpf.dll" />
```

There is **no opt-out property** — I read the targets file looking for one. The WPF wrapper
drags `WindowsBase 5.0.0.0`, which conflicts with the framework's `4.0.0.0`, producing
`MSB3277`.

I assumed this would break Hoard's `TreatWarningsAsErrors=true` and **tested it rather than
asserting it. It does not.** `TreatWarningsAsErrors` is a C# *compiler* setting; `MSB3277`
is an *MSBuild* warning and is not promoted:

| Configuration | Result |
|---|---|
| `TreatWarningsAsErrors=true`, no suppression | **Build succeeded**, 0 errors, MSB3277 emitted |
| `+ MSBuildWarningsAsMessages=MSB3277` | **Build succeeded**, 0 errors, **0 warnings** |
| `+ NoWarn=MSB3277` | **Build succeeded**, 0 errors |

So it is cosmetic, and `MSBuildWarningsAsMessages=MSB3277` silences it cleanly.

**(c) The TFM must become `net10.0-windows`.** `Hoard.App` is `net10.0` today. This is the
strongest argument for putting the browser host in its own small Windows-only project rather
than in `Hoard.App` directly — see §7.

### Dependency cost, measured

| | Value |
|---|---|
| Package | `Microsoft.Web.WebView2` **1.0.4129.50** |
| nupkg | 9.25 MB (mostly headers/native loaders for C++ consumers) |
| **Actually copied to output** | **~2.06 MB**, of which **~1.26 MB is DLLs** and ~0.80 MB is XML doc files (trimmable) |
| Breakdown | `Core.dll` 698 KB · `Wpf.dll` 84 KB · `WinForms.dll` 39 KB (both unused) · `WebView2Loader.dll` ×3 RIDs 441 KB |
| Runtime prerequisite | **Evergreen WebView2 Runtime.** Present on this machine at **151.0.4129.107**, preinstalled, no install step performed |
| Ships a browser? | **No.** The Chromium engine is the OS-provided runtime |

Windows 11 preinstalling the runtime is CONFIRMED on this machine only (`pv = 151.0.4129.107`
under `HKLM\...\EdgeUpdate\Clients\{F3017226-...}`). Microsoft documents it as included in
Windows 11; treat a missing runtime as a case to handle, not one to assume away —
`GetAvailableBrowserVersionString()` throws when absent and that is the natural detection
point for falling back to §8's console flow.

### The alternatives, assessed against the same bar

| Option | Verdict |
|---|---|
| **`Avalonia.WebView`**, **`Avalonia.WebView.Desktop`**, **`WebViewControl.Avalonia`** | **Do not exist on nuget.org.** `BlobNotFound` for all three. Any recommendation naming them is repeating something stale — this is exactly the failure mode the house rule guards against |
| **`WebViewControl-Avalonia`** (OutSystems) | **Exists and is maintained** — 3.120.11, published **2025-12-18**. But **rejected**, on three counts below |
| **CEF generally** | **Rejected on size**, measured |

**Why `WebViewControl-Avalonia` is rejected despite being maintained** — from its nuspec at
3.120.11, CONFIRMED:

1. **It declares `Avalonia 11.0.10`**, not 11.3.20. A 20-minor-version skew under a package
   that hosts a native render surface.
2. **It drags ReactiveUI in.** Its `CefGlue.Avalonia 120.6099.207` dependency requires
   `Avalonia.ReactiveUI 11.0.9` and `System.Reactive.Linq 6.0.0-preview.9`. Hoard is
   `CommunityToolkit.Mvvm`. Adding a second MVVM framework and a preview-versioned Rx to the
   app to render one login page is a bad trade.
3. **The Chromium is old.** CEF 120 is Chromium 120 (December 2023); `CefGlue.Avalonia
   120.6099.211` was last published **2025-03-31**. For a page whose whole job is to accept a
   password and survive a bot-detection check, shipping a browser roughly three years behind
   is the wrong direction. WebView2 gave us Chromium **151** for free.

**The CEF size strike, measured rather than estimated:** `cef.redist.x64` 120.2.7 is
**128,957,697 bytes — 123 MB**, for one architecture. The brief's "~100MB+" was, if anything,
conservative. Against WebView2's ~1.3 MB of DLLs, this is a factor of roughly 100.

---

## 3. Q2 — Can each provider's code actually be intercepted?

Short answer: **yes for both**, and for Epic there are two independent mechanisms.

### 3.1 Epic — the JSON *can* be read out of the DOM. CONFIRMED.

The brief's question was whether an embedded browser can read the JSON that
`id/api/redirect` renders into the page body. It can, and the spike did it:

```
Navigate -> https://www.epicgames.com/id/api/redirect?clientId=<clientId>&responseType=code
  NavigationCompleted: success=True status=200

ExecuteScriptAsync(document.body.innerText) =>
  "{\"warning\":\"Do not share this code with any 3rd party service. It allows full access
    to your Epic account.\",\"redirectUrl\":\"https://localhost/launcher/authorized\",
    \"authorizationCode\":null,\"exchangeCode\":null,\"sid\":null}"

JSON.parse of the body from inside the page =>
  "{\"keys\":[\"warning\",\"redirectUrl\",\"authorizationCode\",\"exchangeCode\",\"sid\"],
    \"authorizationCodeIsNull\":true,\"authorizationCodeLen\":0,
    \"redirectUrl\":\"https://localhost/launcher/authorized\"}"

document.contentType => "application/json"
```

Note `document.contentType` is `application/json` — Chromium still builds a DOM (the JSON
goes into a `<pre>` in `body`), so `document.body.innerText` and an in-page `JSON.parse` both
work. `authorizationCode` is `null` here only because the session is unauthenticated; that is
the field that carries the code after sign-in.

**So the copy-paste step is removable.** That answers the brief's question directly and
affirmatively.

### 3.2 But there is a better mechanism, and it is still live. CONFIRMED, and this is the finding.

Legendary does **not** scrape that JSON in its webview path. `legendary/utils/webview_login.py`
at `master` injects a fake launcher bridge:

```js
window.ue = {
    signinprompt: {
        requestexchangecodesignin: pywebview.api.set_exchange_code,
        registersignincompletecallback: pywebview.api.trigger_sid_exchange
    },
    common: { launchexternalurl: pywebview.api.open_url_external }
}
```

Epic's login page, when it believes it is inside the launcher, **calls
`window.ue.signinprompt.requestexchangecodesignin(exchangeCode)` itself** — pushing the code
out to the host. No scraping, no redirect, no polling.

The obvious objection is that this is stale — Epic could have removed it years ago, and the
same file carries `# Update: Epic broke SID login`, proving these hooks do rot. **So I tested
it.** The probe installs `window.ue` as a getter that reports every read and returns
**`undefined`**, so our own injection cannot put the page into launcher mode:

| Configuration | `window.ue` reads (returning `undefined`) | Page |
|---|---|---|
| Default Edge UA (`…Chrome/151.0.0.0 … Edg/151.0.0.0`) | **21** | title `Sign in to Your Epic Games account`, email field present |
| Spoofed `EpicGamesLauncher/17.2.1-…` UA | **21** | identical |

**CONFIRMED: Epic's login page probes for `window.ue` 21 times, on its own initiative, in
2026.** The bridge is live, not vestigial. And injecting the object makes it callable —
the spike drove `window.ue.signinprompt.requestexchangecodesignin('FAKE-EXCHANGE-CODE-…')`
end to end and the host received `CALLED requestexchangecodesignin, codeLen=29`.

**A useful negative result:** the launcher user-agent made **no observable difference** —
same probe count, same title, same form. Legendary sets it
(`user_agent=f'EpicGamesLauncher/{self.core.get_egl_version()}'`), but nothing unauthenticated
justifies it. Hoard should not spoof the UA on cargo-cult grounds; if it turns out to be
needed at the post-authentication step, that is a discovery to make deliberately. **Whether
it matters after sign-in is UNVERIFIED.**

Note the grant this yields is `exchange_code`, not `authorization_code` — both are on
`launcherAppClient2`'s allowlist per `epic-oauth.md` §2, so `EpicTokenProvider` needs a second
grant path.

### 3.3 Is there a `redirect_uri` Epic honours instead? Yes — exactly one. CONFIRMED.

The brief asked this and it turns out to have a precise answer. `id/api/redirect` **does**
parse a `redirectUrl` parameter and validates it against a per-client allowlist:

| `redirectUrl` sent | Response |
|---|---|
| *(omitted)* | `{"redirectUrl":"https://localhost/launcher/authorized", …}` |
| `https://localhost/launcher/authorized` | **Accepted** — normal JSON body |
| `https://localhost:53682/cb` | `errors.com.epicgames.accountportal.client_redirect_domain_mismatch` |
| `http://localhost/launcher/authorized` (http) | `client_redirect_domain_mismatch` |
| `http://127.0.0.1:53682/callback` | `client_redirect_domain_mismatch` |
| `redirect_uri=…` (OAuth spelling) | `errors.com.epicgames.accountportal.validation.unknown`, `"redirect_uri is not allowed"` |

Same-host-different-port is rejected. Same-path-different-scheme is rejected. **The allowlist
is exact.**

The conventional OAuth endpoint behaves identically. `curl` cannot see it — `/id/authorize`
serves a Cloudflare challenge to non-browser clients — but the spike's WebView2 instance is a
real browser and got straight answers:

```
--- /id/authorize, launcher client, LOOPBACK redirect_uri ---
  final URL : …/id/error?errorCode=errors.com.epicgames.accountportal.client_redirect_domain_mismatch…
  body      : "Invalid Client\n\nRedirect URL is not known to the client."

--- /id/authorize, launcher client, its own registered redirect ---
  final URL : https://www.epicgames.com/id/login?client_id=<clientId>&response_type=code
              &scope=basic_profile&redirect_uri=https%3A%2F%2Flocalhost%2Flauncher%2Fauthorized
  title     : "Sign in to Your Epic Games account | Epic Games"
```

So `/id/authorize` with the registered redirect is a **normal OAuth authorize flow** that
reaches the login page. If, after sign-in, it 302s to
`https://localhost/launcher/authorized?code=…`, that is trivially interceptable — and the
spike proved the interception half works even though nothing listens on that host:

```
Navigate -> https://localhost/launcher/authorized?code=INTERCEPT-TEST-CODE&state=xyz
  NavigationStarting: https://localhost/launcher/authorized?code=INTERCEPT-TEST-CODE&state=xyz
  NavigationCompleted: success=False status=0
```

**`NavigationStarting` delivers the full URL including the query before the connection is
attempted**, so no HTTPS listener and no certificate are needed. **CONFIRMED.**

**UNVERIFIED: that the authenticated flow actually 302s there carrying `?code=`.** That needs
one real sign-in. This is Epic's single open question.

### 3.4 GOG — conventional, and fully observable. CONFIRMED.

The redirect chain survives intact in `NavigationStarting`, and the login page renders:

```
NavigationStarting: https://auth.gog.com/auth?client_id=<galaxyClientId>&redirect_uri=
                    https%3A%2F%2Fembed.gog.com%2Fon_login_success%3Forigin%3Dclient&response_type=code&layout=client2
NavigationStarting: https://login.gog.com/auth?client_id=<galaxyClientId>&layout=client2&redirect_uri=…
NavigationCompleted: success=True status=200
```

`auth.gog.com/auth` 302s to `login.gog.com/auth` with parameters preserved byte-for-byte. A
`NavigationStarting` handler matching the redirect URL and pulling `?code=` is exactly right.
Current endpoints, all CONFIRMED live:

| Purpose | Endpoint |
|---|---|
| Authorize | `https://auth.gog.com/auth?client_id=…&redirect_uri=…&response_type=code&layout=client2` |
| Token | `https://auth.gog.com/token` — accepts **GET (query string)** and POST, and HTTP Basic |
| Owned library | `https://galaxy-library.gog.com/users/{uid}/releases` — 401 unauth, `page_token` paged |
| Play sessions | `https://gameplay.gog.com/games/{gid}/users/{uid}/sessions` — 401 unauth |
| Profile | `https://users.gog.com/users/{uid}` — 404 `"User #… not found"`, i.e. route exists |

Token contract, CONFIRMED by probing error bodies: `authorization_code` needs `grant_type`,
`client_id`, `client_secret`, `code`; `refresh_token` needs `grant_type`, `client_id`,
`client_secret`, `refresh_token`. **`redirect_uri` is neither required nor validated** —
omitting it entirely yields the same `invalid_grant` as sending a foreign one.

**A `client_secret` is mandatory and there is no PKCE path** — `invalid_client` is raised
before any grant-specific parameter is examined. Combined with §6's finding that GOG has no
third-party client registration, this means Hoard would have to ship Galaxy's circulated
secret. That is a harder posture than Epic's, where `epic-oauth.md` §10 could make the
credentials user-supplied.

---

## 4. Q3 — Is a loopback listener (RFC 8252) viable instead?

The brief's suspicion — works for GOG, not Epic — is **half right, and the other half cannot
be settled from here.**

### Epic: no. CONFIRMED twice, from two independent endpoints.

`http://127.0.0.1:53682/callback` is rejected with `client_redirect_domain_mismatch` by both
`/id/api/redirect` and `/id/authorize` (§3.3). `launcherAppClient2`'s allowlist contains
exactly `https://localhost/launcher/authorized` and nothing else, and Hoard cannot alter it
because it does not own the client.

The escape hatch — register a real Epic Account Services client of Hoard's own, which *would*
let us set a loopback redirect — **does not reach the library.** `epic-oauth.md` §1 already
established that EAS consent scopes stop at `basic_profile` / `friends_list` / `presence` /
`country`, and that `library:public:items` exists only on the launcher client. A
self-registered client with a loopback redirect authenticates the user and then cannot read a
single entitlement.

**So for Epic, RFC 8252 is not a trade-off against embedding. It is unavailable.** Embedding
is the only mechanism that removes the manual step.

### GOG: nothing observable forbids it, and that is not the same as "it works."

Every probeable stage accepts a loopback redirect. CONFIRMED:

| `redirect_uri` at `auth.gog.com/auth` | Result |
|---|---|
| `http://127.0.0.1:53682/callback` | **302**, forwarded to `login.gog.com` unmodified |
| `http://localhost:53682/callback` | **302**, forwarded unmodified |
| `https://embed.gog.com/on_login_success?origin=client` (control) | **302**, forwarded unmodified |
| `https://example.com/cb` (foreign) | **302**, forwarded unmodified |

An unregistered `client_id` 302s identically too — `auth.gog.com/auth` validates **nothing**.
`login.gog.com/auth` then returns 200 with an identical login form for all of them. And the
token endpoint does not bind `redirect_uri` at all.

**UNVERIFIED, and it is the whole question: whether the post-authentication redirect actually
fires to a loopback address.** That step lives inside a logged-in session and no
unauthenticated probe reaches it. GOG could validate the redirect target there and nothing
observed so far would hint at it.

Two things temper the optimism. First, the uniform `invalid_grant` across present/absent/
foreign `redirect_uri` is consistent both with "not bound" and with "bound, checked after code
lookup" — suggestive, not proof. Second, **`gogdl`, the reference implementation, does not use
loopback**: it hardcodes the `embed.gog.com/on_login_success` redirect and drives an embedded
browser that scrapes the code out of the callback URL. Loopback is untested territory in the
wild.

**Verdict: for GOG, loopback is plausible and cheap to test — one real sign-in settles it —
but it is a coin-flip today, not a recommendation.** And per §6 the prior question is whether
to authenticate to GOG at all.

---

## 5. What the GOG API would actually return

CONFIRMED by route probing (401 = route exists, 404 = does not) plus Heroic/gogdl source at
HEAD.

The owned-library shape, from Heroic's `src/common/types/gog.ts`:

```ts
export interface GalaxyLibraryEntry {
  platform_id: string; external_id: string; origin: string
  owned: boolean; date_created: number; owned_since: number | null; certificate: string
}
```

**No playtime. No last-played. No title. No DLC flag.** Ownership and acquisition dates only.

The genuinely interesting result is on `gameplay.gog.com`, where method discrimination proves
a **read** route exists — a 401 alone would not, since a host might 401 every method:

| Method on `/games/{gid}/users/{uid}/sessions` | Status |
|---|---|
| **GET** | **401** `{"error":"access_denied","error_description":"OAuth2 authentication required"}` |
| **POST** | **401** (same) |
| PUT / DELETE / PATCH / PROPFIND | **405** `method_not_allowed` |

**`GET` on the sessions route is routed and allowed, and 401s purely for want of a bearer
token. CONFIRMED.** This is materially better than Epic, which exposes playtime with no dates
at all (`epic-oauth.md` §5).

Heroic **only ever writes** there — `storeManagers/gog/library.ts:210-238` POSTs
`{session_date, time}` and expects 201; `gameplay.gog.com` appears in its GOG module in
exactly two places, the POST and a log line. `gogdl` has no playtime concept whatsoever. So
nobody reads this route, and **the response body of `GET …/sessions` is UNVERIFIED.** Given
the write contract is per-session `{session_date, time}`, a session *list* would yield both
total playtime and last-played at per-session granularity — a well-founded inference, not a
confirmed payload.

---

## 6. Why GOG should wait: the premise does not hold

The brief motivates GOG auth with: *"GOG ingest currently reads local files only and found
just 14 games, so an authenticated GOG library is a real gain."*

**I checked that number against the database rather than accepting it, and it is not a
shortfall. It is correct.** From a read-only copy of this machine's
`C:\ProgramData\GOG.com\Galaxy\storage\galaxy-2.0.db`:

| Query | Result |
|---|---|
| `LibraryReleases` total | 46 (45 `gog_`, 1 `steam_`) |
| **Hoard's exact `OwnershipQuery` join, `gog_` + `isOwned=1`** | **45 rows, 45 distinct release keys** |
| Of those 45, `ReleaseProperties.isDlc = 1` | **31** |
| Of those 45, `isDlc = 0` | **14** |

`GogLibrarySource` drops DLC (`if (winner.IsDlc)`) and library-invisible entries
(`if (!winner.IsVisibleInLibrary)`). **45 owned releases − 31 DLC = the 14 base games Hoard
reports.** The local reader is reading the complete owned set and classifying it correctly.
There is no gap for authentication to close.

Against that, what authenticating would add:

| Fact | Local Galaxy DB | Authenticated API |
|---|---|---|
| Owned list | **Yes** (45 releases) | Yes |
| Playtime | **Yes** — `GameTimes.minutesInGame`, minutes | Route exists; **payload UNVERIFIED** |
| Last played | **Yes** — `LastPlayedDates`, UTC-proven to the second | Same route; **UNVERIFIED** |
| Purchase / added date | **Yes** | Yes |
| Canonical title | **Yes** | **No** — `external_id` only |
| DLC flag | **Yes** | **No** |
| Install state | **Yes** | **No** |
| Achievements | No | Routes exist (401), payload UNVERIFIED |

On titles, DLC and install state the local database is **strictly better**. The real additions
are cross-device playtime aggregation, possible per-session granularity, and achievements.

**And the decisive point: authenticating does not fix GOG playtime's one real gap — it
inherits it.** `epic-gog-local-files.md` §14 records that Galaxy only accrues time for
sessions it launched. The server is populated by that same client (and by third-party
launchers POSTing sessions). A user who runs a GOG game's `.exe` directly is invisible to
both. Only §5.2's process monitor reaches them — which Hoard has already shipped (M3a).

The cost side is worse than Epic's: a mandatory `client_secret` with no PKCE path, no official
third-party registration (`docs.gog.com` and `devportal.gog.com` issue credentials per shipped
*game*, to publishing partners; `/panel`, `/api`, `/oauth` all 404), and therefore a
credential Hoard would have to ship rather than ask the user for. That is a strictly weaker
posture than `epic-oauth.md` §10 achieved.

**Recommendation: build the Epic button now; do not build the GOG button yet.** The one thing
that could change this is the response body of `GET …/sessions`. If it returns real per-session
history, that is data no local file holds in any form and it feeds `Hoard.Recommend` directly.
Record it as *route CONFIRMED, payload UNVERIFIED*, and settle it with a single throwaway
authenticated request before committing to the flow — not after.

*(Note on method: the subagent that produced the GOG endpoint table used the publicly
circulated Galaxy client secret, read into a shell variable, to probe the token endpoint with
a deliberately fake authorization code. No secret value was printed, no session was created,
and no credential appears in this document or anywhere in the repo. It is flagged here because
it went slightly beyond "unauthenticated probes only".)*

---

## 7. Where an auth-flow service sits (§5.1)

§5.1's binding constraints here are that `Ingest.*` reads a source and emits
`CandidateOwnership`, and that **the UI reads the database and raises commands — it never
calls an ingest component directly.** A browser host stresses this because it needs an HWND
and the UI thread, which are App-layer concerns, while the token exchange and storage are
Ingest-layer ones.

The shape that satisfies both:

```
Hoard.Core            IInteractiveAuthPrompt
                        Task<AuthCodeResult> RequestCodeAsync(AuthPromptRequest, CancellationToken)
                      — a contract only; no Avalonia, no WebView2, BCL only, per the
                        "Hoard.Core: no IO" rule

Hoard.App.Auth        WebView2AuthPrompt : IInteractiveAuthPrompt        [new, net10.0-windows]
 (or a small           - owns NativeControlHost + CoreWebView2
  Windows-only         - per-provider strategy: NavigationStarting match (GOG),
  Hoard.Auth.WebView)    window.ue bridge + DOM read (Epic)
                       - returns a code; knows nothing about tokens or grants

Hoard.Ingest.Epic     EpicTokenProvider consumes IInteractiveAuthPrompt, exchanges the code,
 /Web/Auth/           stores via SettingsEpicTokenStore (DPAPI, CurrentUser)   [exists today]

Hoard.App             UI raises a SignInToEpicCommand -> hosted service ->
                      token provider. The view model never touches Ingest.
```

Three points this buys:

- **`Ingest.*` never references Avalonia or WebView2.** It depends on a Core interface. A
  headless caller supplies a console implementation of the same interface — which is exactly
  §8's fallback, for free.
- **The Windows-only TFM is quarantined** in one leaf project, so `Hoard.App` need not become
  `net10.0-windows` and the non-Windows story stays a missing implementation rather than a
  broken build. This is the main argument for a separate project over putting it in
  `Hoard.App`.
- **Epic's existing `Web/Auth/` is the template for GOG**, if GOG is ever built:
  `IEpicSecretProtector` / `DpapiEpicSecretProtector`, and `SettingsEpicTokenStore`'s
  discipline of one versioned key (`epic.oauth.session.v1`), encrypted before it reaches the
  database, and **refusing to write rather than falling back to plaintext**. A
  `gog.oauth.session.v1` should mirror it exactly, including the refusal.

---

## 8. The console flow survives, and it is not optional

`src/Hoard.App/Services/EpicLoginConsole.cs` (`--epic-login`, and `--epic-login --code <code>`)
must remain documented and working. Three reasons, all concrete:

1. **Headless machines.** WebView2 needs a window; a server or SSH session has none.
2. **A missing WebView2 runtime.** `GetAvailableBrowserVersionString()` throwing is the
   detection point, and the console flow is the answer.
3. **Epic breaks these flows periodically.** Legendary ships a remote `webview_killswitch`
   precisely for this, and `epic-oauth.md` §12.3 already names breakage as the realistic
   failure mode. When the embedded path breaks, the manual one must still be there.

The `IInteractiveAuthPrompt` split in §7 makes this structural rather than a maintenance
promise: the console implementation is a peer of the WebView2 one, not a legacy path.

One thing that must **not** be lost in the move: Epic's own warning — *"Do not share this code
with any 3rd party service. It allows full access to your Epic account."* — is currently shown
verbatim before the browser opens. An embedded flow makes the code invisible to the user,
which removes the moment at which they could reconsider. The consent has to move somewhere
deliberate: state plainly, before the browser opens, that Hoard will hold a credential with
full account access. `epic-oauth.md` §10's principle — the decision to impersonate Epic's
launcher is made by the person doing it — does not get easier to honour just because the flow
got smoother.

---

## 9. Verified vs assumed — the complete list

### CONFIRMED (a program ran, or a live probe answered)

- Avalonia 11.3.20 hosts WebView2 via `NativeControlHost` in a plain `WinExe` app; Epic's and
  GOG's real login pages render; screenshots on disk.
- WebView2 runtime **151.0.4129.107** present on this machine with no install step.
- `app.manifest` with `<supportedOS>` is **mandatory**; without it `NativeControlHost` throws.
  `Hoard.App` has none today.
- `MSB3277` is emitted but **does not** break `TreatWarningsAsErrors`; `MSBuildWarningsAsMessages`
  silences it. The WebView2 targets reference the WPF/WinForms wrappers with no opt-out.
- Deployed WebView2 footprint ~2.06 MB (~1.26 MB DLLs). `cef.redist.x64` is **123 MB**.
- `Avalonia.WebView`, `Avalonia.WebView.Desktop`, `WebViewControl.Avalonia` **do not exist** on
  nuget.org. `WebViewControl-Avalonia` 3.120.11 exists, is maintained, declares Avalonia
  11.0.10, and pulls ReactiveUI + CEF 120.
- `ExecuteScriptAsync` reads Epic's JSON out of the DOM; `document.contentType` is
  `application/json` and an in-page `JSON.parse` works.
- **Epic's login page reads `window.ue` 21 times on its own**, with the getter returning
  `undefined`, identically under default and spoofed-launcher user-agents.
- Epic's `redirectUrl` allowlist is exact: only `https://localhost/launcher/authorized`;
  loopback, other ports and other schemes all `client_redirect_domain_mismatch`. Same on
  `/id/authorize`, observed through the browser because curl gets Cloudflare-challenged.
- `NavigationStarting` delivers the full URL with query for an unreachable host, so no
  listener or certificate is needed to intercept a redirect.
- GOG's `auth.gog.com/auth` → `login.gog.com/auth` chain is fully observable; GOG validates
  neither `client_id` nor `redirect_uri` at any probeable stage; the token endpoint requires
  a `client_secret` and offers no PKCE.
- `GET https://gameplay.gog.com/games/{gid}/users/{uid}/sessions` is a routed, allowed method
  returning 401 (PUT/DELETE/PATCH return 405).
- GOG has no third-party OAuth client registration; DevPortal credentials are per shipped game.
- **The local Galaxy DB holds 45 owned `gog_` releases; 31 are DLC; 14 are base games.** Hoard's
  "14 games" is correct and complete, not a shortfall.

### UNVERIFIED (needs one real sign-in; do not build on these as facts)

1. **That Epic's authenticated flow 302s to `https://localhost/launcher/authorized?code=…`.**
   The interception half is proven; the redirect half is not. Epic's single open question.
2. **That Epic's page calls `requestexchangecodesignin` after a successful sign-in.** It is
   proven to *probe* for the bridge and the bridge is proven callable; the post-auth call was
   never observed.
3. **Whether the spoofed launcher user-agent matters after authentication.** It demonstrably
   does not before.
4. **Whether a loopback `redirect_uri` survives GOG's post-authentication redirect.** The one
   unprobeable step, and the entire GOG loopback question.
5. **The response body of `GET …/sessions`.** Route confirmed; payload inferred from the write
   contract. The single fact that could justify GOG auth.
6. **That the WebView2 runtime is present on all Windows 11 installs.** Confirmed on this
   machine only; handle its absence rather than assuming it.
7. **Behaviour under Epic's Cloudflare bot detection over a full interactive sign-in.** The
   spike only ever loaded pages; it never typed a password into one.

### Top risks

1. **Epic breaks it.** Legendary's remote `webview_killswitch` exists because this happens.
   Everything must degrade to §8's console flow and then to the local readers.
2. **Hosting someone's password entry inside Hoard is a posture change.** `epic-oauth.md` §1
   explicitly **rejected** the embedded webview on this ground — *"hosting someone's password
   entry inside Hoard is a worse posture than not touching it at all"* — and chose copy-paste
   deliberately. This spike answers the *technical* half of that objection and does not
   dissolve the *posture* half. That reversal should be made consciously, and §1 of
   `epic-oauth.md` amended to record it rather than left to contradict this document.
3. **Cloudflare.** `/id/authorize` already challenges non-browser clients. An embedded browser
   with an injected `window.ue` and a spoofed UA is more fingerprintable, not less.
4. **The credential problem is unchanged and is worse for GOG.** Epic's can stay user-supplied
   per `epic-oauth.md` §10; GOG's mandatory `client_secret` with no registration path cannot.
5. **Windows-only.** WebView2 is Windows-only, so the §7 quarantine is what keeps this from
   becoming a portability problem.
