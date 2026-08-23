using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Hoard.Resolve;
using Hoard.Resolve.Matching;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The sweep is what makes §5.3 step 2 exist at runtime. Everything under it —
/// the matcher and the queue writer — was already tested and already correct;
/// what was missing was anything that ever handed them a pair, which is why
/// <c>merge_candidates</c> stayed empty in a real run and the queue's empty
/// state was a claim nobody had checked.
///
/// <para>These run against a real migrated SQLite file and the real
/// repositories. No network is involved anywhere: the sweep reads the database
/// and compares strings, which is the whole reason it is safe to run behind a
/// scan (§5.1).</para>
/// </summary>
public sealed class LibrarySoftMatchSweepTests
{
    // ── The point of the whole thing ─────────────────────────────────────────

    [Fact]
    public async Task A_duplicate_release_reaches_the_queue()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("The Witcher 3: Wild Hunt", 2015);
        var right = await fixture.AddAsync("The Witcher III: Wild Hunt", 2015);

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(1, report.Outcome.Queued);
        Assert.Equal(0, report.Outcome.AutoMerged);

        var pending = await fixture.Candidates.GetPendingAsync();
        var row = Assert.Single(pending);
        Assert.Equal(Math.Min(left, right), row.LeftReleaseId);
        Assert.Equal(Math.Max(left, right), row.RightReleaseId);
        Assert.Equal(MergeCandidateStatuses.Pending, row.Status);
    }

    /// <summary>
    /// The library the sweep has to survive: mostly unrelated games, one real
    /// duplicate. Precision is the property under test — a queue full of noise
    /// is a queue nobody clears.
    /// </summary>
    [Fact]
    public async Task An_ordinary_library_produces_only_the_real_duplicate()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("Portal", 2007);
        await fixture.AddAsync("Portal 2", 2011);
        await fixture.AddAsync("Half-Life", 1998);
        await fixture.AddAsync("Half-Life 2", 2004);
        await fixture.AddAsync("Half-Life 2: Episode One", 2006);
        await fixture.AddAsync("Half-Life 2: Episode Two", 2007);
        await fixture.AddAsync("Prey", 2006);
        await fixture.AddAsync("Prey", 2017);
        await fixture.AddAsync("Mega Man X", 1993);
        await fixture.AddAsync("Mega Man 10", 2010);
        await fixture.AddAsync("Doom", 2016);
        await fixture.AddAsync("Doom Eternal", 2020);

        // The one genuine pair: same game, two store spellings.
        await fixture.AddAsync("Counter-Strike: Global Offensive", 2012);
        await fixture.AddAsync("Counter Strike Global Offensive", 2012);

        var report = await fixture.Sweep.SweepAsync();

        var pending = await fixture.Candidates.GetPendingAsync();
        var titles = await fixture.TitlesOfAsync(pending);
        // Canonicalised (lower id, higher id), so the punctuated spelling —
        // seeded first — is the left side.
        Assert.Equal(
            ["Counter-Strike: Global Offensive | Counter Strike Global Offensive"],
            titles);
        Assert.Equal(1, report.Outcome.Queued);
    }

    // ── Idempotency (the sweep runs on every launch) ─────────────────────────

    [Fact]
    public async Task A_second_sweep_writes_nothing_new()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("The Witcher 3: Wild Hunt", 2015);
        await fixture.AddAsync("The Witcher III: Wild Hunt", 2015);

        var first = await fixture.Sweep.SweepAsync();
        var second = await fixture.Sweep.SweepAsync();

        Assert.Equal(1, first.Outcome.Queued);
        Assert.Equal(0, second.Outcome.Queued);
        Assert.Equal(1, second.Outcome.AlreadyPending);

        // Same pairs examined both times: the pass is a re-run, not a resume.
        Assert.Equal(first.PairsProposed, second.PairsProposed);
        Assert.Single(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>
    /// The answer the user already gave is the one thing a re-scan must never
    /// undo. Re-asking is how a confirmation queue teaches people to click
    /// through it without reading.
    /// </summary>
    [Fact]
    public async Task A_rejected_pair_is_never_resurrected()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("Prey", 2017);
        await fixture.AddAsync("Prey", 2017);

        await fixture.Sweep.SweepAsync();
        var queued = Assert.Single(await fixture.Candidates.GetPendingAsync());
        await fixture.Candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Rejected);

        var second = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, second.Outcome.Queued);
        Assert.Equal(1, second.Outcome.PreviouslyRejected);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    // ── What the sweep refuses to compare ────────────────────────────────────

    /// <summary>
    /// Two releases of one work are the Skyrim / Skyrim Special Edition case —
    /// already correctly modelled as separate rows. Offering to merge them is
    /// offering to collapse Release into Work (§9 pitfall 5).
    /// </summary>
    [Fact]
    public async Task Two_releases_of_one_work_are_never_offered_as_a_pair()
    {
        using var fixture = new SweepFixture();
        var workId = await fixture.Works.InsertAsync(new Work { Name = "Prey", FirstReleaseYear = 2017 });
        await fixture.AddReleaseAsync(workId, "Prey");
        await fixture.AddReleaseAsync(workId, "Prey");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.PairsProposed);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>
    /// A placeholder name is derived from the appid, so comparing two of them
    /// compares two appids and learns nothing.
    /// </summary>
    [Fact]
    public async Task Provisional_placeholder_names_are_excluded()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("App 620", provisional: true);
        await fixture.AddAsync("App 630", provisional: true);
        await fixture.AddAsync("Portal 2", 2011);

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(2, report.Excluded);
        Assert.Equal(1, report.Releases);
        Assert.Equal(0, report.PairsProposed);
    }

    // ── Blocking ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Blocking is what keeps the sweep off the quadratic path. It must not
    /// cost recall: two titles cannot clear the 0.70 similarity floor without
    /// sharing a token, so a pair the blocking drops is a pair the matcher
    /// would have discarded.
    /// </summary>
    [Fact]
    public async Task Blocking_compares_far_fewer_pairs_than_the_cross_product()
    {
        using var fixture = new SweepFixture();
        for (var i = 0; i < 60; i++)
        {
            await fixture.AddAsync($"Unrelated Game {i} Zulu{i}", 2000 + (i % 20));
        }

        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(62, report.Releases);
        Assert.True(
            report.PairsProposed < 62 * 61 / 2,
            $"blocking proposed {report.PairsProposed} pairs, no better than the cross product");

        // And the duplicate still comes out.
        Assert.Single(await fixture.Candidates.GetPendingAsync());
    }

    [Fact]
    public async Task The_comparison_ceiling_truncates_rather_than_running_away()
    {
        using var fixture = new SweepFixture(new SoftMatchSweepOptions { MaxComparisons = 1 });
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);

        var report = await fixture.Sweep.SweepAsync();

        Assert.True(report.Truncated);
        Assert.Equal(1, report.PairsProposed);
    }

    // ── State the empty screen reads ─────────────────────────────────────────

    [Fact]
    public async Task A_completed_sweep_is_recorded()
    {
        using var fixture = new SweepFixture();
        Assert.Null(await fixture.ResolveState.GetLastSoftMatchSweepAsync());

        await fixture.AddAsync("Portal 2", 2011);
        await fixture.Sweep.SweepAsync();

        Assert.NotNull(await fixture.ResolveState.GetLastSoftMatchSweepAsync());
    }

    /// <summary>
    /// An empty library HAS been compared, vacuously but truthfully — which is
    /// what lets a machine with no Steam install read "nothing ambiguous"
    /// rather than "not compared yet".
    /// </summary>
    [Fact]
    public async Task An_empty_library_still_counts_as_swept()
    {
        using var fixture = new SweepFixture();

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.Releases);
        Assert.NotNull(await fixture.ResolveState.GetLastSoftMatchSweepAsync());
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed class SweepFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 500000;

        public SweepFixture(SoftMatchSweepOptions? options = null)
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Candidates = new MergeCandidateRepository(_db.Factory);
            ResolveState = new ResolveStateRepository(_db.Factory);

            Sweep = new LibrarySoftMatchSweep(
                Releases,
                new SoftMatchResolver(new SoftMatcher(), Candidates, _db.Factory),
                ResolveState,
                options);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public IResolveStateRepository ResolveState { get; }

        public LibrarySoftMatchSweep Sweep { get; }

        /// <summary>One work, one release, one Steam id — the M1 shape.</summary>
        public async Task<long> AddAsync(string title, int? year = null, bool provisional = false)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = title,
                FirstReleaseYear = year,
                NameIsProvisional = provisional,
            });

            return await AddReleaseAsync(workId, title);
        }

        public async Task<long> AddReleaseAsync(long workId, string name)
        {
            var releaseId = await Releases.InsertAsync(new Release { WorkId = workId, Name = name });
            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

            return releaseId;
        }

        /// <summary>"left | right" per pending row, sorted, so a failure names the games.</summary>
        public async Task<IReadOnlyList<string>> TitlesOfAsync(IReadOnlyList<MergeCandidate> pending)
        {
            var described = new List<string>(pending.Count);
            foreach (var candidate in pending)
            {
                var left = await Releases.GetAsync(candidate.LeftReleaseId);
                var right = await Releases.GetAsync(candidate.RightReleaseId);
                described.Add($"{left?.Name} | {right?.Name}");
            }

            described.Sort(StringComparer.Ordinal);
            return described;
        }

        public void Dispose() => _db.Dispose();
    }
}
