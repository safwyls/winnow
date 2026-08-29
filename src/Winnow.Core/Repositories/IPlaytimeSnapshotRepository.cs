using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IPlaytimeSnapshotRepository
{
    /// <summary>Inserts a snapshot (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// Appends a point unless the same one is already stored, returning the new
    /// id or null when it was already there. Identity is ownership, observed-at
    /// and minutes: two readers reporting the same figure at the same instant
    /// are reporting one point in the series.
    ///
    /// <para>The out-of-order surface, as on
    /// <see cref="IPlayRecordRepository.TryAppendAsync"/>: a historical point is
    /// appended on its own merits rather than compared against the newest.</para>
    /// </summary>
    Task<long?> TryAppendAsync(PlaytimeSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Most recent snapshot for an ownership, or null. Ties broken by highest id.</summary>
    Task<PlaytimeSnapshot?> GetLatestAsync(long ownershipId, CancellationToken ct = default);

    /// <summary>All snapshots for an ownership, oldest first.</summary>
    Task<IReadOnlyList<PlaytimeSnapshot>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
}
