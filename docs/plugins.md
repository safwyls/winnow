# Winnow provider plugins

Winnow loads optional local .NET plugins for library imports, metadata, artwork and recommendation
feeds. SteamGridDB is the bundled reference plugin in `plugins/Winnow.Plugin.SteamGridDb`.
It references the public SDK alone; the application supplies storage and HTTP services.

For an explanation of the design with a SteamGridDB trace and a buildable example, read
[Building Winnow's plugin system, from the inside out](plugin-system-walkthrough.md).

## Install and configure

1. Open **Settings → Metadata & artwork → Open plugins folder**. The folder is `plugins`
   inside Winnow's data directory, including when using `--data-dir`.
2. Copy the extracted package into its own subdirectory. That directory must contain
   `plugin.json`, the entry DLL and any private dependencies. Restart Winnow to discover it.
3. Enable the plugin in the same settings tab, then restart to activate it. Third-party
   packages start disabled. Enabling a plugin authorizes its code to run on your device.
4. Enter its declared settings and credentials. Saving queues a background refresh;
   **Refresh** queues another pass. Artwork providers appear in the source-order controls.

Desktop and fullscreen expose the same configuration through their own controls. To update,
close Winnow and replace the package files; to uninstall, close it and remove its directory.
Disabling or uninstalling retains imported library facts and cached metadata. It stops future
provider execution after restart. There is no online gallery, package downloader or hot reload.

## Trust and lifecycle

Plugins execute as **trusted code inside Winnow's process**. They can exercise the operating
system permissions of Winnow. Declared hosts and scoped services constrain calls made through
the SDK; they cannot constrain a DLL that directly uses .NET filesystem or network APIs.
An `AssemblyLoadContext` separates dependency resolution, not security permissions.
[Microsoft documents this distinction](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support).

The host validates manifests before executing code, shares one SDK assembly, and checks that
declared capabilities are implemented. Invalid, duplicate or incompatible packages appear as
diagnostics in settings. Initializers have a 30-second deadline and provider calls a 120-second
deadline. A timeout disables the plugin for the session. Ordinary exceptions fail that operation
and produce a fixed diagnostic without the exception text. Cancellation is passed to providers.
These measures handle cooperative failures; native crashes and malicious or noncooperative code
require process or OS isolation, which this version does not provide.

Discovery, initialization and provider work run off the UI thread. The background pass waits for
startup synchronization, imports libraries, then enriches original owned works. Feed providers
run concurrently with the built-in feed. Built-in shelves publish as soon as they are ready;
optional shelves append without replacing existing cards. One five-second aggregate budget
covers feed snapshot reads, queued provider invocations and execution across all providers.
Expiry during shared input reads produces an empty optional supplement; caller cancellation
remains cancellation. Both use the same boundary as expiry while waiting for a provider.
Completed provider results survive another provider's timeout. A newer feed generation,
feedback change or closed screen rejects its old supplement. Plugins should cache network
data and answer promptly; their work cannot delay the built-in recommendations.

## Author a plugin

The SDK targets .NET 10 and has no Winnow application, UI, database or other package dependency.
Build its local NuGet package with:

```powershell
dotnet pack src/Winnow.PluginSdk -c Release -o artifacts/plugin-sdk
```

Reference `Winnow.PluginSdk` version `1.0.0` from that local package source. Set
`EnableDynamicLoading=true` in your .NET 10 class-library project and keep the SDK reference
out of your package's runtime dependencies (`Private=false` for a project reference, or
`ExcludeAssets=runtime` for a package reference). Copy your DLL, `.deps.json`, manifest and
private dependencies into the package directory. The host always supplies the SDK assembly.

Minimal manifest:

```json
{
  "id": "example-art",
  "name": "Example artwork",
  "version": "1.0.0",
  "apiVersion": 1,
  "entryAssembly": "Example.Art.dll",
  "entryType": "Example.Art.Provider",
  "capabilities": ["artwork"],
  "settings": [
    { "key": "apikey", "label": "API key", "secret": true, "required": true }
  ],
  "network": {
    "allowedHosts": ["api.example.com", "art.example.com"],
    "requestsPerSecond": 1,
    "maxRetries": 2,
    "maxResponseBytes": 2097152,
    "timeoutSeconds": 90
  }
}
```

