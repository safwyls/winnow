using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IPlayRecordRepository
{
    /// <summary>Inserts a play record (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(PlayRecord record, CancellationToken ct = default);

    /// <summary>
    /// Appends an observation unless the same one is already stored, returning
    /// the new id or null when it was already there. Identity is the whole fact
    /// — ownership, source, observed-at, minutes and last-played — so a replayed
    /// cache entry, a delayed source or a re-run historical import is a no-op
    /// however often it arrives, while two readers that genuinely disagree at
    /// the same instant both survive.
    ///
    /// <para>This is the surface for out-of-order writes. Unlike the resolver's
    /// change detection it does not compare against the newest row, so a
    /// historical point may be appended without being read as a change to the
    /// present.</para>
    /// </summary>
    Task<long?> TryAppendAsync(PlayRecord record, CancellationToken ct = default);

    /// <summary>Most recently observed record for an ownership, or null if never observed.</summary>
    Task<PlayRecord?> GetLatestAsync(long ownershipId, CancellationToken ct = default);

    Task<IReadOnlyList<PlayRecord>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
}
