using System.Net;
using Winnow.Core.Domain;
using Winnow.Covers;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;

namespace Winnow.App.Services;

/// <summary>Steam-only browser keys cannot be answered by an automatic IGDB fallback.</summary>
public sealed class SteamBrowserArtworkSource : ICoverSource
{
    public const string CoverProvider = "artwork-steam-cover";
    public const string HeroProvider = "artwork-steam-hero";
    public const string StandardHeroProvider = "artwork-steam-hero-standard";
    public const string IconProvider = "artwork-steam-icon";

    private readonly IHttpClientFactory _clients;
    private readonly IBuildInfoClient _appInfo;
    private readonly SteamCapsuleSource _covers;
    private readonly SteamHeroSource _heroes;

    public SteamBrowserArtworkSource(IHttpClientFactory clients, CoverCacheOptions options,
        ISteamLibraryAssetLookup assets, IBuildInfoClient appInfo)
    {
        _clients = clients;
        _appInfo = appInfo;
        _covers = new SteamCapsuleSource(clients, options, assets: assets);
        _heroes = new SteamHeroSource(clients, options, assets);
    }

    public string Name => "steam-artwork-browser";

    public static CoverKey Key(string appId, ArtworkSlot slot, bool standard = false) => new(slot switch
    {
        ArtworkSlot.Cover => CoverProvider,
        ArtworkSlot.Hero => standard ? StandardHeroProvider : HeroProvider,
        ArtworkSlot.Icon => IconProvider,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    }, appId);

    public bool CanHandle(CoverKey key)
        => key.Provider is CoverProvider or HeroProvider or StandardHeroProvider or IconProvider
            && !string.IsNullOrEmpty(key.Id) && key.Id.All(char.IsAsciiDigit);

    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        if (!CanHandle(key)) return null;
        if (key.Provider == CoverProvider)
            return await _covers.TryFetchAsync(CoverKey.Steam(key.Id), ct).ConfigureAwait(false);
        if (key.Provider is HeroProvider or StandardHeroProvider)
            return await _heroes.TryFetchAsync(key.Provider == HeroProvider
                ? CoverKey.SteamHero(key.Id) : CoverKey.SteamHeroStandard(key.Id), ct).ConfigureAwait(false);

        var result = await _appInfo.GetAppInfoAsync(key.Id, ct: ct).ConfigureAwait(false);
        if (result.Outcome == AppInfoOutcome.Unavailable)
            throw new HttpRequestException("Steam icon metadata is temporarily unavailable.");
        if (result.Info?.IconHash is not { Length: 40 } hash || !hash.All(char.IsAsciiHexDigit)) return null;

        // common.icon is a content hash, never an arbitrary URL or filesystem path.
        var url = $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{key.Id}/{hash.ToLowerInvariant()}.jpg";
        using var client = _clients.CreateClient(SteamCapsuleSource.HttpClientName);
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var bytes = await CoverDownload.ReadAsync(response.Content, ct).ConfigureAwait(false);
        return bytes.Length == 0 ? null : bytes;
    }
}
