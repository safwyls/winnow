using System.Net;

namespace Winnow.Covers;

/// <summary>
/// Steam library landscapes for known app IDs. Each rendition has its own key
/// so a missing high-resolution asset cannot hide a standard hero or cache it
/// under a key whose resolution the backdrop selector relies on.
/// </summary>
public sealed class SteamHeroSource(IHttpClientFactory clients, CoverCacheOptions options) : ICoverSource
{
    public string Name => "steam-library-hero";

    public bool CanHandle(CoverKey key)
        => key.Provider is CoverProviders.SteamHero or CoverProviders.SteamHeroStandard
           && !string.IsNullOrEmpty(key.Id)
           && key.Id.All(char.IsAsciiDigit);

    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        if (!CanHandle(key)) return null;

        var file = key.Provider == CoverProviders.SteamHero
            ? "library_hero_2x.jpg"
            : "library_hero.jpg";
        var url = $"{options.SteamCdnBaseUrl.TrimEnd('/')}/{key.Id}/{file}";
        using var http = clients.CreateClient(SteamCapsuleSource.HttpClientName);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        // A blocked or unavailable CDN must not create a 30-day missing marker.
        response.EnsureSuccessStatusCode();
        var bytes = await CoverDownload.ReadAsync(response.Content, ct).ConfigureAwait(false);
        return bytes.Length > 0 ? bytes : null;
    }
}
