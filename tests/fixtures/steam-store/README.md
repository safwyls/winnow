# Steam store-frontend fixtures

Captured 2026-08-23 by read-only `GET` with a descriptive `User-Agent`, no auth
and no API key, and stored **verbatim** — no trimming, no sanitizing. There is
nothing to sanitize: these are public storefront responses containing no account
data. See `docs/spikes/steam-store-tags.md` for the findings they encode.

| File | Endpoint | Request |
|---|---|---|
| `getitems-v1.json` | `IStoreBrowseService/GetItems/v1/` | appids `1245620, 570, 440, 760`; `include_tag_count: 20` plus basic info / assets / release / platforms |
| `gettaglist-v1.json` | `IStoreService/GetTagList/v1/` | `{"language":"english"}` |
| `getstorecategories-v1.json` | `IStoreBrowseService/GetStoreCategories/v1/` | `{"language":"english"}` (captured 2026-08-25) |
| `getitems-related-v1.json` | `IStoreBrowseService/GetItems/v1/` | 13 appids covering `type` and `related_items`; no extra `data_request` flags (captured 2026-09-02) |

## Why these are pinned

Neither endpoint appears in `ISteamWebAPIUtil/GetSupportedAPIList`. They are
undocumented store-frontend endpoints under no stability promise, so
`SteamStoreContractTests` asserts the exact shape Winnow's parser depends on
against these bytes. **The test is the early-warning system**: when someone
recaptures a fixture and the assertions break, Valve changed the contract, and
the client's soft-fail path — which already degrades to "no data" rather than
throwing — has started silently returning nothing.

Recapture with (Git Bash):

```
curl -sS -G -A "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)" \
  --data-urlencode 'input_json={"ids":[{"appid":1245620},{"appid":570},{"appid":440},{"appid":760}],"context":{"language":"english","country_code":"US","steam_realm":1},"data_request":{"include_tag_count":20,"include_basic_info":true,"include_assets":true,"include_release":true,"include_platforms":true}}' \
  -o getitems-v1.json "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/"

curl -sS -G -A "Winnow/0.1 (+https://github.com/winnow-app; local game library manager)"   --data-urlencode 'input_json={"language":"english"}'   -o getstorecategories-v1.json "https://api.steampowered.com/IStoreBrowseService/GetStoreCategories/v1/"
```

## Quirks deliberately preserved

| In the fixture | Why it matters |
|---|---|
| appid `760` (Steam Screenshots) comes back `"success": 15, "visible": false, "name": ""` inside a **200** response | Per-item failure is graceful; the batch still succeeds. This is a genuine miss and may be cached as one. |
| that same item carries **`"appid": 0`** while `"id": 760` | The correlation key is `id`, not `appid`. Correlating on `appid` would silently attribute the miss to app 0. The spike did not record this. |
| 4 items requested, 4 returned, in request order | Convenient but *not* guaranteed — the spike warns never to assume 1:1 alignment, so the parser keys on `id` regardless. |
| exactly 20 `tags` per successful app, descending `weight`, with a parallel `tagids` array | Steam publishes a top-20 list; `include_tag_count: 100` returns the same 20. |
| `weight` values (1077, 789, …) | Per-app normalised — comparable within an app, not across. Winnow stores **rank**; the weights survive only here and in `metadata_cache`. |
| `best_purchase_option.final_price_in_cents` is the **string** `"5999"` while `weight` is a number | Steam mixes numeric encodings within one object; the parser reads numbers from strings everywhere. |
| every successful item carries a **`categories`** object — and `getitems-v1.json` was captured 2026-08-23, two days before anything read it | Proof that `supported_player_categoryids` / `feature_categoryids` / `controller_categoryids` need **no extra `data_request` flag**. Migration 0007's facets are therefore a re-parse of bodies already in `metadata_cache`, not a backfill. |
| Dota 2 has no `controller_categoryids`; appid `760` has no `categories` key at all | A partial or absent block is ordinary. The parser reads a missing list as empty, never as an error. |
| `getstorecategories-v1.json`: ids 55 and 56 share the display name `DualShock Controller Support`, as do 57/58 and 30/51 | Valve ships duplicate display names. Migration 0007 keys facets on the NAME, which collapses them into one checkbox — the answer a filter panel wants. |
| three categories (80, 81, 82) have `display_name` values like `#category_playable_at_your_own_pace` | Unresolved localization tokens on Valve's side. The client falls back to `internal_name`, which reads correctly. |
| tagid `29482` → `Souls-like` and `1091588` → `Roguelike Deckbuilder` in the tag list | The two tags §4.3 names by example; their presence is what makes the vocabulary useful. |
| `version_hash` `711684454`, 446 tags, 15792 bytes | Byte-identical to the spike's capture. `version_hash` is how a caller detects the vocabulary moving. |

## getitems-related-v1.json

Thirteen store items lifted verbatim from the author's own `metadata_cache` on
2026-09-02. Real bytes, not a hand-written approximation, because the whole
claim of the Steam half is that these fields are already on disk; a fixture
invented from the proto would prove nothing about that.

The fixture covers an upward `parent_appid` pointer on each of the five types
that carry one, the downward `demos`, `standalone_demos` and `playtests` arrays,
both encodings of the demo pointer, and the self-referential parent.

| Appid | Title | Type | Notes |
|---|---|---|---|
| 65900 | Civ V Demo | 1 (demo) | parent 8930 |
| 8930 | Civ V | 0 (game) | names its demo |
| 1875460 | Midnight Ghost Hunt Playtest | 12 (beta/playtest) | parent 915810 |
| 915810 | Midnight Ghost Hunt | 0 (game) | |
| 100 | Condition Zero Deleted Scenes | 14 (retired) | parent 80 |
| 34450 | Civ IV: Warlords (retail-era) | 14 (retired) | parent 3990 |
| 224580 | Arma II: DayZ Mod | 2 (mod) | parent 33930 |
| 400430 | Vanishing of Ethan Carter Redux | 4 (DLC) | parent 258520 |
| 3900 | Civilization IV | 0 (game) | names ITSELF as parent |
| 42910 | Magicka | 0 (game) | names its demo |
| 73050 | Magicka Demo | 1 (demo) | |
| 3107230 | Pantheon: Rise of the Fallen | 0 (game) | carries a `playtests` array |
| 418370 | Resident Evil 7 Biohazard | 0 (game) | demo under both `demos` and `standalone_demos`, both encodings |

These are public store bodies and carry no account identifiers, so no
sanitization was required.
