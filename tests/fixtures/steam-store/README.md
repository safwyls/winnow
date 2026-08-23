# Steam store-frontend fixtures

Captured 2026-08-23 by read-only `GET` with a descriptive `User-Agent`, no auth
and no API key, and stored **verbatim** — no trimming, no sanitizing. There is
nothing to sanitize: these are public storefront responses containing no account
data. See `docs/spikes/steam-store-tags.md` for the findings they encode.

| File | Endpoint | Request |
|---|---|---|
| `getitems-v1.json` | `IStoreBrowseService/GetItems/v1/` | appids `1245620, 570, 440, 760`; `include_tag_count: 20` plus basic info / assets / release / platforms |
| `gettaglist-v1.json` | `IStoreService/GetTagList/v1/` | `{"language":"english"}` |

## Why these are pinned

Neither endpoint appears in `ISteamWebAPIUtil/GetSupportedAPIList`. They are
undocumented store-frontend endpoints under no stability promise, so
`SteamStoreContractTests` asserts the exact shape Hoard's parser depends on
against these bytes. **The test is the early-warning system**: when someone
recaptures a fixture and the assertions break, Valve changed the contract, and
the client's soft-fail path — which already degrades to "no data" rather than
throwing — has started silently returning nothing.

Recapture with (Git Bash):

```
curl -sS -G -A "Hoard/0.1 (+https://github.com/hoard-app; local game library manager)" \
  --data-urlencode 'input_json={"ids":[{"appid":1245620},{"appid":570},{"appid":440},{"appid":760}],"context":{"language":"english","country_code":"US","steam_realm":1},"data_request":{"include_tag_count":20,"include_basic_info":true,"include_assets":true,"include_release":true,"include_platforms":true}}' \
  -o getitems-v1.json "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/"
```

## Quirks deliberately preserved

| In the fixture | Why it matters |
|---|---|
| appid `760` (Steam Screenshots) comes back `"success": 15, "visible": false, "name": ""` inside a **200** response | Per-item failure is graceful; the batch still succeeds. This is a genuine miss and may be cached as one. |
| that same item carries **`"appid": 0`** while `"id": 760` | The correlation key is `id`, not `appid`. Correlating on `appid` would silently attribute the miss to app 0. The spike did not record this. |
| 4 items requested, 4 returned, in request order | Convenient but *not* guaranteed — the spike warns never to assume 1:1 alignment, so the parser keys on `id` regardless. |
| exactly 20 `tags` per successful app, descending `weight`, with a parallel `tagids` array | Steam publishes a top-20 list; `include_tag_count: 100` returns the same 20. |
| `weight` values (1077, 789, …) | Per-app normalised — comparable within an app, not across. Hoard stores **rank**; the weights survive only here and in `metadata_cache`. |
| `best_purchase_option.final_price_in_cents` is the **string** `"5999"` while `weight` is a number | Steam mixes numeric encodings within one object; the parser reads numbers from strings everywhere. |
| tagid `29482` → `Souls-like` and `1091588` → `Roguelike Deckbuilder` in the tag list | The two tags §4.3 names by example; their presence is what makes the vocabulary useful. |
| `version_hash` `711684454`, 446 tags, 15792 bytes | Byte-identical to the spike's capture. `version_hash` is how a caller detects the vocabulary moving. |
