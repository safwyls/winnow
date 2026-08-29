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

    /// <summary>
    /// Where the last sweep stopped when it hit its comparison ceiling, or null
    /// when the last sweep covered the whole library. Opaque to this layer: the
    /// sweep owns the format.
    /// </summary>
    Task<string?> GetSoftMatchCursorAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the resume point of a truncated sweep, or clears it (null) once a
    /// sweep has covered everything. Without this a capped sweep restarts at the
    /// same prefix on every launch and the tail is never compared at all.
    /// </summary>
    Task SetSoftMatchCursorAsync(string? cursor, CancellationToken ct = default);
}
