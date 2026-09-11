# Facet provenance — where every filter value comes from

Read this when a filter group looks wrong. It states, per facet kind, the exact
endpoint and field path the value is read from, the transformation applied, where
it is cached, and how often it refreshes. Field paths, not prose, so a wrong
checkbox can be traced to a specific byte on disk.

This is the field-mapping reference for built-in metadata and plugin facets. Background
refresh, source separation and cache rules are stated here alongside their mappings.

Validated end-to-end against the author's 946-release library on 2026-08-25 — see
**Validation record** at the foot.

---

## The two layers, and why a value is on one and not the other

`work_facets` holds facts about the GAME (`works`). `release_facets` holds facts
about ONE STOREFRONT LISTING (`releases`, i.e. one Steam appid). IGDB describes
the game, so IGDB descriptors land on the work; Steam user tags are voted on per
appid, so they land on the release. A reader unions both layers and source-scoped plugin
assignments onto the release it is drawing a tile for (`FacetRepository.GetSnapshotAsync`).

`game_mode` is the one kind written at BOTH layers, because both providers answer
it. It is also the only kind whose vocabulary Winnow owns rather than passes
through.

## Plugin observations

Enabled metadata plugins also supply `genre` and `tag` names through `PluginMetadata`.
`PluginSyncService` stores the source response under `metadata_cache` provider `plugin:<id>`,
key `metadata:<workId>`, and writes up to 100 names per kind (each at most 100 characters).
Migration 0031's `plugin_work_facets` retains assignments per original work and plugin source.
The read snapshot unions them with built-in assignments. A provider refresh replaces only its
own assignments, so IGDB and other plugins cannot erase one another's observations. Refreshes
run after startup and when requested through plugin settings; provider caches control network TTL.

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
| Cache | `metadata_cache` provider `igdb`, key `game:{igdbId}`, game payload version 5 |
| TTL | 30 days (`IgdbOptions.CacheTtl`) |

**Field paths** (response → `IgdbGameDto` → `IgdbGame` → `FacetSyncService.WorkFacets`):

| Facet kind | Response field | Stored as |
|---|---|---|
| `genre` | `genres[].name` | name verbatim, slugged |
| `theme` | `themes[].name` | name verbatim, slugged |
| `player_perspective` | `player_perspectives[].name` | name verbatim, slugged |
| `game_mode` | `game_modes[].name` | **normalised** via `GameModes.FromIgdbName` |

**The IGDB cache stores the PROJECTION, not the raw response.** `metadata_cache`
holds a versioned envelope around a serialised `IgdbGame` (snake_case JSON:
`igdb_id`, `genres`, `themes`, `game_modes`, `player_perspectives`, and so on), not
IGDB's body. This is why the
vocabulary is keyed on names: the ids were dropped at projection time and are not
recoverable without a refetch.

`IgdbClient.GamePayloadVersion` is 5. Adding projected fields requires a version bump;
an older shape or expired entry requests a refetch on its next read instead of waiting
out a fresh TTL. A compatible older positive envelope or bare legacy game remains an
offline fallback, with its original fetch time, when credentials or a successful response
are unavailable. Future versions, mismatched game IDs and stale negative entries are not
fallback evidence. A fallback can still lack a newly added field until a refetch succeeds.

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

## Reception — ratings, review summaries and screenshots

These are **not facets**: nothing here is a filter value, and none of it reaches
`work_facets` or `release_facets`. It is recorded in this document because this
document is where "which byte on disk did this number come from" is answered, and
an unattributed score is the thing the design refuses to draw.

Storage: `work_images` and `work_ratings` (migration 0028), not `metadata_cache` —
the projection is stored the same way facets are.

### IGDB reception

| | |
|---|---|
| Endpoint | `POST https://api.igdb.com/v4/games`, Apicalypse body as `text/plain` (§4.4) |
| Query | `Apicalypse.Games()` in `src/Winnow.Enrich.Igdb/Apicalypse.cs` — the same shared query that already supplies genres and themes, so this costs no additional request |
| Auth | Twitch client-credentials; token cached ~60 days, refreshed on 401 (§4.4) |
| Rate limit | 4 req/s, shared Polly limiter on the typed client |
| Batch | 400 ids per request (`IgdbOptions.BatchSize`) |
| Cache | `metadata_cache` provider `igdb`, key `game:{igdbId}`, payload version **5**, TTL 30 days |

**Field paths** (response → `IgdbGameDto` → `IgdbGame` → `ReceptionSyncService`):

| Response field | Stored in | Key / source |
|---|---|---|
| `screenshots[].image_id` | `work_images` | kind `screenshot` |
| `artworks[].image_id` | `work_images` | kind `artwork` |
| `rating` + `rating_count` | `work_ratings` | source `igdb_users` |
| `aggregated_rating` + `aggregated_rating_count` | `work_ratings` | source `igdb_critics` |

