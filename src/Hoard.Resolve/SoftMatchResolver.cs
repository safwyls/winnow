using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Resolve.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Resolve;

/// <summary>One subject and the possibilities it should be scored against.</summary>
/// <param name="Subject">The local release being resolved.</param>
/// <param name="Possibilities">
/// Everything it might be the same game as — other local releases, IGDB search
/// hits already materialised as releases, whatever the orchestrator assembled.
/// Hoard.Resolve never fetches these itself (§5.1: no network in this module).
/// </param>
public sealed record SoftMatchRequest(MatchSubject Subject, IReadOnlyList<MatchSubject> Possibilities);

/// <summary>
/// What one <see cref="SoftMatchResolver.ResolveAsync"/> pass did, for the
/// orchestrating sync to log. Every pair examined lands in exactly one bucket,
/// so <c>Compared == Queued + SkippedBelowFloor + AlreadyPending +
/// PreviouslyRejected + PreviouslyConfirmed</c>.
/// </summary>
/// <param name="Compared">Distinct release pairs scored this pass.</param>
/// <param name="Queued">New <c>status='pending'</c> rows written.</param>
/// <param name="Priority">
/// Subset of <paramref name="Queued"/> at or above the priority threshold —
/// shown first in the review queue. <b>Not</b> merged; nothing here is merged.
/// </param>
/// <param name="SkippedBelowFloor">Scored below the queue floor, or vetoed. Discarded, not stored.</param>
/// <param name="AlreadyPending">A pending row for this pair already existed; left untouched.</param>
/// <param name="PreviouslyRejected">The user already said "Different games". Terminal — never re-queued.</param>
/// <param name="PreviouslyConfirmed">The user already said "Same game". Terminal.</param>
public sealed record SoftMatchOutcome(
    int Compared,
    int Queued,
    int Priority,
    int SkippedBelowFloor,
    int AlreadyPending,
    int PreviouslyRejected,
    int PreviouslyConfirmed)
{
    public static SoftMatchOutcome Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// <b>Always zero.</b> §5.3 step 2 is "queue, never auto": in M1 the
    /// external-id hard join (<see cref="ExternalIdResolver"/>) is the only
    /// thing permitted to merge without asking. Reported so a sync log can
    /// state the fact rather than leave it to be inferred.
    /// </summary>
    public int AutoMerged => 0;
}

/// <summary>
/// §5.3 step 2's write half: takes soft-match scores and lands the survivors in
/// <c>merge_candidates</c> with <c>status='pending'</c>, for the batch
/// confirmation UI to clear.
///
/// <para><b>It merges nothing.</b> It holds no work, release or ownership
/// repository — only <see cref="IMergeCandidateRepository"/> — so there is no
/// code path from a high score to a changed library, by construction rather
/// than by discipline. Precision over recall, always, with a human in the loop
/// (§5.3).</para>
///
/// <para><b>Re-scan safety.</b> A library gets scanned on every launch, so this
/// pass runs hundreds of times over the same pairs. Three rules keep that from
/// degrading into an unusable queue:</para>
/// <list type="bullet">
///   <item>pairs are canonicalised to (lower id, higher id), so A→B and B→A are
///     one row rather than two;</item>
///   <item>an existing row for the pair — in any status — blocks the insert;</item>
///   <item><c>confirmed</c> and <c>rejected</c> are terminal. A pair the user
///     answered "Different games" is never resurrected. Re-asking a question the
///     user already answered is how a confirmation queue teaches people to
///     click through it without reading, which is the failure mode that makes
///     the whole human-in-the-loop design worthless.</item>
/// </list>
///
/// <para><b>Atomicity.</b> The pass runs in one <see cref="IUnitOfWork"/>, like
/// <see cref="ExternalIdResolver"/>: a crash mid-pass queues nothing rather than
/// half a queue whose remainder the idempotency check would then suppress
/// forever.</para>
/// </summary>
public sealed class SoftMatchResolver
{
    private readonly SoftMatcher _matcher;
    private readonly IMergeCandidateRepository _candidates;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ILogger<SoftMatchResolver> _logger;

