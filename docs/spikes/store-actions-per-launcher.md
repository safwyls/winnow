# Spike: Store actions per launcher

> **Evidence, not a rule.** This document records how something was measured and is
> never the place to look up what to do. The current rule is in `design-system.md` §10.3.

**Settles:** what Epic and GOG actually support for launch, install, store page and patch
notes, and which Winnow-stored fields each route needs. Measured 2026-09-05.

## Method

Epic has a discriminating oracle: `%LOCALAPPDATA%\EpicGamesLauncher\Saved\Logs\EpicGamesLauncher.log` logs every URI dispatch. A registered route logs `LogUriHandler: Started processing URI [...] with <HandlerName>`; an unregistered one logs `LogUriHandler: Warning: Was unable to find URI Handler for the URI [...]`.

The control that makes the oracle trustworthy: `OnSignInUriHandler` is a **catch-all**. Both `com.epicgames.launcher://winnowbogusroute` and `.../winnowbogusroute/somepath` resolved to it. So "OnSignInUriHandler picked it up" proves nothing at all. Only a *named second handler* proves registration. The `apps` route is the exception: it is claimed exclusively by the app handlers keyed on `action=`, and an unrecognised action there produces the warning. Positive control: `apps/<bogus key>?action=verify` resolved `AppVerifyUriHandler` — a bogus key still resolves the *handler* while resolving no *game*, which is what makes the probe command below safe.

Launcher under test: build `20.2.9-57799698+++UE5+Release-Distro-5.5` (UE 5.5.4), running `-silent -launchcontext=boot`, signed out (`EOS SDK: NotLoggedIn`). GOG Galaxy was running (pid 8316). `galaxy-2.0.db` (`user_version = 40`) was copied together with its `-wal` and `-shm` before being read; nothing under Steam, Epic or GOG was written, per `AGENTS.md`.

### Provenance categories

Each claim carries one of four labels:

- **verified-by-execution** — fired a real URI or API call and observed the result.
- **verified-by-inspection** — read the binary, registry or local file; not executed.
- **verified-by-vendor-documentation** — taken from a vendor's published documentation (Epic protocol-activation page: dev.epicgames.com/docs/epic-games-store/protocol-activation). Documentation is a source of *candidates*, never of *findings*; where documentation and measurement disagree, the measurement wins and the disagreement is noted.
- **needs-execution-by-the-user** — a candidate template that has not yet been fired.

## Protocol handler registrations — verified-by-inspection

| Key | Value |
|---|---|
| `HKCR\com.epicgames.launcher` | `(default)="Epic Games Launcher Link"`, `URL Protocol` present |
| `HKCR\com.epicgames.launcher\shell\open\command` | `"C:\Program Files\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe" %1` |
| `HKCU\Software\Classes\com.epicgames.launcher` | absent — no per-user override |
| `HKCR\goggalaxy\shell\open\command` | `"C:\Program Files\GOG Galaxy\GalaxyClient.exe" /urlProtocol="%1"` |
| `HKCU\Software\Classes\goggalaxy\shell\open\command` | identical string; HKCU shadows HKCR and the two agree |

Both schemes resolve on this machine. Galaxy self-registers into HKCU: `GalaxyClient.exe` at offset `0x965318` holds `HKEY_CURRENT_USER\Software\Classes`, `{}\shell\open\command` and `"{}" /urlProtocol="%1"` contiguously.

## Epic

### Launch, installed — verified-by-inspection

`com.epicgames.launcher://apps/{ns}%3A{catalogItemId}%3A{appName}?action=launch&silent=true`. At `EpicGamesLauncher.exe` offset `0x278b6c0` the strings `?action=launch&silent=true` and `com.epicgames.launcher://apps/` sit contiguously with `AppShortcutDesktopDesc`, `ShortcutDesktopDesc` and `Launch {0}` — that block is the desktop-shortcut generator, so what Winnow ships is literally the string Epic writes into its own shortcuts. `AppLaunchUriHandler` symbol present. Not executed: a launch verb starts a game. Needs Namespace, CatalogItemId and AppName; all three are already held in `EpicLaunchKey`.

### Install, uninstalled — verified-by-execution

