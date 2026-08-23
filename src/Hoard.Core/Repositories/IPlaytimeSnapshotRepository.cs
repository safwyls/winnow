using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IPlaytimeSnapshotRepository
{
    /// <summary>Inserts a snapshot (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>All snapshots for an ownership, oldest first.</summary>
    Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
}
