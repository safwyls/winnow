using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Winnow.App.Services;

internal sealed record ApplicationRelease(ReleaseVersion Version, string ReleaseUrl,
    string DownloadUrl, string AssetName, long Size, string? Sha256);

internal sealed class GitHubReleaseClient(HttpClient http)
{
    internal const string Repository = "safwyls/winnow";
    internal const long MaximumPackageBytes = 2L * 1024 * 1024 * 1024;

    public async Task<ApplicationRelease?> FindAsync(ReleaseVersion current, bool beta, string suffix, CancellationToken ct)
    {
        ApplicationRelease? best = null;
        for (var page = 1; page <= 10; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
            request.Headers.UserAgent.ParseAdd("Winnow-Updater/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, ct).ConfigureAwait(false);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid release list.");
            foreach (var release in json.RootElement.EnumerateArray())
            {
                var candidate = Select(release, current, beta, suffix);
                if (candidate is not null && (best is null || candidate.Version.CompareTo(best.Version) > 0)) best = candidate;
            }
            if (json.RootElement.GetArrayLength() < 100) return best;
        }
        throw new InvalidDataException("The release list is too large to check safely.");
    }

    internal static ApplicationRelease? Select(JsonElement release, ReleaseVersion current, bool beta, string suffix)
    {
        if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
            || !release.TryGetProperty("prerelease", out var prerelease)
            || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        var tag = String(release, "tag_name");
        var version = tag?.StartsWith('v') == true ? ReleaseVersion.Parse(tag[1..]) : null;
        if (version is null || version.IsDevelopment || version.CompareTo(current) <= 0
            || (!beta && (prerelease.GetBoolean() || version.Pre.Length != 0))) return null;
        var name = $"Winnow-{version.Text}-{suffix}";
        var releaseUrl = $"https://github.com/{Repository}/releases/tag/{tag}";
        var assetUrl = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        // A known newer tag without a usable package is still an available release.
        // Its release page explains the missing asset; it must not look like "up to date".
        var unavailable = new ApplicationRelease(version, releaseUrl, releaseUrl, "", 0, null);
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return unavailable;
        var matches = assets.EnumerateArray().Where(a => String(a, "name") == name).ToArray();
        if (matches.Length != 1) return unavailable;
        var asset = matches[0];
        if (String(asset, "browser_download_url") != assetUrl || String(asset, "state") != "uploaded"
            || !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes)
            || bytes <= 0 || bytes > MaximumPackageBytes) return unavailable;
        var digest = String(asset, "digest");
        var hash = digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? digest[7..] : null;
        if (hash?.Length != 64 || !hash.All(char.IsAsciiHexDigit)) hash = null;
        return new(version, releaseUrl, assetUrl, name, bytes, hash);
    }

    public async Task DownloadAsync(ApplicationRelease release, string path, Action<double> progress, CancellationToken ct)
    {
        if (release.Sha256 is null) throw new InvalidDataException("GitHub did not provide a SHA-256 digest for this installer.");
        var uri = new Uri(release.DownloadUrl);
        HttpResponseMessage? response = null;
        try
        {
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                    || uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                    throw new InvalidDataException("The download left GitHub's release storage.");
                response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) break;
                var location = response.Headers.Location ?? throw new InvalidDataException("Missing redirect location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                response = null;
            }
            if (response is null) throw new InvalidDataException("Too many download redirects.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                throw new InvalidDataException("The installer size changed.");
            await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > release.Size) throw new InvalidDataException("The installer is larger than expected.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress(total * 100d / release.Size);
            }
            if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer failed SHA-256 verification. Try downloading again.");
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { response?.Dispose(); }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
