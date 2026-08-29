using System.Diagnostics;
using Winnow.Core.Matching;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Resolve.Matching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Resolve;

/// <summary>
/// Tuning for <see cref="LibrarySoftMatchSweep"/>'s candidate generation. Both
/// numbers are safety rails, not quality knobs — the matcher decides what is a
/// match; these only decide how many pairs it is asked about.
/// </summary>
public sealed record SoftMatchSweepOptions
{
    public static SoftMatchSweepOptions Default { get; } = new();

    /// <summary>
    /// A token shared by more than this many releases is too common to be a
    /// useful blocking key. Every release still lands in at least one block
    /// via its rarest token, even if all its tokens exceed this limit.
    /// </summary>
    public int MaxBlockSize { get; init; } = 60;

    /// <summary>
    /// Hard ceiling on pairs proposed in one sweep. A truncated sweep is safe
    /// to re-run: idempotent and deterministic in id order.
    /// </summary>
    public int MaxComparisons { get; init; } = 250_000;
}

/// <summary>What one <see cref="LibrarySoftMatchSweep.SweepAsync"/> pass did.</summary>
/// <param name="Releases">Releases the sweep considered.</param>
/// <param name="Excluded">Releases skipped (provisional name or empty normalised title).</param>
/// <param name="Blocks">Blocking keys that produced at least one pair.</param>
/// <param name="PairsProposed">Distinct release pairs handed to the resolver.</param>
/// <param name="Truncated">True when <see cref="SoftMatchSweepOptions.MaxComparisons"/> cut the pass short.</param>
/// <param name="Outcome">What the resolver did with those pairs.</param>
/// <param name="Elapsed">Wall-clock time for read, blocking and resolve.</param>
public sealed record SoftMatchSweepReport(
    int Releases,
    int Excluded,
    int Blocks,
    int PairsProposed,
    bool Truncated,
    SoftMatchOutcome Outcome,
    TimeSpan Elapsed,
    int ExcludedWithdrawn = 0)
{
    public static SoftMatchSweepReport Empty { get; } =
        new(0, 0, 0, 0, false, SoftMatchOutcome.Empty, TimeSpan.Zero);
}

/// <summary>
/// Candidate-pair generator for soft matching (§5.3 step 2). Compares the
/// library against itself using token-based blocking to avoid a quadratic
/// cross-product, then hands surviving pairs to <see cref="SoftMatchResolver"/>.
/// Only games are compared; provisional names are excluded.
/// Idempotent: re-runs are absorbed by the resolver's existing-row check.
/// </summary>
public sealed class LibrarySoftMatchSweep
{
    private readonly IReleaseRepository _releases;
    private readonly SoftMatchResolver _resolver;
    private readonly IResolveStateRepository _state;
    private readonly SoftMatchSweepOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<LibrarySoftMatchSweep> _logger;

    public LibrarySoftMatchSweep(
        IReleaseRepository releases,
        SoftMatchResolver resolver,
        IResolveStateRepository state,
        SoftMatchSweepOptions? options = null,
        TimeProvider? clock = null,
        ILogger<LibrarySoftMatchSweep>? logger = null)
    {
        _releases = releases;
        _resolver = resolver;
        _state = state;
        _options = options ?? SoftMatchSweepOptions.Default;
        _clock = clock ?? TimeProvider.System;
        _logger = logger ?? NullLogger<LibrarySoftMatchSweep>.Instance;
    }

    /// <summary>
    /// Compares the library against itself and queues ambiguous pairs for review.
    /// Records completion time only after the resolver commits.
    /// </summary>
    public async Task<SoftMatchSweepReport> SweepAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var identities = await _releases.GetIdentitiesAsync(ct);
        if (identities.Count == 0)
        {
            // Record that the (empty) library has been compared.
            await _state.SetLastSoftMatchSweepAsync(_clock.GetUtcNow(), ct);
            _logger.LogInformation("Soft-match sweep: no releases to compare.");
            return SoftMatchSweepReport.Empty with { Elapsed = stopwatch.Elapsed };
        }

        var entries = Admit(identities, out var excluded);
        var requests = BuildRequests(entries, ct, out var blocks, out var pairs, out var truncated);

        var outcome = requests.Count == 0
            ? SoftMatchOutcome.Empty
            : await _resolver.ResolveAsync(requests, ct);

        await _state.SetLastSoftMatchSweepAsync(_clock.GetUtcNow(), ct);

        stopwatch.Stop();
        _logger.LogInformation(
            "Soft-match sweep: {Releases} releases ({Excluded} excluded), {Blocks} blocks, "
            + "{Pairs} pairs proposed, {Queued} queued for review, {Rescored} rescored on new "
            + "metadata, {Withdrawn} withdrawn, in {Elapsed:n1}s.{Truncated}",
            entries.Count, excluded, blocks, pairs, outcome.Queued, outcome.Rescored,
            outcome.Withdrawn, stopwatch.Elapsed.TotalSeconds,
            truncated ? " Pass was truncated at the comparison ceiling and will resume next run." : string.Empty);

