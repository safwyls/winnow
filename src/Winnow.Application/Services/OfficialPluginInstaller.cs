using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Winnow.Plugins;
using Winnow.PluginSdk;

namespace Winnow.App.Services;

/// <summary>Installs packages selected from the catalogue of an exact official GitHub release.</summary>
public sealed class OfficialPluginInstaller(HttpClient http, PluginCatalog catalog, IPluginSettingsBackend settings,
    ArtworkPreferences? artworkPreferences = null, CancellationToken applicationStopping = default) : IOfficialPluginInstaller, IDisposable
{
    private const string Repository = "safwyls/winnow";
    private const string CatalogueName = "winnow-plugins.json";
    private const int MaximumJsonBytes = 1024 * 1024;
    private readonly SemaphoreSlim _installation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(applicationStopping);

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(120),
    };

    public async Task<PluginInstallResult> InstallAsync(PluginInstallRequest request,
        IProgress<PluginInstallProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IsValid) return Failed(request, "This plugin install link is invalid.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        await _installation.WaitAsync(token).ConfigureAwait(false);
        string? download = null;
        try
        {
            progress?.Report(new("Preparing plugin installation…"));
            await catalog.DiscoveryReady.WaitAsync(token).ConfigureAwait(false);
            var existing = catalog.Plugins.SingleOrDefault(plugin => plugin.Manifest.Id == request.PluginId);
            if (existing is not null)
                return new(PluginInstallOutcome.AlreadyInstalled, request.PluginId,
                    existing.Loaded ? "This plugin is already installed. Its settings are ready."
                        : "This plugin is already installed. Review its settings and restart Winnow if needed.");

            progress?.Report(new("Checking the official release…"));
            using var release = await ReadReleaseAsync(request.ReleaseTag, token).ConfigureAwait(false);
            var catalogueAsset = SelectAsset(release.RootElement, request.ReleaseTag, CatalogueName, MaximumJsonBytes);
            using var catalogueBytes = new MemoryStream();
            await DownloadAsync(catalogueAsset, catalogueBytes, token).ConfigureAwait(false);
            using var catalogue = JsonDocument.Parse(catalogueBytes.ToArray());
            var package = ReadPackage(catalogue.RootElement, request);
            var asset = SelectAsset(release.RootElement, request.ReleaseTag, package.AssetName, PluginArchiveInstaller.MaximumArchiveBytes);
            Require(asset.Size == package.Size && asset.Sha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase));

            var root = Path.GetFullPath(settings.UserPluginDirectory);
            Directory.CreateDirectory(root);
            Require((File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0);
            // An interrupted transfer is not a ZIP and therefore cannot be picked up by startup discovery.
            download = Path.Combine(root, ".download-" + Guid.NewGuid().ToString("N") + ".partial");
            progress?.Report(new("Downloading the plugin…"));
            await using (var output = new FileStream(download, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await DownloadAsync(asset, output, token).ConfigureAwait(false);

            progress?.Report(new("Verifying and installing the plugin…"));
            var installed = await catalog.InstallVerifiedArchiveAsync(download, root, manifest =>
            {
                Require(manifest.Id == request.PluginId && manifest.Version == package.Version
                    && manifest.ApiVersion == package.ApiVersion);
            }, token).ConfigureAwait(false);
            if (installed is null) return Failed(request, "The plugin package could not be installed. Check plugin settings for details.");
            artworkPreferences?.ConfigureSources(catalog.GetActive<IArtworkProviderPlugin>()
                .Select(plugin => new ArtworkSourceOption("plugin:" + plugin.Manifest.Id, plugin.Manifest.Name)));
            return new(PluginInstallOutcome.Installed, request.PluginId, installed.Loaded
                ? "Plugin installed and enabled. Open its settings to finish setup."
                : "Plugin installed, but could not start. Open its settings for details.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || _lifetime.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return Failed(request, "Plugin installation timed out. Check your connection and try again.");
        }
        catch (InvalidDataException)
        {
            return Failed(request, "This release's plugin package could not be verified or is incompatible with this Winnow version.");
        }
        catch (Exception)
        {
            // Response bodies, filesystem paths and exception messages never enter browser-facing feedback.
            return Failed(request, "The plugin could not be installed. Check your connection and try again.");
        }
        finally
        {
            if (download is not null)
            {
                try { File.Delete(download); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
            _installation.Release();
        }
    }

    private async Task<JsonDocument> ReadReleaseAsync(string tag, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/tags/{tag}");
        request.Headers.UserAgent.ParseAdd("Winnow-Plugins/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(MaximumJsonBytes, ct).ConfigureAwait(false);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        try
        {
            Require(json.RootElement.ValueKind == JsonValueKind.Object
                && String(json.RootElement, "tag_name") == tag
                && json.RootElement.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.False);
            return json;
        }
        catch { json.Dispose(); throw; }
    }

    private static Asset SelectAsset(JsonElement release, string tag, string name, long maximumBytes)
    {
        Require(release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array);
        var matches = assets.EnumerateArray().Where(asset => String(asset, "name") == name).ToArray();
        Require(matches.Length == 1);
        var selected = matches[0];
        var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        var digest = String(selected, "digest");
        Require(String(selected, "browser_download_url") == expectedUrl && String(selected, "state") == "uploaded"
            && selected.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) && bytes > 0 && bytes <= maximumBytes
            && digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal)
            && digest[7..].All(char.IsAsciiHexDigit));
        return new(new Uri(expectedUrl), selected.GetProperty("size").GetInt64(), digest![7..]);
    }

    private static Package ReadPackage(JsonElement catalogue, PluginInstallRequest request)
    {
        Require(catalogue.ValueKind == JsonValueKind.Object
            && catalogue.TryGetProperty("schemaVersion", out var schema) && schema.TryGetInt32(out var schemaVersion) && schemaVersion == 1
            && String(catalogue, "releaseTag") == request.ReleaseTag && String(catalogue, "appVersion") == request.ReleaseTag[1..]
            && catalogue.TryGetProperty("plugins", out var plugins) && plugins.ValueKind == JsonValueKind.Array
            && plugins.GetArrayLength() is > 0 and <= 3);
        var entries = catalogue.GetProperty("plugins").EnumerateArray().ToArray();
        Require(entries.All(entry => PluginInstallRequest.IsOfficialId(String(entry, "id")))
            && entries.Select(entry => String(entry, "id")).Distinct(StringComparer.Ordinal).Count() == entries.Length);
        var matches = entries.Where(entry => String(entry, "id") == request.PluginId).ToArray();
        Require(matches.Length == 1);
        var package = matches[0];
        var version = String(package, "version");
        var sha256 = String(package, "sha256");
        Require(version is { Length: > 0 and <= 40 } && Version.TryParse(version, out _)
            && version.All(c => char.IsAsciiDigit(c) || c == '.')
            && package.TryGetProperty("apiVersion", out var api) && api.TryGetInt32(out var apiVersion) && apiVersion == PluginApi.Version
            && sha256 is { Length: 64 } && sha256.All(char.IsAsciiHexDigit)
            && package.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes)
            && bytes > 0 && bytes <= PluginArchiveInstaller.MaximumArchiveBytes);
        if (package.TryGetProperty("minimumSdkVersion", out var minimumSdk))
        {
            var current = typeof(IPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
            Require(minimumSdk.ValueKind == JsonValueKind.String && Version.TryParse(minimumSdk.GetString(), out var minimum)
                && Version.TryParse(current, out var supported) && minimum <= supported);
        }
        var stem = request.PluginId switch { "psn" => "Psn", "xbox" => "Xbox", _ => "SteamGridDb" };
        var assetName = $"Winnow.Plugin.{stem}-{version}.zip";
        Require(String(package, "assetName") == assetName);
        return new(assetName, version!, PluginApi.Version, package.GetProperty("size").GetInt64(), sha256!);
    }

    private async Task DownloadAsync(Asset asset, Stream output, CancellationToken ct)
    {
        var uri = asset.Uri;
        HttpResponseMessage? response = null;
        try
        {
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                Require(uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
                    && (uri == asset.Uri || uri.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com"));
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("Winnow-Plugins/1.0");
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) break;
                var location = response.Headers.Location ?? throw new InvalidDataException();
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                response = null;
            }
            Require(response is not null);
            response.EnsureSuccessStatusCode();
            Require(response.Content.Headers.ContentLength is not { } length || length == asset.Size);
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                total += read;
                Require(total <= asset.Size);
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            Require(total == asset.Size && Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase));
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { response?.Dispose(); }
    }

    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition)
    {
        if (!condition) throw new InvalidDataException();
    }
    private static PluginInstallResult Failed(PluginInstallRequest request, string message) => new(PluginInstallOutcome.Failed, request.PluginId, message);
    private sealed record Asset(Uri Uri, long Size, string Sha256);
    private sealed record Package(string AssetName, string Version, int ApiVersion, long Size, string Sha256);
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifetime.CancelAsync().ConfigureAwait(false);
        await catalog.StopAsync(ct).ConfigureAwait(false);
        await _installation.WaitAsync(ct).ConfigureAwait(false);
        _installation.Release();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        http.Dispose();
    }
}
