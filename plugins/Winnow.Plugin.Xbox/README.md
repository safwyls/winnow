# Xbox for Winnow

Imports installed Xbox PC games and optionally games in an Xbox account's played history.
The package uses Winnow's public SDK 1.1 and requires a host with account and game-action
support. It does not reference Winnow's application or database assemblies.

## Install

Download Xbox from the [Winnow plugins page](https://winnow.gg/plugins/).
**Install in Winnow** installs and enables a new package, then opens its settings.
Use the ZIP download if your installation has no browser link handler.

To build an installable archive from source, run from the repository root:

```powershell
.\plugins\Winnow.Plugin.Xbox\Package.ps1
```

Copy the resulting `artifacts/xbox-plugin/Winnow.Plugin.Xbox-1.0.0.zip` into the folder opened
by **Settings → Plugins → Open plugins folder**. Restart Winnow, enable Xbox, and restart
again. Use **Refresh now** to import. Desktop and fullscreen expose the same settings and actions.

## Connect an Xbox account

Local PC discovery works without sign-in. For optional played history:

1. Select **Sign in** and follow the Microsoft verification link with the displayed code.
   Sign-in uses your browser. Winnow does not receive your password.
2. Enable **Import played Xbox PC games**. Enable **Include played console games** separately
   if desired. Save and refresh.

The plugin includes Winnow's public application ID. You do not need an Entra account or an
application registration. **Show advanced settings → Microsoft application ID override** is
only for developers using a different registration. Leave it blank for the built-in sign-in;
clear an old override and save to switch back to Winnow. Changing the effective application
ID requires signing in again. An invalid override disables sign-in until it is corrected or cleared.

The host protects the refresh credential with current-user DPAPI on Windows. Sign-in refuses
to complete when protected persistence is unavailable. **Sign out** removes the saved credential
and prevents further reads of that account's history cache. Previously imported library entries
remain, as with other Winnow plugin imports.

## Maintainer registration

Winnow's registration uses the public client ID `7681b3e4-c26b-4c0e-b91e-ee53af8dd423`,
supports **Personal accounts only**, and enables **Allow public client flows** and Live SDK
support. The device-code flow does not require a redirect URI or client secret. The application
requests `XboxLive.SignIn XboxLive.offline_access`, then exchanges the Microsoft access token
for Xbox user and XSTS tokens. The ID is a public identifier, not a credential.

Maintainers manage this registration in Microsoft Entra. Forks that use a different registration
must enable the same account type and public-client flow, then validate Xbox service access;
creating a generic application ID alone does not prove that the service accepts it. The optional
advanced override supports testing without changing the packaged default.

## What is imported

- Registered Windows PC games with readable GDK or Xbox services manifests, including games
  that have never been launched. Installation discovery copies manifests before reading them
  and never changes Microsoft-owned files. A registered package can be matched by exact package
  family name to a game in account history, covering older Store packages.
- Optional played history, available last-played dates, and reported cumulative minutes.
  Missing playtime stays unknown. A past played title may have come from Game Pass, a trial,
  a disc or a shared account. The library source label explains this.
- Microsoft Store descriptions, release dates where supplied, genres, covers, backgrounds and
  screenshots. Catalog lookups use exact package, title or Store IDs; titles never establish a
  cross-store identity match.
- **Play** for registered launchable PC games. Each action checks the current installation and
  uses its registered Windows application identity. Readable install paths enable Winnow's
  normal local session monitoring. **Open store** opens the exact correlated Microsoft Store
  product, where installation and purchase remain under Microsoft's control.

Never-played uninstalled purchases are unavailable: Xbox's played-history API is not an owned
inventory API. Installed games also do not prove purchase ownership. The plugin does not claim
complete entitlement parity with Steam, Epic or GOG, and does not distinguish purchase from
Game Pass licensing. PC history requires a package family name; title-only PC rows are omitted
to preserve stable identity when a game is installed later. Console titles cannot launch or record local PC sessions. No achievements,
friends, remote console control, patch polling, purchase history or subscription management is
implemented. Local installation and launch require Windows; account availability also depends
on Microsoft's registration, account and regional restrictions.

## Caching and verification

Account history is cached for six hours under the application ID and Xbox account identity;
refresh tokens, device codes and Xbox bearer tokens never enter ordinary caches. Catalog
metadata is cached for seven days. Compatible older positive data remains available after a
network failure without extending its cache expiry. Account changes cannot reuse another
account's history. A complete local scan can clear install state, never ownership; partial or
unreadable scans preserve unknown state.

All API calls use the host's bounded HTTP service and its rate/retry policy. Library calls have
a 100-second internal budget including local discovery; optional statistics have a 20-second
budget within it. Large histories prioritize titles still missing playtime on the next fetch.
Metadata runs separately from library import. Failed or conflicting responses do not become
confirmed empty observations. No live Microsoft calls are required by the test suite.

History requests ask for at most 5,000 rows and only the service-configuration decoration;
the plugin does not assume an undocumented pagination contract. Statistics join through the
exact service-configuration ID and account; absent or ambiguous IDs leave minutes unknown.
Generic Win32/Game Bar history is excluded because it does not establish an Xbox PC package.

```powershell
dotnet test tests/Winnow.Plugin.Xbox.Tests
```

Provider tests use synthetic responses shaped from maintained implementations and sanitized
local fixtures. These verify protocol handling independently of live service availability.
Winnow's registration also passed a live sign-in and PC/console history check. That check returned
last-played dates but no playtime values from the plugin; cumulative minutes remain a separate
validation item. See the [validation report](../../docs/spikes/xbox-integration-validation.md).

## Protocol references

- [Microsoft device authorization](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-device-code)
- [Prism's Xbox device scopes](https://github.com/PrismLauncher/PrismLauncher/blob/develop/launcher/minecraft/auth/steps/MSADeviceCodeStep.cpp)
- [Playnite's title and playtime requests](https://github.com/JosefNemec/PlayniteExtensions/blob/master/source/Libraries/XboxLibrary/Services/XboxAccountClient.cs)
- [OpenXbox catalog client](https://github.com/OpenXbox/xbox-webapi-python/blob/master/xbox/webapi/api/provider/catalog/__init__.py)
- [OpenXbox user-statistics response fixture](https://github.com/OpenXbox/xbox-webapi-python/blob/master/tests/data/responses/userstats_batch_by_scid.json)
- [Microsoft game manifest reference](https://learn.microsoft.com/en-us/gaming/gdk/docs/reference/system/microsoftgameconfig/microsoftgameconfig-schema)
- [Publisher-scoped collections API](https://learn.microsoft.com/en-us/windows/uwp/monetize/query-for-products)
