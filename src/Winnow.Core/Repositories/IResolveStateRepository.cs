namespace Winnow.Core.Repositories;

/// <summary>
/// Durable state the resolver keeps about itself (over the <c>settings</c> table).
/// Distinguishes "sweep found nothing" from "sweep never ran". Only completed sweeps are recorded.
/// </summary>
public interface IResolveStateRepository
{
    /// <summary>
    /// When a soft-match sweep last ran to completion, or null when none ever
    /// has on this database.
    /// </summary>
    Task<DateTimeOffset?> GetLastSoftMatchSweepAsync(CancellationToken ct = default);

    /// <summary>Records a completed sweep. Called only after the queue writes commit.</summary>
    Task SetLastSoftMatchSweepAsync(DateTimeOffset completedAt, CancellationToken ct = default);
}
