using Winnow.Covers;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;

namespace Winnow.App.Services;

/// <summary>Adapts shared, cached appinfo to cover lookup without another metadata transport.</summary>
public sealed class SteamLibraryAssetLookup(IBuildInfoClient appInfo) : ISteamLibraryAssetLookup
{
    private const int MaximumPaths = 8;
    private static readonly TimeSpan ArtworkCacheTtl = TimeSpan.FromDays(7);
    // Same-app capsule and hero requests share the newly populated body cache.
    // Fixed stripes avoid retaining a lock for every game ever browsed.
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IReadOnlyList<string>?> GetPathsAsync(CoverKey key, CancellationToken ct = default)
    {
        if (key.Provider is not (CoverProviders.Steam or CoverProviders.SteamHero or CoverProviders.SteamHeroStandard))
            return [];

        var gate = _gates[(int)((uint)StringComparer.Ordinal.GetHashCode(key.Id) % (uint)_gates.Length)];
        AppInfoFetch result;
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            result = await appInfo.GetAppInfoAsync(key.Id, cacheTtl: ArtworkCacheTtl, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
        if (result.Outcome == AppInfoOutcome.Unavailable)
            return null;

        var assets = result.Info?.LibraryAssets;
        // Malformed optional metadata must not become a long-lived missing-art answer.
        if (assets?.IsMalformed == true)
            return null;
        var image = key.Provider == CoverProviders.Steam ? assets?.Capsule : assets?.Hero;
        if (image is null)
            return [];

        var candidates = key.Provider switch
        {
            CoverProviders.Steam => Ordered(image.Image2x).Take(MaximumPaths / 2)
                .Concat(Ordered(image.Image).Take(MaximumPaths / 2)),
            CoverProviders.SteamHero => Ordered(image.Image2x),
            _ => Ordered(image.Image),
        };
        return candidates.Distinct(StringComparer.Ordinal).Take(MaximumPaths).ToArray();
    }

    private static IEnumerable<string> Ordered(IReadOnlyDictionary<string, string> paths)
        => paths.OrderBy(pair => pair.Key.Equals("english", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value);
}
