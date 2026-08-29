using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Resolve.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Resolve;

/// <summary>One subject and the possibilities it should be scored against.</summary>
/// <param name="Subject">The local release being resolved.</param>
/// <param name="Possibilities">
/// Everything it might be the same game as — other local releases, IGDB search
/// hits already materialised as releases, whatever the orchestrator assembled.
/// Winnow.Resolve never fetches these itself (§5.1: no network in this module).
/// </param>
public sealed record SoftMatchRequest(MatchSubject Subject, IReadOnlyList<MatchSubject> Possibilities);

/// <summary>What one <see cref="SoftMatchResolver.ResolveAsync"/> pass did.</summary>
/// <param name="Compared">Distinct release pairs scored this pass.</param>
/// <param name="Queued">New <c>status='pending'</c> rows written.</param>
/// <param name="Priority">Subset of <paramref name="Queued"/> at or above the priority threshold.</param>
/// <param name="SkippedBelowFloor">Scored below the queue floor, or vetoed.</param>
/// <param name="AlreadyPending">A pending row for this pair already existed.</param>
/// <param name="PreviouslyRejected">User already said "Different games". Terminal.</param>
/// <param name="PreviouslyConfirmed">User already said "Same game". Terminal.</param>
/// <param name="Rescored">Pending pairs whose score was refreshed on new metadata.</param>
/// <param name="Withdrawn">Pending pairs removed because they no longer clear the queue floor.</param>
/// <param name="Retired">
/// Pending pairs removed by reconciliation because no sweep could propose them
/// again — a member is gone or no longer admitted, the two sides now belong to
/// one work, or their titles no longer share a blocking key. Counted apart from
/// <paramref name="Compared"/>: these pairs were never submitted this pass, which
/// is exactly why they needed reconciling.
/// </param>
public sealed record SoftMatchOutcome(
    int Compared,
    int Queued,
    int Priority,
    int SkippedBelowFloor,
    int AlreadyPending,
    int PreviouslyRejected,
    int PreviouslyConfirmed,
    int Rescored = 0,
    int Withdrawn = 0,
    int Retired = 0)
{
    public static SoftMatchOutcome Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>Always zero. Soft matches never auto-merge (§5.3).</summary>
    public int AutoMerged => 0;
}

/// <summary>
/// Queues soft-match survivors into <c>merge_candidates</c> as <c>pending</c>.
/// Merges nothing -- holds only <see cref="IMergeCandidateRepository"/>, no
/// work/release repositories. Pairs are canonicalised (lower id, higher id);
/// confirmed/rejected answers are terminal. Pending pairs are re-scored when
/// metadata changes, withdrawn if they drop below the queue floor, and retired
/// if no future sweep could propose them at all.
///
/// <para><b>Shape of a pass.</b> Read once, score in memory, write in bounded
/// batches. The read is a single <see cref="IMergeCandidateRepository.GetAllAsync"/>;
/// scoring touches no database at all; only the writes take the SQLite writer,
/// and they take it a batch at a time. The earlier shape — one transaction held
/// open across a per-pair lookup — could issue a quarter of a million round trips
/// while every other writer in the app waited.</para>
///
/// <para><b>Atomicity.</b> A pass that writes no more than
/// <see cref="DefaultWriteBatchSize"/> rows — which is every realistic sweep, since
/// the queue floor discards the overwhelming majority of compared pairs — is still
/// exactly one transaction. Beyond that the pass commits in batches: an interrupted
/// run leaves whole batches applied, never a half-written row, and the next sweep
/// absorbs the remainder because queueing is idempotent.</para>
/// </summary>
public sealed class SoftMatchResolver
{
    /// <summary>
    /// Rows per write transaction. Large enough that a normal sweep is a single
    /// atomic commit, small enough that a pathological one yields the writer
    /// back regularly instead of holding it for the whole pass.
    /// </summary>
    public const int DefaultWriteBatchSize = 500;

    private readonly SoftMatcher _matcher;
    private readonly IMergeCandidateRepository _candidates;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ILogger<SoftMatchResolver> _logger;
    private readonly int _writeBatchSize;

