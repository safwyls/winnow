using Winnow.Core.Lifecycle;

namespace Winnow.Core.Repositories;

public interface ILifecycleRepository
{
    Task<long> AppendAsync(LifecycleObservation observation, CancellationToken ct = default);
    /// <summary>Applicable observations; IGDB rows must name the work's current mapping. Retired raw history stays stored.</summary>
    Task<IReadOnlyList<LifecycleObservation>> GetForReleaseAsync(long releaseId, CancellationToken ct = default);
    /// <summary>Applicable observations for all releases, preserving independent provider sources.</summary>
    Task<IReadOnlyList<LifecycleObservation>> GetAllAsync(CancellationToken ct = default);
}
