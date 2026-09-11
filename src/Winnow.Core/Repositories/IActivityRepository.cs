using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>A bounded activity page for the caller's visible ownerships and half-open UTC period.</summary>
public interface IActivityRepository
{
    Task<ActivityPage> GetPageAsync(IReadOnlyCollection<long> ownershipIds, DateTime fromUtc, DateTime untilUtc,
        ActivitySection section, ActivityCursor? after = null, int pageSize = 50, CancellationToken ct = default);
}
