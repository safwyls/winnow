# Steam Web API fixtures

## Provenance

**`getownedgames-v1.json` is a sanitized live capture.** Captured 2026-08-24
from a live `IPlayerService/GetOwnedGames/v1/` call against the developer's own
account, with a real user-supplied API key, and SANITIZED: the account's 841
entries were trimmed to the 7 below, `game_count` adjusted to match, and the
icon hash of one entry replaced (it was the only one that would have been a
stable cross-reference to a private profile). Field names, key order, encodings
and per-entry quirks are otherwise verbatim from the wire.

**The four M5 fixtures are NOT captures.** `clientgetlastplayedtimes-v1.json`,
`getuseryearinreview-2024-v1.json`, `getuseryearinreview-2025-v1.json`, and
`getuseryearinreview-protomonths-v1.json` are constructed to the envelope
verified live on 2026-08-28
(`response.stats.{account_id, year, playtime_stats.{total_stats, games[]}}` and
the per-game field list from `ClientGetLastPlayedTimes`), with figures chosen so
the reconstruction arithmetic is checkable by hand. Field names and nesting
follow the verified envelope and the proto in
`docs/spikes/steam-gdpr-export.md`; the VALUES are invented. Anyone recapturing
must re-derive the expected numbers in `PlaytimeSeriesReconstructionTests` and
`SteamPlaytimeBackfillTests`.

**The key itself is not in these files and never can be** — it travels in the
query string, which is not part of a response. See
`src/Winnow.Enrich.SteamWeb/Http/SteamWebRedaction.cs` for how it is kept out of
logs.

The fake account id is `11111111` (steam3), SteamID64 `76561197971376839`.
`ForeignYearInReview` in `tests/Winnow.Tests/SteamWeb/SteamWebFixtures.cs` uses
`22222222` to exercise the account-mismatch guard.

| File | Endpoint | Request | Provenance |
|---|---|---|---|
| `getownedgames-v1.json` | `IPlayerService/GetOwnedGames/v1/` | `steamid=<own>`, `include_appinfo=1`, `include_played_free_games=1`, `skip_unvetted_apps=false`, `format=json` | Live capture, sanitized |
| `clientgetlastplayedtimes-v1.json` | `IPlayerService/ClientGetLastPlayedTimes/v1/` | `format=json`, `key` only, **no `steamid`** | Constructed |
| `getuseryearinreview-2024-v1.json` | `ISaleFeatureService/GetUserYearInReview/v1/` | `steamid`, `year=2024`, `format=json`, `key` | Constructed |
| `getuseryearinreview-2025-v1.json` | `ISaleFeatureService/GetUserYearInReview/v1/` | `steamid`, `year=2025`, `format=json`, `key` | Constructed |
| `getuseryearinreview-protomonths-v1.json` | `ISaleFeatureService/GetUserYearInReview/v1/` | (same as above, `year=2023`) | Constructed |

The five `ClientGetLastPlayedTimes` entries use appids 10, 20, 804270, 933480,
1203620, the same appids as the owned-games capture, so the two fixtures
cross-reference. The protomonths fixture uses the alternative month placement
the spike's proto describes (months at the `playtime_stats` level with a
per-month `appid[]` array) rather than the per-game months observed live.

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
| `first_playtime: 0` on three of five `ClientGetLastPlayedTimes` entries | Zero is "not tracked", so the parser maps it to null, not to 1970-01-01. The same sentinel convention as `rtime_last_played`. |
| `rtime_first_played: 0` on appid 933480 in the 2024 year fixture | Same sentinel on the Year in Review endpoint. |
| `rtime_month` carries **epoch seconds** (e.g. 1704067200 = 2024-01-01), not a 1-12 index | Reading it as an index would put January's play in a month that does not exist. |
| appid 444440000 has an empty `months` array | A game listed for the year with no monthly breakdown is ordinary, not a parse failure. |
| appid 555550000 has months but no ownership anywhere in the test library | The "Steam reports an appid Winnow has no row for" path, which is counted and skipped rather than resolved. |
| appid 933480 per-platform splits in `ClientGetLastPlayedTimes` do not sum to `playtime_forever` (12 + 0 + 6 vs 100) | Same trap as the owned-games capture. `playtime_forever` is the only figure to trust. |

## Worked reconstruction

The figures in the fixtures are chosen so the backward walk can be verified by
hand. Appid 1203620:

- Months: Jan 2024 = 6000s, Feb 2024 = 12000s, May 2024 = 3000s, Mar 2025 = 9000s
- Anchor: `playtime_forever` = 817 minutes (from `clientgetlastplayedtimes-v1.json`)
- Walk (minutes, backwards from anchor): 817 (anchor) -> 667 (after Mar 2025) -> 617 (after May 2024) -> 417 (after Feb 2024)
- Pre-coverage floor: 317 minutes at 2023-12-31T23:59:59Z

## Recapture

Set `Steam__ApiKey` first, and **never paste the key into a file or a commit**
(Git Bash):

```
curl -sS -A "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)" \
  -o getownedgames-v1.json \
  "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?steamid=$STEAMID64&include_appinfo=1&include_played_free_games=1&skip_unvetted_apps=false&format=json&key=$Steam__ApiKey"
```

```
curl -sS -A "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)" \
  -o clientgetlastplayedtimes-v1.json \
  "https://api.steampowered.com/IPlayerService/ClientGetLastPlayedTimes/v1/?format=json&key=$Steam__ApiKey"
```

```
curl -sS -A "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)" \
  -o getuseryearinreview-2024-v1.json \
  "https://api.steampowered.com/ISaleFeatureService/GetUserYearInReview/v1/?steamid=$STEAMID64&year=2024&format=json&key=$Steam__ApiKey"
```

Replace `year=2024` with each year from 2022 onward to capture additional years.

`$STEAMID64` is `76561197960265728` + the `userdata/<steam3id>` folder name; see
`src/Winnow.Enrich.SteamWeb/SteamId.cs`. Then re-sanitize before committing.
Note that `ClientGetLastPlayedTimes` does not take a `steamid` parameter; the
key identifies the account.

**If recapturing the M5 fixtures**, the expected numbers in
`PlaytimeSeriesReconstructionTests` and `SteamPlaytimeBackfillTests` must be
re-derived from the new figures. The current fixtures are constructed, not
captured, so a recapture replaces invented values with real ones and every
assertion anchored to the old values will break.
