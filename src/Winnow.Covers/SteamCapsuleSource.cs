using System.Net;
using Microsoft.Extensions.Logging;

namespace Winnow.Covers;

/// <summary>
/// Steam's portrait capsule from the public CDN. No authentication, no API key,
/// no rate-limit contract beyond politeness. Legacy 2x/1x paths are tried first;
/// a 404 can also mean Steam moved the image to a published hashed asset path.
/// </summary>
public sealed class SteamCapsuleSource : ICoverSource
{
    /// <summary>Named client so DI can hang the User-Agent, timeout and Polly pipeline on it.</summary>
    public const string HttpClientName = "winnow-covers";

    private static readonly string[] CapsuleFiles = ["library_600x900_2x.jpg", "library_600x900.jpg"];

    private readonly IHttpClientFactory _clients;
    private readonly CoverCacheOptions _options;
    private readonly ILogger<SteamCapsuleSource> _log;
    private readonly ISteamLibraryAssetLookup? _assets;

    public SteamCapsuleSource(
        IHttpClientFactory clients,
        CoverCacheOptions options,
        ILogger<SteamCapsuleSource>? log = null,
        ISteamLibraryAssetLookup? assets = null)
    {
        _clients = clients;
        _options = options;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SteamCapsuleSource>.Instance;
        _assets = assets;
    }

    public string Name => "steam-capsule";
    public string SourceSetId => _assets is null ? Name : "steam-capsule-published-v1";
    public bool CanRefreshCachedFallback => _assets is not null;

    public bool CanHandle(CoverKey key)
        => key.Provider == CoverProviders.Steam
           && key.Id.Length > 0
           && key.Id.All(char.IsAsciiDigit);

    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        if (!CanHandle(key))
        {
            return null;
        }

        using var client = _clients.CreateClient(HttpClientName);
        foreach (var file in CapsuleFiles)
        {
            var url = $"{_options.SteamCdnBaseUrl.TrimEnd('/')}/{key.Id}/{file}";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            // A missing legacy filename does not rule out a published asset path.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            // A 403 is NOT an answer about existence. A CDN or WAF block during
            // first launch — the moment a cold library asks for several hundred
            // capsules at once — would otherwise read as "no art exists" for
            // every visible tile, and CoverPipeline would stamp a `.none` marker
            // on each one. Those markers hold for the full 30-day NegativeTtl
            // and CoverDiskCache clears them only on a successful write, so the
            // whole grid would sit on procedural art for a month with no way
            // back. Surfaced as a transport failure instead: the pipeline logs
            // it, caches nothing, and the next realization tries again.
            response.EnsureSuccessStatusCode();
            var bytes = await CoverDownload.ReadAsync(response.Content, ct).ConfigureAwait(false);
            if (bytes.Length > 0)
            {
                return bytes;
            }
        }

        if (await SteamPublishedAssets.TryFetchAsync(client, _assets, _options, key, ct).ConfigureAwait(false) is { } published)
            return published;

        _log.LogDebug("No Steam capsule for {Key}", key);
        return null;
    }
}
