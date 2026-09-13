using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

public interface IAchievementRepository
{
    Task<IReadOnlyList<SteamAchievementCandidate>> GetDueSteamAsync(string accountRef, DateTime asOfUtc,
        int limit, CancellationToken ct = default);
    Task SaveAsync(long releaseId, string accountRef, AchievementFetch result, CancellationToken ct = default);
    Task<IReadOnlyList<ReleaseAchievementSummary>> GetForAccountAsync(
        IReadOnlyList<long> releaseIds, string? accountRef, DateTime asOfUtc, CancellationToken ct = default);
}

public sealed record SteamAchievementCandidate(long ReleaseId, string AppId);
