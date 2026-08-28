using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// §5.3 step 2's write half, on a real migrated temp database: which pairs
/// reach <c>merge_candidates</c>, and — the part that matters over a library's
/// lifetime — what happens when the same scan runs again tomorrow.
///
/// <para>A library is re-scanned on every launch. If a re-scan duplicates rows
/// the queue grows without bound; if it resurrects a pair the user already
/// answered, the queue starts asking questions it has already been told the
/// answer to, and users learn to click through it without reading. Either way
/// the human-in-the-loop design §5.3 depends on stops working.</para>
/// </summary>
public sealed class SoftMatchResolverTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly MergeCandidateRepository _candidates;
    private readonly SoftMatchResolver _resolver;

    public SoftMatchResolverTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _candidates = new MergeCandidateRepository(_db.Factory);
        _resolver = new SoftMatchResolver(new SoftMatcher(), _candidates, _db.Factory);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Creates a real releases row so the FK on merge_candidates is satisfiable.</summary>
    private async Task<long> SeedReleaseAsync(string name)
    {
        var workId = await _works.InsertAsync(new Work { Name = name });
        return await _releases.InsertAsync(new Release { WorkId = workId, Name = name });
    }

    private static MatchSubject Subject(
        long releaseId, string title, int? year = null, string? publisher = null)
        => new()
        {
            ReleaseId = releaseId,
            Title = title,
            ReleaseYear = year,
            Publisher = publisher,
        };

    private static SoftMatchRequest Request(MatchSubject subject, params MatchSubject[] possibilities)
        => new(subject, possibilities);

    // ── Queueing ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AMidBandPairIsWrittenAsPendingWithScoreAndSignals()
    {
        var a = await SeedReleaseAsync("The Witcher 3: Wild Hunt");
        var b = await SeedReleaseAsync("The Witcher III: Wild Hunt");

        var outcome = await _resolver.ResolveAsync(
            [Request(Subject(a, "The Witcher 3: Wild Hunt"), Subject(b, "The Witcher III: Wild Hunt"))]);

        Assert.Equal(1, outcome.Compared);
        Assert.Equal(1, outcome.Queued);
        Assert.Equal(0, outcome.AutoMerged);

        var pending = Assert.Single(await _candidates.GetPendingAsync());
        Assert.Equal(MergeCandidateStatuses.Pending, pending.Status);
        Assert.True(pending.Score > 0);

        var payload = SoftMatchSignalsJson.Deserialize(pending.SignalsJson);
        Assert.NotNull(payload);
        Assert.False(payload.AutoMergeAllowed);
        Assert.Equal(pending.Score, payload.Score);
        Assert.NotEmpty(payload.Signals);
    }

    /// <summary>
    /// The pair is stored canonicalised (lower release id on the left) whichever
    /// way round the scan happened to walk it, so the UNIQUE(left, right) index
    /// actually constrains the pair rather than the orientation.
    /// </summary>
    [Fact]
    public async Task ThePairIsStoredCanonicalisedRegardlessOfScanDirection()
    {
        var a = await SeedReleaseAsync("Braid");
        var b = await SeedReleaseAsync("Braid");

        await _resolver.ResolveAsync([Request(Subject(b, "Braid"), Subject(a, "Braid"))]);

        var pending = Assert.Single(await _candidates.GetPendingAsync());
        Assert.Equal(Math.Min(a, b), pending.LeftReleaseId);
        Assert.Equal(Math.Max(a, b), pending.RightReleaseId);
    }

    /// <summary>
    /// A high score buys review priority and nothing else. Even a pair agreeing
    /// on every available signal is written as <c>pending</c>: in M1 only the
    /// external-id hard join may merge without asking (§5.3).
    /// </summary>
    [Fact]
    public async Task EvenAPriorityBandPairIsOnlyQueued_NeverMerged()
    {
        var a = await SeedReleaseAsync("Hollow Knight");
        var b = await SeedReleaseAsync("Hollow Knight");

        var outcome = await _resolver.ResolveAsync(
            [Request(
                Subject(a, "Hollow Knight", 2017, "Team Cherry"),
                Subject(b, "Hollow Knight", 2017, "Team Cherry"))]);

        Assert.Equal(1, outcome.Queued);
        Assert.Equal(1, outcome.Priority);
        Assert.Equal(0, outcome.AutoMerged);

        var pending = Assert.Single(await _candidates.GetPendingAsync());
        Assert.Equal(MergeCandidateStatuses.Pending, pending.Status);

        // Nothing about the library itself changed: both releases still stand,
        // under their own separate works. Queueing is not merging.
        var left = await _releases.GetAsync(a);
        var right = await _releases.GetAsync(b);
        Assert.NotNull(left);
        Assert.NotNull(right);
        Assert.NotEqual(left.WorkId, right.WorkId);
    }

    // ── The floor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Below the floor the pair is discarded, not stored. A queue full of noise
    /// is a queue nobody clears.
    /// </summary>
    [Fact]
    public async Task SubFloorPairsAreDiscardedRatherThanQueued()
    {
        var a = await SeedReleaseAsync("Portal");
        var b = await SeedReleaseAsync("Portal 2");
        var c = await SeedReleaseAsync("Stardew Valley");

        var outcome = await _resolver.ResolveAsync(
            [Request(Subject(a, "Portal"), Subject(b, "Portal 2"), Subject(c, "Stardew Valley"))]);

        Assert.Equal(2, outcome.Compared);
        Assert.Equal(0, outcome.Queued);
        Assert.Equal(2, outcome.SkippedBelowFloor);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    /// <summary>
    /// The Prey trap, end to end: identical titles eleven years apart never
    /// reach the queue, and nothing in the library is touched.
    /// </summary>
    [Fact]
    public async Task PreyTrap_NeverReachesTheQueue()
    {
        var a = await SeedReleaseAsync("Prey");
        var b = await SeedReleaseAsync("Prey");

        var outcome = await _resolver.ResolveAsync(
            [Request(
                Subject(a, "Prey", 2006, "2K Games"),
                Subject(b, "Prey", 2017, "Bethesda Softworks"))]);

        Assert.Equal(0, outcome.Queued);
        Assert.Equal(1, outcome.SkippedBelowFloor);
        Assert.Equal(0, outcome.AutoMerged);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    /// <summary>Skyrim / Skyrim SE are different Releases and never queue (§9 pitfall 5).</summary>
    [Fact]
    public async Task SkyrimAndSpecialEditionNeverReachTheQueue()
    {
        var a = await SeedReleaseAsync("The Elder Scrolls V: Skyrim");
        var b = await SeedReleaseAsync("The Elder Scrolls V: Skyrim Special Edition");

        var outcome = await _resolver.ResolveAsync(
            [Request(
                Subject(a, "The Elder Scrolls V: Skyrim", 2011, "Bethesda Softworks"),
                Subject(b, "The Elder Scrolls V: Skyrim Special Edition", 2016, "Bethesda Softworks"))]);

        Assert.Equal(0, outcome.Queued);
        Assert.Equal(1, outcome.SkippedBelowFloor);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    // ── Idempotency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReRunningAScanDoesNotDuplicatePendingRows()
    {
        var a = await SeedReleaseAsync("Celeste");
        var b = await SeedReleaseAsync("Celeste");
        var request = Request(Subject(a, "Celeste", 2018), Subject(b, "Celeste", 2018));

        var first = await _resolver.ResolveAsync([request]);
        var second = await _resolver.ResolveAsync([request]);
        var third = await _resolver.ResolveAsync([request]);

        Assert.Equal(1, first.Queued);
        Assert.Equal(0, second.Queued);
        Assert.Equal(1, second.AlreadyPending);
        Assert.Equal(0, third.Queued);
        Assert.Equal(1, third.AlreadyPending);

        Assert.Single(await _candidates.GetPendingAsync());
    }

    /// <summary>
    /// The mirrored pair — B's possibility list contains A while A's contains B —
    /// is one candidate, not two. Without canonicalisation the UNIQUE index does
    /// not catch it, because (a,b) and (b,a) are different rows.
    /// </summary>
    [Fact]
    public async Task TheMirroredPairIsQueuedOnceWithinOnePass()
    {
        var a = await SeedReleaseAsync("Bastion");
        var b = await SeedReleaseAsync("Bastion");

        var outcome = await _resolver.ResolveAsync(
        [
            Request(Subject(a, "Bastion", 2011), Subject(b, "Bastion", 2011)),
            Request(Subject(b, "Bastion", 2011), Subject(a, "Bastion", 2011)),
        ]);

        Assert.Equal(1, outcome.Compared);
        Assert.Equal(1, outcome.Queued);
        Assert.Single(await _candidates.GetPendingAsync());
    }

    /// <summary>A release paired with itself is not a merge candidate.</summary>
    [Fact]
    public async Task ASelfPairIsNeverQueued()
    {
        var a = await SeedReleaseAsync("Tunic");

        var outcome = await _resolver.ResolveAsync([Request(Subject(a, "Tunic", 2022), Subject(a, "Tunic", 2022))]);

        Assert.Equal(0, outcome.Compared);
        Assert.Equal(0, outcome.Queued);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    // ── Terminal statuses ───────────────────────────────────────────────────

    /// <summary>
    /// A pair the user answered "Different games" stays answered. Re-asking is
    /// how a confirmation queue trains people to stop reading it.
    /// </summary>
    [Fact]
    public async Task ARejectedPairIsNeverReQueued()
    {
        var a = await SeedReleaseAsync("Prey");
        var b = await SeedReleaseAsync("Prey");
        var request = Request(Subject(a, "Prey"), Subject(b, "Prey"));

        await _resolver.ResolveAsync([request]);
        var queued = Assert.Single(await _candidates.GetPendingAsync());
        await _candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Rejected);

        var again = await _resolver.ResolveAsync([request]);
        var andAgain = await _resolver.ResolveAsync([request]);

        Assert.Equal(0, again.Queued);
        Assert.Equal(1, again.PreviouslyRejected);
        Assert.Equal(0, andAgain.Queued);
        Assert.Equal(1, andAgain.PreviouslyRejected);
        Assert.Empty(await _candidates.GetPendingAsync());

        var row = await _candidates.FindByPairAsync(a, b);
        Assert.NotNull(row);
        Assert.Equal(MergeCandidateStatuses.Rejected, row.Status);
    }

    /// <summary>Rejection survives the pair being seen from the other direction, too.</summary>
    [Fact]
    public async Task ARejectedPairStaysRejectedWhenSeenInTheOppositeOrder()
    {
        var a = await SeedReleaseAsync("Prey");
        var b = await SeedReleaseAsync("Prey");

        await _resolver.ResolveAsync([Request(Subject(a, "Prey"), Subject(b, "Prey"))]);
        var queued = Assert.Single(await _candidates.GetPendingAsync());
        await _candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Rejected);

        var reversed = await _resolver.ResolveAsync([Request(Subject(b, "Prey"), Subject(a, "Prey"))]);

        Assert.Equal(0, reversed.Queued);
        Assert.Equal(1, reversed.PreviouslyRejected);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    /// <summary>Confirmed is terminal as well: a decided pair is not re-litigated.</summary>
    [Fact]
    public async Task AConfirmedPairIsNeverReQueued()
    {
        var a = await SeedReleaseAsync("Hades");
        var b = await SeedReleaseAsync("Hades");
        var request = Request(Subject(a, "Hades", 2020), Subject(b, "Hades", 2020));

        await _resolver.ResolveAsync([request]);
        var queued = Assert.Single(await _candidates.GetPendingAsync());
        await _candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Confirmed);

        var again = await _resolver.ResolveAsync([request]);

        Assert.Equal(0, again.Queued);
        Assert.Equal(1, again.PreviouslyConfirmed);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    // ── Outcome bookkeeping ─────────────────────────────────────────────────

    [Fact]
    public async Task EveryComparedPairIsAccountedForExactlyOnce()
    {
        var same = await SeedReleaseAsync("Celeste");
        var sameAgain = await SeedReleaseAsync("Celeste");
        var rejected = await SeedReleaseAsync("Braid");
        var rejectedAgain = await SeedReleaseAsync("Braid");
        var noise = await SeedReleaseAsync("Portal 2");

        await _resolver.ResolveAsync(
            [Request(Subject(rejected, "Braid", 2008), Subject(rejectedAgain, "Braid", 2008))]);
        var queued = Assert.Single(await _candidates.GetPendingAsync());
        await _candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Rejected);

        var outcome = await _resolver.ResolveAsync(
        [
            Request(Subject(same, "Celeste", 2018), Subject(sameAgain, "Celeste", 2018)),
            Request(Subject(rejected, "Braid", 2008), Subject(rejectedAgain, "Braid", 2008)),
            Request(Subject(noise, "Portal 2"), Subject(same, "Celeste", 2018)),
        ]);

        Assert.Equal(3, outcome.Compared);
        Assert.Equal(1, outcome.Queued);
        Assert.Equal(1, outcome.PreviouslyRejected);
        Assert.Equal(1, outcome.SkippedBelowFloor);
        Assert.Equal(0, outcome.AlreadyPending);
        Assert.Equal(0, outcome.PreviouslyConfirmed);
        Assert.Equal(
            outcome.Compared,
            outcome.Queued + outcome.SkippedBelowFloor + outcome.AlreadyPending
                + outcome.PreviouslyRejected + outcome.PreviouslyConfirmed);
    }

    [Fact]
    public async Task AnEmptyPassWritesNothing()
    {
        var outcome = await _resolver.ResolveAsync([]);

        Assert.Equal(SoftMatchOutcome.Empty, outcome);
        Assert.Empty(await _candidates.GetPendingAsync());
    }

    /// <summary>
    /// The whole pass is one transaction, like <see cref="ExternalIdResolver"/>'s:
    /// a cancellation part-way through must queue nothing, not half a queue
    /// whose remainder the idempotency check then suppresses forever.
    /// </summary>
    [Fact]
    public async Task ACancelledPassQueuesNothing()
    {
        var a = await SeedReleaseAsync("Celeste");
        var b = await SeedReleaseAsync("Celeste");
        var c = await SeedReleaseAsync("Braid");
        var d = await SeedReleaseAsync("Braid");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _resolver.ResolveAsync(
        [
            Request(Subject(a, "Celeste", 2018), Subject(b, "Celeste", 2018)),
            Request(Subject(c, "Braid", 2008), Subject(d, "Braid", 2008)),
        ], cts.Token));

        Assert.Empty(await _candidates.GetPendingAsync());
    }

    /// <summary>
    /// The queue is ordered by score descending, so the priority band really is
    /// what the user sees first when the confirmation UI batches them (§5.3
    /// step 3).
    /// </summary>
    [Fact]
    public async Task ThePendingQueueIsOrderedHighestConfidenceFirst()
    {
        var strongA = await SeedReleaseAsync("Hollow Knight");
        var strongB = await SeedReleaseAsync("Hollow Knight");
        var weakA = await SeedReleaseAsync("Bastion");
        var weakB = await SeedReleaseAsync("Bastion");

        await _resolver.ResolveAsync(
        [
            Request(Subject(weakA, "Bastion"), Subject(weakB, "Bastion")),
            Request(
                Subject(strongA, "Hollow Knight", 2017, "Team Cherry"),
                Subject(strongB, "Hollow Knight", 2017, "Team Cherry")),
        ]);

        var pending = await _candidates.GetPendingAsync();
        Assert.Equal(2, pending.Count);
        Assert.True(pending[0].Score > pending[1].Score);
        Assert.Equal(strongA, pending[0].LeftReleaseId);
    }
}
