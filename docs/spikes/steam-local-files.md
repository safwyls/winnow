# Spike: Steam local file formats — empirical verification

Date: 2026-08-23
Verified against: live Steam install at `C:\Program Files (x86)\Steam`, Windows 11,
two accounts present under `userdata\` (steam3 ids sanitized in fixtures).
Resolves the [VERIFY] items in `game-library-design.md` §4.1.

Fixtures captured from these real files (sanitized) live in `tests/fixtures/steam/`.
Production readers should be coded from this document and tested against those fixtures.

---

## 1. `steamapps/libraryfolders.vdf` — CONFIRMED

**Format:** text VDF (KeyValues1). Root key `"libraryfolders"`.

Structure:

```
"libraryfolders"
{
    "0"                     // library index, "0", "1", ... as quoted strings
    {
        "path"        "C:\\Program Files (x86)\\Steam"   // backslashes escaped
        "label"       ""
        "contentid"   "7590589792512823842"
        "totalsize"   "0"                // "0" observed for the primary (OS-drive) library
        "update_clean_bytes_tally"  "..."
        "time_last_update_verified" "..."   // epoch seconds
        "apps"
        {
            "228980"   "157818239"       // appid -> size on disk in bytes (string)
            ...
        }
    }
}
```

Notes for the reader implementation:

- The per-library `apps` map is the authoritative "which appids live in this root" list.
  The value is size-on-disk in bytes, and can be `"0"` (observed for an app pending
  install/update), so do not treat `"0"` as "not installed" — the appmanifest is the
  authority on install state.
- `totalsize` is `"0"` for the primary library; do not rely on it.
- Each library root's manifests are at `<path>\steamapps\appmanifest_<appid>.acf`.

## 2. `steamapps/appmanifest_<appid>.acf` — CONFIRMED, one casing surprise

**Format:** text VDF. Root key `"AppState"`.

Confirmed fields (exact on-disk casing, verified across 13 manifests):

| Field | Casing on disk | Value semantics |
|---|---|---|
| `appid` | lowercase | string appid |
| `name` | lowercase | display name at install time (may go stale vs store renames) |
| `installdir` | lowercase | folder name under `steamapps\common\` (NOT a full path) |
| `StateFlags` | PascalCase | bitfield; `"4"` = fully installed. All 13 local manifests were `"4"`; other values (update pending etc.) **not** locally verified |
| `lastupdated` | **all lowercase** — spec draft said `LastUpdated`; that casing is wrong on disk | epoch seconds of last content update |
| `buildid` | lowercase | current installed build id |
| `LastPlayed` | PascalCase | epoch seconds; `"0"` if never launched. Matches localconfig `LastPlayed` (±1 s) — a useful per-machine fallback |
| `SizeOnDisk` | PascalCase | bytes |
| `LastOwner` | PascalCase | **steamid64 of the account that installed/owns the install — treat as PII, sanitize in fixtures.** Also gives you steam3id: `steamid64 − 76561197960265728` |
| `TargetBuildID` | PascalCase | build the client wants to be on |
| `InstalledDepots` | map depotid → `{ manifest, size }` | |
| `SharedDepots` | map depotid → owning appid (e.g. Steamworks redist 228980) | |
| `UserConfig` / `MountedConfig` | contain `language` | |

**Rule: parse VDF keys case-insensitively.** Casing is inconsistent within a single file
(`appid` vs `StateFlags` vs `lastupdated`), and Valve's own KeyValues is case-insensitive.
ValveKeyValue handles this; never string-match keys case-sensitively.

## 3. `userdata/<steam3id>/config/localconfig.vdf` — THE §4.1 [VERIFY] ANSWER

**Format:** text VDF on this install (first bytes are ASCII `"UserLocalConfigStore"`,
verified by hex dump). Root key `"UserLocalConfigStore"`. Still parse with ValveKeyValue
per §9 — binary KeyValues exist elsewhere in the config tree, and this file embeds hex
blobs as string values.

**Exact key path for playtime / last-played:**

```
UserLocalConfigStore / Software / Valve / Steam / apps / <appid>
```

Per-app keys (exact casing, confirmed on both accounts):

| Key | Casing | Units | Notes |
|---|---|---|---|
| `Playtime` | PascalCase | **minutes**, total | |
| `LastPlayed` | PascalCase | **Unix epoch seconds** | matches appmanifest `LastPlayed` ±1 s |
| `Playtime2wks` | exactly `Playtime2wks` | **minutes**, rolling two weeks | NOT `playtime2wks`, NOT `playtime_two_weeks` (the spec's candidate name). Present only on recently played apps |

**Units cross-check (appid 2686630, played 2026-08-21):**
`autocloud.lastexit − autocloud.lastlaunch = 1787336129 − 1787334036 = 2093 s ≈ 34.9 min`;
`Playtime2wks = "34"` → minutes confirmed. Total `Playtime = "244"` (~4 h) consistent with
`GetOwnedGames` `playtime_forever` semantics. `LastPlayed = "1787336130"` vs appmanifest
`"1787336129"` → epoch seconds confirmed.

**Traps and quirks (all observed, code for them):**

1. **`LastPlayed` sentinel `"86400"`** (= 1970-01-02) appears on many old entries — games
   last played before Steam tracked timestamps. Treat any value below a sanity floor
   (e.g. < 315532800, year 1980) as "unknown", not as a real date.
2. **Key order inside an app block is not stable.** One account has
   `LastPlayed, Playtime`; the other has `LastPlayed, BadgeData, Playtime2wks, Playtime`.
   Never parse positionally.
3. **App blocks may contain NO playtime keys at all** — e.g. entries holding only a
   `cloud { last_sync_state }` or `cloud { quota_bytes ... }` block (appids 7, 760, tool
   appids). Skip blocks lacking `Playtime` rather than emitting zeros.
4. **Extra per-app keys** you will encounter and should ignore for v1: `cloud {...}`,
   `autocloud { lastlaunch, lastexit }` (epoch seconds — could later cross-check session
   detection), `BadgeData` (hex), `<appid>_eula_0` (EULA version marker).
5. **False-match hazard:** `UserLocalConfigStore/apptickets` is ALSO a map keyed by appid
   (values are long hex blobs), and `UserAppConfig` / `depots` blocks exist too. Navigate
   the full `Software/Valve/Steam/apps` path; never grab the first appid-keyed node.
6. **Multiple accounts:** `userdata\` had two steam3id folders on this machine. Enumerate
   all of them; each has its own `localconfig.vdf` with the same structure. Attribute
   playtime per account (`CandidateOwnership` should carry the steam3id). To pick the
   "current" account, `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` or
   `config\loginusers.vdf` (`MostRecent`) are options — not verified in this spike.
7. Playtime is **local-config, not cloud-truth**: minutes played on another PC appear only
   after the client syncs. Per §4.1, treat reads as eventually consistent.

## 4. `userdata/<steam3id>/config/cloudstorage/cloud-storage-namespace-1.json` — CONFIRMED (2025 path is live)

Exists on both accounts. Sibling files: `cloud-storage-namespaces.json`
(`[[3,"0"],[1,"1134"]]` — array of `[namespaceId, version]` pairs; namespace 1 is the
collections/library namespace), `cloud-storage-namespace-1.modified.json` (`[]` when no
pending local mutations), and a namespace-3 pair (empty here).

**Format:** single-line JSON. Top level is an **array of `[key, entry]` pairs** — NOT an
object map. 159 entries on the main account.

Entry object shape:

```json
["user-collections.uc-63zK9reLRAOU", {
  "key": "user-collections.uc-63zK9reLRAOU",
  "timestamp": 1695618618,          // epoch seconds
  "value": "{\"id\":\"uc-...\",...}",  // JSON *encoded as a string* — double-parse
  "version": "322",                  // numeric string, monotonic per namespace
  "conflictResolutionMethod": "last-write"   // optional, newer entries only
}]
```

**Tombstones:** deleted entries persist as
`{ "key": ..., "timestamp": ..., "is_deleted": true, "version": ... }` — **no `value`
field at all.** Both a deleted collection (`user-collections.uc-…`) and deleted
`showcases.*` entries were observed. Skip entries with `is_deleted: true`.

**Collections are the entries whose key starts with `user-collections.`.** Observed id
namespaces:

- `user-collections.favorite`, `user-collections.hidden` — system collections.
- `user-collections.uc-<12-char id>` — user-created. **Id alphabet includes `+`, `/` and
  `*`** (e.g. `uc-+GVfSJIcS8KE`, `uc-nZ0/GknCnlrC`, `uc-P0zQoYunq*+s7`) — do not assume
  URL-safe or filename-safe ids.
- `user-collections.srm-<base64>` — created by third-party tools (Steam ROM Manager);
  suffix is base64 of the collection name.
- `user-collections.partner-ea-access` — partner collection whose value has **only
  `{id, name}` — no `added`/`removed` keys.** Parser must tolerate missing fields.

Decoded `value` shape for a collection:

```json
{
  "id": "uc-Mnd2PWUey2Y3",
  "name": "backlog",
  "added":   [377160, 22370, ...],   // appids explicitly added
  "removed": [],                      // appids explicitly removed (from a dynamic base)
  "filterSpec": { ... }               // ONLY on dynamic collections
}
```

- **Static collections:** no `filterSpec`; membership = `added`.
- **Dynamic collections:** have `filterSpec` (`nFormatVersion: 2`, `strSearchText`,
  `filterGroups: [{rgOptions: [ints], bAcceptUnion}]`, `setSuggestions`) and typically
  `added: []` — membership is computed client-side from the filter. **v1 should ingest
  static membership (`added` minus `removed`) and record but not evaluate `filterSpec`.**
- Other key families in the namespace (ignore): `NewContentRollup_<appid>` (129 entries),
  `showcases.*`, `showcases-version`, `sc-version`, `whatsnew`, `GameReleased`,
  `collection-bootstrap-complete`.

## Summary of deviations from §4.1 draft

| Spec said | Reality |
|---|---|
| appmanifest field `LastUpdated` (implied casing) | on disk it is `lastupdated`; parse keys case-insensitively |
| localconfig candidate `playtime_two_weeks` | actual key is `Playtime2wks` |
| playtime units unstated | `Playtime`/`Playtime2wks` are **minutes**; `LastPlayed` is **epoch seconds** |
| — | `LastPlayed` sentinel `86400` = unknown |
| — | collections file is an array of `[key, entry]` pairs with string-encoded JSON values and `is_deleted` tombstones |
| — | collection values may lack `added`/`removed` (`partner-*`), and `uc-` ids contain `+ / *` |

§4.1's core claims all held: paths are correct, the 2025 collections path is live,
`sharedconfig.vdf`/htmlcache were not needed, and localconfig is where playtime lives.
