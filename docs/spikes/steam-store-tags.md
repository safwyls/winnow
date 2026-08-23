# Spike: Steam store tags — which endpoint actually returns them

Date: 2026-08-23. Resolves the `[VERIFY]` in `game-library-design.md` §4.3.

**§1–§3 were verified live** from this machine on 2026-08-23: read-only `GET`s, descriptive
`User-Agent`, ~20 requests total, no auth, no API key. **§4 (IGDB) is documentation-only** — no
Twitch credentials in this environment, so nothing IGDB is claimed as live-verified.

| Route | Verdict |
|---|---|
| `IStoreBrowseService/GetItems` | **VIABLE — recommended.** Keyless, batches 100+ appids, weighted tags |
| `IStoreService/GetTagList` | **VIABLE — required companion.** Keyless tagid → name map (446 tags) |
| `IStoreService` (other methods) | **NOT VIABLE.** No tag method; `GetAppList` is 403 without a key |
| Store page HTML (`InitAppTagModal`) | **VIABLE-WITH-CAVEATS — do not use.** 180× the bytes, scrape-shaped |
| `store/api/appdetails` | **CONFIRMED NEGATIVE.** §4.3 is correct: zero tag data |
| IGDB `genres`/`themes`/`keywords` | **VIABLE FALLBACK, materially weaker.** No weights, no ordering |

---

## 1. `IStoreBrowseService/GetItems` — VIABLE, this is the answer

No key. `input_json` is a URL-encoded JSON query param:

```
GET https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={
  "ids":[{"appid":1245620},{"appid":570},{"appid":440}],
  "context":{"language":"english","country_code":"US","steam_realm":1},
  "data_request":{"include_tag_count":20,"include_basic_info":true,"include_assets":true,
                  "include_release":true,"include_platforms":true}}
```

Live response (Elden Ring, trimmed; full single-app response was 2556 bytes). Sibling keys also
returned: `store_url_path`, `basic_info{short_description,developers,publishers}`,
`release{steam_release_date}`, `platforms{windows,steam_deck_compat_category}`, `assets`.

```json
{"response":{"store_items":[{
  "id":1245620,"appid":1245620,"success":1,"visible":true,"name":"ELDEN RING",
  "tagids":[29482,1695,4604,122,4026,4231,3859,1697,...],
  "tags":[{"tagid":29482,"weight":1077},{"tagid":1695,"weight":789},
          {"tagid":4604,"weight":753},{"tagid":122,"weight":720},...] }]}}
```

Resolved via `GetTagList` — 1245620: `Souls-like 1077, Open World 789, Dark Fantasy 753, RPG 720,
Difficult 705, Action RPG 548`. 570: `Free to Play 3009, MOBA 1019, Multiplayer 778, Strategy 723,
eSports 598`. 440: `Free to Play 2218, Hero Shooter 2142, Multiplayer 813, FPS 564, Shooter 457`.
This is exactly the §4.3 "highest-signal metadata".

Verified behaviours:

- **No API key.** Confirmed by contrast: `IStoreService/GetAppList/v1/` keyless returns
  `403 Forbidden — "Please verify your key= parameter"`; `GetItems` returns 200. Note
  `steamapi.xpaw.me` lists `key` as required for `GetItems` — **that is wrong**; xpaw's generated
  docs mark `key` required on every interface. Trust the live result.
- **Batching works.** 102 appids in one request → 102 `store_items`, 124187 bytes (~1.2 KB/app).
  A ~100k-app backfill is ~1000 requests, not ~100k. §4.3's "~35 hours" figure is an appdetails
  constraint and does not apply here.
- **Per-item failure is graceful.** Non-store appids (760, 1391110) return inside the array as
  `{"success":15,"visible":false,"name":""}` with no `tags` key; the request still 200s. Skip
  items lacking `tags`; never assume 1:1 request/response alignment.
- **Tags cap at 20** regardless of `include_tag_count` (100 returned the same 20) — Steam itself
  publishes only a top-20 list, so there is nothing to work around.
- **`weight` is not a raw vote count.** Against the store page's raw counts for the same app the
  ratio was constant at 7.032–7.037 across all 20 tags, rank order byte-identical — a per-app
  normalisation. Comparable *within* an app, not *across*. Take a headline genre from rank.
- Headers: `Cache-Control: public, max-age=120`, `X-eresult: 1`. No rate-limit headers.
- **Throttling:** 8 back-to-back batched requests (800 appids, ~3 s wall clock) all 200, no
  `Retry-After`. A *lower bound only*, not the ceiling. §4.3's warning still governs.

### Companion: `IStoreService/GetTagList` — VIABLE, keyless

`tags` gives tagids only; names come from here. One request for the whole vocabulary.

```
GET https://api.steampowered.com/IStoreService/GetTagList/v1/?input_json={"language":"english"}
→ {"response":{"version_hash":"711684454","tags":[{"tagid":9,"name":"Strategy"},
   {"tagid":122,"name":"RPG"},{"tagid":29482,"name":"Souls-like"},
   {"tagid":1091588,"name":"Roguelike Deckbuilder"},...]}}
```

