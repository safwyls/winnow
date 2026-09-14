import { sitePath } from '@/lib/site-path';
import type { DocArticle } from './content-types';

const source = 'https://github.com/safwyls/winnow/blob/main/';
const example = '/examples/installed-and-unplayed/';
const provider = `using Winnow.PluginSdk;

namespace Example.Installed;

public sealed class Provider : IRecommendationFeedPlugin
{
    public ValueTask InitializeAsync(IPluginContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(
        IReadOnlyList<PluginGame> library,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PluginRecommendation>();
        foreach (var game in library)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (game.Installed && game.PlaytimeMinutes == 0 && game.LastPlayedAt is null)
                results.Add(new(game.Id, 0.8,
                    "Installed and ready, with no recorded playtime."));
        }
        return Task.FromResult<IReadOnlyList<PluginRecommendation>?>(results);
    }
}`;

export const sdkArticle: DocArticle = {
  slug: 'plugin-sdk',
  audience: 'Plugin authors',
  title: 'Build a Winnow plugin',
  description: 'A working first provider, the complete API 1 contract, and the rules for shipping a reliable package.',
  sections: [
    { id: 'choose-a-capability', title: 'Choose what your plugin adds', content: <>
      <p>The SDK is a .NET 10 library with no UI or database dependency. API 1 supports four provider contracts. A single entry class may implement several; declare each one in its manifest.</p>
      <table><thead><tr><th>Capability</th><th>Interface</th><th>Use it for</th></tr></thead><tbody>
        <tr><td><code>library</code></td><td><code>ILibrarySourcePlugin</code></td><td>Ownership, installation and playtime observations.</td></tr>
        <tr><td><code>metadata</code></td><td><code>IMetadataProviderPlugin</code></td><td>Descriptions, release dates, genres and tags.</td></tr>
        <tr><td><code>artwork</code></td><td><code>IArtworkProviderPlugin</code></td><td>Static covers, hero backgrounds and screenshots.</td></tr>
        <tr><td><code>recommendations</code></td><td><code>IRecommendationFeedPlugin</code></td><td>A named feed shelf with scores and explanations.</td></tr>
      </tbody></table>
      <p>Winnow renders the results in desktop and fullscreen using its own components. API 1 does not expose custom screens, launcher installation or sign-in flows, arbitrary launch commands, or UI replacement.</p>
      <p>Plugins run as trusted code inside Winnow. Host services restrict their own calls, but cannot prevent a DLL from calling filesystem or network APIs directly. Install code only from authors you trust; dependency isolation is not a security sandbox.</p>
    </> },
    { id: 'first-plugin', title: 'Tutorial: your first feed provider', content: <>
      <p>This example adds an “Installed and unplayed” shelf using only the supplied library snapshot. It needs no credential or network connection. Install the .NET 10 SDK and clone the <a href="https://github.com/safwyls/winnow">Winnow repository</a>, then run these commands from its root.</p>
      <pre><code>{`dotnet pack src/Winnow.PluginSdk -c Release -o artifacts/plugin-sdk
dotnet new classlib -n Example.Installed -f net10.0 -o examples/Example.Installed`}</code></pre>
      <p>Replace the generated project file with the following. The local package is version 1.0.0; these instructions do not assume a published NuGet feed. Delete the generated <code>Class1.cs</code>.</p>
      <pre><code>{`<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Winnow.PluginSdk" Version="1.0.0"
                      ExcludeAssets="runtime" />
    <None Update="plugin.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>`}</code></pre>
      <p>Add <code>Provider.cs</code>. Return the supplied handle unchanged; do not turn titles or external IDs into game handles.</p>
      <pre><code>{provider}</code></pre>
      <p>Add <code>plugin.json</code> alongside the project file.</p>
      <pre><code>{`{
  "id": "example-installed",
  "name": "Installed and unplayed",
  "version": "1.0.0",
  "apiVersion": 1,
  "entryAssembly": "Example.Installed.dll",
  "entryType": "Example.Installed.Provider",
  "description": "A local example shelf for installed games with no recorded playtime.",
  "capabilities": ["recommendations"]
}`}</code></pre>
      <pre><code>{`dotnet restore examples/Example.Installed --source artifacts/plugin-sdk
dotnet build examples/Example.Installed -c Release --no-restore`}</code></pre>
      <p>Download the same <a href={sitePath(example + 'Example.Installed.csproj')} download>project file</a>, <a href={sitePath(example + 'Provider.cs')} download>provider source</a>, and <a href={sitePath(example + 'plugin.json')} download>manifest</a> if you prefer starting from files.</p>
      <p>Copy the Release output into <code>plugins/example-installed</code> in a throwaway Winnow data directory. Include the DLL, <code>.deps.json</code>, manifest, and private dependencies. Do not include <code>Winnow.PluginSdk.dll</code>: the host supplies it.</p>
      <pre><code>{`# PowerShell, from the Winnow repository root
$demoData = Join-Path $env:TEMP ("winnow-plugin-demo-" + [guid]::NewGuid())
$package = Join-Path $demoData 'plugins/example-installed'
New-Item -ItemType Directory -Path $package -Force
Copy-Item examples/Example.Installed/bin/Release/net10.0/* $package
dotnet run --project src/Winnow.App -- --data-dir $demoData --seed-sample --no-sync`}</code></pre>
      <p>In either interface, open Settings → Plugins, enable the example, close Winnow, and rerun the same command with the same <code>$demoData</code>. Open the feed. The optional shelf appears only when the current eligible snapshot contains matching games; an empty result is expected otherwise. The plugin adds recommendations, never ownerships.</p>
    </> },
    { id: 'manifest', title: 'Manifest reference', content: <>
      <p>The host reads <code>plugin.json</code> before executing code. The file may be at most 64 KiB. Property names are case-insensitive; IDs and capability values are case-sensitive. <a href={source + 'src/Winnow.Plugins/PluginManifestReader.cs'}>Manifest validation source</a>.</p>
      <table><thead><tr><th>Field</th><th>Rules</th></tr></thead><tbody>
        <tr><td><code>id</code></td><td>Required, 1–64 characters; pattern <code>^[a-z0-9][a-z0-9.-]*$</code>. Keep it stable: it namespaces settings, cache and provider identity.</td></tr>
        <tr><td><code>name</code>, <code>version</code></td><td>Nonblank name, up to 100 characters. Version must parse as .NET <code>System.Version</code>, such as <code>1.0.0</code>; prerelease suffixes are not supported.</td></tr>
        <tr><td><code>apiVersion</code></td><td>Must equal <code>1</code> (<code>PluginApi.Version</code>).</td></tr>
        <tr><td><code>entryAssembly</code></td><td>DLL filename in the package root, at most 180 characters; no directory separators or colon.</td></tr>
        <tr><td><code>entryType</code></td><td>Fully qualified public, concrete class name, at most 256 characters. It must have a public parameterless constructor and implement <code>IPlugin</code> plus its declared capabilities.</td></tr>
        <tr><td><code>description</code>, <code>website</code></td><td>Optional description up to 2,000 characters. Website must be an absolute HTTPS URL without embedded credentials, up to 2,048 characters.</td></tr>
        <tr><td><code>capabilities</code></td><td>One to four distinct values from <code>library</code>, <code>metadata</code>, <code>artwork</code>, <code>recommendations</code>.</td></tr>
        <tr><td><code>settings</code></td><td>At most 32 <code>PluginSettingDefinition</code> entries; defaults to empty.</td></tr>
        <tr><td><code>network</code></td><td><code>PluginNetworkOptions</code>; defaults to no allowed hosts, 1 request/second, 2 retries, 2 MiB responses, 90-second timeout.</td></tr>
      </tbody></table>
      <p>Each setting has a unique <code>key</code> with the same syntax as the plugin ID, a nonblank <code>label</code> up to 120 characters, optional <code>help</code> up to 1,000 characters, optional HTTPS <code>setupUrl</code>, and boolean <code>secret</code> and <code>required</code> flags (both default false). Settings generate controls in both interfaces. Check missing credentials in your provider as well.</p>
      <pre><code>{`"settings": [
  { "key": "apikey", "label": "API key", "secret": true, "required": true,
    "help": "Create a read-only key for artwork requests.",
    "setupUrl": "https://api.example.com/account" }
],
"network": {
  "allowedHosts": ["api.example.com", "images.example.com"],
  "requestsPerSecond": 1,
  "maxRetries": 2,
  "maxResponseBytes": 2097152,
  "timeoutSeconds": 90
}`}</code></pre>
      <p>Allowed hosts are exact DNS names, at most 32, with no scheme, path, wildcard, trailing dot or IP literal. The valid request rate is 0.05–4 per second, retries 0–2, response bound 1 byte–32 MiB, and timeout 1–120 seconds.</p>
    </> },
    { id: 'contracts', title: 'Provider interfaces and data types', content: <>
      <p>All contracts live in <code>Winnow.PluginSdk</code>. Every method accepts an optional <code>CancellationToken</code>; pass it through to storage, HTTP and your own asynchronous operations. These are the complete provider signatures (default token arguments omitted for space).</p>
      <pre><code>{`IPlugin:
  ValueTask InitializeAsync(IPluginContext context, CancellationToken ct)
ILibrarySourcePlugin : IPlugin:
  Task<IReadOnlyList<PluginLibraryGame>?> GetLibraryAsync(CancellationToken ct)
IMetadataProviderPlugin : IPlugin:
  Task<PluginMetadata?> GetMetadataAsync(PluginGame game, CancellationToken ct)
IArtworkProviderPlugin : IPlugin:
  Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken ct)
IRecommendationFeedPlugin : IPlugin:
  Task<IReadOnlyList<PluginRecommendation>?> GetRecommendationsAsync(
    IReadOnlyList<PluginGame> library, CancellationToken ct)`}</code></pre>
      <table><thead><tr><th>Type</th><th>Constructor and properties</th></tr></thead><tbody>
        <tr><td><code>PluginGame</code></td><td>Constructor: <code>(string Id, string Title, IReadOnlyDictionary&lt;string, string&gt; ExternalIds)</code>. Init properties: <code>bool Installed</code>, <code>long PlaytimeMinutes</code>, <code>DateTimeOffset? LastPlayedAt</code>, <code>IReadOnlyList&lt;string&gt; Genres</code> and <code>Tags</code> (empty by default).</td></tr>
        <tr><td><code>PluginLibraryGame</code></td><td>Constructor: <code>(string SourceId, string Title)</code>. Init properties: <code>ExternalIds</code> (string dictionary, empty by default), <code>string? AccountRef</code>, <code>string? InstallPath</code>, <code>bool? Installed</code>, <code>long? PlaytimeMinutes</code>, <code>DateTimeOffset? LastPlayedAt</code>, <code>DateTimeOffset? AcquiredAt</code>.</td></tr>
        <tr><td><code>PluginMetadata</code></td><td>Parameterless record. Init properties: <code>string? Summary</code>, <code>DateTimeOffset? ReleaseDate</code>, <code>IReadOnlyList&lt;string&gt; Genres</code> and <code>Tags</code> (empty by default).</td></tr>
        <tr><td><code>PluginArtwork</code></td><td>Constructor: <code>(string Id, string Url, int Width, int Height)</code>. Init properties: <code>PluginArtworkKind Kind</code> (defaults to <code>Background</code>), <code>string? ImageType</code>, <code>bool Animated</code>, <code>bool Transparent</code>. Kind values: <code>Background</code>, <code>Cover</code>, <code>Screenshot</code>.</td></tr>
        <tr><td><code>PluginRecommendation</code></td><td>Constructor: <code>(string GameId, double Score, string Reason)</code>. Use a supplied handle, a finite score in [0, 1], and a nonblank explanation of at most 300 characters without control characters.</td></tr>
      </tbody></table>
      <p><a href={source + 'src/Winnow.PluginSdk/PluginContracts.cs'}>Read the exact C# declarations</a>. Records are source-attributed observations; the host controls application and presentation.</p>
    </> },
    { id: 'result-semantics', title: 'Identity, results and limits', content: <>
      <p><code>PluginGame.Id</code> is an opaque handle for the current request, not a persistent foreign key. Metadata and artwork handles refer to original works; feed handles refer to eligible owned entries. Use <code>ExternalIds</code> for external service lookups, and version any persistent cache format you own.</p>
      <h3>Library imports</h3><p>Keep <code>SourceId</code> stable within your plugin. Only known <code>steam</code>, <code>epic</code> and <code>gog</code> IDs can establish external joins, and all existing matches must agree. Titles never create automatic joins. Missing results never remove ownerships; playtime imports are lower-bound observations.</p>
      <p>The host considers the first 50,000 entries, deduplicates by SourceId, and skips invalid records. Source IDs must be nonblank, at most 256 characters and have no control characters. Titles must be nonblank and at most 500 characters; playtime cannot be negative. Only fully qualified installation paths are used. Up to 32 external IDs are considered; each accepted value is nonblank, at most 256 characters and has no control characters.</p>
      <h3>Metadata</h3><p>Summary and release year fill missing automatic fields; user overrides remain authoritative. Genre and tag assignments retain your source identity. Summary is bounded to 20,000 characters, release years to 1950–2200, and the first 100 names per genre/tag list are considered. Names must be nonblank, at most 100 characters and contain no control characters; they are trimmed and deduplicated.</p>
      <h3>Artwork</h3><p>Return static HTTPS assets on declared hosts, with accurate dimensions. The first 500 results are considered. Each dimension must be 1–8,192 pixels, total area at most 32 Mi pixels, and URL at most 2,048 characters. Animated assets are ignored; transparent covers are not selected. Duplicates use kind and URL; the host generates its own hashed cache identity instead of trusting an arbitrary remote ID. Image downloads have a separate 32 MiB bound.</p>
      <p>A <code>null</code> library or artwork result means unavailable and preserves previous observations. An empty list confirms no results. Empty artwork clears this provider’s backgrounds and screenshots, but does not clear an assigned cover. A nonempty artwork response containing only invalid candidates cannot erase previous valid observations. User-selected backgrounds take precedence over automatic artwork ordering.</p>
      <h3>Recommendations</h3><p>The supplied snapshot excludes suppressed games, hidden account entries, non-games, provisional titles and retired/derelict entries. Confirmed duplicates appear once. The host rejects unknown handles and invalid scores/reasons, sorts by score, deduplicates, and retains up to ten candidates. The shared shelf has six primary candidates and four reserves; desktop currently presents five cards. A null or empty response yields no shelf and does not reuse an older result.</p>
      <p>Optional shelves append to the built-in feed and cannot bring a suppressed game back. A shared five-second budget covers snapshot reads, queueing and provider execution across optional feeds. Return promptly, preferably from cache. Completed providers survive another provider’s timeout; obsolete results are discarded after navigation, a newer generation, or feedback changes.</p>
    </> },
    { id: 'host-services', title: 'Settings, secrets and cache', content: <>
      <p>Retain the <code>IPluginContext</code> passed to initialization. Its <code>PluginId</code>, <code>Settings</code>, <code>Secrets</code>, <code>Cache</code>, and <code>Http</code> properties are scoped to your manifest ID.</p>
      <pre><code>{`IPluginSettings:
  ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
  ValueTask SetAsync(string key, string? value, CancellationToken ct = default)
IPluginSecrets:
  ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
IPluginCache:
  ValueTask<PluginCacheEntry?> GetAsync(string key, CancellationToken ct = default)
  ValueTask SetAsync(string key, PluginCacheEntry entry, CancellationToken ct = default)

public sealed record PluginCacheEntry(byte[] Payload, DateTimeOffset ExpiresAt);`}</code></pre>
      <p>Settings access is limited to declared non-secret keys; values may be at most 4,096 characters. Secrets are read-only through the SDK and must use declared secret keys. Users edit them in Plugins settings. Re-read credentials when making requests so saved changes take effect without rebuilding your plugin.</p>
      <p>Windows persists credentials using current-user DPAPI with plugin/key-specific entropy. Other platforms refuse persisted secret writes. Environment configuration <code>Plugins__&lt;plugin-id&gt;__&lt;key&gt;</code> supplies a fallback when no usable stored secret exists. Do not store credentials in cache payloads or ordinary settings.</p>
      <p>Cache writes allow keys up to 512 characters and payloads up to 2 MiB. Expired entries remain readable: compare <code>ExpiresAt</code> yourself, attempt refresh, and use stale data only when appropriate for your provider. Use a versioned key such as <code>summary:v1:steam:123</code> or version the serialized payload. A null cache entry is a miss, not a negative provider result.</p>
      <p><a href={source + 'src/Winnow.PluginSdk/PluginContext.cs'}>Service contracts</a> · <a href={source + 'src/Winnow.App/Services/PluginStorage.cs'}>Storage implementation</a></p>
    </> },
    { id: 'http', title: 'HTTP requests and resilient providers', content: <>
      <pre><code>{`IPluginHttp:
  Task<PluginHttpResponse> SendAsync(PluginHttpRequest request,
      CancellationToken cancellationToken = default)

public sealed record PluginHttpRequest(string Url)
{
    public string Method { get; init; } = "GET";
    public IReadOnlyDictionary<string, string> Headers { get; init; }
        = new Dictionary<string, string>();
    public byte[]? Body { get; init; }
    public string? ContentType { get; init; }
}
public sealed record PluginHttpResponse(int StatusCode, byte[] Body,
    IReadOnlyDictionary<string, string> Headers);`}</code></pre>
      <p>Use <code>context.Http</code> for API calls. Requests require HTTPS on port 443 and an exact declared host, without URL credentials or fragments. Only uppercase <code>GET</code>, <code>POST</code> and <code>HEAD</code> are supported. Request bodies are limited to 2 MiB. Supply the content type through <code>ContentType</code>; Host, Proxy-Authorization, Connection, Transfer-Encoding and Content-Length headers are rejected.</p>
      <p>Redirects and cookies are disabled. Response limits apply to declared length and streamed bytes. Each plugin shares a rate budget across its requests, retries and artwork downloads. The host retries transport failures, HTTP 408, 429 and 5xx responses within the manifest budget, using exponential backoff with jitter and bounded Retry-After delays. Make retryable POST requests idempotent.</p>
      <p><code>SendAsync</code> returns the final HTTP status, including unsuccessful statuses. Check <code>StatusCode</code> before parsing. Handle authentication failures by returning unavailable data rather than caching a false empty result. Cache confirmed “not found” responses separately from transient errors, and pass cancellation through every await.</p>
      <pre><code>{`// Within an initialized provider; context is the retained IPluginContext.
var key = await context.Secrets.GetAsync("apikey", cancellationToken);
if (string.IsNullOrWhiteSpace(key)) return null;
var response = await context.Http.SendAsync(
    new PluginHttpRequest("https://api.example.com/games/123")
    {
        Headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + key
        }
    }, cancellationToken);
if (response.StatusCode != 200) return null;
// Deserialize response.Body, validate it, and return your capability's result.`}</code></pre>
      <p><a href={source + 'src/Winnow.Plugins/PluginHttpClient.cs'}>HTTP implementation</a> · <a href={source + 'plugins/Winnow.Plugin.SteamGridDb/SteamGridDbPlugin.cs'}>SteamGridDB reference provider</a></p>
    </> },
    { id: 'lifecycle', title: 'Lifecycle, failures and cleanup', content: <>
      <ol><li>Winnow validates packages and manifests, resolving bundled IDs before user packages. Third-party packages begin disabled.</li><li>On restart, enabled entry classes are constructed and initialized off the UI thread. Initialization has a 30-second deadline.</li><li>Background synchronization imports libraries, then enriches original owned works. Feed providers run alongside the built-in feed. Settings saves and Refresh queue new background work.</li><li>Calls to a given plugin are serialized by the host. Ordinary provider calls have a 120-second deadline; feed calls also face the shorter shared budget described above.</li><li>On shutdown, the host calls <code>IAsyncDisposable</code> if implemented, otherwise <code>IDisposable</code>, with a two-second cleanup wait.</li></ol>
      <p>An ordinary exception fails the operation and produces a fixed diagnostic without exception text, which could contain credentials. An invocation timeout disables the plugin for the session. Cancellation is forwarded; if work remains unfinished when the caller cancels, the host can also disable the instance. Restart to retry. Cancellation is cooperative: a hung native call or malicious code is not isolated by these deadlines.</p>
      <p>Keep constructors and initialization light. Do not spawn unbounded background work, hold UI references, or depend on a specific invocation thread. Enable/disable changes take effect after restart; there is no hot reload.</p>
    </> },
    { id: 'package-and-version', title: 'Package, install and version', content: <>
      <p>Keep the entry DLL, its <code>.deps.json</code>, <code>plugin.json</code> and private dependencies together. The host shares its SDK assembly and resolves dependencies within the plugin’s package, not from other plugin directories. For project references use <code>Private=false</code>; for NuGet references use <code>ExcludeAssets=runtime</code>.</p>
      <pre><code>{`example-installed/
  plugin.json
  Example.Installed.dll
  Example.Installed.deps.json
  ...private dependencies, if any`}</code></pre>
      <p>Distribute a ZIP with these files at its root or inside one enclosing folder. Users drop it into the data directory’s <code>plugins</code> folder and restart, then enable the package and restart again. Successful archives move to <code>plugins/.archives</code>. Installation rejects unsafe paths, links, conflicting filenames and invalid packages.</p>
      <p>Archive limits are 2,048 entries, 256 MiB compressed and unpacked, and paths of at most 1,024 characters and 32 components. Imports never overwrite an existing package or replace a bundled plugin. For updates, close Winnow and replace the package directory, or remove it before importing the replacement ZIP. Uninstall by closing Winnow and removing the directory. Imported facts and cached metadata remain.</p>
      <p>Keep the plugin ID stable when updating and increment your manifest version. API major <code>1</code> and SDK assembly major <code>1</code> are independent of the Winnow application version. Build against the supported SDK; a different API major or incompatible assembly major is rejected. There is no automatic plugin updater, package gallery or compatibility negotiation.</p>
    </> },
    { id: 'testing-and-troubleshooting', title: 'Test and troubleshoot', content: <>
      <p>Unit-test providers with a fake <code>IPluginContext</code>: supply deterministic settings, cache entries and HTTP responses, and call the capability methods directly. Test cancellation, empty and null results, credential changes, stale-cache fallback, malformed payloads and rate-limit responses. For feed tests, include unknown IDs and boundary scores; for library sources, keep stable SourceIds across refreshes.</p>
      <p>Then test the complete package through the real loader using a separate <code>--data-dir</code>. Verify enable/restart, refresh, disable/restart, both desktop and fullscreen, offline behavior, and a clean install. Never use your personal library for destructive or experimental imports. The source repository’s <a href="https://github.com/safwyls/winnow/tree/main/tests/Winnow.Plugins.Tests">plugin host tests</a> demonstrate real loader and network-boundary checks.</p>
      <table><thead><tr><th>Symptom</th><th>Check</th></tr></thead><tbody>
        <tr><td>Package not listed</td><td>Use Settings → Plugins → Open plugins folder for the active data directory. Ensure plugin.json is inside the package folder. Restart and inspect installation diagnostics.</td></tr>
        <tr><td>Could not start</td><td>Check entry filename/type, public parameterless constructor, declared interfaces, net10.0 target, compatible SDK and included private dependencies.</td></tr>
        <tr><td>Enabled but not running</td><td>Restart after toggling. The loaded list describes this session, not the pending enable state. Check for initialization failure or timeout.</td></tr>
        <tr><td>No shelf</td><td>Verify eligible matching entries, supplied GameIds, finite scores and nonblank reasons. Return within the shared five-second feed budget.</td></tr>
        <tr><td>Requests fail</td><td>Check exact declared host, port 443, disabled redirects, method spelling, credentials, status code and response bounds.</td></tr>
        <tr><td>Undeclared setting</td><td>Match the manifest key exactly and use Secrets for secret keys, Settings for non-secret keys.</td></tr>
        <tr><td>Old artwork stays</td><td>Null preserves prior results. A confirmed empty list clears only this provider’s backgrounds/screenshots. User choices and assigned covers have separate precedence.</td></tr>
        <tr><td>Replacement ZIP rejected</td><td>Close Winnow and replace/remove the old package directory first. Imports deliberately do not overwrite installed packages.</td></tr>
      </tbody></table>
      <p>For implementation detail, read the <a href={source + 'docs/plugins.md'}>repository plugin guide</a>, <a href={source + 'docs/plugin-system-walkthrough.md'}>plugin-system walkthrough</a>, and <a href="https://github.com/safwyls/winnow/tree/main/src/Winnow.PluginSdk">SDK source</a>. These contracts are the shared extension boundary for both Winnow interfaces.</p>
    </> },
  ],
};
