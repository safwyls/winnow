namespace Winnow.Covers;

/// <summary>Provider names a <see cref="CoverKey"/> may carry. Match Winnow.Core's ExternalIdProviders.</summary>
public static class CoverProviders
{
    public const string Steam = "steam";
    public const string Igdb = "igdb";
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

    /// <summary>
    /// A key for IGDB cover art, keyed by <c>image_id</c> (not the game id,
    /// because <c>works.igdb_id</c> is UNIQUE and shared across duplicate pairs).
    /// </summary>
    public static CoverKey Igdb(string imageId) => new(CoverProviders.Igdb, imageId);

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
