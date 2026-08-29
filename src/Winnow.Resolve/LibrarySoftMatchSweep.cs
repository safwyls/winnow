using System.Diagnostics;
using System.Globalization;
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
    /// Hard ceiling on pairs proposed in one sweep. A truncated sweep resumes
    /// where it stopped: the next run starts after the last pair this one
    /// accepted and wraps around, so the ceiling delays coverage rather than
    /// denying it.
    /// </summary>
    public int MaxComparisons { get; init; } = 250_000;
}

/// <summary>What one <see cref="LibrarySoftMatchSweep.SweepAsync"/> pass did.</summary>
/// <param name="Releases">Releases the sweep considered.</param>
/// <param name="Excluded">Releases skipped (non-game classification, provisional name, or empty normalised title).</param>
/// <param name="Blocks">Blocking keys that produced at least one pair.</param>
/// <param name="PairsProposed">Distinct release pairs handed to the resolver.</param>
/// <param name="Truncated">
/// True when <see cref="SoftMatchSweepOptions.MaxComparisons"/> cut the pass short.
/// A truncated pass does NOT record a completion time — it did not compare the
/// library, only a window of it — but it does record where to resume.
/// </param>
/// <param name="Outcome">What the resolver did with those pairs.</param>
/// <param name="Elapsed">Wall-clock time for read, blocking and resolve.</param>
/// <param name="ExcludedWithdrawn">
/// Pending pairs retired because no future sweep could propose them: a member is
/// no longer admitted (reclassified as a non-game, renamed to nothing, deleted),
/// the two sides now belong to one work, or their titles no longer share a
/// blocking key. Only proposals are retired; a confirmed or rejected answer is
/// never touched.
/// </param>
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
/// Only games are compared: provisional names and rows a storefront has classified
/// as tools, engine builds or asset packs are excluded.
///
/// <para>Two properties make it safe to run on every launch. It is
/// <b>idempotent</b>: re-runs are absorbed by the resolver's existing-row check.
/// And it is <b>fair</b>: when the comparison ceiling truncates a pass, the
/// resume point is persisted, so the next run examines the pairs this one did not
/// reach instead of re-examining the same prefix forever.</para>
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
    /// Records completion time only after the resolver commits, and only when the
    /// pass actually covered the library.
    /// </summary>
    public async Task<SoftMatchSweepReport> SweepAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var identities = await _releases.GetIdentitiesAsync(ct);
        if (identities.Count == 0)
        {
            // Record that the (empty) library has been compared. Nothing can be
            // pending against a library with no releases — the foreign keys see
            // to that — so there is nothing to reconcile either.
            await _state.SetLastSoftMatchSweepAsync(_clock.GetUtcNow(), ct);
            await _state.SetSoftMatchCursorAsync(null, ct);
            _logger.LogInformation("Soft-match sweep: no releases to compare.");
            return SoftMatchSweepReport.Empty with { Elapsed = stopwatch.Elapsed };
        }

        var entries = Admit(identities, out var excluded);
        var index = BuildIndex(entries);
        var admission = BuildAdmission(entries, index);

        var cursor = SweepCursor.Parse(await _state.GetSoftMatchCursorAsync(ct));
        var pass = BuildRequests(entries, index, cursor, ct);

        // Always call: even a pass that proposes nothing has to reconcile, or a
        // library whose last two matchable releases were just reclassified keeps
        // a question in the queue that nothing will ever ask again.
        var outcome = await _resolver.ResolveAndReconcileAsync(pass.Requests, admission, ct);

        // Only a pass that reached every pair has compared the library. Stamping
        // completion on a truncated pass is how the UI ends up claiming a sweep
        // finished while a permanent tail of it has never been looked at.
        if (pass.Truncated)
        {
            await _state.SetSoftMatchCursorAsync(pass.NextCursor?.Format(), ct);
        }
        else
        {
            await _state.SetLastSoftMatchSweepAsync(_clock.GetUtcNow(), ct);
            await _state.SetSoftMatchCursorAsync(null, ct);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Soft-match sweep: {Releases} releases ({Excluded} excluded), {Blocks} blocks, "
            + "{Pairs} pairs proposed, {Queued} queued for review, {Rescored} rescored on new "
            + "metadata, {Withdrawn} withdrawn, {Retired} retired as unproposable, in "
            + "{Elapsed:n1}s.{Truncated}",
            entries.Count, excluded, pass.Blocks, pass.Pairs, outcome.Queued, outcome.Rescored,
            outcome.Withdrawn, outcome.Retired, stopwatch.Elapsed.TotalSeconds,
            pass.Truncated
                ? $" Pass was truncated at the comparison ceiling; the next run resumes at {pass.NextCursor?.Format()}."
                : string.Empty);

        return new SoftMatchSweepReport(
            entries.Count, excluded, pass.Blocks, pass.Pairs, pass.Truncated, outcome,
            stopwatch.Elapsed, outcome.Retired);
    }

    /// <summary>
    /// Turns rows into normalised subjects, dropping the ones there is no point
    /// matching.
    /// </summary>
    /// <param name="excluded">How many rows were dropped, for any reason.</param>
    private static List<Entry> Admit(IReadOnlyList<ReleaseIdentity> identities, out int excluded)
    {
        var entries = new List<Entry>(identities.Count);
        excluded = 0;

        foreach (var identity in identities)
        {
            // Engine builds, dedicated servers and marketplace asset packs match each
            // other perfectly and truthfully. The title match is right; the question is
            // not worth asking, so it never reaches the queue. An unclassified row —
            // most of a real library — is a game until something says otherwise.
            if (identity.IsNonGame)
            {
                excluded++;
                continue;
            }

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
    /// Indexes every admitted release under its blocking keys. Keys are returned
    /// sorted ordinally: the sweep's fairness depends on a total order over
    /// blocks that does not change between runs, and <c>Dictionary</c>
    /// enumeration order is not one.
    /// </summary>
    private BlockIndex BuildIndex(List<Entry> entries)
    {
        var frequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var token in entry.DistinctTokens)
            {
                frequency[token] = frequency.TryGetValue(token, out var n) ? n + 1 : 1;
            }
        }

        var blocks = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var keysByEntry = new List<HashSet<string>>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in BlockingKeys(entries[i], frequency))
            {
                if (!keys.Add(token))
                {
                    continue;
                }

                if (!blocks.TryGetValue(token, out var bucket))
                {
                    blocks[token] = bucket = [];
                }

                // Entries arrive in release-id order, so buckets are too.
                bucket.Add(i);
            }

            keysByEntry.Add(keys);
        }

        var orderedKeys = new List<string>(blocks.Keys);
        orderedKeys.Sort(StringComparer.Ordinal);

        return new BlockIndex(blocks, orderedKeys, keysByEntry);
    }

    /// <summary>
    /// The library as the resolver needs to see it in order to reconcile pending
    /// rows: who is admitted, which work they belong to, what they are indexed
    /// under.
    /// </summary>
    private static SoftMatchAdmission BuildAdmission(List<Entry> entries, BlockIndex index)
    {
        var builder = SoftMatchAdmission.CreateBuilder(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            builder.Add(entries[i].Subject, entries[i].WorkId, index.KeysByEntry[i]);
        }

        return builder.Build();
    }

    /// <summary>
    /// Walks the blocking index and proposes the pairs that share a key, starting
    /// just after <paramref name="cursor"/> and wrapping around the end.
    ///
    /// <para>The walk defines one total, stable order over candidate pairs —
    /// blocks by ordinal key, pairs within a block by release id — so a position
    /// in it means the same thing on the next launch. Starting from the last
    /// position instead of from the beginning is the whole fix for a capped
    /// sweep: the previous shape re-proposed the same prefix on every launch and
    /// the pairs past the cap were never compared even once.</para>
    /// </summary>
    private SweepPass BuildRequests(
        List<Entry> entries, BlockIndex index, SweepCursor? cursor, CancellationToken ct)
    {
        if (entries.Count < 2 || index.OrderedKeys.Count == 0)
        {
            return new SweepPass([], 0, 0, false, null);
        }

        var blocks = 0;
        var truncated = false;
        SweepCursor? nextCursor = null;

        var proposed = new HashSet<(long Low, long High)>();
        var possibilities = new Dictionary<int, List<MatchSubject>>();

        var startBlock = StartBlock(index.OrderedKeys, cursor);

        // One extra step when resuming: the final visit re-enters the starting
        // block without the skip, so the pairs before the cursor are covered too
        // and a full circle really is full coverage. Dedup makes the revisit free.
        var steps = cursor is null ? index.OrderedKeys.Count : index.OrderedKeys.Count + 1;

        for (var step = 0; step < steps && !truncated; step++)
        {
            ct.ThrowIfCancellationRequested();

            var key = index.OrderedKeys[(startBlock + step) % index.OrderedKeys.Count];
            var bucket = index.Blocks[key];
            if (bucket.Count < 2)
            {
                continue;
            }

            // Only the first visit to the resume block skips; the wrap-around
            // visit at the end must see everything.
            var skipTo = step == 0 && cursor is { } c && string.Equals(key, c.BlockKey, StringComparison.Ordinal)
                ? c
                : (SweepCursor?)null;

            var producedAPair = false;

            for (var a = 0; a < bucket.Count && !truncated; a++)
            {
                for (var b = a + 1; b < bucket.Count; b++)
                {
                    var low = entries[bucket[a]];
                    var high = entries[bucket[b]];

                    // Two releases of one work are already correctly modelled as separate.
                    if (low.WorkId == high.WorkId)
                    {
                        continue;
                    }

                    var pair = (low.ReleaseId, high.ReleaseId);

                    // Everything up to and including the cursor was proposed by
                    // the run that set it.
                    if (skipTo is { } resume && !IsAfter(pair, resume))
                    {
                        continue;
                    }

                    if (!proposed.Add(pair))
                    {
                        continue;
                    }

                    if (proposed.Count > _options.MaxComparisons)
                    {
                        proposed.Remove(pair);
                        truncated = true;
                        break;
                    }

                    if (!possibilities.TryGetValue(bucket[a], out var list))
                    {
                        possibilities[bucket[a]] = list = [];
                    }

                    list.Add(high.Subject);
                    producedAPair = true;

                    // Where a truncated pass will resume: strictly after the last
                    // pair it accepted, so every run makes forward progress.
                    nextCursor = new SweepCursor(pair.Item1, pair.Item2, key);
                }
            }

            if (producedAPair)
            {
                blocks++;
            }
        }

        // Emit in release-id order for deterministic resolver output.
        var requests = new List<SoftMatchRequest>(possibilities.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            if (possibilities.TryGetValue(i, out var list))
            {
                requests.Add(new SoftMatchRequest(entries[i].Subject, list));
            }
        }

        return new SweepPass(requests, blocks, proposed.Count, truncated, nextCursor);
    }

    /// <summary>
    /// Where to re-enter the block order: the cursor's own block if it still
    /// exists, otherwise the next one after it, wrapping to the start. A blocking
    /// key can disappear between runs — a title was enriched, a release removed —
    /// and resuming at the next surviving key keeps progress moving rather than
    /// silently restarting from the beginning.
    /// </summary>
    private static int StartBlock(List<string> orderedKeys, SweepCursor? cursor)
    {
        if (cursor is not { } resume)
        {
            return 0;
        }

        for (var i = 0; i < orderedKeys.Count; i++)
        {
            if (string.CompareOrdinal(orderedKeys[i], resume.BlockKey) >= 0)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>True when a pair falls after the cursor in the within-block order (release id, then release id).</summary>
    private static bool IsAfter((long Low, long High) pair, SweepCursor cursor)
        => pair.Low != cursor.Low ? pair.Low > cursor.Low : pair.High > cursor.High;

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

    /// <summary>What one walk of the blocking index produced.</summary>
    private sealed record SweepPass(
        List<SoftMatchRequest> Requests,
        int Blocks,
        int Pairs,
        bool Truncated,
        SweepCursor? NextCursor);

    /// <summary>The blocking index plus the stable key order the cursor is expressed in.</summary>
    private sealed record BlockIndex(
        Dictionary<string, List<int>> Blocks,
        List<string> OrderedKeys,
        List<HashSet<string>> KeysByEntry);

    /// <summary>
    /// A position in the pair walk, persisted between runs as
    /// <c>low:high:blockKey</c>. Numbers first so the key — a title token, which
    /// may contain anything the normaliser allows — is the unparsed remainder and
    /// needs no escaping.
    /// </summary>
    private readonly record struct SweepCursor(long Low, long High, string BlockKey)
    {
        public static SweepCursor? Parse(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var first = raw.IndexOf(':', StringComparison.Ordinal);
            if (first <= 0)
            {
                return null;
            }

            var second = raw.IndexOf(':', first + 1);
            if (second < 0)
            {
                return null;
            }

            // An unreadable cursor is treated as absent: the next sweep starts
            // from the beginning, which is slower than resuming but never wrong.
            return long.TryParse(
                    raw.AsSpan(0, first), NumberStyles.Integer, CultureInfo.InvariantCulture, out var low)
                && long.TryParse(
                    raw.AsSpan(first + 1, second - first - 1),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var high)
                ? new SweepCursor(low, high, raw[(second + 1)..])
                : null;
        }

        public string Format()
            => string.Create(CultureInfo.InvariantCulture, $"{Low}:{High}:{BlockKey}");
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

        public long ReleaseId => Subject.ReleaseId;

        public long WorkId { get; }

        /// <summary>Title tokens, de-duplicated, in title order.</summary>
        public IReadOnlyList<string> DistinctTokens { get; }
    }
}
