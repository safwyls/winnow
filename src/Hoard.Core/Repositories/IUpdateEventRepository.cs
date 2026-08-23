using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IUpdateEventRepository
{
    /// <summary>Inserts an update event (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(UpdateEvent updateEvent, CancellationToken ct = default);

    /// <summary>All raw update signals for a release, oldest first.</summary>
    Task<IReadOnlyList<UpdateEvent>> GetByReleaseAsync(long releaseId, CancellationToken ct = default);
}
