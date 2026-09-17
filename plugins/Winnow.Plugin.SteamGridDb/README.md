# SteamGridDB artwork plugin

This is Winnow's bundled example of a separately loaded artwork provider. Its only Winnow
dependency is `Winnow.PluginSdk`; it does not reference the app, database, image cache or any
enrichment module.

The host reads `plugin.json`, creates `SteamGridDbPlugin` and supplies an `IPluginContext`.
The plugin uses that context for its protected `apikey` setting, metadata cache and HTTP.
The manifest declares the permitted service hosts, one request per second, two retries and
a 2 MiB response limit. Winnow applies those network policies through its shared HTTP layer.

Only a known `steam` external ID triggers a lookup. Automatic enrichment requests static
heroes and returns background candidates with a `hero` presentation hint. The optional
`IArtworkBrowserPlugin` interface offers separate paged hero, portrait cover and icon choices.
It never searches game titles or changes library identity.

Browsing uses the documented `heroes`, `grids` and `icons` Steam-ID endpoints. Each request
asks for at most 50 static assets and excludes NSFW, humor and epilepsy content. Results are
checked again for content flags, dimensions and supported formats. Heroes and covers accept
PNG, JPEG and WebP; icons request PNG, since the host imports raster images rather than ICO
containers. Cover requests select the documented portrait grid dimensions. Artwork URLs
must use the declared HTTPS CDN host, canonical hash paths and supported extensions,
including the `/file/sgdb-cdn/` prefix. No signed queries or arbitrary image hosts are accepted.
Candidates retain creator names, safe thumbnails and a link to the asset's SteamGridDB page.

Successful results and confirmed misses are cached for 30 days. An expired positive cache
remains available when credentials are absent or requests fail. A failure never becomes a
confirmed miss. The cache envelope remains compatible with the former built-in SteamGridDB
client, and the host supplies compatibility for its previously saved key and cache namespace.
Browser pages use a separate versioned cache keyed by exact Steam ID, artwork kind and page.
Cursors are opaque to the host and bound to that game and kind. Filtered-out assets still
count for provider pagination, so a filtered page does not hide later pages. Missing or
rejected keys produce a setup state; temporary failures produce an unavailable state. Cached
positive pages remain browsable when the key is removed or a request fails. Cancellation
propagates without caching a miss or pausing the credential.

Plugin version 1.1 requires a host supplying `Winnow.PluginSdk` 1.2. Manifest `apiVersion`
remains 1; the existing automatic-provider method and legacy hero cache stay compatible.
Collection enumeration is not implemented: the published API inspected on 2026-09-17 does
not document it. See the [collection feasibility evidence](../../docs/spikes/steamgriddb-collections.md).

Build the project and package `plugin.json` with `Winnow.Plugin.SteamGridDb.dll`. Winnow
supplies `Winnow.PluginSdk.dll`; no host assemblies or API keys belong in a plugin package.
See [plugin authoring](../../docs/plugins.md) for installation and API compatibility.

`tests/Winnow.Plugin.SteamGridDb.Tests` runs entirely against an in-memory SDK context and
canned responses. It also verifies that the plugin assembly depends only on the public SDK
among Winnow assemblies. Live API requests are never part of these tests.
