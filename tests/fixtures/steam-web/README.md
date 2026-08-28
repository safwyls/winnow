# Steam Web API fixtures

Captured 2026-08-24 from a live `IPlayerService/GetOwnedGames/v1/` call against
the developer's own account, with a real user-supplied API key, and SANITIZED:
the account's 841 entries were trimmed to the 7 below, `game_count` adjusted to
match, and the icon hash of one entry replaced (it was the only one that would
have been a stable cross-reference to a private profile). Field names, key
order, encodings and per-entry quirks are otherwise verbatim from the wire.

**The key itself is not in these files and never can be** — it travels in the
query string, which is not part of a response. See
`src/Winnow.Enrich.SteamWeb/Http/SteamWebRedaction.cs` for how it is kept out of
logs.

| File | Endpoint | Request |
|---|---|---|
| `getownedgames-v1.json` | `IPlayerService/GetOwnedGames/v1/` | `steamid=<own>`, `include_appinfo=1`, `include_played_free_games=1`, `skip_unvetted_apps=false`, `format=json` |

## What was verified live, and why it is pinned

`SteamWebContractTests` asserts the exact shape the parser depends on against
these bytes. **The test is the early-warning system**: when someone recaptures a
fixture and the assertions break, Valve changed the contract, and the client's
soft-fail path — which already degrades to "unanswered" rather than throwing —
has started silently returning nothing.

### §4.2's `skip_unvetted_apps` trap is real, and was measured

The same account was queried twice on 2026-08-24, identically except for the
flag:

| Request | `game_count` |
|---|---|
| with `skip_unvetted_apps=false` | **841** |
| with the parameter omitted | **834** |

Seven owned titles vanish with no error, no warning, and nothing in the response
to indicate an omission — including both Enderal releases, which are in this
fixture for that reason. §4.2 says apps flagged "Profile Features Limited" are
silently omitted without the flag; this is that, quantified.

### `rtime_last_played` is populated

§4.2: returned **only when the API key belongs to the queried account**. It does
here, and 508 of the 841 entries carried a non-zero timestamp. With a third
party's key the field would be absent, which is why §4.1 makes local files the
primary playtime source rather than this endpoint.

## Quirks deliberately preserved

| In the fixture | Why it matters |
|---|---|
| `rtime_last_played: 0` is **present**, not omitted, on never-played games | Zero is the sentinel for "never", so the parser maps `0` to null rather than to 1970-01-01. |
| `playtime_2weeks` is **absent** on all but one entry | Steam omits it entirely when it is zero, so absent and zero are indistinguishable; both must read as 0. |
| `has_community_visible_stats` appears on some entries and not others | Optional fields are the norm here; nothing may be required beyond `appid`. |
| `content_descriptorids` appears on some entries and not others | Same. |
| appid 933480: `playtime_forever` is **100** while the per-platform splits are 12 + 0 + 6 + 0 = **18** | The splits do **not** sum to the total. Anything deriving a total from them would be wrong; `playtime_forever` is the only figure to trust. |
| appid 804270 carries an **empty** `img_icon_url` | A present-but-blank string, not an absent field. |
| appid 20 / 30 have `playtime_forever: 0` and are still returned | This is the entire population `localconfig.vdf` cannot see — it records only games with playtime. It is why this endpoint is an ingest source and not just a name fallback. |

## Recapture

Set `Steam__ApiKey` first, and **never paste the key into a file or a commit**
(Git Bash):

```
curl -sS -A "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)" \
  -o getownedgames-v1.json \
  "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?steamid=$STEAMID64&include_appinfo=1&include_played_free_games=1&skip_unvetted_apps=false&format=json&key=$Steam__ApiKey"
```

`$STEAMID64` is `76561197960265728` + the `userdata/<steam3id>` folder name; see
`src/Winnow.Enrich.SteamWeb/SteamId.cs`. Then re-sanitize before committing.