`com.epicgames.launcher://apps/{ns}%3A{catalogItemId}%3A{appName}?action=install`. Fired at a real owned-but-uninstalled Epic game, the launcher refreshed the entitlement, resolved the catalog item, dispatched the install, and opened Epic's own install-location selector. Winnow never downloads anything — Epic does, after the user confirms in that selector. Needs Namespace, CatalogItemId and AppName; all three are held in `EpicLaunchKey`.

This verb is **undocumented** — it appears nowhere in Epic's protocol-activation documentation. The documented verb is `action=installer`, which does something else entirely (see below). **Vendor documentation and measured behaviour disagree.**

The inspection evidence that predicted this template: the old finding recorded in `StoreActions.cs` was that no install action existed in the binary. Against build 20.2.9 that is no longer true. The complete `*UriHandler*` symbol inventory from the Portal binary — the whole list, not a search for one name, which is what makes the earlier absence claim checkable:

`AddFriend, AppInstall, AppLaunch, AppVerify, BuildNotification, CodeRedemption, GameInvite, InstallAction, ModDownload, ModUpload, Navigation, Notifications, OnSignIn, Settings, UninstallAction, UpdateAction` (plus `UriHandlerService`).

`FAppInstallUriHandler`, registered at `Domain.cpp:863`, carries a full install-dispatch log vocabulary, including `AppInstallUriHandler: Dispatching install for AppId %s; will navigate to Library when the install-location selector concludes`, `Catalog item resolved for AppId %s; dispatching install`, and `Install-location selector concluded for AppId %s (accepted=%s); navigating to Library`. Two disambiguations matter: `InstallAction`, `UninstallAction` and `UpdateAction` are the Mod SDK handlers (every log line is about mod offers), not game install; `AppInstall` is the game one. Supporting evidence: a contiguous query-parameter vocabulary at `0x2782b28` — `prompttype, noprompt, action, install, uninstall, installtag, uninstalltag, installcomponent, uninstallcomponent` — beside `FSelectiveDownloadRequest`, a literal `action=download` string, and the generic builder `com.epicgames.launcher://%s` + `&action=%s` + `&silent=true`.

### Install verb: `action=installer` — verified-by-vendor-documentation, contradicted by execution

Epic's protocol-activation documentation (dev.epicgames.com/docs/epic-games-store/protocol-activation) names `action=installer` as the install verb. In practice it routes to `SelectiveDownloadUpdate` — the optional-components screen for an already-installed app — and is a no-op on an uninstalled one. The undocumented `action=install` is the working verb (see above). **Vendor documentation and measured behaviour disagree; the measurement wins.**

### Update check: `action=updatecheck` — verified-by-vendor-documentation, contradicted by execution

Also documented on Epic's protocol-activation page. Not registered in build 20.2.9: it is rejected before dispatch. **Vendor documentation and measured behaviour disagree; the measurement wins.**

### URL-encoded install-directory route — verified-by-execution, not built

The launcher resolved a bare Fez directory path back into its full launch triple. But an uninstalled game has no directory, so the route can only address games the triple already serves. Recorded as measured and deliberately not built.

### Store page — verified-by-execution that the route exists; the slug is missing

`com.epicgames.launcher://store/product/<slug>` works: `MainRouter` rewrites it to `launcher.store.epicgames.com/store/product/<slug>`. What Winnow lacks is not a route but the **slug**, which it does not store.

