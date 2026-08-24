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

    /// <summary>
    /// Rewrites the score and signal breakdown of a row that is <b>still
    /// pending</b>, leaving its identity and status untouched.
    ///
    /// <para><b>Why this exists.</b> A queued pair carries a frozen record of
    /// the evidence as it stood when it was queued, and the confirm UI renders
    /// that record rather than re-scoring. That is right for a threshold tune —
    /// the user should see the score they are being asked about. It is wrong
    /// for new FACTS: a pair scored before enrichment knew any release year or
    /// publisher was scored on title alone, and would otherwise sit in the queue
    /// forever explaining itself with "release year known on one side only" long
    /// after both years were known.</para>
    ///
    /// <para><b>Terminal statuses are unreachable from here.</b> The status
    /// predicate is in the SQL, not in the caller: a <c>confirmed</c> or
    /// <c>rejected</c> row matches nothing and is returned as not updated. A
    /// pair the user already answered is never re-opened, re-scored or
    /// re-explained.</para>
    /// </summary>
    /// <returns>True when a pending row was updated; false when the row was
    /// missing or had already been answered.</returns>
    Task<bool> UpdatePendingScoreAsync(
        long id, double score, string? signalsJson, CancellationToken ct = default);

    /// <summary>
    /// Removes a <b>still pending</b> row whose pair no longer clears the queue
    /// floor.
    ///
    /// <para>A pending row is a proposal, not a decision. Once the metadata that
    /// was missing when it was queued arrives and the pair scores below the
    /// floor, the matcher no longer wants that question asked — and the queue's
    /// contents should be a function of the current evidence, not of the order
    /// in which the evidence happened to arrive. Leaving it queued makes the
    /// review list depend on which launch enriched the library, which is exactly
    /// the noise §5.3 says gets a queue abandoned.</para>
    ///
    /// <para><b>Never touches an answered row.</b> The status predicate is in
    /// the SQL. A rejected pair stays rejected and a confirmed pair stays
    /// confirmed — nothing here can delete a user's answer, and a withdrawn
    /// proposal is re-proposed by the next sweep if the evidence changes back.</para>
    /// </summary>
    /// <returns>True when a pending row was removed.</returns>
    Task<bool> WithdrawPendingAsync(long id, CancellationToken ct = default);
}