    public SoftMatchResolver(
        SoftMatcher matcher,
        IMergeCandidateRepository candidates,
        IUnitOfWorkFactory unitOfWork,
        ILogger<SoftMatchResolver>? logger = null,
        int writeBatchSize = DefaultWriteBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(writeBatchSize, 1);

        _matcher = matcher;
        _candidates = candidates;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<SoftMatchResolver>.Instance;
        _writeBatchSize = writeBatchSize;
    }

    public SoftMatcher Matcher => _matcher;

    public Task<SoftMatchOutcome> ResolveAsync(
        MatchSubject subject,
        IReadOnlyList<MatchSubject> possibilities,
        CancellationToken ct = default)
        => ResolveAsync([new SoftMatchRequest(subject, possibilities)], ct);

    /// <summary>
    /// Scores and queues the submitted pairs. Touches no pending row outside
    /// them — a caller submitting one subject's possibilities is not making a
    /// statement about the rest of the queue.
    /// </summary>
    public Task<SoftMatchOutcome> ResolveAsync(
        IReadOnlyCollection<SoftMatchRequest> requests,
        CancellationToken ct = default)
        => RunAsync(requests, admission: null, ct);

    /// <summary>
    /// The library-wide pass: scores and queues the submitted pairs, then
    /// reconciles every OTHER pending row against <paramref name="admission"/> and
    /// retires the ones no sweep could propose again.
    ///
    /// <para>Only a caller that has just examined the whole library may use this,
    /// because retirement is decided by absence — a pair missing from the current
    /// admission is a pair with no future. Pairs that are still proposable but
    /// simply fell outside this pass's comparison window are left alone, so a
    /// truncated sweep never mistakes "not reached yet" for "no longer valid".</para>
    /// </summary>
    public Task<SoftMatchOutcome> ResolveAndReconcileAsync(
        IReadOnlyCollection<SoftMatchRequest> requests,
        SoftMatchAdmission admission,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        return RunAsync(requests, admission, ct);
    }

    private async Task<SoftMatchOutcome> RunAsync(
        IReadOnlyCollection<SoftMatchRequest> requests,
        SoftMatchAdmission? admission,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);

        // Nothing submitted and nothing to reconcile against: a pass with no
        // question to answer must not even open a connection.
        if (requests.Count == 0 && admission is null)
        {
            return SoftMatchOutcome.Empty;
        }

        // ── Read: one query for the entire pass ──────────────────────────────
        // Every row, not just pending: the terminal ones are what stop a
        // rejected pair being re-queued, and looking each up on demand is the
        // per-pair round trip this preload exists to remove.
        var byPair = new Dictionary<(long Low, long High), MergeCandidate>();
        foreach (var candidate in await _candidates.GetAllAsync(ct))
        {
            var key = Canonical(candidate.LeftReleaseId, candidate.RightReleaseId);

            // Ordered by id, so an older duplicate row wins — the same
            // precedence FindByPairAsync's `ORDER BY id LIMIT 1` gives.
            if (!byPair.ContainsKey(key))
            {
                byPair[key] = candidate;
            }
        }

        // ── Score: pure CPU, no database, no writer held ─────────────────────
        var tally = new Tally();
        var writes = new List<WriteOp>();
        var submitted = new HashSet<(long Low, long High)>();