The earlier probe on this same machine concluded that no in-launcher route existed. That conclusion was wrong for two compounding reasons documented in [Methodological failures](#methodological-failures) below: the probe read `OnSignInUriHandler` (a catch-all this document's own Method section warns against) as its oracle while never checking `MainRouter`, and it ran while signed out. The original measurements follow for the record: `://store`, `://store/product/fez`, `://store/en-US/product/fez` and `://library` all fell through to the catch-all; bare `://apps` resolved no handler; on the `apps` route `action=productdetail`, `action=store` and `action=show` each produced `Was unable to find URI Handler`. The `NavigationUriHandler` internal location table at `0x27ab190` reads `/apps /epics /mods /coderedemption /debug /social /invite /settings /null` plus `firstrun twinmotion epoodle requiresbuildrefresh` — no `/store`. The string `/store/en-US/product/` does exist in the binary, but only as a web path used by the friends-product flow, not as a URI route. The probe was right that `NavigationUriHandler` has no `/store` entry; the error was in treating that handler as the only router.

### Store page from stored ids alone — verified-by-inspection, no

The live `catcache.bin` (297 entries) was decoded and its top-level and `customAttributes` keys censused. Top level is exactly `id, namespace, entitlementName, eulaIds, title, description, longDescription, technicalDetails, developer, lastModifiedDate, keyImages, categories, releaseInfo, customAttributes, dlcItemList, mainGameItem`. No slug, no product URL, no offer id anywhere. The only URL-shaped outliers are `egs.links.release_notes` (1 entry, Unreal Engine), `com.epicgames.portal.product.websiteUrl` (1, Fortnite) and `ListingIdentifier` (20, Fab/UE marketplace assets). Winnow's authenticated catalog response has none either — `tests/fixtures/epic-oauth/catalog-bulk-items-games.json` and every other Epic fixture return zero hits for "slug".

### The route that would work, partially — verified-by-execution

`https://store-content.ak.epicgames.com/api/content/productmapping` is public and unauthenticated, needs no key and no headers, and returned HTTP 200 / 68,473 bytes / 1,283 entries: a flat JSON object mapping catalog **namespace** to store **slug** (`{"fn":"fortnite","crab":"satisfactory","min":"hades",...}`). Measured against this machine's real library it covers 56 of 67 owned base games (84%); the 11 misses are delisted or giveaway-only titles (Frostpunk, Palia, LOTR Return to Moria, Moonlighter, ABZU, Dauntless, Drawful 2, Torchlight, Unreal Tournament, >observer_, Hob). Slug validity was confirmed with a negative control against `https://store-content.ak.epicgames.com/api/en-US/content/products/{slug}`: 200 for `soma`, `this-war-of-mine`, `tiny-tinas-wonderlands` and `world-war-z`; 404 for `winnow-bogus-slug-xyz`.

`store.epicgames.com` returns 403 to every request from this machine, bogus paths and real ones alike, so the final web URL `https://store.epicgames.com/p/{slug}` is **needs-execution-by-the-user**: open `https://store.epicgames.com/p/soma` in a browser and confirm it lands on SOMA. The slug is measured; the URL template built from it is not.

### Patch notes — verified-by-inspection, none exists

No route, no verb, no local field, no service. `egs.links.release_notes` appears on exactly one of 297 catalogue entries and points at unrealengine.com. There is nothing for games.

### Root cause of the empty Epic action band — verified-by-execution

This is the finding the whole exercise was chasing. Measured on the user's real library: 67 Epic ownership rows; **0** of them with a complete launch triple; `metadata_cache` held **zero** `epic-catalog` rows — the rows `SqliteEpicCatalogCache` writes — against 4,523 rows across igdb, steam-store, update-poll, steamcmd, gamesdb, steam-web and steam-news. So `SqliteEpicLaunchKeys.GetAllAsync` returned an empty dictionary and **no Epic game could offer any action at all**, not even Play on the one installed title.

The remaining quantities: `external_ids` holds the catalogItemId for all 67 and all 67 match `catcache.bin`; the 67 `gamesdb` cache rows are keyed `release:epic:<AppName>` and those AppNames match `catcache.bin` 67 for 67; and `catcache.bin` carries the **namespace** for 67 of 67 owned base games, locally and offline, with no network call. 66 of the 67 rows are uninstalled and 1 is installed. Persisting the namespace at ingest takes the triple from 0/67 to 67/67. That work has landed in `EpicLibrarySource`.

## GOG

### The complete verb table — verified-by-inspection

Eight contiguous null-terminated ASCII strings at `GalaxyClient.exe` offset `0x0099b298`, immediately followed by the dispatcher's own log strings: `launchGame, installGame, installDlc, focusGame, installationScreen, refreshGame, openStoreUrl, openGameView`. Adjacent dispatcher strings: `Handling protocol command: '{}'.`, `No handler for protocol command: {}`, `Received command '{}', but it's disabled in the config.`, `Received external command from URL, but in the wrong format: {}.`. Parsing is path-based.

`disabledProtocolCommands` is a remote-config list that can disable any verb server-side; it is not present in any local config on this machine, so nothing is disabled here.

### Launch, installed — verified-by-inspection

`goggalaxy://launchGame/gog_{productId}`, corroborated by the error string `Launch game view command failed to convert '{}' to a GRK.`

### Show in GOG Galaxy — verified-by-inspection

`C:\ProgramData\GOG.com\Galaxy\logs\GalaxyClient.log*` carries two real historical dispatches on this machine: `Handling protocol command: 'goggalaxy://openGameView/gog_0000000001'.` and `Handling protocol command: 'goggalaxy://opengameview/gog_0000000002'.` No `No handler for protocol command` line appears anywhere in those logs. The second is lowercase, so verb matching is case-insensitive — which matters, because `Uri.ToString()` lowercases the authority and GOG puts its command name there.

### Install, uninstalled — verified-by-inspection, needs-execution-by-the-user

`goggalaxy://installationScreen/{productId}` takes the bare numeric product id, not a release key. The bare-id/release-key asymmetry is real and is not a bug. `openGameView` and `launchGame` both take a GRK (`gog_<id>`); `installationScreen` takes the bare numeric product id. Evidence: the first two carry `Open game view command failed to convert '{}' to a GRK.` and `Launch game view command failed to convert '{}' to a GRK.`, while the third carries `Product ID for the installation screen command cannot be empty.` and performs no GRK conversion. Corroborated by the mangled C++ symbols `showInstallationScreen@ClientCore@GalaxyClient@@AEAAXAEBVProductId@fundamentals@galaxy@@` and `handleShowInstallationScreenEvent@ClientCore@...(ProductId, LoginTypes)`, which take a `ProductId` directly. Related strings: `Cannot show installation screen for '{}', because it's already installed.`

Not executed — it is one click from a multi-gigabyte download on an unattended machine.

```powershell
Start-Process "goggalaxy://installationScreen/1207658871"; Start-Sleep 6; Select-String "C:\ProgramData\GOG.com\Galaxy\logs\GalaxyClient.log" -Pattern "protocol command|installation screen" | Select-Object -Last 5
```

Compare against `goggalaxy://installationScreen/gog_1207658871`; the bare-id form is expected to be accepted and the prefixed form to fail.

### Store page — verified-by-execution, the numeric id does not work

`https://www.gog.com/game/1207658871` redirects 302 to `/en/game/1207658871` and then 302 to `https://www.gog.com/games`, the catalogue landing page; `https://embed.gog.com/game/1207658871` behaves identically. A slug does work: `https://www.gog.com/en/game/panzer_general_2` returned 200 with the title `Panzer General 2 ... | GOG.COM`.

The slug is one anonymous call from the stored product id — **verified-by-execution**. `https://api.gog.com/v1/games/{productId}` returns 200 `application/hal+json` with `_links.store.href` (plus support, forum and icon links); `https://api.gog.com/products/{productId}?expand=changelog` returns 200 with `slug`, `title`, `id`, `links`, `purchase_link` and `changelog`. Neither needs a key or an auth header. This is Galaxy's own route rather than a guess: the path `v1/games/` at `0xa4adb8` and the literal expand string `downloads,expanded_dlcs,related_products,changelog` at `0xa4af58` are both in `GalaxyClient.exe`, surrounded by `Failure getting product details for Product ID {}`.

`goggalaxy://openStoreUrl` also exists and takes a full http(s) URL, allowlist-checked (`Provided URL is not allowed for Store.` sits between the `http:` and `https:` literals). Its shape is **unverified**; the check is `Start-Process "goggalaxy://openStoreUrl/https%3A%2F%2Fwww.gog.com%2Fen%2Fgame%2Fpanzer_general_2"`.

### Patch notes — verified-by-execution

`https://api.gog.com/products/{productId}?expand=changelog` returned real Panzer General 2 patch notes as HTML. This is the one capability GOG has and Epic does not. There is no `goggalaxy://` patch-notes verb; the eight-verb table above is complete.

### GOG local data — the slug and the changelog are on disk, but sparse

From `galaxy-2.0.db`: `Details.slug` is non-null on 4 of 4 Details rows, but there are only 4 Details rows for 16 Products; `Details.changelog` is non-null on 1 of 4; `LimitedDetails.links` carries a `product_card` URL on 4 rows; the `productLinks` GamePiece carries a `productCard` URL on 9 of 45 `gog_` release keys; the `changelog` GamePiece exists on 4 of 45 and is non-null on 1. These rows are populated lazily, when the user opens that game's page in Galaxy, so an action built on them alone would light up for roughly a fifth of the library, unpredictably. They are a free cache in front of the API, not a basis for an action.

## Methodological failures

Two failures in this spike's original findings, recorded so the next person does not repeat them.

### Failure one: a verb taken from vendor documentation

`action=installer` came from Epic's own protocol-activation documentation and is wrong in practice. Documentation is a source of *candidates*, never of *findings*. The undocumented verb — `action=install` — is the working one. When a vendor publishes a protocol specification, that publication proves the vendor intended the verb, not that the binary implements it. Measure.

### Failure two: the wrong oracle, and the wrong session state

The store-route probe reached a correct negative for two compounding reasons that were both avoidable. It used the *correct* slug — `productmapping` confirms `fez` — so the input was never the problem, which is what made the wrong conclusion so convincing. But it read `OnSignInUriHandler` as its oracle while **never checking `MainRouter`**, and it ran **while signed out**. The Method section of this document already warns that `OnSignInUriHandler` is a catch-all and that "OnSignInUriHandler picked it up" proves nothing — the probe then read a catch-all resolution as a negative finding anyway. A negative from an oracle you have already documented as non-discriminating is not a negative, and session state is part of the experimental setup.

## The matrix

| Store | Capability | URI or route | Provenance | Winnow-stored fields needed |
|---|---|---|---|---|
| Steam | Launch (installed) | `steam://run/<appid>` | verified-by-execution (existing) | `steamAppId` |
| Steam | Install (not installed) | `steam://install/<appid>` | verified-by-execution (existing) | `steamAppId` |
| Steam | Store page | `https://store.steampowered.com/app/<appid>/` | verified-by-execution (existing) | `steamAppId` |
| Steam | Patch notes | `https://store.steampowered.com/news/app/<appid>` | verified-by-execution (existing) | `steamAppId` |
| GOG | Launch (installed) | `goggalaxy://launchGame/gog_<productId>` | verified-by-inspection | `gogProductId` |
| GOG | Install (not installed) | `goggalaxy://installationScreen/<productId>` | verified-by-inspection; needs-execution-by-the-user | `gogProductId` |
| GOG | Show in GOG Galaxy | `goggalaxy://openGameView/gog_<productId>` | verified-by-inspection | `gogProductId` |
| GOG | Store page | none shipped; slug from `api.gog.com` would unlock it | verified-by-execution (API), not built | `gogProductId` + slug from API |
| GOG | Patch notes | none shipped; changelog from `api.gog.com` would unlock it | verified-by-execution (API), not built | `gogProductId` + changelog from API |
| Epic | Launch (installed) | `com.epicgames.launcher://apps/<ns>%3A<catalogItemId>%3A<appName>?action=launch&silent=true` | verified-by-inspection | `EpicLaunchKey` (all three parts) |
| Epic | Install (not installed) | `com.epicgames.launcher://apps/<ns>%3A<catalogItemId>%3A<appName>?action=install` | verified-by-execution; **undocumented** — the documented verb `action=installer` is wrong (see [Methodological failures](#methodological-failures)) | `EpicLaunchKey` (all three parts) |
| Epic | Store page | `com.epicgames.launcher://store/product/<slug>` | verified-by-execution (route exists); slug not stored; slug from productmapping would unlock it | `CatalogNamespace` (now stored) + slug from productmapping API |
| Epic | Patch notes | none exists | verified-by-inspection | — |

## What Winnow does not store but could

One landed; three remain as follow-on work.

1. **Epic `CatalogNamespace` at ingest — now stored.** `EpicLibrarySource` persists the namespace from `catcache.bin` at ingest, taking the complete launch triple from 0/67 to 67/67. This was the highest-leverage change for the action band and has landed.
2. **Epic slug, from the 68KB productmapping** — one request for the whole library, trivially cacheable. Unlocks the store page for about 84% of Epic titles.
3. **GOG slug, from `api.gog.com`**, or opportunistically free from the Galaxy database when it happens to have cached it.
4. **GOG changelog** — the same call as the slug, so patch notes cost nothing extra once the slug is being fetched.
