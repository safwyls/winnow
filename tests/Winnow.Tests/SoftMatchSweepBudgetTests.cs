using System.Diagnostics;
using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The cost of a sweep, measured rather than assumed.
///
/// <para>Soft matching used to open one transaction and then issue a
/// <c>FindByPairAsync</c> per compared pair inside it. At the configured ceiling
/// of 250,000 comparisons that is a quarter of a million round trips while
/// SQLite's single writer is held — and everything else in the app that writes
/// (acknowledgements, journal notes, feed feedback, session records) waits behind
/// it. The bug is invisible in a four-row test: it only appears at library scale,
/// which is why these run against one.</para>
///
/// <para>The property under test is a shape, not a number: <b>queries scale with
/// what the pass WRITES, never with what it compares.</b> A library where nothing
/// has changed must cost one query no matter how many pairs the blocking pass
/// generated.</para>
/// </summary>
public sealed class SoftMatchSweepBudgetTests
{
    /// <summary>
    /// Roughly a large real library. Big enough that the blocking pass generates
    /// tens of thousands of pairs, which is where the old per-pair lookup hurt.
    /// </summary>
    private const int LibrarySize = 1_200;

    /// <summary>Genuine duplicates planted in the noise, so the pass has real work to write.</summary>
    private const int DuplicatePairs = 15;

    [Fact]
    public async Task A_library_scale_sweep_costs_one_read_plus_its_writes()
    {
        using var fixture = new BudgetFixture();
        await fixture.SeedRealisticLibraryAsync(LibrarySize, DuplicatePairs);

        var stopwatch = Stopwatch.StartNew();
        var report = await fixture.Sweep.SweepAsync();
        stopwatch.Stop();

        // The fixture has to actually exercise the path, or the budget below is
        // vacuous.
        Assert.True(
            report.PairsProposed > 5_000,
            $"fixture only produced {report.PairsProposed} pairs; it is not exercising the hot path");

        // One preload for the whole pass — not one lookup per pair.
        Assert.Equal(1, fixture.Candidates.GetAllCalls);
        Assert.Equal(0, fixture.Candidates.FindByPairCalls);
        Assert.Equal(0, fixture.Candidates.GetPendingCalls);

        // And nothing else. Total repository traffic is exactly the preload plus
        // the rows the pass had a reason to write; the tens of thousands of pairs
        // that scored below the floor cost no database work at all.
        var writes = fixture.Candidates.WriteCalls;
        Assert.Equal(writes + 1, fixture.Candidates.TotalCalls);

        // Sanity on the fixture itself: the duplicates are what gets written.
        Assert.Equal(DuplicatePairs, report.Outcome.Queued);
        Assert.Equal(DuplicatePairs, writes);

        // The writer is taken once, briefly, for a batch of 15 — not held across
        // the whole comparison pass.
        Assert.Equal(1, fixture.UnitsOfWork.BeginCalls);

        // Deliberately loose. The query count above is the real assertion; this
        // only catches a return to per-pair round trips, which is orders of
        // magnitude slower than anything a healthy pass does.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"sweep of {LibrarySize} releases took {stopwatch.Elapsed.TotalSeconds:n1}s");
    }

    /// <summary>
    /// A re-sweep of a library nothing changed in. Every pair is compared again
    /// and every one of them is either below the floor or already answered, so
    /// the pass has nothing to write — and a pass with nothing to write must not
    /// open a write transaction at all.
    /// </summary>
    [Fact]
    public async Task An_unchanged_library_costs_exactly_one_query_and_no_transaction()
    {
        using var fixture = new BudgetFixture();
        await fixture.SeedRealisticLibraryAsync(LibrarySize, DuplicatePairs);
        await fixture.Sweep.SweepAsync();

        fixture.ResetCounts();
        var report = await fixture.Sweep.SweepAsync();

        Assert.True(report.PairsProposed > 5_000);
        Assert.Equal(DuplicatePairs, report.Outcome.AlreadyPending);
        Assert.Equal(0, report.Outcome.Queued);

        Assert.Equal(1, fixture.Candidates.TotalCalls);
        Assert.Equal(0, fixture.Candidates.WriteCalls);
        Assert.Equal(0, fixture.UnitsOfWork.BeginCalls);
    }

