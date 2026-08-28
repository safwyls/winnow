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
    /// A token shared by more than this many releases stops being a blocking
    /// key. "simulator", "of", "2" and "edition" are in hundreds of titles, and
    /// a block of 400 releases is 80,000 pairs on its own — all of them
    /// comparisons between games whose only commonality is the word "of".
    ///
    /// <para>Every release still lands in at least one block: if all of its
    /// tokens are this common, its rarest one is used regardless. A release
    /// that could not be blocked at all would be invisible to the matcher
    /// forever, which is a worse failure than one oversized block.</para>
    /// </summary>
    public int MaxBlockSize { get; init; } = 60;

    /// <summary>
    /// Hard ceiling on pairs proposed in one sweep. Not expected to bind — a
    /// 3,000-release library blocks down to a few tens of thousands of pairs —
    /// but the blocking is heuristic, and a library that somehow defeats it
    /// must degrade into a partial sweep rather than into a startup that never
    /// finishes. A truncated sweep is reported as truncated and is safe to
    /// re-run: everything it queued stays queued, and it walks releases in id
    /// order, so the same prefix is examined again and the pass is idempotent
    /// rather than randomly sampled.
    /// </summary>
    public int MaxComparisons { get; init; } = 250_000;
}

/// <summary>What one <see cref="LibrarySoftMatchSweep.SweepAsync"/> pass did.</summary>
/// <param name="Releases">Releases the sweep considered.</param>
/// <param name="Excluded">
/// Releases skipped before matching: a provisional placeholder name, or a title
/// that normalises to nothing.
/// </param>
/// <param name="Blocks">Blocking keys that produced at least one pair.</param>
/// <param name="PairsProposed">Distinct release pairs handed to the resolver.</param>
/// <param name="Truncated">
/// True when <see cref="SoftMatchSweepOptions.MaxComparisons"/> cut the pass
/// short. The queue is still correct, just not yet complete.
/// </param>
/// <param name="Outcome">What the resolver did with those pairs.</param>
/// <param name="Elapsed">Wall-clock time for read, blocking and resolve.</param>
public sealed record SoftMatchSweepReport(
    int Releases,
    int Excluded,
    int Blocks,
    int PairsProposed,
    bool Truncated,
    SoftMatchOutcome Outcome,
    TimeSpan Elapsed)
{
    public static SoftMatchSweepReport Empty { get; } =
        new(0, 0, 0, 0, false, SoftMatchOutcome.Empty, TimeSpan.Zero);
}

/// <summary>
/// §5.3 step 2's missing half: the thing that decides WHICH pairs get scored.
/// <see cref="SoftMatcher"/> scores a pair and <see cref="SoftMatchResolver"/>
/// queues the survivors, but until this existed nothing in a real run ever
/// handed either of them a pair, so <c>merge_candidates</c> was permanently
/// empty and the queue's empty state was a claim about the user's library that
/// no code had checked.
///
/// <para><b>Where ambiguity actually comes from in M1.</b> Two sources, and
/// they collapse into one operation:</para>
/// <list type="bullet">
///   <item><b>Releases the hard join did not resolve.</b> §5.3 step 1 merges on
///     an exact external id. Anything it could not place — an appid IGDB has no
///     <c>external_games</c> row for, a store that supplied a title but no id
///     Winnow recognises — becomes its own Work + Release, even when the library
///     already holds that game under another id.</item>
///   <item><b>Duplicates already sitting in the library.</b> The same game owned
///     twice, or re-ingested under a changed appid.</item>
/// </list>
/// <para>Both are answered by the same question — "does this release's title
/// match another release's title?" — and in M1 there is no external corpus to
/// ask it against: the local library IS the corpus. So the sweep compares the
/// library with itself, and gains an IGDB-derived corpus for free when
/// enrichment starts writing IGDB search results as releases, with no change
/// here.</para>
///
/// <para><b>Blocking, not a full cross product.</b> Comparing every release with
/// every other is quadratic, and quadratic on the wrong side of a
/// 3,000-release library: 4.5 million pairs, two title normalisations each.
/// Instead every release is indexed by its rare title tokens, and only releases
/// sharing one are compared. Nothing is lost by it that the matcher would have
/// kept: the title-similarity floor is 0.70, and two titles cannot be 70%
/// similar without sharing a token.</para>
///
/// <para><b>§5.1: never blocks a user-facing path.</b> This holds no
/// <c>HttpClient</c> and makes no network call of any kind — it reads
/// SQLite and computes strings. It is still meant for the background: the
/// caller runs it after the Steam sync (and ideally after enrichment, so it
/// sees real titles rather than <c>App 1203620</c> placeholders it would skip),
/// off the UI thread, with the shutdown token.</para>
///
/// <para><b>Idempotent across re-runs.</b> The sweep holds no cursor and no
/// state beyond "when did I last finish": it re-reads the library and re-scores
/// every pair each pass. Re-runs are absorbed by
/// <see cref="SoftMatchResolver"/>, where an existing row for a pair — pending,
/// confirmed or rejected — blocks the insert, and both answers are terminal.
/// Running this on every launch of a library that never changes writes nothing
/// after the first pass.</para>
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
    /// Compares the library against itself and queues what it cannot tell
    /// apart. Merges nothing — <see cref="SoftMatchResolver"/> is structurally
    /// incapable of it, holding no work, release or ownership repository.
    ///
    /// <para>Records the completion time only after the resolver's transaction
    /// commits, so an interrupted sweep leaves the queue screen saying "not yet
    /// compared" rather than claiming a clean bill of health it did not
    /// earn.</para>
    /// </summary>
    public async Task<SoftMatchSweepReport> SweepAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var identities = await _releases.GetIdentitiesAsync(ct);
        if (identities.Count == 0)
        {
            // An empty library HAS been compared — vacuously, but truthfully.
            // Recording it is what lets the queue say "nothing ambiguous"
            // instead of "not compared yet" on a machine with no Steam install.
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
            // A placeholder name is derived from the appid, so comparing two of
            // them compares two appids. They would all be vetoed on the ordinal
            // anyway ("App 620" is [620], "App 630" is [630]) — excluding them
            // here says why, and keeps them out of the blocking index where
            // they would otherwise sit in one enormous "app" block.
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

                    // Cover hash still has no pipeline in M1 — Winnow.Resolve
                    // owns matching, not imaging (see ICoverHashSource). Left
                    // null so the signal does not fire, rather than guessed at.
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

        // Insertion-ordered by construction: entries arrive in release-id order
        // and tokens are walked in title order, so the blocks — and therefore
        // the order pairs are proposed in — are the same on every run.
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

        // Possibilities are attached to the LOWER index of each pair only, so a
        // pair is proposed once. The resolver canonicalises and de-duplicates
        // again — cheap insurance, since it is what protects the UNIQUE index.
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

                    // Two releases of one work are the Skyrim / Skyrim Special
                    // Edition case: correctly modelled as separate rows already,
                    // and merging them collapses Release into Work, which is the
                    // §5.3 four-layer rule and §9 pitfall 5.
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

        // Emitted in release-id order so the resolver's log, and the order rows
        // reach merge_candidates, are stable run to run.
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

            // Ordinal tie-break so a title whose tokens are all equally common
            // picks the same key on every run.
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
