using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

public interface IGameplayStatsRepository
{
    /// <summary>Bounded aggregates over the caller's visible ownerships and half-open UTC period.</summary>
    Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default);
}
