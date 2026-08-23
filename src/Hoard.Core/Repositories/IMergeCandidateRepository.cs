using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IMergeCandidateRepository
{
    /// <summary>Inserts a merge candidate (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default);

    /// <summary>The confirmation queue: all candidates with status 'pending', highest score first.</summary>
    Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// The existing row for a pair of releases in EITHER order, or null.
    ///
    /// <para>This is what makes a re-scan safe. Without it every scan re-queues
    /// every soft match, so the queue grows without bound and — far worse — a
    /// pair the user already answered "Different games" comes back as pending
    /// and gets asked again. <c>confirmed</c> and <c>rejected</c> are both
    /// terminal: a rejected pair stays rejected.</para>
    /// </summary>
    Task<MergeCandidate?> FindByPairAsync(
        long leftReleaseId, long rightReleaseId, CancellationToken ct = default);

    /// <summary>Sets status to a <see cref="MergeCandidateStatuses"/> value.</summary>
    Task SetStatusAsync(long id, string status, CancellationToken ct = default);
}
