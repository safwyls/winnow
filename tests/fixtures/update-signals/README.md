# Update-signal fixtures

Captured 2026-08-23 by read-only `GET` with a descriptive `User-Agent`, no auth
and no API key, and stored **verbatim**. There is nothing to sanitize: these are
public announcement and depot-metadata responses containing no account data. See
`docs/spikes/update-signals.md` for the findings they encode.

| File | Endpoint | Request |
|---|---|---|
| `getnewsforapp-patchnotes-413150.json` | `ISteamNews/GetNewsForApp/v2/` | Stardew Valley; `count=1&maxlength=1&tags=patchnotes` |
| `getnewsforapp-nomatches-790.json` | `ISteamNews/GetNewsForApp/v2/` | appid 790 — has a feed, nothing tagged `patchnotes` |
| `steamcmd-info-413150.json` | `api.steamcmd.net/v1/info/{appid}` | Stardew Valley |
| `steamcmd-info-missing.json` | `api.steamcmd.net/v1/info/{appid}` | appid 999999999 — an app that does not exist |
| `steamcmd-info-demo-4028270.json` | `api.steamcmd.net/v1/info/{appid}` | Everwind Demo — the `common` block: `name`, `type: "Demo"`, `parent: "2253100"` (captured 2026-08-24) |
| `steamcmd-info-restricted-3065170.json` | `api.steamcmd.net/v1/info/{appid}` | Monster Hunter Wilds Beta test — `_missing_token`, no `common` at all (captured 2026-08-24) |

Two response shapes are **not** files here because they have no body worth
pinning, and both are asserted directly in `Updates/SteamNewsClientTests.cs`:

- **HTTP 403 with body `{}`** from `GetNewsForApp` — appids 460, 480, 520 and
  750, all re-verified on capture day. This means *"this appid has no news
  feed"*, **not** rate limiting. It is the single most dangerous shape in this
  module; see `NewsOutcome.NoFeed`.
- **HTTP 429** — never observed from either host. The handling ships anyway
  (§4.2), so the test synthesises it.

## Why these are pinned

`GetNewsForApp` is documented but its `tags` filter is not (Valve's own
description misspells the value as `patchnodes`), and api.steamcmd.net is an
unofficial volunteer mirror with no SLA — §4.5 records it erroring outright
during design. `UpdateSignalContractTests` asserts the exact shapes the parsers
depend on against these bytes, so when someone recaptures a fixture and the
assertions break, the contract changed and the soft-fail paths — which already
degrade to "no signal" rather than throw — can be re-verified against the new
reality instead of discovered in production.

The two most load-bearing facts in these files:

- `steamcmd-info-missing.json` is **HTTP 200**, not 404. A missing app is an
  empty inner object inside a `"status": "success"` envelope, so the parser must
  branch on the object and never on the status code.
- `steamcmd-info-413150.json` carries five branches — `compatibility`,
  `legacy_1.5.6`, `legacy_1.6.8`, `previous_version` and `public`. Only `public`
  is what a user is running.
- `steamcmd-info-restricted-3065170.json` is also **HTTP 200 with
  `"status": "success"`**, and its inner object is **not empty** — it carries
  `_change_number`, `appid`, `_missing_token: true` and `public_only: "1"` and
  no `common` or `depots` block at all. That is a third shape, distinct from
  both success and the empty missing-app object: the app exists, and an
  anonymous caller is not allowed to be told about it. Five appids of the
  author's library answer this way (8510, 854040, 1883690, 3065170, 3562740);
  `store.steampowered.com/api/appdetails` returns `success: false` for the same
  set. A separate five (229100, 236600, 451070, 813000, 1253950) answer with the
  EMPTY object of `steamcmd-info-missing.json` instead — Steam has no such app.
  Both are "no data" and neither is a parse failure, but only the empty one is a
  permanent negative, so the client stores the refusal verbatim and the empty
  object as a null payload, keeping the two distinguishable on disk.
- `steamcmd-info-demo-4028270.json` proves the `common` block carries `name`
  **and** `type` **and** `parent` — the fields the enrichment name chain and
  `DemoConsolidation` read. Note `type` casing is not stable across appids:
  this one says `Demo`, Bastion (107100) says lower-case `game` and Monster
  Hunter Wilds (2246340) says `Game`.