        foreach (var request in requests)
        {
            foreach (var possibility in request.Possibilities)
            {
                ct.ThrowIfCancellationRequested();

                var (low, high) = Canonical(request.Subject.ReleaseId, possibility.ReleaseId);
                if (low == high || !submitted.Add((low, high)))
                {
                    continue;
                }

                tally.Compared++;

                var score = _matcher.Score(request.Subject, possibility);
                var existing = byPair.GetValueOrDefault((low, high));
                var pending = existing is { Status: MergeCandidateStatuses.Pending } ? existing : null;

                if (!score.ShouldQueue)
                {
                    tally.SkippedBelowFloor++;
                    _logger.LogTrace(
                        "Discarded {Low}/{High} at {Score:F2}{Veto}",
                        low, high, score.Score,
                        score.VetoReason is null ? string.Empty : $" (veto: {score.VetoReason})");

                    // Withdraw pending rows that no longer clear the floor.
                    if (pending is not null)
                    {
                        writes.Add(WriteOp.Withdraw(pending.Id, low, high, pending.Score, score.Score));
                    }

                    continue;
                }

                // Re-score canonically so the stored row is side-independent.
                var canonical = request.Subject.ReleaseId == low
                    ? score
                    : _matcher.Score(possibility, request.Subject);

                if (pending is not null)
                {
                    tally.AlreadyPending++;

                    // Only write on an actual score/signal change.
                    var signals = SoftMatchSignalsJson.Serialize(canonical);
                    if (canonical.Score != pending.Score
                        || !string.Equals(signals, pending.SignalsJson, StringComparison.Ordinal))
                    {
                        writes.Add(WriteOp.Rescore(
                            pending.Id, low, high, pending.Score, canonical.Score, signals));
                    }

                    continue;
                }

                if (existing is not null)
                {
                    // User already answered this pair. Both answers are terminal.
                    switch (existing.Status)
                    {
                        case MergeCandidateStatuses.Rejected:
                            tally.PreviouslyRejected++;
                            _logger.LogDebug(
                                "Pair {Low}/{High} was rejected by the user; not re-queueing", low, high);
                            break;
                        case MergeCandidateStatuses.Confirmed:
                            tally.PreviouslyConfirmed++;
                            break;
                        default:
                            tally.AlreadyPending++;
                            break;
                    }

                    continue;
                }

                writes.Add(WriteOp.Insert(new MergeCandidate
                {
                    LeftReleaseId = low,
                    RightReleaseId = high,
                    Score = canonical.Score,
                    SignalsJson = SoftMatchSignalsJson.Serialize(canonical),
                    Status = MergeCandidateStatuses.Pending,
                }));

                tally.Queued++;
                if (canonical.Band == SoftMatchBand.Priority)
                {
                    tally.Priority++;
                }
            }
        }