        return new SoftMatchSweepReport(
            entries.Count, excluded, blocks, pairs, truncated, outcome, stopwatch.Elapsed);
    }

    /// <summary>
    /// Turns rows into normalised subjects, dropping the ones there is no point
    /// matching.
    /// </summary>
    private static List<Entry> Admit(IReadOnlyList<ReleaseIdentity> identities, out int excluded)
    {
        var entries = new List<Entry>(identities.Count);
        excluded = 0;

        foreach (var identity in identities)
        {
            // Placeholder names are appid-derived; comparing them is meaningless.
            if (identity.NameIsProvisional)
            {
                excluded++;
                continue;
            }

            var normalized = TitleNormalizer.Normalize(identity.MatchTitle);
            if (normalized.IsEmpty)
            {
                excluded++;
                continue;
            }

            entries.Add(new Entry(
                new MatchSubject
                {
                    ReleaseId = identity.ReleaseId,
                    Title = identity.MatchTitle,
                    ReleaseYear = identity.FirstReleaseYear,
                    Publisher = identity.Publisher,

                    // No cover-hash pipeline yet; left null so the signal does not fire.
                    CoverPerceptualHash = null,
                },
                normalized,
                identity.WorkId));
        }

        return entries;
    }

    /// <summary>
    /// Blocking: index every release by its rare title tokens, then propose only
    /// the pairs that share one.
    /// </summary>
    private List<SoftMatchRequest> BuildRequests(
        List<Entry> entries,
        CancellationToken ct,
        out int blocks,
        out int pairs,
        out bool truncated)
    {
        blocks = 0;
        pairs = 0;
        truncated = false;

        if (entries.Count < 2)
        {
            return [];
        }

        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var token in entry.DistinctTokens)
            {
                frequency[token] = frequency.TryGetValue(token, out var n) ? n + 1 : 1;
            }
        }

        // Deterministic order: entries arrive in release-id order, tokens in title order.
        var index = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            foreach (var token in BlockingKeys(entries[i], frequency))
            {
                if (!index.TryGetValue(token, out var bucket))
                {
                    index[token] = bucket = [];
                }

                bucket.Add(i);
            }
        }

        // Each pair is attached to the LOWER index only, so it is proposed once.
        var proposed = new HashSet<(int Low, int High)>();
        var possibilities = new Dictionary<int, List<MatchSubject>>();

        foreach (var bucket in index.Values)
        {
            ct.ThrowIfCancellationRequested();

            if (bucket.Count < 2)
            {
                continue;
            }

            var producedAPair = false;
            for (var a = 0; a < bucket.Count && !truncated; a++)
            {
                for (var b = a + 1; b < bucket.Count; b++)
                {
                    var low = bucket[a];
                    var high = bucket[b];

                    // Two releases of one work are already correctly modelled as separate.
                    if (entries[low].WorkId == entries[high].WorkId)
                    {
                        continue;
                    }

                    if (!proposed.Add((low, high)))
                    {
                        continue;
                    }

                    if (proposed.Count > _options.MaxComparisons)
                    {
                        proposed.Remove((low, high));
                        truncated = true;
                        break;
                    }

                    if (!possibilities.TryGetValue(low, out var list))
                    {
                        possibilities[low] = list = [];
                    }

                    list.Add(entries[high].Subject);
                    producedAPair = true;
                }
            }

            if (producedAPair)
            {
                blocks++;
            }

            if (truncated)
            {
                break;
            }
        }

        pairs = proposed.Count;

        // Emit in release-id order for deterministic resolver output.
        var requests = new List<SoftMatchRequest>(possibilities.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            if (possibilities.TryGetValue(i, out var list))
            {
                requests.Add(new SoftMatchRequest(entries[i].Subject, list));
            }
        }

        return requests;
    }

    /// <summary>
    /// The tokens a release is indexed under: everything rare enough to be
    /// discriminating, or — when nothing is — its single rarest token, so no
    /// release is left out of the index entirely.
    /// </summary>
    private IEnumerable<string> BlockingKeys(Entry entry, Dictionary<string, int> frequency)
    {
        var rare = new List<string>();
        string? rarest = null;
        var rarestCount = int.MaxValue;

        foreach (var token in entry.DistinctTokens)
        {
            var count = frequency[token];
            if (count <= _options.MaxBlockSize)
            {
                rare.Add(token);
            }

            // Ordinal tie-break for determinism.
            if (count < rarestCount
                || (count == rarestCount && string.CompareOrdinal(token, rarest) < 0))
            {
                rarest = token;
                rarestCount = count;
            }
        }

        return rare.Count > 0 ? rare : rarest is null ? [] : [rarest];
    }

    private sealed class Entry
    {
        public Entry(MatchSubject subject, NormalizedTitle title, long workId)
        {
            Subject = subject;
            WorkId = workId;

            var distinct = new List<string>(title.Tokens.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in title.Tokens)
            {
                if (seen.Add(token))
                {
                    distinct.Add(token);
                }
            }

            DistinctTokens = distinct;
        }

        public MatchSubject Subject { get; }

        public long WorkId { get; }

        /// <summary>Title tokens, de-duplicated, in title order.</summary>
        public IReadOnlyList<string> DistinctTokens { get; }
    }
}
