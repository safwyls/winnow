namespace Winnow.Covers;

/// <summary>Provider names a <see cref="CoverKey"/> may carry. Match Winnow.Core's ExternalIdProviders.</summary>
public static class CoverProviders
{
    public const string Steam = "steam";
    public const string SteamHero = "steam-hero";
    public const string SteamHeroStandard = "steam-hero-standard";
    public const string SteamGridDbHero = "steamgriddb-hero";
    public const string Igdb = "igdb";
    /// <summary>
    /// Provider for IGDB screenshot assets. A separate provider from
    /// <see cref="Igdb"/> so a cover and a screenshot of the same image id
    /// produce different <see cref="CoverKey.CacheStem"/> values and are two
    /// files on disk rather than one file at whichever size was asked for
    /// first.
    /// </summary>
    public const string IgdbScreenshot = "igdb-shot";
    public const string IgdbBackdrop = "igdb-backdrop";
    public const string User = "user";
}

/// <summary>
/// Identifies one game's art by the provider id we know it under. The cache is
/// source-agnostic: several <see cref="ICoverSource"/> implementations may
/// answer the same key (Steam's portrait capsule first, IGDB's cover as the
/// gap-filler), so the key names the <em>game</em>, not the artwork file.
/// </summary>
public readonly record struct CoverKey(string Provider, string Id)
{
    public static CoverKey Steam(string appId) => new(CoverProviders.Steam, appId);

    /// <summary>Steam's high-resolution library hero, isolated from standard heroes and capsules.</summary>
    public static CoverKey SteamHero(string appId) => new(CoverProviders.SteamHero, appId);

    /// <summary>Steam's standard library hero; a separate candidate and cache entry.</summary>
    public static CoverKey SteamHeroStandard(string appId) => new(CoverProviders.SteamHeroStandard, appId);

    /// <summary>SteamGridDB hero keyed by its validated CDN asset filename, including extension.</summary>
    public static CoverKey SteamGridDbHero(string assetFileName) => new(CoverProviders.SteamGridDbHero, assetFileName);

    /// <summary>
    /// A key for IGDB cover art, keyed by <c>image_id</c> (not the game id,
    /// because <c>works.igdb_id</c> is UNIQUE and shared across duplicate pairs).
    /// </summary>
    public static CoverKey Igdb(string imageId) => new(CoverProviders.Igdb, imageId);

    /// <summary>
    /// A key for an IGDB screenshot, keyed by <c>image_id</c>. Uses
    /// <see cref="CoverProviders.IgdbScreenshot"/> so it fetches at the
    /// screenshot rendition rather than the cover rendition. IGDB covers are
    /// 3:4 portrait; screenshots are 16:9 landscape, and
    /// <c>t_cover_big_2x</c> on a screenshot asset would be the wrong shape
    /// and the wrong resolution for a strip that expands one shot to a hero.
    /// </summary>
    public static CoverKey IgdbScreenshot(string imageId) => new(CoverProviders.IgdbScreenshot, imageId);

    /// <summary>Full-canvas landscape rendition, isolated from the smaller screenshot cache.</summary>
    public static CoverKey IgdbBackdrop(string imageId) => new(CoverProviders.IgdbBackdrop, imageId);

    /// <summary>
    /// A key for user-supplied art, keyed by the content token (the SHA-256
    /// prefix). The art is the asset: two works that pick the same picture
    /// share one file.
    /// </summary>
    public static CoverKey User(string token) => new(CoverProviders.User, token);

    /// <summary>Filename stem for the disk cache. Sanitized — ids come from external data.</summary>
    public string CacheStem
    {
        get
        {
            var raw = $"{Provider}_{Id}";
            var chars = new char[raw.Length];
            for (var i = 0; i < raw.Length; i++)
            {
                var c = raw[i];
                chars[i] = char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? char.ToLowerInvariant(c) : '_';
            }

            return new string(chars);
        }
    }

    public override string ToString() => $"{Provider}:{Id}";
}
