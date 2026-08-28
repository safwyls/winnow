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
    /// A key naming IGDB's cover asset directly, by its <c>image_id</c>
    /// (<c>co1r76</c>).
    ///
    /// <para><b>Why the artwork id and not the game id.</b> Every cover key in
    /// this app used to be a Steam appid, which meant a release with no Steam id
    /// — every Epic and GOG title in the library — had no key, was never handed
    /// to the pipeline, and rendered a placeholder no matter what
    /// <c>works.cover_url</c> held. The obvious repair is a key carrying the
    /// IGDB <i>game</i> id, and it fails for the population that needs it most:
    /// <c>works.igdb_id</c> is UNIQUE, so when an Epic title and its Steam twin
    /// resolve to the same IGDB game only one of them may hold the id, and the
    /// Epic row is the one that goes without. The image id has no such
    /// constraint — it is already stored, per work, in <c>cover_url</c>, it is
    /// the same string for both rows of a duplicate pair, and it needs no
    /// credentials to fetch. So the key names the artwork.</para>
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
