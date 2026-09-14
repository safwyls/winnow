namespace Winnow.Covers;

/// <summary>Published relative Steam library asset paths, backed by the shared appinfo cache.</summary>
public interface ISteamLibraryAssetLookup
{
    /// <summary>
    /// Returns preferred paths in rendition/language order. Empty means no published asset;
    /// null means metadata was unavailable and must not be recorded as missing artwork.
    /// </summary>
    Task<IReadOnlyList<string>?> GetPathsAsync(CoverKey key, CancellationToken ct = default);
}
