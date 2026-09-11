namespace Winnow.Core.Repositories;

/// <summary>Edition dates observed on exact storefront listings, independent of Work metadata.</summary>
public interface IReleaseYearEvidenceRepository
{
    /// <summary>
    /// Records Steam's explicit original release year only while the release still owns the app ID.
    /// Missing provider dates leave existing evidence intact. Returns whether evidence changed.
    /// </summary>
    Task<bool> ObserveSteamAsync(long releaseId, string appId, int year, CancellationToken ct = default);
}