    public SoftMatchResolver(
        SoftMatcher matcher,
        IMergeCandidateRepository candidates,
        IUnitOfWorkFactory unitOfWork,
        ILogger<SoftMatchResolver>? logger = null)
    {
        _matcher = matcher;
        _candidates = candidates;
        _unitOfWork = unitOfWork;
        _logger = logger ?? NullLogger<SoftMatchResolver>.Instance;
    }

    public SoftMatcher Matcher => _matcher;

    public Task<SoftMatchOutcome> ResolveAsync(
        MatchSubject subject,
        IReadOnlyList<MatchSubject> possibilities,
        CancellationToken ct = default)
        => ResolveAsync([new SoftMatchRequest(subject, possibilities)], ct);

    public async Task<SoftMatchOutcome> ResolveAsync(
        IReadOnlyCollection<SoftMatchRequest> requests,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return SoftMatchOutcome.Empty;
        }

        var compared = 0;
        var queued = 0;
        var priority = 0;
        var belowFloor = 0;
        var alreadyPending = 0;
        var previouslyRejected = 0;
        var previouslyConfirmed = 0;

        // Two requests can nominate the same pair (A's possibilities include B,
        // B's include A). Scoring it twice is harmless — the matcher is pure —
        // but inserting it twice trips the UNIQUE(left, right) index and rolls
        // the whole pass back.
        var seen = new HashSet<(long Low, long High)>();

        using var scope = _unitOfWork.Begin();

        foreach (var request in requests)
        {
            foreach (var possibility in request.Possibilities)
            {
                ct.ThrowIfCancellationRequested();

                var low = Math.Min(request.Subject.ReleaseId, possibility.ReleaseId);
                var high = Math.Max(request.Subject.ReleaseId, possibility.ReleaseId);
                if (low == high || !seen.Add((low, high)))
                {
                    continue;
                }

                compared++;

                var score = _matcher.Score(request.Subject, possibility);
                if (!score.ShouldQueue)
                {
                    belowFloor++;
                    _logger.LogTrace(
                        "Discarded {Low}/{High} at {Score:F2}{Veto}",
                        low, high, score.Score,
                        score.VetoReason is null ? string.Empty : $" (veto: {score.VetoReason})");
                    continue;
                }

                var existing = await _candidates.FindByPairAsync(low, high, ct);
                if (existing is not null)
                {
                    switch (existing.Status)
                    {
                        case MergeCandidateStatuses.Rejected:
                            previouslyRejected++;
                            _logger.LogDebug(
                                "Pair {Low}/{High} was rejected by the user; not re-queueing", low, high);
                            break;
                        case MergeCandidateStatuses.Confirmed:
                            previouslyConfirmed++;
                            break;
                        default:
                            alreadyPending++;
                            break;
                    }

                    continue;
                }

                // Score is stored canonicalised the same way the pair is, so the
                // row is byte-identical regardless of which side was the subject.
                var canonical = request.Subject.ReleaseId == low
                    ? score
                    : _matcher.Score(possibility, request.Subject);

                await _candidates.InsertAsync(new MergeCandidate
                {
                    LeftReleaseId = low,
                    RightReleaseId = high,
                    Score = canonical.Score,
                    SignalsJson = SoftMatchSignalsJson.Serialize(canonical),
                    Status = MergeCandidateStatuses.Pending,
                }, ct);

                queued++;
                if (canonical.Band == SoftMatchBand.Priority)
                {
                    priority++;
                }
            }
        }

        scope.Commit();

        _logger.LogInformation(
            "Soft match: compared {Compared} pairs, queued {Queued} pending ({Priority} priority), "
            + "discarded {BelowFloor}, {AlreadyPending} already pending, {Rejected} previously rejected, "
            + "{Confirmed} previously confirmed. Auto-merged 0 — soft matches never auto-merge (§5.3).",
            compared, queued, priority, belowFloor, alreadyPending, previouslyRejected, previouslyConfirmed);

        return new SoftMatchOutcome(
            compared, queued, priority, belowFloor, alreadyPending, previouslyRejected, previouslyConfirmed);
    }
}
