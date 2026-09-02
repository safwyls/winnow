using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// The per-release achievement read (§6.2). Rows are returned one per
/// release and are never combined; there is deliberately no "for this work"
/// or "for this group" overload, because the only way to answer such a
/// question would be to blend two platforms' figures into a number no
/// source reported. The details modal renders one row per release nested
/// under the game, which is what §6.2 asks for.
/// </summary>
public interface IAchievementQueryRepository
{
    /// <summary>
    /// One summary per release that defines any achievements. Releases with
    /// none are absent from the result rather than present at zero, because
    /// "no achievements" and "none unlocked" are different facts.
    /// </summary>
    Task<IReadOnlyList<ReleaseAchievementSummary>> GetSummariesAsync(
        IReadOnlyList<long> releaseIds, CancellationToken ct = default);
}
