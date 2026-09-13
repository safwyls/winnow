using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>Per-release standing for the selected Steam account, including explicit unanswered states.</summary>
public sealed class AchievementQueryRepository(ISqliteConnectionFactory factory) : IAchievementQueryRepository
{
    public async Task<IReadOnlyList<ReleaseAchievementSummary>> GetSummariesAsync(
        IReadOnlyList<long> releaseIds, CancellationToken ct = default)
    {
        var account = SteamOwnedAccount.Clean(await new SettingsRepository(factory).GetAsync(SteamOwnedAccount.RefSettingKey, ct));
        return await new AchievementRepository(factory).GetForAccountAsync(releaseIds, account, DateTime.UtcNow, ct);
    }
}