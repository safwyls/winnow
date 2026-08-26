# GOG Galaxy local-file fixtures

Captured 2026-08-25 from a live GOG Galaxy 2.1.8.30 install on Windows 11 (client DB
schema `user_version = 40`) and SANITIZED. See `docs/spikes/epic-gog-local-files.md` for
the findings these encode.

**Sanitized:** the Galaxy user id (`Users.id`, and every `userId` column and every
occurrence inside `GamePieces.value` JSON) was replaced with `11111111111111111`;
`InstalledBaseProducts.installationId` was replaced; `goggame-*.info` `clientId` was
replaced. The **`ProductAuthorizations` table was omitted entirely — it stores a
per-product `clientSecret`.** Never copy that table into a repo. GOG product ids, build
ids, titles and install paths are public/non-personal and are left real.

| File | Simulates | Quirks deliberately preserved |
|---|---|---|
| `galaxy-2.0.min.db` | `%PROGRAMDATA%\GOG.com\Galaxy\storage\galaxy-2.0.db` — 22 tables, 5 owned releases | see below |
| `galaxy-2.0.min.sql` | the generator for the above; `CREATE TABLE` text is **verbatim** from the live DB so constraints and quoted-identifier casing match production | regenerate with `sqlite3 galaxy-2.0.min.db < galaxy-2.0.min.sql` |
| `goggame-1971477531.info` | `<installdir>\goggame-<gameId>.info` for a base game | UTF-8, **no BOM**, LF, 4-space indent; `name` is the **installer-locale** title (Polish), not the store title; `gameId == rootGameId` |
| `goggame-1430742983.info` | the same file for a **DLC** (synthetic) | `gameId != rootGameId` — the no-Galaxy DLC discriminator |
| `gog-games.reg` | `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<gameID>` | `gameName` carries the installer-locale title; `INSTALLDATE` is **local time** (the Galaxy DB's `installationDate` for the same install is **UTC**) |

## What the 5 rows in `galaxy-2.0.min.db` are for

| releaseKey | Role in tests |
|---|---|
| `gog_1971477531` (GWENT) | owned + **installed** + played. Exercises `InstalledBaseProducts`, `PlayTaskLaunchParameters` (whose `label` is the Polish title while `GamePieces.title` is the English one), `GameTimes` = 54 min, `LastPlayedDates` = `2017-07-01 03:32:16` **UTC** |
| `gog_1207664643` (The Witcher 3) | owned, **not installed**, but has 50 min of playtime — proves playtime survives uninstall and must not be gated on install state |
| `gog_1430742983` (New Game +) | owned, `ReleaseProperties.isDlc = 1` — must be excluded from the library |
| `gog_1207658901` (Tyrian 2000) | owned, never played: `GameTimes.minutesInGame = 0` and **no** `LastPlayedDates` row |
| **`steam_1091500` (Cyberpunk 2077)** | **the double-count trap.** A non-GOG release sitting in `LibraryReleases` with `LicensedReleases.isOwned = 1`, even though every `PlatformConnections` row says `Disconnected`. A reader that does not filter on the `gog_` releaseKey prefix will re-import a game the Steam ingest already owns. Any test of the ownership query must assert this row is **absent** from the result |

`gog_2074191081` is present in `ReleaseKeys` / `ReleaseProperties` / `ProductsToReleaseKeys`
but **not** in `LibraryReleases` — a known release the user does not own. `ReleaseKeys` is
not an ownership list.
