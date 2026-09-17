# PlayStation for Winnow

Imports the PS4/PS5 account library returned by Sony, with optional played history and
PS3/PS Vita trophy-title history. This is a separate .NET 10 provider package using Winnow's
public SDK. It needs no Node.js runtime and does not run the npm package.

## Install and connect

Build the ZIP from the repository root:

```powershell
./plugins/Winnow.Plugin.Psn/Package.ps1
```

Copy `artifacts/psn-plugin/Winnow.Plugin.Psn-1.0.0.zip` into the folder opened by
**Settings → Plugins → Open plugins folder**. Restart, enable **PlayStation**, and restart
again. These controls are available on desktop and fullscreen.

1. Sign in to [PlayStation](https://www.playstation.com/) in your browser.
2. In that same browser, open [Sony's session-token endpoint](https://ca.account.sony.com/api/v1/ssocookie).
3. Copy only the 64-character `npsso` value into **Sony session token (NPSSO)** and select
   **Save settings**. Saving queues a refresh. The token is a credential: keep it out of
   messages, screenshots and logs.
4. Enable **Include played games** to add PS4/PS5 played titles and available cumulative
   minutes and last-played dates. Enable **Include PS3 and PS Vita trophy history** separately
   to add legacy trophy sets.

Sony's token workflow does not provide Xbox's device-code sign-in. The plugin uses the host's
masked secret editor and setup link. Windows protects NPSSO and refresh credentials with
current-user DPAPI. Access tokens stay in memory. Hosts without protected secret writes
cannot establish a persistent connection. When Sony rejects both refresh and session
credentials, replace the saved NPSSO with a newly obtained value.

Use **Remove saved PlayStation Sony session token (NPSSO)** to disconnect. The next refresh
discards the derived refresh credential; missing or changed NPSSO immediately prevents reuse
of in-memory account data. Imported games and cached facts remain. If NPSSO came from
`Plugins__psn__npsso`, remove that environment setting too: host configuration is a fallback
for user-editable secrets.

## What gets imported

| Source | Facts | Limits |
|---|---|---|
| PS4/PS5 purchase-library endpoint | Titles, source identity, platform tags and available icons | The service returns active library entries, including subscriptions. This is not a receipt or a guarantee of current access. |
| Optional played history | Additional PS4/PS5 titles, cumulative minutes, last played and available genres | Disc games, trials and subscriptions may appear. History does not prove ownership. |
| Optional legacy trophy history | Titles, descriptions, platform tags and icons for PS3/PS Vita trophy sets | Sets shared with PS4/PS5 are excluded. Trophy synchronization is not a last-played timestamp. |

Purchase entries and played history join only on the exact Sony title ID. Regional or
cross-generation versions remain separate unless the user groups them in Winnow. Trophy-set
IDs have their own namespace; matching names never trigger a merge. The verified numeric PSN
account ID scopes imported observations.

Missing playtime and dates remain unknown. First-played dates, trophy update dates and
library activity dates are not acquisition dates. The plugin does not create sessions,
declare a local installation, or offer Play/Install actions. It does not import individual
trophies into Winnow's achievements data; the current provider SDK has no achievement contract.
PSN PC titles and PS3/Vita purchase inventories are not included.

Icons become cover candidates only after their actual PNG, JPEG or supported static WebP
dimensions have been read. Dimensions are not guessed. Sony's icons can be square; portrait
covers, backgrounds and screenshots are not promised. Unsupported or unavailable images
preserve previous artwork.

## Requests and storage

All network access uses the host's HTTPS allowlist, rate limit, retry policy and response
bounds. No game-launcher files are accessed. The manifest allows only Sony's token, profile,
library, trophy and image hosts. Requests use four per second, two retries, 20-second HTTP
timeouts and a 2 MiB response limit. A library call has a 100-second budget inside the host's
120-second provider deadline.

| Request | Behavior |
|---|---|
| `ca.account.sony.com/api/authz/v3/oauth/authorize` and `/token` | NPSSO cookie → authorization code → access/refresh credentials. Redirects are inspected and never followed. The public PS App client identification comes from the referenced protocol. |
| `us-prof.np.community.playstation.net/userProfile/v1/users/me/profile2?fields=accountId` | Resolves the authenticated account; a refreshed connection must retain the same account. |
| `web.np.playstation.com/api/graphql/v1/op` | `getPurchasedGameList` persisted query, active PS4/PS5 library, 24 entries per page. |
| `m.np.playstation.com/api/gamelist/v2/users/{accountId}/titles` | PS4/PS5 played history, 200 entries per page. |
| `m.np.playstation.com/api/trophy/v1/users/{accountId}/trophyTitles` | Trophy sets, 200 entries per page, filtered to PS3/PS Vita only. |
| `image.api.playstation.com`, `psnobj.prod.dl.playstation.net` | Bounded image-header probes; the host separately downloads accepted covers. No account token is sent to these hosts. |

Each complete endpoint result is cached independently for six hours with account and
credential scope. A failed endpoint can use its previous complete result while others update.
Malformed or incomplete pagination never overwrites a complete cache. Pages must advance;
each endpoint is limited to 10,000 rows and each cache payload to 2 MiB. Very large or slow
libraries may exhaust the invocation budget and leave previous imported observations intact.
Authentication failure also preserves imported observations. **Refresh now** queues a pass;
it does not bypass a fresh six-hour cache.

Token replacement changes cache scope. Removing or replacing the credential, or changing
history options during a request, prevents publication of that request. Secrets are never
written to ordinary settings or response caches. Image dimensions have a separate 30-day
URL-hash cache with stale fallback.

## Protocol references and validation

The implementation follows the protocol documented by
[psn-api](https://www.npmjs.com/package/psn-api), its
[source repository](https://github.com/achievements-app/psn-api), and
[Andshrew's PlayStation Trophies documentation](https://andshrew.github.io/PlayStation-Trophies/).
Authentication and purchase queries follow psn-api; game history, trophy fields and the
account-profile endpoint follow Andshrew's documented responses. These are community
descriptions of Sony's private APIs, not a supported Sony developer integration. Endpoint
shapes, access and persisted-query hashes can change.

Tests use synthetic accounts and canned responses. They cover credential exchange and
rotation, identity isolation, pagination, stale fallback, cancellation, data provenance,
artwork validation and the desktop/fullscreen settings and detail surfaces. Live Sony
sign-in, an actual account inventory and console-reported durations still require validation
with a user account. No live PSN credentials are included in the repository.
