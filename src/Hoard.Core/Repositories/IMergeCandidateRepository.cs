using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IMergeCandidateRepository
{
    /// <summary>Inserts a merge candidate (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default);

    /// <summary>The confirmation queue: all candidates with status 'pending', highest score first.</summary>
    Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>Sets status to a <see cref="MergeCandidateStatuses"/> value.</summary>
    Task SetStatusAsync(long id, string status, CancellationToken ct = default);
}
