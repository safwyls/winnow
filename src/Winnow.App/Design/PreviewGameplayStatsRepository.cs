using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.Design;

internal sealed class PreviewGameplayStatsRepository : IGameplayStatsRepository
{
    public Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default)
    {
        var games = request.Ownerships.Select(x => x.ResolvedWorkId).Distinct().Take(6).ToArray();
        var totals = games.Select((id, index) => new GameplayGameTotal(id, (games.Length - index) * 5400)).ToArray();
        var total = totals.Sum(x => x.RecordedSeconds);
        return Task.FromResult(new GameplayStats
        {
            RecordedSeconds = total, GamesPlayedCount = games.Length, OverlappingSessionCount = 24, StartedSessionCount = 23,
            MedianSessionSeconds = 2700, ExcludedSessionCount = 1,
            Periods = request.TimeBins.Select((x, index) => new GameplayPeriodTotal(x.FromUtc, x.UntilUtc,
                total * (index + 1) / (request.TimeBins.Count * (request.TimeBins.Count + 1) / 2d))).ToArray(),
            TopGames = totals, SessionLengths = [new(0, 1800, 5), new(1800, 3600, 9), new(3600, 7200, 6), new(7200, null, 3)]
        });
    }
}
