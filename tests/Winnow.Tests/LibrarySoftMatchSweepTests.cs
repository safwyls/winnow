using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

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

    /// <summary>
    /// The half of the ceiling that was missing. Three identical releases make
    /// three pairs; a cap of one means no single pass can see them all. The old
    /// walk always restarted at the same deterministic prefix, so pair one was
    /// re-proposed on every launch forever and pairs two and three were never
    /// compared even once — a permanently starved tail behind a knob documented
    /// as "safe to re-run".
    ///
    /// <para>Each run must therefore reach pairs the last one omitted, and three
    /// runs must between them ask about all three pairs.</para>
    /// </summary>
    [Fact]
    public async Task A_truncated_sweep_resumes_where_it_stopped_instead_of_restarting()
    {
        using var fixture = new SweepFixture(new SoftMatchSweepOptions { MaxComparisons = 1 });
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);

        var first = await fixture.Sweep.SweepAsync();
        Assert.True(first.Truncated);
        Assert.Equal(1, first.Outcome.Queued);

        // The pass after a truncated one must examine something new, not
        // re-examine the pair it already queued.
        var second = await fixture.Sweep.SweepAsync();
        Assert.True(second.Truncated);
        Assert.Equal(1, second.Outcome.Queued);
        Assert.Equal(0, second.Outcome.AlreadyPending);

        var third = await fixture.Sweep.SweepAsync();
        Assert.Equal(1, third.Outcome.Queued);

        // All three pairs of the triangle, each asked exactly once.
        var pending = await fixture.Candidates.GetPendingAsync();
        Assert.Equal(3, pending.Count);
        Assert.Equal(
            3,
            pending.Select(p => (p.LeftReleaseId, p.RightReleaseId)).Distinct().Count());

        // Nothing was retired along the way: a pair outside this pass's window
        // is "not reached yet", never "no longer valid".
        Assert.Equal(0, first.ExcludedWithdrawn + second.ExcludedWithdrawn + third.ExcludedWithdrawn);
    }

    /// <summary>
    /// A truncated pass compared a window, not the library, and the empty-state
    /// copy the merge queue renders is a claim about the library. Stamping
    /// completion here is how the UI ends up saying "nothing ambiguous" about
    /// pairs nothing has looked at.
    /// </summary>
    [Fact]
    public async Task A_truncated_sweep_is_not_recorded_as_a_completed_one()
    {
        using var fixture = new SweepFixture(new SoftMatchSweepOptions { MaxComparisons = 1 });
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);

        // However many times it runs. A ceiling this low can never cover the
        // triangle in one pass, and the honest reading of that is "not swept".
        for (var run = 0; run < 4; run++)
        {
            Assert.True((await fixture.Sweep.SweepAsync()).Truncated);
            Assert.Null(await fixture.ResolveState.GetLastSoftMatchSweepAsync());
            Assert.NotNull(await fixture.ResolveState.GetSoftMatchCursorAsync());
        }
    }

    /// <summary>
    /// The other side of it: a pass that did cover the library records the
    /// completion the empty state reads, and drops the resume point so the next
    /// run starts from the top rather than inheriting a stale position.
    /// </summary>
    [Fact]
    public async Task A_completed_sweep_clears_the_resume_point()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);
        await fixture.AddAsync("Hollow Knight", 2017);

        var report = await fixture.Sweep.SweepAsync();

        Assert.False(report.Truncated);
        Assert.Equal(3, report.PairsProposed);
        Assert.NotNull(await fixture.ResolveState.GetLastSoftMatchSweepAsync());
        Assert.Null(await fixture.ResolveState.GetSoftMatchCursorAsync());
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

    // ── Only games are worth asking a human about ────────────────────────────

    /// <summary>
    /// The case that put 27 of 28 pairs in a real review queue. Epic's
    /// authenticated library service returns bare entitlements, so the engine
    /// builds and marketplace asset packs it hands over reach the database as
    /// works — and Epic issues the same display name under two different catalog
    /// item ids, so "Infinity Blade: Weapons" really does duplicate itself. The
    /// title match is correct; the question is not worth asking.
    /// </summary>
    [Fact]
    public async Task Epic_engine_and_asset_entitlements_are_never_compared()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync(
            "Infinity Blade: Weapons",
            epicCategories: "asset-format/game-engine,type/format-item,asset-format");
        await fixture.AddAsync(
            "Infinity Blade: Weapons",
            epicCategories: "assets/showcasedemos,assets");
        await fixture.AddAsync("Unreal Engine", epicCategories: "engines,engines/ue4");
        await fixture.AddAsync("Unreal Engine Chaos", epicCategories: "engines/unstable,engines,engines/ue4");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(4, report.Excluded);
        Assert.Equal(0, report.PairsProposed);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>Valve's vocabulary, same rule: a dedicated server is not a game.</summary>
    [Fact]
    public async Task Steam_tools_are_never_compared()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("Palworld Dedicated Server", steamAppType: "Tool");
        await fixture.AddAsync("Palworld Dedicated Server", steamAppType: "tool");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(2, report.Excluded);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>
    /// The regression this filter could most easily cause. Most of a real
    /// library has never been probed, so an unclassified row is the NORMAL case:
    /// if null read as "not a game" the sweep would compare nothing at all.
    /// </summary>
    [Fact]
    public async Task An_unclassified_release_is_still_compared()
    {
        using var fixture = new SweepFixture();
        await fixture.AddAsync("The Witcher 3: Wild Hunt", 2015);
        await fixture.AddAsync("The Witcher III: Wild Hunt", 2015);

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.Excluded);
        Assert.Equal(1, report.Outcome.Queued);
    }

    /// <summary>
    /// A pair queued before anything knew what these rows were. The classifying
    /// pass writes <c>epic_categories</c>, the next sweep stops admitting them —
    /// and the rows already in the queue have to go, because nothing will ever
    /// propose them again and the user cannot clear them except by answering a
    /// question about two asset packs.
    /// </summary>
    [Fact]
    public async Task A_pending_pair_is_retired_once_its_releases_are_classified()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("Infinity Blade: Ice Lands");
        var right = await fixture.AddAsync("Infinity Blade: Ice Lands");

        Assert.Equal(1, (await fixture.Sweep.SweepAsync()).Outcome.Queued);
        Assert.Single(await fixture.Candidates.GetPendingAsync());

        await fixture.ClassifyEpicAsync(left, "assets/showcasedemos,assets");
        await fixture.ClassifyEpicAsync(right, "asset-format/game-engine,type/format-item");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(1, report.ExcludedWithdrawn);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>
    /// Retirement is a proposal being retracted, never a decision being erased.
    /// Both terminal statuses survive it — a pair the user answered stays
    /// answered, which is what stops a rejected pair coming back.
    /// </summary>
    [Fact]
    public async Task Retiring_never_touches_an_answered_pair()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("Infinity Blade: Ice Lands");
        var right = await fixture.AddAsync("Infinity Blade: Ice Lands");

        await fixture.Sweep.SweepAsync();
        var answered = Assert.Single(await fixture.Candidates.GetPendingAsync());
        await fixture.Candidates.SetStatusAsync(answered.Id, MergeCandidateStatuses.Rejected);

        await fixture.ClassifyEpicAsync(left, "assets/showcasedemos,assets");
        await fixture.ClassifyEpicAsync(right, "assets/showcasedemos,assets");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.ExcludedWithdrawn);
        Assert.Equal(
            MergeCandidateStatuses.Rejected,
            (await fixture.Candidates.FindByPairAsync(left, right))!.Status);
    }

    /// <summary>
    /// Non-game reclassification was only the first way a queued question can go
    /// stale. Enrichment rewrites titles too, and a renamed release shares no
    /// blocking key with its old partner — so the sweep that would have
    /// re-proposed the pair never generates it again, and the question sits in
    /// the queue permanently, answerable only by answering it.
    ///
    /// <para>The sweep submits only what its current blocking pass produced, so
    /// nothing here reaches the resolver as a proposal. Reconciliation is what
    /// notices.</para>
    /// </summary>
    [Fact]
    public async Task A_pending_pair_is_retired_once_a_title_moves_out_of_its_blocking_key()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("Bastion", 2011);
        var right = await fixture.AddAsync("Bastion", 2011);

        Assert.Equal(1, (await fixture.Sweep.SweepAsync()).Outcome.Queued);

        // Enrichment learns what the second row actually is.
        await fixture.Releases.UpdateNameAsync(right, "Transistor");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.PairsProposed);
        Assert.Equal(1, report.ExcludedWithdrawn);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>Same rule, same protection: a rename does not erase an answer.</summary>
    [Fact]
    public async Task Retiring_a_renamed_pair_never_touches_an_answered_one()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("Bastion", 2011);
        var right = await fixture.AddAsync("Bastion", 2011);

        await fixture.Sweep.SweepAsync();
        var answered = Assert.Single(await fixture.Candidates.GetPendingAsync());
        await fixture.Candidates.SetStatusAsync(answered.Id, MergeCandidateStatuses.Rejected);

        await fixture.Releases.UpdateNameAsync(right, "Transistor");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.ExcludedWithdrawn);
        Assert.Equal(
            MergeCandidateStatuses.Rejected,
            (await fixture.Candidates.FindByPairAsync(left, right))!.Status);
    }

    /// <summary>
    /// A pair left over from before the two sides were joined under one work.
    /// Blocking refuses to emit it (§9 pitfall 5), so it can never be
    /// re-proposed — which is exactly why it has to be retired rather than
    /// waited on.
    /// </summary>
    [Fact]
    public async Task A_pending_pair_whose_sides_now_share_a_work_is_retired()
    {
        using var fixture = new SweepFixture();
        var workId = await fixture.Works.InsertAsync(new Work { Name = "Prey", FirstReleaseYear = 2017 });
        var left = await fixture.AddReleaseAsync(workId, "Prey");
        var right = await fixture.AddReleaseAsync(workId, "Prey");

        // The row an earlier build queued, before the releases were joined.
        await fixture.Candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = Math.Min(left, right),
            RightReleaseId = Math.Max(left, right),
            Score = 0.65,
            Status = MergeCandidateStatuses.Pending,
        });

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.PairsProposed);
        Assert.Equal(1, report.ExcludedWithdrawn);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>
    /// The whole of the withdrawal machinery, reached through a link instead of
    /// through a destructive merge. Once the user has said two works are one
    /// game, the sweep resolves both sides to the same work, blocking refuses to
    /// emit the pair, and the existing retire path takes the leftover row away
    /// on its own. No new machinery, and no way for a linked pair to be asked
    /// about twice.
    /// </summary>
    [Fact]
    public async Task A_pending_pair_whose_works_have_been_linked_is_retired()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("Prey", 2017);
        var right = await fixture.AddAsync("Prey", 2017);

        // The row this library's own sweep would queue.
        var first = await fixture.Sweep.SweepAsync();
        Assert.Equal(1, first.Outcome.Queued);
        Assert.Single(await fixture.Candidates.GetPendingAsync());

        var leftWork = (await fixture.Releases.GetAsync(left))!.WorkId;
        var rightWork = (await fixture.Releases.GetAsync(right))!.WorkId;
        await fixture.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = leftWork,
            ChildWorkIds = [rightWork],
        });

        var second = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, second.PairsProposed);
        Assert.Equal(1, second.ExcludedWithdrawn);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
    }

    /// <summary>
    /// And once retracted, the pair is an ordinary proposable pair again. The
    /// sweep is a living view of the library, not a one-way ratchet.
    /// </summary>
    [Fact]
    public async Task Retracting_a_link_makes_the_pair_proposable_again()
    {
        using var fixture = new SweepFixture();
        var left = await fixture.AddAsync("Prey", 2017);
        var right = await fixture.AddAsync("Prey", 2017);

        var leftWork = (await fixture.Releases.GetAsync(left))!.WorkId;
        var rightWork = (await fixture.Releases.GetAsync(right))!.WorkId;
        var actId = await fixture.Links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = leftWork,
            ChildWorkIds = [rightWork],
        });

        Assert.Equal(0, (await fixture.Sweep.SweepAsync()).PairsProposed);

        Assert.True(await fixture.Links.RetractActAsync(actId));

        var after = await fixture.Sweep.SweepAsync();
        Assert.Equal(1, after.PairsProposed);
        Assert.Single(await fixture.Candidates.GetPendingAsync());
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
            Links = new IdentityLinkRepository(_db.Factory);

            Sweep = new LibrarySoftMatchSweep(
                Releases,
                new SoftMatchResolver(new SoftMatcher(), Candidates, _db.Factory),
                ResolveState,
                options,
                links: Links);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public IResolveStateRepository ResolveState { get; }

        public IIdentityLinkRepository Links { get; }

        public LibrarySoftMatchSweep Sweep { get; }

        /// <summary>One work, one release, one Steam id — the M1 shape.</summary>
        /// <param name="steamAppType">Valve's <c>common.type</c>; null is the unprobed norm.</param>
        /// <param name="epicCategories">Epic's comma-joined category paths; null is the unprobed norm.</param>
        public async Task<long> AddAsync(
            string title,
            int? year = null,
            bool provisional = false,
            string? steamAppType = null,
            string? epicCategories = null)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = title,
                FirstReleaseYear = year,
                NameIsProvisional = provisional,
                SteamAppType = steamAppType,
                EpicCategories = epicCategories,
            });

            return await AddReleaseAsync(workId, title);
        }

        /// <summary>
        /// What the Epic catalog pass does to a work that was already in the
        /// library: fills in the categories nobody had read when it was queued.
        /// </summary>
        public async Task ClassifyEpicAsync(long releaseId, string epicCategories)
        {
            var release = await Releases.GetAsync(releaseId);
            await Works.ApplyEnrichmentAsync(
                new WorkEnrichment(release!.WorkId, EpicCategories: epicCategories));
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
