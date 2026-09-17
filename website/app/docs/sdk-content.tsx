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
  description: 'A working first provider, the SDK 1.1 contracts for API 1, and the rules for shipping a reliable package.',
  sections: [
    { id: 'choose-a-capability', title: 'Choose what your plugin adds', content: <>
      <p>The SDK is a .NET 10 library with no UI or database dependency. SDK 1.1 supports six capabilities under API major 1. A single entry class may implement several; declare each one in its manifest.</p>
      <table><thead><tr><th>Capability</th><th>Interface</th><th>Use it for</th></tr></thead><tbody>
        <tr><td><code>library</code></td><td><code>ILibrarySourcePlugin</code></td><td>Ownership, installation and playtime observations.</td></tr>
        <tr><td><code>metadata</code></td><td><code>IMetadataProviderPlugin</code></td><td>Descriptions, release dates, genres and tags.</td></tr>
        <tr><td><code>artwork</code></td><td><code>IArtworkProviderPlugin</code></td><td>Static covers, hero backgrounds and screenshots.</td></tr>
        <tr><td><code>recommendations</code></td><td><code>IRecommendationFeedPlugin</code></td><td>A named feed shelf with scores and explanations.</td></tr>
        <tr><td><code>account</code></td><td><code>IPluginAccount</code></td><td>Device-code sign-in, connection status and sign-out through shared controls.</td></tr>
        <tr><td><code>game-actions</code></td><td><code>IPluginGameActions</code></td><td>Play or open the store page for an imported game.</td></tr>
      </tbody></table>
      <p>Winnow renders the results and account controls in desktop and fullscreen using its own components. Providers resolve game actions from their stable source IDs; they do not give the host command text to execute. API 1 does not expose custom screens, game-installation actions or UI replacement.</p>
      <p>Plugins run as trusted code inside Winnow. Host services restrict their own calls, but cannot prevent a DLL from calling filesystem or network APIs directly. Install code only from authors you trust; dependency isolation is not a security sandbox.</p>
    </> },
    { id: 'first-plugin', title: 'Tutorial: your first feed provider', content: <>
      <p>This example adds an “Installed and unplayed” shelf using only the supplied library snapshot. It needs no credential or network connection. Install the .NET 10 SDK and clone the <a href="https://github.com/safwyls/winnow">Winnow repository</a>, then run these commands from its root.</p>
      <pre><code>{`dotnet pack src/Winnow.PluginSdk -c Release -o artifacts/plugin-sdk
dotnet new classlib -n Example.Installed -f net10.0 -o examples/Example.Installed`}</code></pre>
      <p>Replace the generated project file with the following. The local SDK package is version 1.1.0; these instructions do not assume a published NuGet feed. Delete the generated <code>Class1.cs</code>.</p>
      <pre><code>{`<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Winnow.PluginSdk" Version="1.1.0"
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
dotnet run --project src/Winnow.App -- --data-dir $demoData --seed-sample`}</code></pre>
      <p>Close the seeded session, then run the command below. Both <code>--seed-sample</code> and <code>--no-sync</code> suppress plugin discovery, so omit them when testing a provider. Normal startup can also import local launcher data into this throwaway library.</p>
      <pre><code>{`dotnet run --project src/Winnow.App -- --data-dir $demoData`}</code></pre>
      <p>In either interface, open Settings → Plugins, enable the example, close Winnow, and rerun this normal-startup command with the same <code>$demoData</code>. Open the feed. The optional shelf appears only when the current eligible snapshot contains matching games; an empty result is expected otherwise. The plugin adds recommendations, never ownerships.</p>
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
        <tr><td><code>capabilities</code></td><td>One to six distinct values from <code>library</code>, <code>metadata</code>, <code>artwork</code>, <code>recommendations</code>, <code>account</code>, <code>game-actions</code>.</td></tr>
        <tr><td><code>settings</code></td><td>At most 32 <code>PluginSettingDefinition</code> entries; defaults to empty.</td></tr>
        <tr><td><code>network</code></td><td><code>PluginNetworkOptions</code>; defaults to no allowed hosts, 1 request/second, 2 retries, 2 MiB responses, 90-second timeout.</td></tr>
      </tbody></table>
      <p>Each setting has a unique <code>key</code> with the same syntax as the plugin ID, a nonblank <code>label</code> up to 120 characters, optional <code>help</code> up to 1,000 characters, and optional HTTPS <code>setupUrl</code>. The flags below default to false. Check missing credentials in your provider as well.</p>
      <table><thead><tr><th>Setting flag</th><th>Host behavior</th></tr></thead><tbody>
        <tr><td><code>secret</code></td><td>Use protected credential storage and a masked editor. Saved values never appear in settings snapshots.</td></tr>
        <tr><td><code>required</code></td><td>The editor requires a value when saving; providers still handle unavailable credentials.</td></tr>
        <tr><td><code>isBoolean</code></td><td>Render a toggle. Cannot be combined with <code>secret</code>.</td></tr>
        <tr><td><code>isAdvanced</code></td><td>Place the editable field under Show advanced settings on desktop and fullscreen. Collapsing it preserves its value when saving.</td></tr>
        <tr><td><code>managedByPlugin</code></td><td>Hide a credential maintained by the provider, such as a refresh token, from the editors. Requires <code>secret: true</code>.</td></tr>
      </tbody></table>
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
    IReadOnlyList<PluginGame> library, CancellationToken ct)
IPluginAccount : IPlugin:
  Task<PluginAccountStatus> GetAccountStatusAsync(CancellationToken ct)
  Task<PluginSignInChallenge?> BeginSignInAsync(CancellationToken ct)
  Task<PluginSignInResult> PollSignInAsync(string attemptId, CancellationToken ct)
  Task SignOutAsync(CancellationToken ct)
  Task CancelSignInAsync(string attemptId, CancellationToken ct)
IPluginGameActions : IPlugin:
  Task<PluginGameActionResult> ExecuteGameActionAsync(string sourceId,
    PluginGameActionKind action, CancellationToken ct)`}</code></pre>
      <table><thead><tr><th>Type</th><th>Constructor and properties</th></tr></thead><tbody>
        <tr><td><code>PluginGame</code></td><td>Constructor: <code>(string Id, string Title, IReadOnlyDictionary&lt;string, string&gt; ExternalIds)</code>. Init properties: <code>bool Installed</code>, <code>long PlaytimeMinutes</code>, <code>DateTimeOffset? LastPlayedAt</code>, <code>IReadOnlyList&lt;string&gt; Genres</code> and <code>Tags</code> (empty by default).</td></tr>
        <tr><td><code>PluginLibraryGame</code></td><td>Constructor: <code>(string SourceId, string Title)</code>. Init properties: <code>bool TitleIsProvisional</code>, <code>ExternalIds</code> (string dictionary, empty by default), <code>string? AccountRef</code>, <code>string? InstallPath</code>, <code>bool? Installed</code>, <code>long? PlaytimeMinutes</code>, <code>DateTimeOffset? LastPlayedAt</code>, <code>DateTimeOffset? AcquiredAt</code>, <code>string? LibrarySourceLabel</code>, <code>IReadOnlyList&lt;PluginGameActionKind&gt; Actions</code> (empty by default).</td></tr>
        <tr><td><code>PluginMetadata</code></td><td>Parameterless record. Init properties: <code>string? Summary</code>, <code>DateTimeOffset? ReleaseDate</code>, <code>IReadOnlyList&lt;string&gt; Genres</code> and <code>Tags</code> (empty by default).</td></tr>
        <tr><td><code>PluginArtwork</code></td><td>Constructor: <code>(string Id, string Url, int Width, int Height)</code>. Init properties: <code>PluginArtworkKind Kind</code> (defaults to <code>Background</code>), <code>string? ImageType</code>, <code>bool Animated</code>, <code>bool Transparent</code>. Kind values: <code>Background</code>, <code>Cover</code>, <code>Screenshot</code>.</td></tr>
        <tr><td><code>PluginRecommendation</code></td><td>Constructor: <code>(string GameId, double Score, string Reason)</code>. Use a supplied handle, a finite score in [0, 1], and a nonblank explanation of at most 300 characters without control characters.</td></tr>
        <tr><td><code>PluginAccountStatus</code></td><td>Constructor: <code>(bool Connected, string Message)</code>. The shared UI uses its own status copy based on the connection state.</td></tr>
        <tr><td><code>PluginSignInChallenge</code></td><td>Constructor: <code>(string AttemptId, string VerificationUrl, string UserCode, DateTimeOffset ExpiresAt, int PollIntervalSeconds)</code>. Only the verification address and user code are shown; the attempt ID stays opaque.</td></tr>
        <tr><td><code>PluginSignInResult</code></td><td>Constructor: <code>(PluginSignInState State, string Message)</code>. States: <code>Pending</code>, <code>SlowDown</code>, <code>Connected</code>, <code>Failed</code>. The host uses fixed feedback instead of displaying the provider’s message.</td></tr>
        <tr><td><code>PluginGameActionResult</code></td><td>Constructor: <code>(bool HandedOff)</code>. True means the provider handed the action to its target, not that the game started successfully. <code>PluginGameActionKind</code> values: <code>Play</code>, <code>OpenStore</code>.</td></tr>
      </tbody></table>
      <p>Read the exact <a href={source + 'src/Winnow.PluginSdk/PluginContracts.cs'}>data declarations</a> and <a href={source + 'src/Winnow.PluginSdk/PluginInteractionContracts.cs'}>account and game-action declarations</a>. Records are source-attributed observations; the host controls application and presentation.</p>
    </> },
    { id: 'result-semantics', title: 'Identity, results and limits', content: <>
      <p><code>PluginGame.Id</code> is an opaque handle for the current request, not a persistent foreign key. Metadata and artwork handles refer to original works; feed handles refer to eligible owned entries. Use <code>ExternalIds</code> for external service lookups, and version any persistent cache format you own.</p>
      <h3>Library imports</h3><p>Keep <code>SourceId</code> stable within your plugin. Only known <code>steam</code>, <code>epic</code> and <code>gog</code> IDs can establish external joins, and all existing matches must agree. Titles never create automatic joins. Missing results never remove ownerships; playtime imports are lower-bound observations.</p>
      <p>Set <code>TitleIsProvisional</code> when the title is only an unresolved identifier; a later import can replace that placeholder. Use <code>LibrarySourceLabel</code> to explain inclusion, such as installed games or played history, without claiming a purchase. Labels are limited to 250 characters without control characters and appear in both detail views. Advertise supported <code>Actions</code> separately.</p>
      <p>Imports publish before optional metadata/artwork and queue merge suggestions across stores. Confirmed and rejected merge decisions remain intact. Desktop Merges and fullscreen Library tools → Possible identity matches also offer Refresh suggestions; this local pass does not fetch another inventory or accept suggestions.</p>
      <p>The host considers the first 50,000 entries, deduplicates by SourceId, and skips invalid records. Source IDs must be nonblank, at most 256 characters and have no control characters. Titles must be nonblank and at most 500 characters; playtime cannot be negative. Only fully qualified installation paths are used. Up to 32 external IDs are considered; each accepted value is nonblank, at most 256 characters and has no control characters.</p>
      <h3>Metadata</h3><p>Summary and release year fill missing automatic fields; user overrides remain authoritative. Genre and tag assignments retain your source identity. Summary is bounded to 20,000 characters, release years to 1950–2200, and the first 100 names per genre/tag list are considered. Names must be nonblank, at most 100 characters and contain no control characters; they are trimmed and deduplicated.</p>
      <h3>Artwork</h3><p>Return static HTTPS assets on declared hosts, with accurate dimensions. The first 500 results are considered. Each dimension must be 1–8,192 pixels, total area at most 32 Mi pixels, and URL at most 2,048 characters. Animated assets are ignored; transparent covers are not selected. Duplicates use kind and URL; the host generates its own hashed cache identity instead of trusting an arbitrary remote ID. Image downloads have a separate 32 MiB bound.</p>
      <p>A <code>null</code> library or artwork result means unavailable and preserves previous observations. An empty list confirms no results. Empty artwork clears this provider’s backgrounds and screenshots, but does not clear an assigned cover. A nonempty artwork response containing only invalid candidates cannot erase previous valid observations. User-selected backgrounds take precedence over automatic artwork ordering.</p>
      <h3>Recommendations</h3><p>The supplied snapshot excludes suppressed games, hidden account entries, non-games, provisional titles and retired/derelict entries. Confirmed duplicates appear once. The host rejects unknown handles and invalid scores/reasons, sorts by score, deduplicates, and retains up to ten candidates. The shared shelf has six primary candidates and four reserves; desktop currently presents five cards. A null or empty response yields no shelf and does not reuse an older result.</p>
      <p>Optional shelves append to the built-in feed and cannot bring a suppressed game back. A shared five-second budget covers snapshot reads, queueing and provider execution across optional feeds. Return promptly, preferably from cache. Completed providers survive another provider’s timeout; obsolete results are discarded after navigation, a newer generation, or feedback changes.</p>
    </> },
    { id: 'accounts-and-actions', title: 'Account connection and game actions', content: <>
      <h3>Account connection</h3>
      <p>Declare <code>account</code> and implement <code>IPluginAccount</code> to use the shared Sign in, Cancel sign-in and Sign out controls on desktop and fullscreen. Keep each method short: the host owns the wait between polling calls, and every call still has the provider deadline.</p>
      <p><code>BeginSignInAsync</code> returns a challenge or null when unavailable. Keep device codes and access tokens inside the provider; return an opaque attempt ID containing no credential. The host shows only the user code and a verification address whose HTTPS host is declared in <code>network.allowedHosts</code>. URLs require the default HTTPS port and no embedded credentials.</p>
      <p>Attempt IDs are 1–256 characters without control characters. User codes are 1–64 ASCII letters, digits, spaces or hyphens and must include a letter or digit. Challenges expire within one hour, with an initial polling interval of 1–60 seconds. <code>SlowDown</code> adds five seconds to the interval, capped at 300 seconds. The host stops on connection, failure, expiry or cancellation and asks the provider to cancel unsuccessful attempts.</p>
      <p>Persist refresh credentials through declared <code>managedByPlugin</code> secrets before reporting Connected. Remove them on sign-out and invalidate the account’s in-memory state and caches. Connection and sign-out queue a refresh; previously imported games remain. Provider messages are not a route for custom UI copy: the host displays fixed status text.</p>
      <p>The <a href={source + 'plugins/Winnow.Plugin.Xbox/README.md'}>Xbox provider</a> demonstrates this flow. PlayStation uses a user-entered NPSSO secret with Save and Remove saved secret controls because its connection does not follow this device-code contract.</p>
      <h3>Game actions</h3>
      <p>Declare <code>game-actions</code>, implement <code>IPluginGameActions</code>, and list <code>Play</code> or <code>OpenStore</code> in each imported game’s <code>Actions</code>. Play also requires an installed entry. Both UI surfaces use the same advertised actions and an active provider.</p>
      <p>Before dispatch, Winnow rechecks the local ownership, provider, source ID, advertised action and installation state where needed. The provider resolves that known source ID to a current target and returns <code>HandedOff</code>. Do not trust cached paths or turn a source ID into arbitrary command text. Console-only entries can omit actions while retaining library history and metadata.</p>
      <p><a href={source + 'src/Winnow.App/Services/PluginSettingsBackend.cs'}>Account adapter</a> · <a href={source + 'src/Winnow.App/Services/PluginGameActionService.cs'}>Action validation and dispatch</a></p>
    </> },
    { id: 'host-services', title: 'Settings, secrets and cache', content: <>
      <p>Retain the <code>IPluginContext</code> passed to initialization. Its <code>PluginId</code>, <code>Settings</code>, <code>Secrets</code>, <code>Cache</code>, and <code>Http</code> properties are scoped to your manifest ID.</p>
      <pre><code>{`IPluginSettings:
  ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
  ValueTask SetAsync(string key, string? value, CancellationToken ct = default)
IPluginSecrets:
  ValueTask<string?> GetAsync(string key, CancellationToken ct = default)
  ValueTask SetAsync(string key, string value, CancellationToken ct = default)
  ValueTask RemoveAsync(string key, CancellationToken ct = default)
IPluginCache:
  ValueTask<PluginCacheEntry?> GetAsync(string key, CancellationToken ct = default)
  ValueTask SetAsync(string key, PluginCacheEntry entry, CancellationToken ct = default)

public sealed record PluginCacheEntry(byte[] Payload, DateTimeOffset ExpiresAt);`}</code></pre>
      <p>Settings access is limited to declared non-secret keys; values may be at most 4,096 characters. SDK 1.1 adds secret writes and removal for declared secret keys. Secret values are limited to 4,096 characters without control characters. Re-read credentials when making requests so saved changes take effect without rebuilding your plugin. Writing through the storage API does not itself request a library refresh.</p>
      <p>User-editable secrets appear as masked fields. Declare <code>secret: true, managedByPlugin: true</code> for refresh credentials maintained by your plugin; these stay out of the editors and read only protected stored values. Windows persists credentials using current-user DPAPI with plugin/key-specific entropy. Other platforms refuse persisted secret writes; the SDK’s default write/remove implementations also throw <code>NotSupportedException</code> for hosts or test contexts that have not implemented them.</p>
      <p>For user-editable secrets, environment configuration <code>Plugins__&lt;plugin-id&gt;__&lt;key&gt;</code> supplies a fallback when no usable stored secret exists. Removing the saved value does not remove that configuration. Managed secrets have no configuration fallback, so signing out cannot resurrect a configured refresh token. Never put device codes or tokens in ordinary settings, caches, logs or account messages.</p>
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
      <ol><li>Winnow validates packages and manifests, resolving bundled IDs before user packages. Manually added packages begin disabled unless a saved preference enables them.</li><li>Enabled entry classes are constructed and initialized off the UI thread. Initialization has a 30-second deadline. New first-party packages installed from the website can load immediately; changes to existing packages require restart.</li><li>Library imports start after discovery, publish committed data, and queue merge suggestions and separate metadata/artwork work. They do not wait for other stores’ enrichment. Feed providers run alongside the built-in feed. Settings saves, account connection changes and Refresh queue new background work.</li><li>Calls to a given plugin are serialized by the host. Ordinary provider calls have a 120-second deadline; feed calls also face the shorter shared budget described above.</li><li>On shutdown, pending installs are cancelled and drained before catalogue disposal. The host calls <code>IAsyncDisposable</code> if implemented, otherwise <code>IDisposable</code>, with a two-second cleanup wait. An initializer that completes after cancellation cannot become active.</li></ol>
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
      <p>The <a href={sitePath('/plugins/')}>official plugins catalogue</a> offers ZIPs and Install in Winnow for Xbox, PlayStation and SteamGridDB. Release CI builds these packages with a <code>winnow-plugins.json</code> catalogue and checksums. Links select a known plugin ID and exact release tag: <code>winnow://plugins/install?id=psn&amp;release=v0.2.0</code>. The app verifies the official release, hashes, sizes and package identity before installing and enabling a new provider. Existing packages and their enabled state are preserved. This download service accepts only first-party IDs; distribute third-party packages through the ZIP flow above.</p>
      <p>Keep the plugin ID stable when updating and increment your manifest version. SDK package version <code>1.1.0</code>, API major <code>1</code> and SDK assembly version <code>1.0.0.0</code> are separate from the Winnow application version and your plugin’s manifest version. Using SDK 1.1 features requires a host that supplies them; the unchanged API major alone does not guarantee support. The official release catalogue can declare <code>minimumSdkVersion</code>, which the website installer checks; it is not a field of the SDK’s <code>PluginManifest</code>. Automatic plugin updates and general compatibility negotiation are not supported.</p>
    </> },
    { id: 'testing-and-troubleshooting', title: 'Test and troubleshoot', content: <>
      <p>Unit-test providers with a fake <code>IPluginContext</code>: supply deterministic settings, cache entries and HTTP responses, and call the capability methods directly. Test cancellation, empty and null results, credential changes, stale-cache fallback, malformed payloads and rate-limit responses. For feed tests, include unknown IDs and boundary scores; for library sources, keep stable SourceIds across refreshes.</p>
      <p>Then test the complete package through the real loader using a separate <code>--data-dir</code>. Verify enable/restart, refresh, disable/restart, both desktop and fullscreen, offline behavior, and a clean install. Never use your personal library for destructive or experimental imports. The source repository’s <a href="https://github.com/safwyls/winnow/tree/main/tests/Winnow.Plugins.Tests">plugin host tests</a> demonstrate real loader and network-boundary checks.</p>
      <table><thead><tr><th>Symptom</th><th>Check</th></tr></thead><tbody>
        <tr><td>Package not listed</td><td>Use Settings → Plugins → Open plugins folder for the active data directory. Ensure plugin.json is inside the package folder. Restart without --seed-sample or --no-sync, which suppress discovery, and inspect installation diagnostics.</td></tr>
        <tr><td>Could not start</td><td>Check entry filename/type, public parameterless constructor, declared interfaces, net10.0 target, compatible SDK and included private dependencies.</td></tr>
        <tr><td>Enabled but not running</td><td>Restart after toggling. The loaded list describes this session, not the pending enable state. Check for initialization failure or timeout.</td></tr>
        <tr><td>No shelf</td><td>Verify eligible matching entries, supplied GameIds, finite scores and nonblank reasons. Return within the shared five-second feed budget.</td></tr>
        <tr><td>Requests fail</td><td>Check exact declared host, port 443, disabled redirects, method spelling, credentials, status code and response bounds.</td></tr>
        <tr><td>Undeclared setting</td><td>Match the manifest key exactly and use Secrets for secret keys, Settings for non-secret keys.</td></tr>
        <tr><td>Secret write fails</td><td>Use a host with protected credential writes, or implement them explicitly in the test context. Do not fall back to plaintext storage. Managed credentials cannot come from environment configuration.</td></tr>
        <tr><td>Sign-in challenge rejected</td><td>Check the verification host, code format, opaque attempt ID, polling interval and expiry described above. Keep provider credentials out of the challenge.</td></tr>
        <tr><td>Old artwork stays</td><td>Null preserves prior results. A confirmed empty list clears only this provider’s backgrounds/screenshots. User choices and assigned covers have separate precedence.</td></tr>
        <tr><td>Replacement ZIP rejected</td><td>Close Winnow and replace/remove the old package directory first. Imports deliberately do not overwrite installed packages.</td></tr>
      </tbody></table>
      <p>For implementation detail, read the <a href={source + 'docs/plugins.md'}>repository plugin guide</a>, <a href={source + 'docs/plugin-system-walkthrough.md'}>plugin-system walkthrough</a>, and <a href="https://github.com/safwyls/winnow/tree/main/src/Winnow.PluginSdk">SDK source</a>. These contracts are the shared extension boundary for both Winnow interfaces.</p>
    </> },
  ],
};
