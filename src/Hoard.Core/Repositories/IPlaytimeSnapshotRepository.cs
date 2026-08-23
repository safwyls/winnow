using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IPlaytimeSnapshotRepository
{
    /// <summary>Inserts a snapshot (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// The most recent snapshot for an ownership, or null when there is none.
    /// Ingest change-detection needs only this row — reading the whole history
    /// to look at its last element is an N+1 over a table that grows forever.
    /// Ties on observed_at (the timestamp handler stores whole seconds, so two
    /// scans in one second tie) are broken by highest id, matching
    /// <see cref="IPlayRecordRepository.GetLatestAsync"/>.
    /// </summary>
    Task<PlaytimeSnapshot?> GetLatestAsync(long ownershipId, CancellationToken ct = default);

    /// <summary>All snapshots for an ownership, oldest first.</summary>
    Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
}