Both scores are on a 0-100 scale.

**How the field names were established.** IGDB's own published protobuf schema,
fetched unauthenticated (no Client-ID, no Bearer token) from
`https://api.igdb.com/v4/igdbapi.proto` on 2026-09-05. `message Game` declares
`repeated Artwork artworks = 6`, `double aggregated_rating = 3`,
`int32 aggregated_rating_count = 4`, `double rating = 30`,
`int32 rating_count = 31`, `repeated Screenshot screenshots = 33`.
`message Screenshot` and `message Artwork` are the same shape and both carry
`string image_id`. No live credentialed API call was made.

**What IGDB does not provide here:** no review text, no per-review data, no
label of its own for either figure (Steam has one and IGDB does not, which is
why `work_ratings.label` is null on both IGDB rows).
`total_rating`/`total_rating_count` exist and are deliberately not used.

Payload version 5 includes image IDs, dimensions, transparency, animation and artwork
image type. Compatible older positive payloads follow the refetch-and-fallback rule above.

### Steam reception

| | |
|---|---|
| Endpoint | `GET https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=…` — the same keyless, undocumented, 100-appid-batched call that already supplies tags and categories |
| Auth | none |
| Rate limit | 2 req/s (`SteamStoreOptions.RequestsPerSecond`), Polly limiter on the typed client |
| Cache | `metadata_cache` provider `steam-store`, key `app:{appid}`, the raw store item body verbatim, TTL 7 days |

**Field paths** (response → `StoreItem` → `ReceptionSyncService`):

| Response field | Stored in | Column |
|---|---|---|
| `reviews.summary_filtered.review_count` (fallback `summary_unfiltered`) | `work_ratings` | `rating_count` |
| `reviews.summary_filtered.percent_positive` | `work_ratings` | `score` |
| `reviews.summary_filtered.review_score_label` | `work_ratings` | `label` |

Source token: `steam`.

`review_score_label` is Steam's own words ("Very Positive"), stored verbatim
rather than re-derived from the percentage — the design shows Steam's own label
with the percentage and count.

`reviews` requires `include_reviews: true` in `data_request`. Cached bodies without that
block gain review data when refreshed through the normal 7-day TTL.

The pinned fixture `tests/fixtures/steam-store/getitems-v1.json` was captured anonymously
on 2026-09-06 with that flag. Its three successful items carry filtered and language-specific
summaries; the failed item has no reviews. `SteamStoreContractTests` verifies that the
production reader returns the captured count, percentage, enum and label. The fixture README
contains the capture command. These tests use saved responses and make no live requests.

**What Steam does not provide here:** no numeric score out of 100 (only a
percent positive and a 1-9 `review_score` enum), no review text, and nothing at
all for an appid with no store page. A second, unrelated Steam source carries a
review percentage — `common.review_percentage` and `common.review_score` from
the steamcmd.net PICS mirror, visible in
`tests/fixtures/update-signals/steamcmd-info-413150.json` — but it carries
**no count and no label**, so it cannot answer the question this feature asks
and is not used.

---

## Refresh cadence

`FacetSyncService.SyncAsync` and `ReceptionSyncService` run in the background
`LibraryRefreshPipeline` after metadata enrichment. Startup, scheduled ownership refresh
and successful account changes share this pipeline; IGDB credential refresh also invokes
its IGDB-relevant steps. The window does not wait for these operations. Reception writes
`work_images` and `work_ratings` from the same caches used by facets.

Both clients consult `metadata_cache` before the network. A complete, compatible, fresh
cache can answer without requests, and unchanged projections do not rewrite assignment
rows. Expiry or incompatible shape requests a refetch. The ordinary freshness intervals are:

| Source | Effective refresh |
|---|---|
| Steam store item (tags, categories, reviews) | 7 days |
| Steam tag vocabulary | 30 days |
| Steam category vocabulary | 30 days |
| IGDB game (genres, themes, modes, perspectives, ratings, images) | 30 days |

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

In the 2026-08-25 database copy, **no cached IGDB payload carried `game_modes` or
`player_perspectives`** (0 of 865): every entry predated those query fields. The
IGDB half was absent, and every stored `game_mode` came from Steam's player categories.
This describes that measurement, not current cache coverage.

Coverage, and what an IGDB cache refresh would change:

| Kind | Measured 2026-08-25 | Projected after refresh |
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

The live response in that measurement reported `player_perspectives` for 802/865
games and `game_modes` for 863/865. Current installations use the versioned refetch
rule above; manual cache deletion is not required to adopt newly projected fields.
