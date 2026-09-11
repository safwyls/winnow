# SteamGridDB artwork plugin

This is Winnow's bundled example of a separately loaded artwork provider. Its only Winnow
dependency is `Winnow.PluginSdk`; it does not reference the app, database, image cache or any
enrichment module.

The host reads `plugin.json`, creates `SteamGridDbPlugin` and supplies an `IPluginContext`.
The plugin uses that context for its protected `apikey` setting, metadata cache and HTTP.
The manifest declares the permitted service hosts, one request per second, two retries and
a 2 MiB response limit. Winnow applies those network policies through its shared HTTP layer.

Only a known `steam` external ID triggers a lookup. The provider requests static heroes,
rejects unsafe tags and unsupported URLs or dimensions, and returns background candidates
with a `hero` presentation hint. It never searches game titles or changes library identity.

Successful results and confirmed misses are cached for 30 days. An expired positive cache
remains available when credentials are absent or requests fail. A failure never becomes a
confirmed miss. The cache envelope remains compatible with the former built-in SteamGridDB
client, and the host supplies compatibility for its previously saved key and cache namespace.

Build the project and package `plugin.json` with `Winnow.Plugin.SteamGridDb.dll`. Winnow
supplies `Winnow.PluginSdk.dll`; no host assemblies or API keys belong in a plugin package.
See [plugin authoring](../../docs/plugins.md) for installation and API compatibility.

`tests/Winnow.Plugin.SteamGridDb.Tests` runs entirely against an in-memory SDK context and
canned responses. It also verifies that the plugin assembly depends only on the public SDK
among Winnow assemblies. Live API requests are never part of these tests.
