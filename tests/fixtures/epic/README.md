# Epic Games Launcher local-file fixtures

Captured 2026-08-25 from a live Epic Games Launcher 20.2.4 install on Windows 11 and
SANITIZED. Structure, key names, casing, encoding and line endings are otherwise verbatim
from disk. See `docs/spikes/epic-gog-local-files.md` for the findings these encode.

**Sanitized:** the Epic account id (a 32-hex value that appears as an INI section prefix and
as a filename under `Saved\Data\`) was replaced with `00000000000000000000000000000000`
everywhere it appeared; `InstallSessionId` and `EoshRevision` were zeroed; the `[RememberMe]` and `[Offline]` sections of
`GameUserSettings.ini` (encrypted credential blobs) were **deleted, not faked** — never
copy those into a repo. `CatalogNamespace` / `CatalogItemId` / `AppName` values are
**real and deliberately left real**: they are public Epic store identifiers, not PII, and
tests need them to exercise the IGDB bridge described in the spike.

| File | Simulates | Quirks deliberately preserved |
|---|---|---|
| `A47587CE819533CC1BDD688E306742B3.item` | `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests\<InstallationGuid>.item` — an installed base game (Fez) | **no BOM**, CRLF, tab indent; `AppName` is the codename `Bluebird`, not a title; `MainGame*` present but **empty strings**, not absent; `TechnicalType` is the comma-joined `AppCategories` |
| `B1000000000000000000000000000002.item` | an installed **DLC** (synthetic; ids and `AppCategories` copied from the real Borderlands 3 DLC catalog entry) | `MainGameAppName` / `MainGameCatalogItemId` non-empty — the **only** DLC discriminator. `AppCategories` still contains `games`+`applications`, so categories do **not** identify DLC |
| `C2000000000000000000000000000003.item` | a partially-downloaded install (synthetic) | `bIsIncompleteInstall: true`, `bNeedsValidation: true`, `InstallSize: 0` |
| `catcache.bin` | `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Catalog\catcache.bin` — the launcher's entitlement catalog, i.e. **the owned library including titles that are not installed** | base64 of plain UTF-8 JSON (**not** gzipped); trimmed to 6 entries |
| `catcache.decoded.json` | the same 6 entries, decoded, for readability | Fez (base game); a BL3 DLC with `mainGameItem` set; an "Audience" entitlement with `categories` = `audience,public` only; Watch Dogs (`ThirdPartyManagedProvider`); Twinmotion (`software`, **not** a game); LEGO Fortnite (`games/experience` under a parent). `dlcItemList` is `[]` on every entry — it is never populated |
| `GameUserSettings.ini` | `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\WindowsEditor\GameUserSettings.ini` (excerpt) | `LastPlayedGame` is a **single slot**, not per-game history — the only playtime-adjacent value Epic writes to disk. Per-account sections are prefixed with the account id. Composite key form `namespace:catalogItemId:appName` |
| `ThirPartyManagedApps/…_Jasper.json` | `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\ThirPartyManagedApps\<ns>_<catalogId>_<appName>.json` | Epic-owned titles that install through **another launcher** (`Provider: UbisoftConnect`). These get **no `.item` manifest**, so a manifests-only reader misses them. Directory name is misspelled by Epic — `ThirParty`, not `ThirdParty` |
| `LauncherInstalled.dat` | `%PROGRAMDATA%\Epic\UnrealEngineLauncher\LauncherInstalled.dat` | **`InstallationList: []` while a game is demonstrably installed.** Regression fixture: a reader must never treat this file as the install list |
