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
public sealed record SoftMatchOutcome(
    int Compared,
    int Queued,
    int Priority,
    int SkippedBelowFloor,
    int AlreadyPending,
    int PreviouslyRejected,
    int PreviouslyConfirmed,
    int Rescored = 0,
    int Withdrawn = 0)
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
/// metadata changes, and withdrawn if they drop below the queue floor.
/// Runs atomically in one <see cref="IUnitOfWork"/>.
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
        var rescored = 0;
        var withdrawn = 0;

        // Deduplicate pairs across requests to avoid UNIQUE index violations.
        var seen = new HashSet<(long Low, long High)>();

        using var scope = _unitOfWork.Begin();

        // Load the full pending queue into memory (small by construction)
        // to avoid per-pair DB lookups during scoring.
        var pending = new Dictionary<(long Low, long High), MergeCandidate>();
        foreach (var candidate in await _candidates.GetPendingAsync(ct))
        {
            var key = (
                Math.Min(candidate.LeftReleaseId, candidate.RightReleaseId),
                Math.Max(candidate.LeftReleaseId, candidate.RightReleaseId));
            pending[key] = candidate;
        }

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
                var queuedRow = pending.GetValueOrDefault((low, high));

                if (!score.ShouldQueue)
                {
                    belowFloor++;
                    _logger.LogTrace(
                        "Discarded {Low}/{High} at {Score:F2}{Veto}",
                        low, high, score.Score,
                        score.VetoReason is null ? string.Empty : $" (veto: {score.VetoReason})");

                    // Withdraw pending rows that no longer clear the floor.
                    if (queuedRow is not null
                        && await _candidates.WithdrawPendingAsync(queuedRow.Id, ct))
                    {
                        withdrawn++;
                        _logger.LogInformation(
                            "Withdrew pending pair {Low}/{High}: rescored {Old:F2} → {New:F2} "
                            + "on new metadata, below the queue floor.",
                            low, high, queuedRow.Score, score.Score);
                    }

                    continue;
                }

                // Re-score canonically so the stored row is side-independent.
                var canonical = request.Subject.ReleaseId == low
                    ? score
                    : _matcher.Score(possibility, request.Subject);

                if (queuedRow is not null)
                {
                    alreadyPending++;

                    // Only write on an actual score/signal change.
                    var signals = SoftMatchSignalsJson.Serialize(canonical);
                    if (canonical.Score != queuedRow.Score
                        || !string.Equals(signals, queuedRow.SignalsJson, StringComparison.Ordinal))
                    {
                        if (await _candidates.UpdatePendingScoreAsync(
                            queuedRow.Id, canonical.Score, signals, ct))
                        {
                            rescored++;
                            _logger.LogInformation(
                                "Rescored pending pair {Low}/{High}: {Old:F2} → {New:F2} on new metadata.",
                                low, high, queuedRow.Score, canonical.Score);
                        }
                    }

                    continue;
                }

                var existing = await _candidates.FindByPairAsync(low, high, ct);
                if (existing is not null)
                {
                    // User already answered this pair. Both answers are terminal.
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
            + "discarded {BelowFloor} ({Withdrawn} withdrawn from the queue), {AlreadyPending} already "
            + "pending ({Rescored} rescored), {Rejected} previously rejected, {Confirmed} previously "
            + "confirmed. Auto-merged 0 — soft matches never auto-merge (§5.3).",
            compared, queued, priority, belowFloor, withdrawn, alreadyPending, rescored,
            previouslyRejected, previouslyConfirmed);

        return new SoftMatchOutcome(
            compared, queued, priority, belowFloor, alreadyPending, previouslyRejected,
            previouslyConfirmed, rescored, withdrawn);
    }
}