IDs and setting keys are lowercase. Use a stable plugin ID: it owns the package's storage,
artwork order and feed identity. `apiVersion` must equal the host's supported API major (1).
The SDK assembly major also remains 1 independently of the application's version. The loader
does not resolve plugin dependencies from other plugin directories.

Implement `IPlugin.InitializeAsync` to retain `IPluginContext`, plus one or more capabilities:

| Interface | Return value and host behavior |
|---|---|
| `ILibrarySourcePlugin` | Inventory, installation and playtime observations. Stable source IDs become provider-scoped ownerships. Known Steam/Epic/GOG IDs can join an existing release when all matches agree; titles never cause an automatic join. Missing results never delete ownerships. |
| `IMetadataProviderPlugin` | Summary, release date, genres and tags for the supplied game handle. Summary/year fill missing automatic fields; user overrides remain authoritative. Genre/tag observations have separate source-scoped assignments. |
| `IArtworkProviderPlugin` | Static backgrounds, covers or screenshots with URLs and dimensions. The host validates HTTPS hosts, dimensions and bounded counts, retains source attribution, and uses isolated hashed cache keys. |
| `IRecommendationFeedPlugin` | Scores from 0 to 1 and a concise explanation for supplied library handles. The host rejects unknown handles and invalid values and renders one named shelf using existing cards, feedback and reserves. |

Library-source support initially covers inventory and playtime import. A wholly new launcher's
custom launch, installation and sign-in workflow is not exposed by this SDK. Existing Steam,
Epic and GOG actions remain available where their known IDs establish the corresponding links.

`PluginGame.Id` is opaque and valid for that request. Metadata/artwork handles identify original
works; recommendation handles identify eligible owned entries. Use `ExternalIds` for service
lookups. Feed inputs include grouped installation/playtime facts, last-played dates, genres and
tags. They exclude hidden account entries, non-games, provisional names, retired/derelict games
and active dismissal/snooze groups. Confirmed duplicates appear once. A returned handle cannot
reintroduce a suppressed game. Each shelf displays six cards with four reserves.

For library and artwork lists, `null` means unavailable and preserves previous observations;
an empty list means a confirmed empty result. The host never infers unownership from absence.
A confirmed empty artwork result removes that plugin's backgrounds and screenshots; it does
not clear an assigned cover. Recommendation results are computed for the current feed: `null`
or an empty list produces no shelf, and the host does not retain an older successful result.
Saved user backgrounds take precedence over automatic source ordering. Screenshot results
use the existing gallery/lightbox.

## Host services

- **Settings:** only declared non-secret fields, scoped by plugin ID.
- **Secrets:** reads declared secret fields; editing happens through Winnow settings. Windows
  persists them with current-user DPAPI and distinct plugin/key entropy. Other hosts refuse
  persisted secret writes. `Plugins__<plugin-id>__<key>` environment configuration can supply
  a value without saving it. Secrets never appear in settings read snapshots.
- **Cache:** up to 2 MiB per payload, with an expiry supplied by the provider. Expired entries
  remain readable for offline fallback. Providers should version their cache keys or payloads.
- **HTTP:** HTTPS on exact declared hosts, redirects disabled, bounded responses and a shared
  per-plugin Polly rate/retry policy. Use this service for API access; artwork downloads also
  go through host checks and a separate 32 MiB response bound.

The SteamGridDB plugin demonstrates exact-ID lookup, credential changes, negative caching,
static-art filtering and stale-response fallback. Its original API key, response cache,
stored hero observations and downloaded source images migrate when the plugin host first runs.
The legacy `SteamGridDb__ApiKey` environment variable continues to work.

## Verification and scope

`tests/Winnow.Plugins.Tests` loads a separate fixture DLL through the real loader and tests
manifest validation, opt-in state, failures, cancellation and HTTP limits. The standalone
SteamGridDB tests reference only its plugin and SDK. App integration tests exercise imported
ownerships, field provenance, artwork, grouped feeds and generated settings on temporary data.

There is no custom screen or UI replacement contract in API 1. Settings declarations and shared
data-driven shelves/gallery are the supported presentation extension points. Native sandboxing,
an online marketplace and automatic plugin updates are also outside this initial SDK.