    /// <summary>
    /// When a pass does have a lot to write, it commits in bounded batches and
    /// yields the writer between them. A single transaction spanning every write
    /// is the shape that starves other writers.
    /// </summary>
    [Fact]
    public async Task A_large_write_set_commits_in_bounded_batches()
    {
        const int pairs = 40;
        const int batchSize = 5;

        using var fixture = new BudgetFixture(batchSize);
        await fixture.SeedRealisticLibraryAsync(releases: 0, duplicatePairs: pairs);

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(pairs, report.Outcome.Queued);
        Assert.Equal(pairs, fixture.Candidates.WriteCalls);
        Assert.Equal(pairs / batchSize, fixture.UnitsOfWork.BeginCalls);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed class BudgetFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private readonly WorkRepository _works;
        private readonly ReleaseRepository _releases;

        public BudgetFixture(int writeBatchSize = SoftMatchResolver.DefaultWriteBatchSize)
        {
            _works = new WorkRepository(_db.Factory);
            _releases = new ReleaseRepository(_db.Factory);

            Candidates = new CountingMergeCandidates(new MergeCandidateRepository(_db.Factory));
            UnitsOfWork = new CountingUnitOfWorkFactory(_db.Factory);

            Sweep = new LibrarySoftMatchSweep(
                _releases,
                new SoftMatchResolver(
                    new SoftMatcher(), Candidates, UnitsOfWork, logger: null, writeBatchSize),
                new ResolveStateRepository(_db.Factory));
        }

        public CountingMergeCandidates Candidates { get; }

        public CountingUnitOfWorkFactory UnitsOfWork { get; }

        public LibrarySoftMatchSweep Sweep { get; }

        public void ResetCounts()
        {
            Candidates.Reset();
            UnitsOfWork.Reset();
        }

        /// <summary>
        /// A library shaped like a real one: mostly unrelated titles that
        /// nonetheless share a word with a few dozen others — which is what makes
        /// blocking produce tens of thousands of candidate pairs that the matcher
        /// then discards — plus a handful of genuine duplicates.
        /// </summary>
        public async Task SeedRealisticLibraryAsync(int releases, int duplicatePairs)
        {
            // One transaction: seeding is not what these tests measure, and 2,400
            // individual commits would dominate their runtime.
            using var scope = _db.Factory.Begin();

            for (var i = 0; i < releases; i++)
            {
                // Three tokens: a shared "franchise" word (about 30 releases
                // deep, so it is a legitimate blocking key) and two that vary
                // within it, so the pair shares a key but not a title.
                await AddAsync(
                    $"{Word(i % 40)} {Word(100 + (i % 53))} {Word(300 + (i % 47))}");
            }

            for (var k = 0; k < duplicatePairs; k++)
            {
                var title = $"{Word(5_000 + k)} {Word(6_000 + k)} {Word(7_000 + k)}";
                await AddAsync(title);
                await AddAsync(title);
            }

            scope.Commit();
        }

        private async Task AddAsync(string title)
        {
            var workId = await _works.InsertAsync(new Work { Name = title });
            await _releases.InsertAsync(new Release { WorkId = workId, Name = title });
        }

        /// <summary>
        /// Deterministic pronounceable nonsense: 686 distinct words, far enough
        /// apart that two of them never look like the same game.
        /// </summary>
        private static string Word(int seed)
        {
            string[] onsets = ["Br", "Cl", "Dr", "Fl", "Gr", "Kr", "Pl", "Sh", "St", "Th", "Tr", "Vr", "Wr", "Zh"];
            string[] nuclei = ["a", "e", "i", "o", "u", "ae", "ou"];
            string[] codas = ["nd", "rk", "st", "lm", "ph", "ng", "tz"];

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{onsets[seed % 14]}{nuclei[seed / 14 % 7]}{codas[seed / 98 % 7]}");
        }

        public void Dispose() => _db.Dispose();
    }

    /// <summary>
    /// Counts repository traffic. The finding F08 describes is a call-count
    /// problem, so the call count is what the test asserts on — measured at the
    /// repository boundary, where "one lookup per pair" actually happened.
    /// </summary>
    private sealed class CountingMergeCandidates : IMergeCandidateRepository
    {
        private readonly IMergeCandidateRepository _inner;

        public CountingMergeCandidates(IMergeCandidateRepository inner) => _inner = inner;

        public int GetAllCalls { get; private set; }

        public int GetPendingCalls { get; private set; }

        public int FindByPairCalls { get; private set; }

        public int InsertCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public int WithdrawCalls { get; private set; }

        public int SetStatusCalls { get; private set; }

        public int WriteCalls => InsertCalls + UpdateCalls + WithdrawCalls + SetStatusCalls;

        public int TotalCalls => WriteCalls + GetAllCalls + GetPendingCalls + FindByPairCalls;

        public void Reset()
        {
            GetAllCalls = GetPendingCalls = FindByPairCalls = 0;
            InsertCalls = UpdateCalls = WithdrawCalls = SetStatusCalls = 0;
        }

        public Task<IReadOnlyList<MergeCandidate>> GetAllAsync(CancellationToken ct = default)
        {
            GetAllCalls++;
            return _inner.GetAllAsync(ct);
        }

        public Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default)
        {
            GetPendingCalls++;
            return _inner.GetPendingAsync(ct);
        }

        public Task<MergeCandidate?> FindByPairAsync(
            long leftReleaseId, long rightReleaseId, CancellationToken ct = default)
        {
            FindByPairCalls++;
            return _inner.FindByPairAsync(leftReleaseId, rightReleaseId, ct);
        }

        public Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default)
        {
            InsertCalls++;
            return _inner.InsertAsync(candidate, ct);
        }

        public Task SetStatusAsync(long id, string status, CancellationToken ct = default)
        {
            SetStatusCalls++;
            return _inner.SetStatusAsync(id, status, ct);
        }

        public Task<bool> UpdatePendingScoreAsync(
            long id, double score, string? signalsJson, CancellationToken ct = default)
        {
            UpdateCalls++;
            return _inner.UpdatePendingScoreAsync(id, score, signalsJson, ct);
        }

        public Task<bool> WithdrawPendingAsync(long id, CancellationToken ct = default)
        {
            WithdrawCalls++;
            return _inner.WithdrawPendingAsync(id, ct);
        }
    }

    /// <summary>Counts how many times the pass takes SQLite's single writer.</summary>
    private sealed class CountingUnitOfWorkFactory : IUnitOfWorkFactory
    {
        private readonly IUnitOfWorkFactory _inner;

        public CountingUnitOfWorkFactory(IUnitOfWorkFactory inner) => _inner = inner;

        public int BeginCalls { get; private set; }

        public void Reset() => BeginCalls = 0;

        public IUnitOfWork Begin()
        {
            BeginCalls++;
            return _inner.Begin();
        }
    }
}
