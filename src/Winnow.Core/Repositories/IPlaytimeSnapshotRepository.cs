using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IPlaytimeSnapshotRepository
{
    /// <summary>Inserts a snapshot (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Most recent snapshot for an ownership, or null. Ties broken by highest id.</summary>
    Task<PlaytimeSnapshot?> GetLatestAsync(long ownershipId, CancellationToken ct = default);

    /// <summary>All snapshots for an ownership, oldest first.</summary>
    Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
}
