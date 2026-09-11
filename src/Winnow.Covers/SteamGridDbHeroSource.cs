using System.Net;

namespace Winnow.Covers;

/// <summary>Accepts only SteamGridDB's public hero files; the API's numeric image ID is not a CDN key.</summary>
public static class SteamGridDbHeroUrl
{
    private const string Prefix = "https://cdn2.steamgriddb.com/hero/";

    public static CoverKey? Key(string? url)
    {
        if (url is null || !url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var file = url[Prefix.Length..].ToLowerInvariant();
        return IsAssetFileName(file) ? CoverKey.SteamGridDbHero(file) : null;
    }

    internal static string? Url(CoverKey key) => key.Provider == CoverProviders.SteamGridDbHero
        && IsAssetFileName(key.Id) ? Prefix + key.Id : null;

    private static bool IsAssetFileName(string? file)
    {
        if (file is null || file.Length is not (36 or 37) || file[32] != '.') return false;
        if (file[33..] is not ("png" or "jpg" or "webp")) return false;
        foreach (var character in file.AsSpan(0, 32))
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) return false;
        return true;
    }
}

/// <summary>Bounded public CDN downloads, isolated from Steam heroes and portrait covers.</summary>
public sealed class SteamGridDbHeroSource(IHttpClientFactory clients) : ICoverSource
{
    public string Name => "steamgriddb-hero";

    public bool CanHandle(CoverKey key) => SteamGridDbHeroUrl.Url(key) is not null;

    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        if (SteamGridDbHeroUrl.Url(key) is not { } url) return null;
        using var http = clients.CreateClient(SteamCapsuleSource.HttpClientName);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        // Transport failures must not be cached as an absent asset.
        response.EnsureSuccessStatusCode();
        var bytes = await CoverDownload.ReadAsync(response.Content, ct).ConfigureAwait(false);
        return bytes.Length > 0 ? bytes : null;
    }
}
