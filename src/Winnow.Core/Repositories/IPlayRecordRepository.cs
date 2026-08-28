using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IPlayRecordRepository
{
    /// <summary>Inserts a play record (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(PlayRecord record, CancellationToken ct = default);

    /// <summary>Most recently observed record for an ownership, or null if never observed.</summary>
    Task<PlayRecord?> GetLatestAsync(long ownershipId, CancellationToken ct = default);

    Task<IReadOnlyList<PlayRecord>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default);
}
