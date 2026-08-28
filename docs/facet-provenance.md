# Facet provenance — where every filter value comes from

Read this when a filter group looks wrong. It states, per facet kind, the exact
endpoint and field path the value is read from, the transformation applied, where
it is cached, and how often it refreshes. Field paths, not prose, so a wrong
checkbox can be traced to a specific byte on disk.

Governing documents: `game-library-design.md` §4.3 (store metadata), §4.4 (IGDB),
§5.1 (enrichment must never block a user-facing path);
`docs/spikes/steam-store-tags.md` (which store endpoint is actually viable);
`src/Winnow.Data/Migrations/0007_facets.sql` (why the vocabulary is keyed on the
name and not the provider's id).

Validated end-to-end against the author's 946-release library on 2026-08-25 — see
**Validation record** at the foot.

---

## The two layers, and why a value is on one and not the other

`work_facets` holds facts about the GAME (`works`). `release_facets` holds facts
about ONE STOREFRONT LISTING (`releases`, i.e. one Steam appid). IGDB describes
the game, so IGDB descriptors land on the work; Steam user tags are voted on per
appid, so they land on the release. A reader unions the two onto the release it
is drawing a tile for (`FacetRepository.GetSnapshotAsync`).

`game_mode` is the one kind written at BOTH layers, because both providers answer
it. It is also the only kind whose vocabulary Winnow owns rather than passes
through.

## Common transformation: the slug

Every kind is keyed on `(kind, slug)` where `slug = Facet.Slugify(name)`:
lower-cased invariant, every run of non-alphanumerics folded to one `_`, ends
trimmed, diacritics preserved. `Shared/Split Screen Co-op` becomes
`shared_split_screen_co_op`.

This is the natural key, and it is the same function in the backfill, in
migration 0007's seed, and in `GameModes.FromIgdbName`. Two consequences worth
knowing before filing a bug:

* **Valve's duplicate display names collapse into one checkbox.** Ids 55/56 are
  both `DualShock Controller Support` (wired and Bluetooth), 57/58 both
  `DualSense Controller Support`, 30/51 both `Steam Workshop` (global and Steam
  China). Keying on the name is what makes them one checkbox instead of two with
  split counts. Measured on the author's library: 111 apps carry both DualShock
  ids, 112 carry both DualSense ids, 6 carry both Workshop ids — and each yields
  exactly one facet row.
* **`game_mode` is the exception and MUST carry an explicit slug.** Its six rows
  are seeded with fixed ids by migration 0007, and the display name `Co-op` folds
  to `co_op`, which is NOT the seeded key `co_operative`. Build these with
  `GameModes.Assignment(slug)`; an assignment whose slug is not one of the six is
  dropped by `FacetRepository.SetAsync` rather than minting a seventh mode.

---

## IGDB kinds — `genre`, `theme`, `player_perspective`, and half of `game_mode`

| | |
|---|---|
| Endpoint | `POST https://api.igdb.com/v4/games`, Apicalypse body as `text/plain` (§4.4) |
| Query | `Apicalypse.Games()` in `src/Winnow.Enrich.Igdb/Apicalypse.cs` |
| Auth | Twitch client-credentials; token cached ~60 days, refreshed on 401 (§4.4) |
| Rate limit | 4 req/s, shared Polly limiter on the typed client |
| Batch | 400 ids per request (`IgdbOptions.BatchSize`); 865 games = 3 requests |
| Cache | `metadata_cache` provider `igdb`, key `game:{igdbId}` |
| TTL | 30 days (`IgdbOptions.CacheTtl`) |

**Field paths** (response → `IgdbGameDto` → `IgdbGame` → `FacetSyncService.WorkFacets`):

| Facet kind | Response field | Stored as |
|---|---|---|
| `genre` | `genres[].name` | name verbatim, slugged |
| `theme` | `themes[].name` | name verbatim, slugged |
| `player_perspective` | `player_perspectives[].name` | name verbatim, slugged |
| `game_mode` | `game_modes[].name` | **normalised** via `GameModes.FromIgdbName` |

**The IGDB cache stores the PROJECTION, not the raw response.** `metadata_cache`
holds a serialised `IgdbGame` (snake_case JSON: `igdb_id`, `genres`, `themes`,
`game_modes`, `player_perspectives`, and so on), not IGDB's body. This is why the
vocabulary is keyed on names: the ids were dropped at projection time and are not
recoverable without a refetch.

It is also the single most likely cause of an empty IGDB-derived filter group.
A payload written before a field was added to `Apicalypse.Games()` simply has no
property for it; the deserializer supplies the default, and the field reads empty
forever until that cache entry expires. `IgdbGame.GameModes` and
`PlayerPerspectives` are init properties rather than positional parameters
specifically so that old payloads still deserialize and keep their genres — the
cost is that they carry no modes or perspectives. **A new field on `IgdbGame`
does not backfill; it waits out the 30-day TTL.**

`game_mode` normalisation (`GameModes.FromIgdb`, matched on the slugged name so
casing drift cannot silently drop a mode):

```
single_player | singleplayer                 -> single_player
multiplayer                                  -> multiplayer
co_operative | cooperative | co_op           -> co_operative
split_screen                                 -> split_screen
massively_multiplayer_online_mmo | mmo       -> mmo
battle_royale                                -> battle_royale
```

An IGDB mode with no entry here is **dropped, not minted** — the vocabulary is
closed by design. Measured: zero unmapped mode names across 865 live games.

---

## Steam kinds — `tag`, `feature`, `controller`, and the other half of `game_mode`

| | |
|---|---|
| Endpoint | `GET https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=…` |
| Why this one | `store/api/appdetails` carries **no tag data** — confirmed live, `docs/spikes/steam-store-tags.md` §3. `appdetails` is not used for facets at all, and must never be: one appid per request, ~200 req/5 min/IP, background backfill only (§4.3). |
| Auth | none; keyless and undocumented |
| Rate limit | 2 req/s (`SteamStoreOptions.RequestsPerSecond`), Polly limiter on the typed client |
| Batch | 100 appids per request; 946 appids = 10 requests |
| Cache | `metadata_cache` provider `steam-store`, key `app:{appid}` — **the raw store item body, verbatim** |
| TTL | 7 days (`SteamStoreOptions.CacheTtl`); §4.3's floor is 24 h |

Correlate response items by `id`, never by `appid` or position: an appid with no
store page comes back inside the array as `{"id":760,"appid":0,"success":15}`.

### `tag`

* Field: `tags[]` — `{tagid, weight}`. Names are NOT in this response.
* Name resolution: `GET IStoreService/GetTagList/v1/` gives
  `response.tags[].{tagid,name}`. Cached as `steam-store` / `taglist:english`,
  TTL 30 days, 446 entries, `version_hash` `711684454`.
* Transformation: sort by `weight` descending, ties broken by the order Steam
  returned them; **position becomes 1-based `release_facets.rank`.**
* **Rank, never weight.** The spike measured `weight` against the store page's raw
  vote counts and found a constant per-app ratio (7.032–7.037) with identical rank
  order: it is a per-app normalisation, comparable *within* an app and meaningless
  *across* apps. Elden Ring's 1077 and a small indie's 40 are not on the same
  scale. Only the order is stored; the raw weights survive verbatim in the cached
  body.
* Steam publishes at most 20 tags per app regardless of `include_tag_count`.
* A tagid absent from the vocabulary is skipped, not invented. Measured: zero
  unresolvable tagids across 946 apps.

### `feature` and `controller`

* Fields: `categories.feature_categoryids[]` and
  `categories.controller_categoryids[]` — ids only.
* Name resolution: `GET IStoreBrowseService/GetStoreCategories/v1/` gives
  `response.categories[].{categoryid, display_name, internal_name}`. Cached as
  `steam-store` / `categories:english`, TTL 30 days, 72 entries.
* `display_name` falls back to `internal_name` when it is an unresolved
  localization token (a leading `#`, e.g. `#category_playable_at_your_own_pace`).
* No `data_request` flag turns `categories` on — it arrives with the query the
  client has always sent, so every body already in `metadata_cache` carries it and
  re-reading is a local parse, not a fetch.
* **The split between the two kinds is Valve's, passed through unchanged**, and it
  is not the one a user expects. `VR Only`, `VR Support`, `VR Supported` and
  `Tracked Controller Support` are in `feature_categoryids`, not
  `controller_categoryids`. Half-Life: Alyx therefore has **no** `controller`
  facet at all. That is correct, not a gap: the `controller` group means gamepad
  support specifically (`Full controller support`, `Partial Controller Support`,
  `Gamepad Recommended`, `Steam Input API Support`, DualShock, DualSense), and
  Alyx cannot be played on a gamepad.

### `game_mode` (Steam half)

* Field: `categories.supported_player_categoryids[]`.
* Transformation: `GameModes.FromSteamPlayerCategory`. One id can mean two modes,
  so the caller unions rather than assigns.

```
 2 Single-player               -> single_player
 1 Multi-player                -> multiplayer
 9 Co-op                       -> co_operative
38 Online Co-op                -> co_operative
48 LAN Co-op                   -> co_operative
39 Shared/Split Screen Co-op   -> co_operative + split_screen
24 Shared/Split Screen         -> split_screen
37 Shared/Split Screen PvP     -> multiplayer + split_screen
27 Cross-Platform Multiplayer  -> multiplayer
36 Online PvP                  -> multiplayer
47 LAN PvP                     -> multiplayer
49 PvP                         -> multiplayer
20 MMO                         -> mmo
```

Those thirteen are **every** category `GetStoreCategories` reports with `type: 1`
— re-verified live 2026-08-25. An unknown id yields no mode; it is never guessed.
No Steam category maps to `battle_royale`, which is why that seeded row can only
ever be populated from IGDB.

---

## Refresh cadence

`FacetSyncService.SyncAsync` runs **once per app launch**, on a background task
after `EnrichmentSyncService`, never gating the window (`Program.cs`; §5.1, §7).

It is a **re-read, not a re-fetch**: both clients consult `metadata_cache` before
the network, so on a warm library the pass costs zero requests, and
`FacetRepository.SetAsync` compares before it writes, so a warm re-run reports
zero rows written. What a facet value actually tracks is therefore its cache
entry's TTL:

| Source | Effective refresh |
|---|---|
| Steam store item (tags, categories) | 7 days |
| Steam tag vocabulary | 30 days |
| Steam category vocabulary | 30 days |
| IGDB game (genres, themes, modes, perspectives) | 30 days |

Two safety properties worth not breaking:

* **Both vocabularies or neither.** If either the tag map or the category map
  comes back empty, the entire Steam half is skipped for that run. A release write
  replaces that release's whole descriptor set, so writing with half a vocabulary
  in hand would silently DELETE the other half's facets.
* **The vocabulary is insert-only.** `facets.id` is what `lists.filter_json`
  stores, so rows are never deleted — a genre that stops appearing anywhere keeps
  its row and every saved filter mentioning it keeps meaning what it meant. Only
  assignment rows are rewritten.

---

## Validation record — 2026-08-25

Every facet in the author's 946-release library was re-derived from the cached
payloads by an independent reimplementation, and separately re-fetched live from
both providers and diffed field-by-field.

**Cache → database: exact.** All 4,745 `work_facets` and all 23,354
`release_facets` rows reproduced, including every tag rank. Zero missing, zero
extra, zero rank mismatches.

**Live → database**, by kind (assignment pairs):

| Kind | Agree | Missing | Extra | Accuracy |
|---|---|---|---|---|
| `genre` | 2,544 | 0 | 0 | 100% |
| `theme` | 2,201 | 0 | 0 | 100% |
| `feature` | 4,585 | 0 | 0 | 100% |
| `controller` | 976 | 0 | 0 | 100% |
| `tag` | 16,088 | 0 | 0 | 99.99% (2 adjacent ranks swapped on 1 app — live vote churn) |
| `game_mode` | 1,705 | 1,747 | 0 | precision 100%, recall 49% |
| `player_perspective` | 0 | 972 | 0 | 0% |

The two shortfalls have one cause and it is not a transformation bug: **no cached
IGDB payload carries `game_modes` or `player_perspectives`** (0 of 865), because
every entry predates those fields being added to `Apicalypse.Games()`. Nothing
stored is wrong; the IGDB half is simply absent. Every `game_mode` in the database
today came from Steam's player categories.

Coverage, and what an IGDB cache refresh would change:

| Kind | Now | After refresh |
|---|---|---|
| `tag` | 93.6% | 93.6% |
| `game_mode` | 92.5% | 95.1% |
| `feature` | 91.8% | 91.8% |
| `genre` | 91.0% | 91.0% |
| `theme` | 88.7% | 88.7% |
| `controller` | 66.3% | 66.3% |
| `player_perspective` | **0%** | **84.8%** |

Percentages are over all 946 releases. Over the ~926 the grid actually shows
(after demo consolidation and the non-game filter) every figure is 0.2–0.3 points
higher, because the rows the grid hides are the ones least likely to carry
facets — all 3 Valve-typed tools carry none. The table is therefore a floor.

`controller` at 66% is the one group that mostly hides things, and it is honest:
a third of the library genuinely declares no gamepad support.

**To populate `player_perspective` now**, delete the `game:%` rows for provider
`igdb` from `metadata_cache` and relaunch. Cost: 3 requests (865 ids / 400 per
batch) at 4 req/s. Otherwise it fills in on its own as the 30-day TTL expires
(entries written 2026-08-24/25, so from ~2026-09-23). Live IGDB currently reports
`player_perspectives` for 802/865 games and `game_modes` for 863/865.