446 tags, 15792 bytes. Both tags §4.3 names by example are present: **`29482 Souls-like`** and
**`1091588 Roguelike Deckbuilder`**. Cache on `version_hash`; refresh when it changes.

**Caveat covering both:** neither method appears in keyless
`ISteamWebAPIUtil/GetSupportedAPIList` (27 interfaces; `IStoreService` listed but only with
`GetGamesFollowed`, `GetGamesFollowedCount`, `GetRecommendedTagsForUser`; `IStoreBrowseService`
absent entirely). They are **undocumented store-frontend endpoints** — publicly callable today,
under no stability promise. Treat a shape change as expected: fail soft to IGDB, never error the
enrichment pass.

## 2. Store page HTML — works, do not use it

`GET https://store.steampowered.com/app/1245620/` (200, 217313 bytes) carries an inline blob:

```js
InitAppTagModal( 1245620,
  [{"tagid":29482,"name":"Souls-like","count":7574,"browseable":true},
   {"tagid":1695,"name":"Open World","count":5551,"browseable":true},...] )
```

Same 20 tags, same order, names inline, plus **raw vote counts** — the only thing GetItems lacks.
Not worth it: 217 KB vs 1.2 KB per app (~180×), one app per request vs 100+, an HTML contract that
breaks on any store redesign, plus age-gate/region redirects needing `birthtime` /
`mature_content` cookies. `robots.txt` does not disallow `/app/`, but that is not the operative
constraint — §4.3's "Valve rate-limits traffic that resembles scraping" is, and a per-app HTML
crawl is exactly that shape. Manual diagnostic only; never in the product.

## 3. `store/api/appdetails` — §4.3's negative claim CONFIRMED

Live fetch for 1245620 returned 38 top-level keys, **none containing "tag"**. Nearest substitutes
are far weaker: `genres: [Action, RPG]` (2 entries) and `categories` (storefront features, not
descriptors). appdetails stays useful for description, price, requirements, ratings — not taxonomy.

## 4. IGDB fallback — DOCUMENTATION ONLY, not live-verified

Per current IGDB v4 docs (`api-docs.igdb.com`, via Context7), `games` exposes `genres`, `themes`,
`keywords`; each of those endpoints returns only `{id, name, slug, url, checksum, created_at,
updated_at}`. **`keywords` is closer in spirit to store tags than `genres`** — free-vocabulary,
many per game, and where a concept like "souls-like" would live. Three structural weaknesses:

1. **No weight, no ordering.** An unordered set, so no defensible "primary tag".
2. **No consensus signal.** Weight 1077 means thousands of players agreed; an IGDB keyword means
   one contributor typed it. Noise is indistinguishable from signal.
3. **Uneven coverage**, contributor-driven and thin on long-tail titles — exactly Hoard's
   unplayed-backlog population. **Unquantified: measure against a real library before relying on
   it.** IGDB maintainers have publicly acknowledged the taxonomy needs a revamp.

Join path unchanged (§4.4): `external_games` / `external.steam` maps appid → IGDB id.

Also observed while probing: `ISteamApps/GetAppList/v2` now 404s
(`Method 'GetAppList' not found in interface 'ISteamApps'`), and `IStoreService/GetAppList` needs
a key. Not this spike's question, but it invalidates the common keyless full-app-list recipe.

## Recommendation

**Primary: `IStoreBrowseService/GetItems` + `IStoreService/GetTagList`.** Batch ~100 appids,
`include_tag_count: 20`, resolve names locally from a `version_hash`-cached `GetTagList` snapshot.
Store `(tagid, weight, rank)` — keep rank, since weight is only within-app comparable. Cache per
§4.3 (≥24 h; the 120 s `max-age` is a CDN hint, not our policy). Typed `HttpClient` + Polly
limiter per the charter — the no-throttle result above is an 8-request sample, not a licence.

Risks: undocumented, can change or close without notice (needs a fixture contract test and a
degrade path, not a hard dependency); keyless today, and if that changes it becomes a §4.2
endpoint with §4.2 limits; 429s unproven here, so honour `Retry-After` and back off exponentially
from the first commit; if persistently throttled, §4.3's remedy is `webapi@valvesoftware.com`.

**Fallback: IGDB `genres` + `themes` + `keywords`** when GetItems yields nothing for an appid
(non-Steam titles, delisted apps, endpoint gone). Store the two vocabularies **separately** — do
not blend — so the UI can prefer Steam tags and a taxonomy change cannot corrupt existing rows.
**Store page HTML scraping is not recommended in any form.**

## §4.3 amendments

| §4.3 says | Reality |
|---|---|
| User tags not in appdetails | **Correct**, confirmed live |
| Needs store HTML **or** `IStoreService`/`IStoreBrowseService` | It is `IStoreBrowseService/GetItems`; plain `IStoreService` has no tag method |
| — | `GetItems` is **keyless** and batches 100+ appids — the one-appid/35-hour math does not apply |
| — | Tag names need a second call, `IStoreService/GetTagList` (keyless, 446 tags) |
| — | Neither method is listed in `GetSupportedAPIList`; both are undocumented |
| IGDB genres/themes are the fallback | Add `keywords` — closest to store tags, but unweighted and unordered |
