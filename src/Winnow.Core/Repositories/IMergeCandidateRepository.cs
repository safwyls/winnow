using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IMergeCandidateRepository
{
    /// <summary>Inserts a merge candidate (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default);

    /// <summary>The confirmation queue: all candidates with status 'pending', highest score first.</summary>
    Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Every row, whatever its status, in id order. The resolver preloads this
    /// into a canonical-pair dictionary so a sweep costs ONE query rather than a
    /// <see cref="FindByPairAsync"/> per compared pair — at the configured
    /// ceiling that was up to 250,000 round trips inside a single writer
    /// transaction, which stalls acknowledgements, journal notes and session
    /// writes for as long as it runs.
    /// </summary>
    Task<IReadOnlyList<MergeCandidate>> GetAllAsync(CancellationToken ct = default);

    /// <summary>The existing row for a pair of releases in either order, or null. Prevents re-queuing answered pairs.</summary>
    Task<MergeCandidate?> FindByPairAsync(
        long leftReleaseId, long rightReleaseId, CancellationToken ct = default);

    /// <summary>Sets status to a <see cref="MergeCandidateStatuses"/> value.</summary>
    Task SetStatusAsync(long id, string status, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the score and signal breakdown of a pending row when new metadata
    /// arrives. Only touches pending rows; confirmed/rejected rows are not updated.
    /// </summary>
    /// <returns>True when a pending row was updated; false when missing or already answered.</returns>
    Task<bool> UpdatePendingScoreAsync(
        long id, double score, string? signalsJson, CancellationToken ct = default);

    /// <summary>
    /// Removes a pending row whose pair no longer clears the queue floor.
    /// Never touches confirmed/rejected rows.
    /// </summary>
    /// <returns>True when a pending row was removed.</returns>
    Task<bool> WithdrawPendingAsync(long id, CancellationToken ct = default);
}
