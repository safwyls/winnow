# Spike: Epic + GOG local file formats — empirical verification

Date: 2026-08-25
Verified against: live **Epic Games Launcher 20.2.4** (one game installed: Fez) and live
**GOG Galaxy 2.1.8.30** (client DB schema `user_version = 40`, one game installed: GWENT)
on Windows 11. IGDB claims verified against the live IGDB v4 API with the project's
credentials.

`game-library-design.md` §5 names an "Epic Manifest Reader" and a "GOG Galaxy Reader" and
gives **no paths or formats for either** — M4 is otherwise unspecified. This document
supplies them. Everything below marked CONFIRMED was observed on this machine; anything
inferred or reported is labelled as such.

Sanitized fixtures live in `tests/fixtures/epic/` and `tests/fixtures/gog/`. Readers should
be coded from this document and tested against those fixtures.

**Read-only, both stores.** §4.1's "v1 is read-only against all store files" applies in
full. Section 11 below documents a case where an apparently read-only SQLite open **writes** to
the store's directory — do not skip it.

---

# Part A — Epic Games Store

## 1. Which source is authoritative — CONFIRMED, and the plan's assumption is wrong

Three candidate sources exist. Only one is usable for installed games.

| Source | Verdict |
|---|---|
| `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item` | **AUTHORITATIVE for installed titles.** One JSON file per installation |
| `%PROGRAMDATA%\Epic\UnrealEngineLauncher\LauncherInstalled.dat` | **DEAD. Do not use.** Reports `{"InstallationList": []}` on this machine while Fez is installed and playable. It tracks Unreal **Engine** installs, not games. Every blog post that recommends it is wrong |
| `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Catalog\catcache.bin` | **AUTHORITATIVE for the owned library** — see section 6. Covers owned-but-not-installed |

**Locating the manifests directory:** do not hardcode the path. The launcher publishes it at
`HKCU\SOFTWARE\Epic Games\EOS` → `ModSdkMetadataDir` (REG_SZ). Observed value:

```
C:/ProgramData/Epic/EpicGamesLauncher/Data/Manifests
```