        // ── Reconcile: the pending rows nothing submitted ────────────────────
        if (admission is not null)
        {
            foreach (var candidate in byPair.Values)
            {
                ct.ThrowIfCancellationRequested();

                // Terminal rows are decisions, not proposals. They are never
                // reconciled, never rescored, never removed.
                if (!string.Equals(
                        candidate.Status, MergeCandidateStatuses.Pending, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = Canonical(candidate.LeftReleaseId, candidate.RightReleaseId);

                // Submitted this pass: already judged on its merits above.
                if (submitted.Contains(key))
                {
                    continue;
                }

                // Still proposable, just not reached this pass (a truncated
                // sweep covers a window at a time). Leave it for the run that
                // gets to it.
                if (admission.CouldPropose(key.Low, key.High))
                {
                    continue;
                }

                writes.Add(WriteOp.Retire(candidate.Id, key.Low, key.High));
            }
        }

        // ── Write: bounded batches, the only phase that takes the writer ─────
        await ApplyAsync(writes, tally, ct);

        _logger.LogInformation(
            "Soft match: compared {Compared} pairs, queued {Queued} pending ({Priority} priority), "
            + "discarded {BelowFloor} ({Withdrawn} withdrawn from the queue), {AlreadyPending} already "
            + "pending ({Rescored} rescored), {Retired} retired as unproposable, {Rejected} previously "
            + "rejected, {Confirmed} previously confirmed, in {Batches} write batch(es). Auto-merged 0 "
            + "— soft matches never auto-merge (§5.3).",
            tally.Compared, tally.Queued, tally.Priority, tally.SkippedBelowFloor, tally.Withdrawn,
            tally.AlreadyPending, tally.Rescored, tally.Retired, tally.PreviouslyRejected,
            tally.PreviouslyConfirmed, BatchCount(writes.Count));

        return new SoftMatchOutcome(
            tally.Compared, tally.Queued, tally.Priority, tally.SkippedBelowFloor,
            tally.AlreadyPending, tally.PreviouslyRejected, tally.PreviouslyConfirmed,
            tally.Rescored, tally.Withdrawn, tally.Retired);
    }

    /// <summary>
    /// Applies the pass's writes, at most <see cref="_writeBatchSize"/> per
    /// transaction. Counters for the guarded statements are incremented here
    /// rather than during scoring, because the repository's
    /// <c>status = 'pending'</c> predicate — not this class — has the last word
    /// on whether an answered row was left alone.
    /// </summary>
    private async Task ApplyAsync(List<WriteOp> writes, Tally tally, CancellationToken ct)
    {
        for (var start = 0; start < writes.Count; start += _writeBatchSize)
        {
            var end = Math.Min(start + _writeBatchSize, writes.Count);

            using var scope = _unitOfWork.Begin();

            for (var i = start; i < end; i++)
            {
                var write = writes[i];
                switch (write.Kind)
                {
                    case WriteKind.Insert:
                        await _candidates.InsertAsync(write.Row!, ct);
                        break;

                    case WriteKind.Rescore:
                        if (await _candidates.UpdatePendingScoreAsync(
                                write.Id, write.Score, write.SignalsJson, ct))
                        {
                            tally.Rescored++;
                            _logger.LogInformation(
                                "Rescored pending pair {Low}/{High}: {Old:F2} → {New:F2} on new metadata.",
                                write.Low, write.High, write.PreviousScore, write.Score);
                        }

                        break;

                    case WriteKind.Withdraw:
                        if (await _candidates.WithdrawPendingAsync(write.Id, ct))
                        {
                            tally.Withdrawn++;
                            _logger.LogInformation(
                                "Withdrew pending pair {Low}/{High}: rescored {Old:F2} → {New:F2} "
                                + "on new metadata, below the queue floor.",
                                write.Low, write.High, write.PreviousScore, write.Score);
                        }

                        break;

                    case WriteKind.Retire:
                        if (await _candidates.WithdrawPendingAsync(write.Id, ct))
                        {
                            tally.Retired++;
                            _logger.LogInformation(
                                "Retired pending pair {Low}/{High}: no sweep can propose it again "
                                + "(a member is no longer admitted, the sides share a work, or the "
                                + "titles no longer share a blocking key).",
                                write.Low, write.High);
                        }

                        break;

                    default:
                        throw new InvalidOperationException($"Unhandled write kind {write.Kind}.");
                }
            }

            scope.Commit();
        }
    }

    private int BatchCount(int writeCount) => (writeCount + _writeBatchSize - 1) / _writeBatchSize;

    private static (long Low, long High) Canonical(long a, long b)
        => (Math.Min(a, b), Math.Max(a, b));

    private enum WriteKind
    {
        Insert,
        Rescore,
        Withdraw,
        Retire,
    }

    /// <summary>One deferred statement. Scoring produces these; only <see cref="ApplyAsync"/> runs them.</summary>
    private readonly record struct WriteOp(
        WriteKind Kind,
        long Id,
        long Low,
        long High,
        double PreviousScore,
        double Score,
        string? SignalsJson,
        MergeCandidate? Row)
    {
        public static WriteOp Insert(MergeCandidate row)
            => new(WriteKind.Insert, 0, row.LeftReleaseId, row.RightReleaseId, 0, row.Score, null, row);

        public static WriteOp Rescore(
            long id, long low, long high, double previousScore, double score, string? signalsJson)
            => new(WriteKind.Rescore, id, low, high, previousScore, score, signalsJson, null);

        public static WriteOp Withdraw(long id, long low, long high, double previousScore, double score)
            => new(WriteKind.Withdraw, id, low, high, previousScore, score, null, null);

        public static WriteOp Retire(long id, long low, long high)
            => new(WriteKind.Retire, id, low, high, 0, 0, null, null);
    }

    private sealed class Tally
    {
        public int Compared;
        public int Queued;
        public int Priority;
        public int SkippedBelowFloor;
        public int AlreadyPending;
        public int PreviouslyRejected;
        public int PreviouslyConfirmed;
        public int Rescored;
        public int Withdrawn;
        public int Retired;
    }
}
