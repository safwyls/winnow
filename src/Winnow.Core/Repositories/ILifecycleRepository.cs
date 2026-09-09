using Winnow.Core.Lifecycle;

namespace Winnow.Core.Repositories;

public interface ILifecycleRepository
{
    Task<long> AppendAsync(LifecycleObservation observation, CancellationToken ct = default);
    Task<IReadOnlyList<LifecycleObservation>> GetForReleaseAsync(long releaseId, CancellationToken ct = default);
    Task<IReadOnlyList<LifecycleObservation>> GetAllAsync(CancellationToken ct = default);
}