Note it uses **forward slashes**. Fall back to `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests`
if the key is absent. A sibling `Manifests\Pending\` directory exists and was empty; ignore it.

## 2. `Data\Manifests\<InstallationGuid>.item` — CONFIRMED

**Encoding: UTF-8 with NO BOM, CRLF line endings, tab-indented, `": "` key separator.**
(The BOM was expected and is **not** there — first bytes are `7B 0D 0A 09` = `{`, CR, LF, TAB.
Read with a BOM-tolerant decoder anyway; `System.Text.Json` handles both.)

Filename stem == the `InstallationGuid` field (32 uppercase hex).

61 keys on the observed manifest. The ones a reader needs, exact casing:

| Key | Type | Meaning |
|---|---|---|
| `DisplayName` | string | **the human title.** The only title field |
| `AppName` | string | Epic's per-artifact id. **A codename — `"Bluebird"` is Fez.** Never render it |
| `CatalogNamespace` | string | 32-hex, or a short word (`"fn"`, `"catnip"`, `"crab"`) |
| `CatalogItemId` | string | 32-hex catalog item id |
| `InstallLocation` | string | **absolute** path (unlike Steam's `installdir`, which is a folder name) |
| `LaunchExecutable` | string | executable **relative to `InstallLocation`** (`"FEZ.exe"`). Empty for non-launchable items |
| `InstallSize` | number | bytes, JSON **number** not string |
| `AppVersionString` | string | installed version |
| `bIsIncompleteInstall` | bool | see section 5 |
| `bIsApplication` / `bIsExecutable` | bool | both `true` for a launchable game |
| `AppCategories` | string[] | see section 4 |
| `TechnicalType` | string | the same list, comma-joined, no spaces (`"public,games,applications"`). Redundant |
| `MainGameAppName`, `MainGameCatalogNamespace`, `MainGameCatalogItemId` | string | see section 4. **Present as empty strings on a base game, not absent** |
| `MandatoryAppFolderName` | string | leaf folder name |
| `InstallationGuid` | string | matches the filename |

Ignorable: `FormatVersion` (`0`), `EoshRevision`, `ManifestLocation` / `CompleteManifestPath`
/ `PendingManifestPath` / `StagingLocation` (point into `<InstallLocation>\.egstore\`),
`ManifestHash`, `SDMetaHash`, `BaseURLs`, `ChunkDbs`, `InstallTags`, `InstallComponents`,
`OwnershipToken` (the string `"false"`, not a bool), `PreloadState`, `AllowedUriEnvVars`.

`<InstallLocation>\.egstore\<InstallationGuid>.manifest` is a **binary** Epic chunk manifest
(magic `0C C0 BE 44`). v1 has no reason to open it.

## 3. Stable identity — use the pair, not `AppName`

**`(CatalogNamespace, CatalogItemId)` is the stable identity.** Reasons, all observed:

- `AppName` is a per-artifact release id, not a product id. It is a **codename** for 58 of
  the 73 catalog entries (`Bluebird`, `Ginger`, `Cardinal`, `Emu`, `Sage`, `Wombat`) and a
  32-hex string for the other 15. Its shape is not testable.
- `CatalogNamespace` alone is not unique — namespace `fn` covers Fortnite plus three child
  items; `catnip` covers Borderlands 3 plus nine DLC entries.
- `CatalogItemId` alone was unique across all 297 catalog entries here, but Epic's own
  composite key everywhere in the launcher (logs, `GameUserSettings.ini`, download history)
  is the **triplet `namespace:catalogItemId:appName`**:
  `41f47fd0d3e248bc938a5815d6d64daa:7a70b499513441c792b541d53505e0b2:Bluebird`.

Store the namespace and catalog item id as the `CandidateOwnership` external id, and keep
`AppName` alongside — section 20 shows it is the key that unlocks a cross-store hard join.

The manifest's `(CatalogNamespace, CatalogItemId)` joins to the catalog cache (section 6) as
`(namespace, id)`, and `AppName` joins to `releaseInfo[].appId`. Verified on Fez.

## 4. Telling a game from Unreal Engine, plugins and DLC — CONFIRMED

**`AppCategories` alone is *sufficient* to reject non-games but *cannot* identify DLC.**

Full category vocabulary observed across all 297 catalog entries (`AppCategories` on a
manifest is the same list as `categories[].path` on the catalog entry):

```
public(159) applications(123) audience(114) games(73) software(50) type(20)
engines(13) engines/ue5(9) engines/ue4(3) engines/preview(1) engines/unstable(2)
assets(9) assets/showcasedemos(9) asset-format(10) asset-format/game-engine(10)
asset-format/game-engine/unreal-engine(10) type/asset(10) type/format-item(10)
projects(2) projects/completeprojects(2) addons(3) addons/durable(1)
addons/launchable(1) games/experience(1) bundles(2) application(6) appproxy(2)
subscription(4) developer(1) testing(3) hidden(2) accesscontrol(1) points(1)
```

**Rule for "is a game":** `AppCategories` contains `"games"` **and** `"applications"`.
That admits all 73 game entries and rejects:

- Unreal Engine → `engines`, `engines/ue5`
- Twinmotion and other tools → `applications`, `software` (no `games`)
- Marketplace/Fab assets → `assets`, `asset-format/…`, `type/asset`
- Cosmetic and entitlement-only add-ons → `audience`, `public` (114 of them — the largest
  single category. Excluding them matters)

**Rule for "is DLC":** `MainGameAppName` (equivalently `MainGameCatalogItemId`) is **non-empty**.
Nothing else works — verified: the Borderlands 3 DLC "Bounty of Blood" carries
`categories = [application, games, applications]`, i.e. it looks exactly like a base game by
category. Its only marker is `mainGameItem = {namespace: "catnip", id: "5cf86732…"}`.

**`dlcItemList` is a trap: it is `[]` on all 297 entries, including base games that
demonstrably have DLC.** Never use it. Resolve DLC bottom-up from the child's
`MainGameCatalogItemId`, never top-down from a parent's list.

Edge case observed: LEGO Fortnite Odyssey carries `addons/launchable`, `games/experience`
**and** `games`+`applications`, with `mainGameItem` pointing at Fortnite. The DLC rule
correctly classifies it as a child.

## 5. `bIsIncompleteInstall` — CONFIRMED semantics

`false` on the completed Fez install. The launcher log records the transition explicitly:

```
LogDownloadManager: HandleTaskComplete: AppId [41f47fd0…:7a70b499…:Bluebird]
  AlertCode=[ok] QueueLen=1 IncompleteInstall=0 AutoResume=1
```

So the flag is the launcher's own "this install finished" bit, cleared on successful task
completion. The `.item` file **exists during the download** (it is written when the install
is queued, not when it finishes), which means a manifests-only reader will otherwise report
a half-downloaded game as installed.

**Treat `bIsIncompleteInstall == true` as "not installed".** Corroborating signals on the
same manifest: `InstallSize` reads `0` and `bNeedsValidation` is `true` mid-download.
Not exhaustively verified across every install state (only one game was installed here);
if a fourth state appears, the flag is still the right gate.

## 6. `catcache.bin` — the owned library, locally, with no OAuth — CONFIRMED

`%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Catalog\catcache.bin`

**Format:** base64 of plain UTF-8 JSON. **Not gzipped, not encrypted** — `base64.b64decode`
then parse. 535 KB base64 → 401 KB JSON → a top-level **array** of 297 catalog entries here.

**This is the account's entitlement catalog, i.e. the owned library — installed or not.**
Proof from the launcher's own log, at the moment the file was created:

```
LogCatalogCache: CatalogCache: No stored cache found
LogCommunityPortalOSS: Display: ...found 50 entitled apps
LogCommunityPortalOSS: Display: ...found 69 entitled apps
LogCommunityPortalOSS: Display: ...found 73 entitled apps
LogCommunityPortalOSS: Display: ...found 74 entitled apps
```

74 entitled apps; the file lands moments later with 73 entries passing the section 4 game filter
plus Fortnite's children. Only one game is installed. **This closes the plan's
"Epic owned-but-not-installed" gap without any network call or OAuth flow** — see section 22.

Entry shape (16 keys, all present on every entry):

```json
{
  "id": "7a70b499513441c792b541d53505e0b2",      // == manifest CatalogItemId
  "namespace": "41f47fd0d3e248bc938a5815d6d64daa", // == manifest CatalogNamespace
  "entitlementName": "7a70b499513441c792b541d53505e0b2",
  "title": "Fez",                                  // == manifest DisplayName
  "developer": "Polytron Corporation, Inc",
  "description": "Fez", "longDescription": "", "technicalDetails": "",
  "eulaIds": ["egstore"],
  "lastModifiedDate": "2019-11-19T17:02:42.064Z",  // ISO 8601 UTC
  "keyImages": [ { "type": "DieselGameBoxTall", "url": "...", "width": 1200,
                   "height": 1600, "size": 2743651, "md5": "..." } ],
  "categories": [ {"name":"","path":"public"}, {"path":"games"}, {"path":"applications"} ],
  "releaseInfo": [ { "appId": "Bluebird",          // == manifest AppName
                     "platform": ["Windows","Mac","Win32"],
                     "compatibleApps": [],
                     "dateAdded": "2019-08-09T00:00:00.000Z" } ],
  "customAttributes": { "FolderName": {"type":"STRING","value":"Fez"}, ... },
  "dlcItemList": [],                               // ALWAYS empty — see section 4
  "mainGameItem": { "namespace": "", "id": "" }    // non-empty => this entry is DLC
}
```

Notes for the reader:

- `keyImages[].type` values worth having: `DieselGameBoxTall` (portrait cover, 1200×1600 or
  1280×1440), `DieselGameBox` (landscape), `DieselGameBoxLogo`. Cover art for free, no
  network call at ingest time.
- `releaseInfo` had exactly one element for every one of the 73 games; zero games had none
  and none had more than one. Don't assume that holds forever — take `[0]` defensively.
- `customAttributes` is a `{name: {type, value}}` map (`value` is always a string, even for
  booleans and numbers). `ThirdPartyManagedProvider`, `RegistryPath`, `RegistryKey`, `GameID`
  appear here for section 7's third-party titles.
- `dateAdded` on `releaseInfo[0]` is the **store release date**, not the acquisition date.
  Nothing on disk records when the user claimed a title.

**Staleness:** the log line shows the cache is rewritten on launcher start-up after login.
It is as eventually-consistent as Steam's config tree (§4.1) — same rule applies.

## 7. Epic-owned titles that install through another launcher — CONFIRMED

`%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\ThirPartyManagedApps\<ns>_<catalogId>_<appName>.json`

(Directory name is misspelled by Epic — `ThirParty`, one word short of `ThirdParty`.)
Single-line JSON, one file per title:

```json
{"AppName":"Jasper","Namespace":"ecebf45065bc4993abfe0e84c40ff18e",
 "CatalogID":"6dc445f656de4e029834b2d32b6a2f77","Provider":"UbisoftConnect",
 "RegistryPath":"SOFTWARE\\WOW6432Node\\Ubisoft\\Launcher\\Installs\\274",
 "RegistryKey":"InstallDir","MainWindowProcessName":"","ProcessNames":[],
 "AdditionalCommandArgs":"","GameID":"274","Title":"Watch Dogs"}
```

Note the key casing differs from the `.item` manifest: `CatalogID` (capital D) here vs
`CatalogItemId` there, and `Namespace` vs `CatalogNamespace`.

Two entries here: Watch Dogs and For Honor, both `Provider: "UbisoftConnect"`. **These are
owned Epic titles that never get a `.item` manifest** — an Epic reader that only walks
`Manifests\` misses them entirely. They appear in `catcache.bin` normally, so section 6 already
covers ownership; this directory is only needed to resolve their **install** state (read
the named registry key, read-only, to get the install dir). v1 can defensibly record them
as owned-not-installed and stop there.

## 8. Playtime and last-played on disk — DEFINITIVE: there is none

I grepped the entire Epic tree (`%PROGRAMDATA%\Epic\**`, `%LOCALAPPDATA%\EpicGamesLauncher\**`)
for `playtime`, `timeplayed`, `lastplayed`, `last_played`, `totalplay`, and searched the
decoded catalog cache for the same. **Zero per-game playtime records anywhere.**

Exactly one playtime-adjacent value exists on disk:

`%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\WindowsEditor\GameUserSettings.ini`

```ini
[<accountId>_Launcher]
LastPlayedGame=41f47fd0d3e248bc938a5815d6d64daa:7a70b499513441c792b541d53505e0b2:Bluebird,2026-08-25T23:11:43.942Z
```

That is a **single slot**, overwritten on every launch. It tells you the most recently
played Epic game and when — and nothing about any other title. `[Launcher] LastActiveTab`
holds the same `AppName` and is even weaker.

**Consequence for the product, stated plainly:** an Epic entry can never leave the
"Never played" bucket from local data alone, except for the single most recent title.

An OAuth-gated Epic endpoint *does* return per-game total playtime (section 21) — but it returns
**no last-played timestamp**, and Hoard's whole staleness model (§6.1) is built on recency,
not on totals. So even the network route cannot bucket an Epic game by dormancy.

**The only mechanism that gives Epic games a real last-played date in Hoard is M3's process
monitor (§5.2).** That is a good outcome: §5.2 already maps executables to releases using
Epic install locations, and the `.item` manifest hands it `InstallLocation` +
`LaunchExecutable` directly — an exact absolute path, better raw material than Steam's
`installdir`. Epic playtime accrues from first launch under Hoard, forward only, with no
history. Surface that honestly in the UI rather than showing a zero that looks like a fact.

> `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Data\*.dat` (named after the account id) and the
> `[RememberMe]` / `[Offline]` sections of `GameUserSettings.ini` are **encrypted credential
> blobs**. Do not read them, do not fixture them, do not ship them.

## 9. Epic traps, ranked

1. **`LauncherInstalled.dat` lies.** `InstallationList: []` with a game installed. section 1.
2. **`AppName` is not a title.** "Bluebird" is Fez. Rendering it ships gibberish. section 3.

3. **Epic's own `title` is lossily transliterated — do NOT "fix" it in the reader.**
   `catcache.bin` really does store `Batman? Arkham Asylum Game of the Year Edition`
   and `LEGO? Batman? 2 DC Super Heroes`: a literal U+003F where the trademark
   symbol belongs. Verified by decoding the base64 payload independently of any
   Hoard code and dumping codepoints — the `?` is in Epic's bytes, not introduced
   by `EpicCatalogReader`. Steam's catalog spells the same games correctly, so the
   two stores disagree on punctuation for titles they both carry. That is a soft-
   matcher problem (`TitleNormalizer` already strips punctuation, and the sweep
   paired Fez, Celeste, ABZU and Borderlands 2 across stores), never a decoding
   one. Anyone "correcting" `?` back to a guessed character in the reader is
   inventing data and will corrupt the genuine question marks in titles like
   `Where in the World is Carmen Sandiego?`.
3. **`dlcItemList` is always empty.** DLC is discovered from the child's `MainGame*`. section 4.
4. **`MainGame*` are empty strings on a base game, not missing keys.** Test for empty, not
   for absence.
5. **`bIsIncompleteInstall`** — the `.item` exists during the download. section 5.
6. **`ThirParty` is misspelled** in the real directory name. section 7.
7. **`InstallSize` is a JSON number, `OwnershipToken` is the *string* `"false"`.** Do not
   assume every `bXxx`-looking value is a bool.
8. **`AppCategories` `"audience"`** is the single most common category (114/297) and is all
   cosmetics and entitlement filler. Filter it out or the library fills with junk.
9. Manifests are in **`%PROGRAMDATA%`, not per-user** — one machine-wide set, shared across
   Windows accounts. Epic account attribution comes from `catcache.bin`/`GameUserSettings.ini`,
   not from the manifests.

---

# Part B — GOG

## 10. Sources — CONFIRMED

| Source | Role |
|---|---|
| `%PROGRAMDATA%\GOG.com\Galaxy\storage\galaxy-2.0.db` | **AUTHORITATIVE when Galaxy is installed.** Owned library, install state, playtime, last-played, titles, purchase dates |
| `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<gameID>` | Fallback for Galaxy-less users; installed titles only |
| `<installdir>\goggame-<gameId>.info` | Same, and the only source that distinguishes DLC without Galaxy |

Do not hardcode the storage path. `%PROGRAMDATA%\GOG.com\Galaxy\config.json` is small
plain JSON and gives it:

```json
{ "storagePath": "C:\\ProgramData\\GOG.com\\Galaxy\\storage",
  "libraryPath":  "C:\\Program Files\\GOG Galaxy\\Games",
  "installationSource": "gog", "installationPaths": [] }
```

Galaxy's presence is also confirmed by `HKLM\SOFTWARE\WOW6432Node\GOG.com\GalaxyClient`
(`clientExecutable`, `version` — observed `2.1.8.30`).

## 11. Safe-read procedure for `galaxy-2.0.db` — CONFIRMED, with a real hazard

The DB is **SQLite in WAL mode** (`pragma journal_mode` → `wal`) and Galaxy holds it open.
On this machine the main file was 11 MB and the **`-wal` was 10 MB** — nearly as large as
the database. Three strategies were measured on copies:

| Strategy | Sees latest data? | Touches the source directory? |
|---|---|---|
| copy `.db` + `-wal` + `-shm`, open the **copy** `?mode=ro` | **YES** | no |
| copy `.db` only, open `?mode=ro` | **NO — silently stale** | no |
| copy `.db` + `-wal`, open `?mode=ro&immutable=1` | **NO — silently stale** | no |

The stale results are not theoretical: diffing all 86 tables between the with-WAL and
without-WAL reads showed `ProductConfiguration` differing. **`immutable=1` produced byte-identical
results to discarding the WAL entirely — it silently ignores the write-ahead log.**

> **`immutable=1` is the intuitive choice for "don't disturb it" and it is the wrong one.**
> It returns data from an arbitrary past checkpoint with no error and no warning.

And the hazard that makes copying mandatory rather than merely tidy:

> **Opening a WAL database with `?mode=ro` CREATES `-wal` and `-shm` files next to it.**
> Measured directly: a directory containing only `galaxy-2.0.db` contained
> `galaxy-2.0.db`, `galaxy-2.0.db-wal` and `galaxy-2.0.db-shm` after a single read-only
> `SELECT`. `mode=ro` restricts writes to the *database*, not to the directory.
> Pointing it at `%PROGRAMDATA%\GOG.com\Galaxy\storage` therefore **writes into a
> store-owned directory** and violates §4.1. Copy first, always.

**Procedure:**

1. Copy `galaxy-2.0.db`, then `galaxy-2.0.db-wal`, then `galaxy-2.0.db-shm` into a Hoard-owned
   temp directory. Copy the main DB first: the WAL only grows within a checkpoint cycle, so a
   later-copied WAL is a superset of what the main file needs. Missing `-wal`/`-shm` is not an
   error (SQLite recreates them beside the copy); missing WAL **data** is a silent correctness
   bug, so copy it whenever it exists.
2. Open the **copy** with `Mode=ReadOnly` (`file:...?mode=ro`, no `immutable`). SQLite recovers
   the WAL into the copy — writes land on Hoard's file, not GOG's.
3. Run `PRAGMA quick_check`. It took **0.02 s** on the 11 MB copy. If it is not `ok`, the
   snapshot was torn by a concurrent checkpoint: delete and retry once, then give up and
   report rather than importing partial data.
4. Delete the copy when done.

Reading cannot disturb Galaxy: nothing is opened for write and nothing in the source
directory is touched. **Never open the live file, for any reason, including `mode=ro`.**

Pin `PRAGMA user_version` (observed **40**) in a log line. Galaxy migrates this schema; a
jump is your early warning that the queries below need re-verification.

> **`ProductAuthorizations` holds a per-product `clientSecret`.** Do not read it, do not log
> it, do not put it in a fixture. It is excluded from `tests/fixtures/gog/galaxy-2.0.min.db`.

## 12. The platform discriminator — THE critical finding

87 tables. The ones that matter:

```
ReleaseKeys(key)                          -- CHECK(key LIKE '_%\_%_' ESCAPE '\')
LibraryReleases(id, userId, releaseKey)   -- the user's library
LicensedReleases(libraryId, isOwned)      -- 1:1 with LibraryReleases
ReleaseProperties(releaseKey, isDlc, isVisibleInLibrary, gameId)
GamePieces(releaseKey, gamePieceTypeId, userId, value, languageId)
GamePieceTypes(id, type)
GameTimes(userId, releaseKey, minutesInGame)
LastPlayedDates(userId, gameReleaseKey, lastPlayedDate)
ProductPurchaseDates(gameReleaseKey, userId, purchaseDate, addedDate)
ProductsToReleaseKeys(externalId, gogId, releaseKey)
Products(id, name, parentId)
InstalledProducts(productId)
InstalledBaseProducts(productId, generation, languageId, installationPath,
                      installationId, buildId, branch, installationDate)
InstalledExternalProducts(id, platformId, productId)
Platforms(id, name)  PlatformConnections(userId, platform, connectionState)
PlayTasks(id, gameReleaseKey, userId, order, typeId, isPrimary)
PlayTaskLaunchParameters(playTaskId, executablePath, commandLineArgs, label)
```

**Everything is keyed by `releaseKey`, whose format is `<platform>_<externalId>`.** The
schema enforces it. **`substr(releaseKey, 1, 4) = 'gog_'` is the discriminator, and it is
the only reliable one.**

Three things make the obvious alternatives wrong:

1. **`Platforms` is a static list of all 86 integrations Galaxy *supports*, not what is
   connected.** It contains `steam`, `epic`, `uplay`, `psn`, `xboxone`, `origin`,
   `battlenet`, `humble`, `itch`, `eaApp`, `rockstar`, … and notably **not `gog` itself** —
   GOG-native releases carry the `gog_` prefix but have no `Platforms` row. Any join to
   `Platforms` to find GOG entries returns nothing.
2. **`PlatformConnections` cannot be trusted as a filter.** All 86 rows on this machine read
   `connectionState = 'Disconnected'` — and the library *still* contains a Steam release.
   Nothing prunes releases when an integration is removed.
3. **The contamination is live and proven here.** `LibraryReleases` holds
   **`steam_1091500`** (Cyberpunk 2077) with `LicensedReleases.isOwned = 1`, alongside 45
   `gog_` rows. Ingesting `LibraryReleases` unfiltered would re-import a Steam game the
   Steam ingest already owns. On a machine with a connected Steam integration that is not
   one duplicate but the entire ~926-game Steam library, duplicated. `tests/fixtures/gog/`
   keeps this row specifically so the ownership query's test can assert it is excluded.

Weak signals that are **not** discriminators, for the record: `ProductPurchaseDates.purchaseDate`
was `NULL` for the Steam row and populated for GOG rows, and `ProductsToReleaseKeys` had no
row for it. Both are consequences, not the rule. Filter on the prefix.

## 13. The ownership query — CONFIRMED against the live DB

```sql
SELECT lr.releaseKey,                                    -- 'gog_1971477531'
       json_extract(gp.value, '$.title')  AS title,
       rp.isDlc, rp.isVisibleInLibrary,
       gt.minutesInGame,                                 -- MINUTES
       lpd.lastPlayedDate,                               -- 'YYYY-MM-DD HH:MM:SS' UTC
       ppd.purchaseDate, ppd.addedDate,
       ibp.installationPath, ibp.installationDate, ibp.buildId
FROM LibraryReleases lr
JOIN LicensedReleases lic ON lic.libraryId = lr.id AND lic.isOwned = 1
LEFT JOIN ReleaseProperties rp  ON rp.releaseKey = lr.releaseKey
LEFT JOIN GamePieces gp         ON gp.releaseKey = lr.releaseKey
                               AND gp.userId = lr.userId
                               AND gp.gamePieceTypeId =
                                   (SELECT id FROM GamePieceTypes WHERE type = 'title')
LEFT JOIN GameTimes gt          ON gt.releaseKey = lr.releaseKey     AND gt.userId  = lr.userId
LEFT JOIN LastPlayedDates lpd   ON lpd.gameReleaseKey = lr.releaseKey AND lpd.userId = lr.userId
LEFT JOIN ProductPurchaseDates ppd ON ppd.gameReleaseKey = lr.releaseKey AND ppd.userId = lr.userId
LEFT JOIN ProductsToReleaseKeys ptrk ON ptrk.releaseKey = lr.releaseKey
LEFT JOIN InstalledBaseProducts ibp  ON ibp.productId = ptrk.gogId
WHERE substr(lr.releaseKey, 1, 4) = 'gog_'      -- section 12. non-negotiable
  AND COALESCE(rp.isDlc, 0) = 0                 -- section 15
ORDER BY title;
```

Returns 14 base games out of 46 library rows (45 `gog_` + 1 `steam_`; 31 of the `gog_` rows
are DLC). `Users` had exactly one row — but the schema keys everything by `userId`, so read
`Users` and iterate, exactly as §4.1 does for Steam's multiple `userdata` folders.

Field notes, all verified:

- **`GamePieces.userId` is NOT NULL for `title`.** It must be in the join or the title comes
  back empty. The split is per piece type: `title`, `originalTitle`, `sortingTitle`, `meta`,
  `media`, `myRating`, `summary` carry a `userId`; `allGameReleases`, `dlcs`, `osCompatibility`,
  `productLinks`, `storeImages`, `goodies`, `changelog` have `userId IS NULL`;
  `storeTags`, `preferredLocalization`, `storeFeatures` additionally carry a **non-null
  `languageId`**. Join defensively: `(gp.userId = ? OR gp.userId IS NULL)`.
- **`GamePieces.value` is a JSON string** in a TEXT column — `{"title":"GWENT: The Witcher Card Game"}`,
  not a bare title. It is **valid UTF-8**; the mojibake you may see when dumping it is your
  console, not the data (verified at byte level: `universe\xe2\x80\x99s`).
- `ReleaseKeys` is **not** an ownership list: it held 84 keys against 46 owned releases.
  Ownership is `LibraryReleases` ⋈ `LicensedReleases`.
- **Install state** comes from `InstalledBaseProducts`, reached via
  `ProductsToReleaseKeys.gogId`. Do not parse the id out of the releaseKey string to get
  there — `ProductsToReleaseKeys` is the sanctioned mapping and enforces
  `CHECK(externalId IS NOT NULL AND gogId IS NULL OR externalId IS NULL AND gogId IS NOT NULL)`.
  `InstalledExternalProducts` is the parallel table for non-GOG integrations; it was empty.
- `ProductStates(productId, installation, operation)` had one row, `installation = 3` for the
  installed game. One sample only — **do not encode that enum**; use `InstalledBaseProducts`.
- `Products.name` was `NULL` on every row. Not a title source. `Products.parentId` does mark
  children (`1286889002 → 1971477531`).

## 14. Playtime and last-played — CONFIRMED, both present

**GOG has real playtime locally, unlike Epic.**

| Field | Units | Notes |
|---|---|---|
| `GameTimes.minutesInGame` | **minutes**, total | schema `CHECK(minutesInGame >= 0)`. `0` on 44 of 46 rows |
| `LastPlayedDates.lastPlayedDate` | TEXT `'YYYY-MM-DD HH:MM:SS'` | **UTC** |

**Timezone proof:** GWENT's `LastPlayedDates` row is `'2017-07-01 03:32:16'`. The
`myFriendsActivity` GamePiece for the same release carries `last_played_date: 1498879936`,
which is `2017-07-01 03:32:16` **UTC** — an exact match to the second. Parse as UTC.

Independently: `InstalledBaseProducts.installationDate` = `'2026-08-26 06:17:36'` while the
registry's `INSTALLDATE` for the same install = `'2026-08-25 23:17:36'`. Seven hours apart
on a UTC−7 machine. **Galaxy's DB is UTC; the registry is local time.** Mixing them shifts
every GOG date by the user's offset.

Two traps:

- **`GameTimes` has a row per release with `minutesInGame = 0`.** A row's existence is not
  evidence of play. "Never played" = `minutesInGame = 0` **and** no `LastPlayedDates` row.
  Do not treat a missing `LastPlayedDates` row as an error.
- **Playtime survives uninstall and is not gated on install state.** The Witcher 3 shows
  50 minutes and a 2018 last-played while not installed. Never `JOIN` playtime through
  `InstalledBaseProducts`.

Scope caveat: Galaxy only accrues time for sessions it launched. A user who runs the .exe
directly gets nothing, exactly as with §4.1's local-config caveat for Steam.

## 15. DLC — CONFIRMED

`ReleaseProperties.isDlc` (INTEGER, **nullable**, no default). 31 of 45 `gog_` library rows
are DLC — mostly Witcher 3 cosmetics and quests, several with untranslated internal names
(`dlc_11_a`, `dlc_7_a`) that would look like garbage in a library view.

Use `COALESCE(rp.isDlc, 0) = 0`; the column is nullable and `ReleaseProperties` may have no
row at all for a release. `ReleaseProperties.isVisibleInLibrary` was `1` everywhere here —
respect it if it is ever `0`, but it is not the DLC filter.

Belt and braces: the base game's `dlcs` GamePiece lists its children
(`{"dlcs":["gog_1142753074", …]}`) and `Products.parentId` links installed children to their
parent. Prefer `isDlc`; the others are cross-checks.

## 16. GOG without Galaxy — CONFIRMED, and the two sources agree on ids

For users who never install Galaxy (standalone GOG installers are a first-class product),
two local sources exist and both are present even when Galaxy *is* installed.

**Registry:** `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<gameID>` — one subkey per installed
game, all values `REG_SZ`:

```
gameID / productID  "1971477531"      -- same value; the GOG product id
gameName            "GWINT: Wiedzminska Gra Karciana"   -- see the trap below
path / workingDir   "C:\Program Files\GOG Galaxy\Games\GWENT The Witcher Card Game"
exe                 "...\Gwent.exe"     exeFile "Gwent.exe"
launchCommand / launchParam / uninstallCommand / startMenu / startMenuLink
ver                 "11.10.10"
BUILDID             "59534219748634025"
INSTALLDATE         "2026-08-25 23:17:36"    -- LOCAL time (section 14)
language "english"  lang_code "en-US"  installer_language "english"  DLC ""
```

**`goggame-<gameId>.info`**, in the install directory. UTF-8, **no BOM**, **LF** line
endings, 4-space indent:

```json
{
    "buildId": "59534219748634025",
    "clientId": "<galaxy sdk client id>",  // sanitized in the fixture; pairs with a
                                       // clientSecret in ProductAuthorizations. Ignore both
    "gameId": "1971477531",
    "language": "English",
    "languages": ["en-US"],
    "name": "GWINT: Wiedźmińska Gra Karciana",
    "playTasks": [ { "category": "game", "isPrimary": true, "languages": ["en-US"],
                     "name": "...", "path": "Gwent.exe", "type": "FileTask" } ],
    "rootGameId": "1971477531",
    "version": 1
}
```

`playTasks[].path` is **relative to the install directory**. **`gameId != rootGameId` marks
a DLC** — the only DLC discriminator available without Galaxy. Siblings in the same
directory: `goggame-<id>.hashdb`, `.ico`, `goggame-galaxyFileList.ini`, `goglog.ini`;
none are needed. GOG also writes an Inno Setup uninstall entry at
`…\CurrentVersion\Uninstall\<gameId>_is1`; it is redundant with the `GOG.com\Games` key.

**Do the sources agree?** On ids, exactly: registry `gameID`/`productID`, `.info` `gameId`,
and the Galaxy releaseKey suffix are all `1971477531`; `BUILDID` == `.info` `buildId` ==
`InstalledBaseProducts.buildId` == `59534219748634025`; install paths match.
**On titles, they do not** — see the next section.

## 17. The localisation trap — CONFIRMED, and it is the reverse of what you'd guess

Every **local, install-side** source carries the **installer-locale** title:

| Source | Title |
|---|---|
| registry `gameName` | `GWINT: Wiedzminska Gra Karciana` |
| registry `startMenu` | `GWINT - Wiedzminska Gra Karciana` |
| `goggame-1971477531.info` → `name` | `GWINT: Wiedźmińska Gra Karciana` |
| `PlayTaskLaunchParameters.label` (in Galaxy's own DB) | `GWINT: Wiedźmińska Gra Karciana` |
| **`GamePieces` type `title`** | **`GWENT: The Witcher Card Game`** |
| `GamePieces` type `originalTitle` | `GWENT: The Witcher Card Game` |

The Polish title is not the user's locale — `installer_language` is `english` and
`lang_code` is `en-US`. It is what the publisher stamped into that installer build.

Two things follow:

1. **Prefer `GamePieces.title` for display.** Fall back to the local sources only when
   Galaxy is absent, and label such titles as low-confidence.
2. **Never let a local title reach the fuzzy matcher.** §5.3's non-negotiable already
   forbids auto-merging on fuzzy title similarity; a Polish title matched against an English
   IGDB corpus is exactly the input that produces a confident wrong answer. The GOG **product
   id** hard-joins (section 19), so titles never need to carry identity for GOG anyway.

Also note the registry drops diacritics (`Wiedzminska`) while the JSON keeps them
(`Wiedźmińska`) — the two "same" titles are not string-equal. And `GamePieces` type
`sortingTitle` (`GWENT The Witcher Card Game`, no colon) is GOG's sort key with
`isModifiedByUser`; use `title` for display, `sortingTitle` only for ordering.

## 18. GOG traps, ranked

1. **Platform contamination.** `LibraryReleases` holds non-GOG releases as owned. section 12.
2. **`immutable=1` silently returns pre-WAL data.** section 11.
3. **`mode=ro` on the live file writes `-wal`/`-shm` into GOG's directory.** section 11.
4. **`GamePieces.userId` must be in the title join**, or every title is empty. section 13.
5. **Local titles are installer-locale.** section 17.
6. **Galaxy DB timestamps are UTC; the registry's `INSTALLDATE` is local.** section 14.
7. **`GameTimes` rows exist with `0` minutes** — presence ≠ played. section 14.
8. **`Platforms` has no `gog` row.** Joining to it to find GOG games returns nothing. section 12.
9. **`ReleaseKeys` is not an ownership list** (84 keys vs 46 owned). section 13.
10. **`ProductAuthorizations` stores a `clientSecret`.** Never read or ship it. section 11.

---

# Part C — Identity for §5.3

## 19. IGDB: §4.4's claim is **half true** — VERIFIED against the live API

§4.4 says `external_games` maps "Steam appid / GOG id / Epic catalog id". Verified with the
project's credentials. `external_game_sources` enumerates **Steam = 1, GOG = 5,
Epic Games Store = 26** (`GET /v4/external_game_sources`), with 174,495 / 9,341 / 10,145
rows respectively.

**GOG — the claim holds. Hard join confirmed.** `external_game_source = 5`, `uid` is the
bare GOG product id as a string — byte-identical to the `gog_<id>` releaseKey suffix, the
registry `gameID`, and `goggame-<id>.info`'s `gameId`. No transformation.

| GOG id | IGDB game |
|---|---|
| `1971477531` (GWENT) | 19474 · Gwent: The Witcher Card Game |
| `1207664643` (Witcher 3 Complete) | 1942 · The Witcher 3: Wild Hunt |
| `1423049311` (Cyberpunk 2077) | 1877 · Cyberpunk 2077 |

Coverage on the user's real library: **13 of 14 owned GOG base games hard-joined in a single
query.** The one miss, `1441199941`, is "The Witcher 3 REDkit" — a modding toolkit, not a
game. Also missing: `2074191081`, GOG's GWENT *preview* SKU. Per-SKU coverage is imperfect;
per-game coverage is excellent.

**Epic — the claim is FALSE. There is no hard join on anything the launcher stores.**
Tested every local identifier for all 73 owned Epic titles against all 10,145 IGDB
Epic-source rows:

| Local field tried | Matches |
|---|---|
| `CatalogItemId` (73 values) | **0** |
| `AppName` (73 values) | **0** |
| `CatalogNamespace` (unique values) | **0** |

IGDB's Epic `uid` is **not** the catalog item id. Fez (IGDB game 1991) has two Epic rows —
`442f123b4d884d8ca85236aa30b99a79` and `78e2d1ca-9ff2-4179-95d4-f67c1acf3b76` — neither of
which is the launcher's `CatalogItemId` `7a70b499513441c792b541d53505e0b2`. Across a
1,500-row sample the uids are 79% 32-hex and 21% dashed-UUID, split cleanly by URL form
(`store.epicgames.com/en-US/p/…` vs `www.epicgames.com/store/p/…`) — two generations of
identifier, neither of them a catalog item id.

I established what they actually are, via Epic's public unauthenticated CMS endpoint
`GET https://store-content.ak.epicgames.com/api/en-US/content/products/<slug>`:

```
pages[0].offer.id     = 442f123b4d884d8ca85236aa30b99a79   <- IGDB uid, 32-hex form
pages[0]._id          = 78e2d1ca-9ff2-4179-95d4-f67c1acf3b76 <- IGDB uid, UUID form
pages[0].item.catalogId = 7a70b499513441c792b541d53505e0b2  <- the launcher's CatalogItemId
pages[0].item.appName   = Bluebird                          <- the launcher's AppName
pages[0].namespace      = 41f47fd0d3e248bc938a5815d6d64daa  <- CatalogNamespace
```

**IGDB stores Epic *store offer* ids and CMS *page* ids. The launcher stores *catalog item*
ids. Different id spaces for the same game.** Neither IGDB uid appears anywhere in Epic's
local data — I grepped the whole tree.

That CMS endpoint is a possible bridge, but I measured it and it is **not** reliable enough
to be a hard join: of 10 titles attempted, 3 resolved cleanly (Fez, Celeste, Alan Wake),
2 returned HTTP 404 for a slug taken straight from IGDB's own `url` (Moonlighter, Frostpunk),
and 1 returned a page with `item.catalogId = ""` and `hasItem: false` (Transistor). It is a
best-effort enrichment, not a join.

**Verdict on IGDB: Steam and GOG auto-merge on a hard join. Epic, through IGDB, cannot —
it would fall to §5.3 layer 2 (title + year) and land in the merge queue.**

## 20. A better Epic join: GOG's own cross-store identity graph — VERIFIED, 67/67

Galaxy's DB carries a GamePiece type **`allGameReleases`** — GOG's cross-platform release
map, the thing that lets Galaxy show one tile for a game you own on three stores:

```json
{"releases":["gog_1971477531","xboxone_606152324","psn_CUSA08234_00","egg_gwent",
             "steam_1284410","gog_2074191081","generic_51152944180514264", ...]}
```

and `ReleaseProperties.gameId` (`51152944180514264`) is the id of the underlying *game*
that all those releases share.

The same graph is served publicly and unauthenticated by the API Galaxy itself uses:

```
GET https://gamesdb.gog.com/platforms/{platform_id}/external_releases/{external_id}
```

`platform_id` uses the same vocabulary as the `Platforms` table (`gog`, `steam`, `epic`,
`psn`, `humble`, `xboxone`, …). The response carries `game_id` plus `game.releases[]`, each
with `platform_id`, `external_id` and `release_per_platform_id`.

**The `external_id` for Epic is the `.item` manifest's `AppName`** — the exact field the
launcher writes to disk. This is the concrete Fez case the plan asks about:

```
GET /platforms/steam/external_releases/224760    -> game_id 51152861476431582   "Fez"
GET /platforms/epic/external_releases/Bluebird   -> game_id 51152861476431582   "Fez"
```

Same id. **Fez-on-Steam and Fez-on-Epic hard-join on an exact identifier.**

Coverage measured over the user's entire Epic library — all 67 owned Epic base games, by
`AppName`: **67 resolved, 0 failed**, in 24 s at ~8 req/s. **62 of the 67 also expose a
Steam release id**, which is precisely the dedup edge M4 needs against the existing
926-game Steam library.

Caveats to record before anyone builds on this:

- Undocumented and unversioned. It is Galaxy's backing service, not a published API. Treat
  it as §4.5 treats steamcmd.net: useful, free, and expected to break. Cache aggressively,
  fail soft, and never put it in a user-facing path.
- It resolves *games*, not *editions*. `steam_224760` and `gog_1207659211` collapse to one
  `game_id`. That is the right granularity for **Work**, and the wrong one for **Release** —
  §5.3's four-layer model and §9's pitfall 5 (Skyrim SE is not Skyrim) still apply. Use it
  to join Works; keep Release distinctions from the store's own data.
- IGDB remains the metadata backbone (§4.4). This is an identity graph, not a substitute.

**Recommendation for §5.3:**

| Store | Hard join (auto-merge) | Source |
|---|---|---|
| Steam | appid → IGDB | `external_games` source 1 (§4.4, already built) |
| GOG | product id → IGDB | `external_games` source 5 — **verified, 13/14** |
| Epic | `AppName` → gamesdb `game_id` → cross to `steam_<appid>` → IGDB | **verified, 67/67** |

Epic's route is two hops rather than one. It is still an exact-identifier join at every hop
and qualifies for auto-merge under §5.3 layer 1. Where the second hop yields no Steam or GOG
release, the Epic title has no cross-store twin to merge with and can be admitted as its own
Work. Nothing here relaxes the ban on fuzzy auto-merge.

---

# Part D

## 21. Epic OAuth — what it costs, from source and docs

> **SUPERSEDED by `docs/spikes/epic-oauth.md` (2026-08-26).** That spike probed the live
> endpoints rather than reading source alone, and corrects this section on three points:
> the token lifetimes below (8 h / ~23 days) are **unverified** and must not be hardcoded;
> the playtime endpoint's lack of a last-played date is now **confirmed by schema** rather
> than inferred; and this section **missed `acquisitionDate`**, which the API exposes and
> which nothing on disk records.

**No Epic OAuth flow was executed during this spike.** Everything in this section is read
from the source of `legendary-gl/legendary` (the only real implementation — Heroic shells
out to its binary, Rare imports it as a library), from the community endpoint documentation
(`MixV2/EpicResearch`, `LeleDerGrasshalmi/FortniteEndpointsDocumentation`), and from live
unauthenticated probes of Epic's login redirect and legendary's config server. It is
**reported, not verified end-to-end.**

**The flow.** Two viable paths, both still working as of 2026-08-26:

- *Embedded webview (what Heroic does, and what Hoard would copy).* Open
  `https://www.epicgames.com/id/login?responseType=code` in an embedded browser with UA
  `…EpicGamesLauncher`, watch for navigation to
  `https://localhost/launcher/authorized?code=<authorizationCode>`, scrape `code`.
  Interactive cost: the user types their Epic password and 2FA in an embedded window.
  Avalonia has no built-in webview — this means hosting WebView2 on Windows, and something
  else everywhere else.
- *Manual copy-paste (the fallback).* User visits `https://legendary.gl/epiclogin`, logs in,
  and pastes an `authorizationCode` out of a JSON response by hand. Confirmed still live —
  the redirect endpoint answers with the documented shape today. legendary's own source
  comment: *"unfortunately the captcha stuff makes a complete CLI login flow kinda impossible
  right now"*.

A device-code flow would be the clean answer and **is not available**: `launcherAppClient2`'s
grant-type allowlist is `authorization_code, client_credentials, exchange_code, refresh_token`.

**The credentials are Epic's, not ours.** Every tool in this space authenticates as
`launcherAppClient2`, client id `34a02cf8f4414e29b15921876da36f9a` with a matching secret,
extracted from the Epic Games Launcher binary and hard-coded in a public repo since 2020.
Epic has never rotated it. There is **no third-party registration path** for this: Epic
Account Services will issue a real client, but its consent scopes stop at
`basic_profile / friends_list / presence / country` — nothing that reads the storefront
library. The `library:public:items` and `launcher:download:*` permissions live only on
Epic's internal launcher client. **Shipping this means shipping Epic's client secret inside
Hoard and impersonating their launcher.** There is no version of it that does not.

**Tokens.** Access token 8 h; refresh token **~23 days**, rolling — each refresh returns a
new one, so it runs unattended indefinitely provided the app talks to Epic at least
fortnightly. When it does expire (idle, password change, Epic-side revocation) there is no
silent recovery: credentials are wiped and the user redoes the full interactive login.
Heroic's tracker carries a long tail of users hitting this far more often than 23 days
predicts, cause unestablished.

**Playtime — this corrects my prior.** Epic *does* expose it, and `launcherAppClient2` has
the permission (`library:public:{accountId}:playtime:all READ`):

```
GET https://library-service.live.use1a.on.epicgames.com/library/api/public/playtime/account/{accountId}/all
  -> [ { "accountId": "...", "artifactId": "Fortnite", "totalTime": 68363 }, ... ]
```

legendary and Heroic simply never call it (`grep -i playtime` over legendary's source
returns nothing; Heroic times the child process instead). But three caveats gut its value
for Hoard specifically:

1. **No last-played timestamp anywhere.** `totalTime` is a running total, nothing more.
   Hoard's staleness buckets (§6.1) are a recency model. A total without a date does not
   place a game in a bucket.
2. **The total only accrues from `PUT`s the real Epic launcher makes.** A user who plays
   through Heroic — or through Hoard's own process monitor — accumulates **zero** Epic-side
   playtime. For an app about forgotten games, undercounting play is the wrong direction of
   error.
3. `artifactId` is `releaseInfo[].appId` (i.e. `AppName`), not the catalog item id, and the
   unit of `totalTime` is undocumented (seconds is the plausible reading, unverified).

**Terms of service.** Epic's ToS has **no clause about automated access or API scraping** —
it is written around game-client cheating. The closest fit is section 3's prohibition on reverse
engineering, which extracting the launcher's client secret plainly is; §8.b's penalty is
account suspension "a year or longer" or termination. There are **no documented cases** of
anyone being banned or rate-limited for legendary/Heroic/Rare, and no published Epic
statement either endorsing or objecting. Epic's clearest signal is the endpoint's own
response text, shown to the user at the moment of the copy-paste:
*"Do not share this code with any 3rd party service. It allows full access to your Epic
account."*

Risk profile versus PSN (§4.6): **materially lower.** PSNAWP's own docs warn of account
bans and recommend a throwaway account; nothing comparable exists here, and six years of
unrotated credentials is de facto tolerance. The realistic Epic failure mode is
**breakage, not bans** — legendary ships a remote `webview_killswitch` precisely because
Epic's login page breaks the flow periodically.

## 22. Verdict: do not build it

> **This verdict was revisited in `docs/spikes/epic-oauth.md` (2026-08-26) and reversed** —
> not because the reasoning below was wrong, but because the module was subsequently built
> as an **opt-in** source adding two facts this section did not account for (acquisition
> dates and playtime) without displacing `catcache.bin` as the ownership source. The core
> point below stands and is restated there: the owned library is already on disk, and this
> is not how Epic ownership is discovered. See section 9 of the new spike.

The user pre-approved this work. I recommend **not** doing it, because the spike removed
the reason for it.

- **The owned library is already on disk.** `catcache.bin` (section 6) is the launcher's entitlement
  catalog — 74 entitled apps, 73 games, one installed. Plain base64 JSON: no auth, no
  network, no token store, no refresh cycle, no borrowed client secret, no ToS surface. It
  carries title, developer, cover art, DLC parentage and store release date — the same
  payload the `assets` + `library/items` + `catalog/bulk/items` call chain would return, for
  none of the cost.
- **The identity problem is solved without it too.** section 20's gamesdb route resolved 67/67 Epic
  titles by `AppName`, unauthenticated, using an id that is already in the local manifest.
- **OAuth buys one thing: a playtime floor with no dates**, which does not feed the staleness
  feature and systematically undercounts anyone who does not launch through Epic's own client.
- **The gap it would close is small and bounded:** `catcache.bin` needs the launcher installed
  and signed in once, and goes stale until the launcher is next opened. §4.1 already commits
  Hoard to eventually-consistent local reads; this is the same bargain.

This is the shape of judgement §4.6 made about PSN — a reverse-engineered flow, someone
else's credentials, a manual re-auth step, shipped to users — with one difference: here we
do not need the data it would fetch. The cost is not just the auth flow; it is an embedded
browser in an Avalonia app, a DPAPI-protected token store, a refresh scheduler, and Epic's
client secret in Hoard's binary, all to duplicate a file already sitting in `%PROGRAMDATA%`.

**Read `catcache.bin`. Document the staleness. Revisit only if Epic encrypts it** — and if
that day comes, revisit with the playtime endpoint's limits (no dates, EGL-only sessions)
already understood, so the decision is not made twice.

---

## 23. Summary of deviations from the plan

| Plan said | Reality |
|---|---|
| §5 "Epic Manifest Reader" — no path given | `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\*.item`, located via `HKCU\SOFTWARE\Epic Games\EOS\ModSdkMetadataDir` |
| §5 "GOG Galaxy Reader" — no path given | `%PROGRAMDATA%\GOG.com\Galaxy\storage\galaxy-2.0.db`, located via `…\Galaxy\config.json` → `storagePath` |
| §4.4 "`external_games` maps Steam appid / **GOG id / Epic catalog id**" | **GOG id: true** (13/14 coverage). **Epic catalog id: false** — 0/73. IGDB stores Epic *offer* and *page* ids, not catalog item ids |
| §8 M4 "installed titles from both appear and dedupe correctly" | Achievable, but Epic dedup needs the gamesdb hop (section 20), not IGDB |
| M4 scoped to *installed* titles | Epic's **owned** library is also free locally (section 6). GOG's always was (`LibraryReleases`). M4 can deliver owned-not-installed for both at no extra cost |
| — | Epic has **no per-game playtime and no last-played on disk** (only one `LastPlayedGame` slot). An OAuth endpoint returns total playtime but **no date**, and only counts sessions the Epic launcher itself started. Epic entries cannot get a real last-played without M3's process monitor (sections 8, 21) |
| — | GOG **does** have playtime (minutes) and last-played (UTC), including for uninstalled games |
| — | `galaxy-2.0.db` is WAL; `immutable=1` silently returns stale data and `mode=ro` writes `-wal`/`-shm` into the store's directory. Copy first (section 11) |
| — | Galaxy's library contains **other stores' releases marked owned**. Filter `substr(releaseKey,1,4)='gog_'` or double-count the Steam library (section 12) |
| — | Epic's `LauncherInstalled.dat` reports an empty install list while a game is installed. Dead path, like §4.1's `sharedconfig.vdf` |
| — | Local GOG titles are installer-locale (Polish for GWENT); Galaxy's `GamePieces.title` is canonical (section 17) |
| §4.6 excluded PSN partly on ban risk | Epic OAuth carries **no documented ban risk**, but requires shipping Epic's own launcher client secret. Recommended **not** built — `catcache.bin` already supplies the library (section 22) |
