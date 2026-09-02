using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// What the enrichment metadata is actually FOR.
///
/// <para>§5.3 scores a soft match on four signals: title, release year within
/// ±1, publisher, cover hash. Three of them had a pipeline. The year had a
/// column nothing ever filled, and the publisher had no column at all — so
/// every pair the sweep queued was scored on title similarity alone, which is
/// the one thing §5.3 says must never be trusted by itself. These tests run the
/// real repositories against a real migrated database and check that the two
/// missing signals now reach <see cref="SoftMatcher"/> and change the answer.
/// </para>
///
/// <para>They also pin the re-scoring rule, which is the awkward half: 14 pairs
/// were queued before any of this metadata existed, and
/// <see cref="SoftMatchResolver"/> refuses to re-queue a pair that already has a
/// row. Without a re-score those pairs keep a title-only score and a
/// "publisher unknown on at least one side" explanation for the life of the
/// database — while an identical pair discovered one launch later scores far
/// higher. The rule: <b>a pending row tracks the evidence, an answered row
/// tracks the user.</b></para>
/// </summary>
public sealed class SoftMatchMetadataTests
{
    // ── The metadata reaches the matcher ─────────────────────────────────────

    /// <summary>
    /// The projection the sweep reads is where the publisher joins the
    /// pipeline. If it does not come out of here it cannot reach anything.
    /// </summary>
    [Fact]
    public async Task The_release_identity_projection_carries_the_year_and_the_publisher()
    {
        using var fixture = new MetadataFixture();
        var release = await fixture.AddAsync("Riven");
        await fixture.EnrichAsync(release, 1997, "Brøderbund");

        var identity = Assert.Single(await fixture.Releases.GetIdentitiesAsync());

        Assert.Equal(1997, identity.FirstReleaseYear);
        Assert.Equal("Brøderbund", identity.Publisher);
    }

    /// <summary>
    /// The headline. The same pair, scored twice: once with the metadata the
    /// library used to hold (none), once with what enrichment now stores. Title
    /// alone lands a pair at the calibrated 0.65 — queued, nowhere near
    /// priority. A matching year and publisher are what lift it into the band
    /// that gets reviewed first.
    /// </summary>
    [Fact]
    public async Task A_known_duplicate_scores_higher_once_the_year_and_publisher_are_stored()
    {
        using var fixture = new MetadataFixture();
        var left = await fixture.AddAsync("Hollow Knight");
        var right = await fixture.AddAsync("Hollow Knight");

        await fixture.Sweep.SweepAsync();
        var beforeRow = Assert.Single(await fixture.Candidates.GetPendingAsync());
        var before = SoftMatchSignalsJson.Deserialize(beforeRow.SignalsJson);

        Assert.NotNull(before);
        Assert.Null(before.PublisherMatch);
        Assert.Null(before.YearDelta);

        // Enrichment lands: both rows learn the same year and the same publisher.
        await fixture.EnrichAsync(left, 2017, "Team Cherry");
        await fixture.EnrichAsync(right, 2017, "Team Cherry");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(1, report.Outcome.Rescored);
        Assert.Equal(0, report.Outcome.Queued);

        var afterRow = Assert.Single(await fixture.Candidates.GetPendingAsync());
        Assert.Equal(beforeRow.Id, afterRow.Id);
        Assert.True(
            afterRow.Score > beforeRow.Score,
            $"score did not improve: {beforeRow.Score:F2} → {afterRow.Score:F2}");

        var after = SoftMatchSignalsJson.Deserialize(afterRow.SignalsJson);
        Assert.NotNull(after);
        Assert.True(after.PublisherMatch);
        Assert.Equal(0, after.YearDelta);
        Assert.Equal("Priority", after.Band);

        // And the frozen explanation the confirm UI renders is no longer lying
        // about what is known.
        var publisherSignal = Assert.Single(
            after.Signals, s => string.Equals(s.Name, "publisher", StringComparison.Ordinal));
        Assert.True(publisherSignal.Fired);
    }

    /// <summary>
    /// A re-sweep of a library nothing changed in must write nothing at all —
    /// re-scoring is a response to new evidence, not a per-launch UPDATE
    /// against every pending row.
    /// </summary>
    [Fact]
    public async Task An_unchanged_library_is_not_rescored()
    {
        using var fixture = new MetadataFixture();
        var left = await fixture.AddAsync("Celeste");
        var right = await fixture.AddAsync("Celeste");
        await fixture.EnrichAsync(left, 2018, "Maddy Makes Games");
        await fixture.EnrichAsync(right, 2018, "Maddy Makes Games");

        await fixture.Sweep.SweepAsync();
        var second = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, second.Outcome.Rescored);
        Assert.Equal(0, second.Outcome.Withdrawn);
        Assert.Equal(1, second.Outcome.AlreadyPending);
    }

    // ── Withdrawal: the queue reflects what is known now ─────────────────────

    /// <summary>
    /// The Prey trap, discovered late. Two identically titled games queued at
    /// 0.65 on title alone; enrichment then reveals 2006 and 2017. The matcher
    /// would not propose that pair today — a far year delta drops it to 0.35,
    /// below the 0.45 queue floor — so the proposal is withdrawn rather than
    /// left asking a question the scorer has since answered.
    ///
    /// <para>The alternative is worse than noise: whether Prey/Prey sits in the
    /// review list would depend on which launch happened to enrich the library
    /// first.</para>
    /// </summary>
    [Fact]
    public async Task A_pending_pair_that_no_longer_clears_the_floor_is_withdrawn()
    {
        using var fixture = new MetadataFixture();
        var left = await fixture.AddAsync("Prey");
        var right = await fixture.AddAsync("Prey");

        await fixture.Sweep.SweepAsync();
        Assert.Single(await fixture.Candidates.GetPendingAsync());

        await fixture.EnrichAsync(left, 2006, "2K Games");
        await fixture.EnrichAsync(right, 2017, "Bethesda Softworks");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(1, report.Outcome.Withdrawn);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());

        // Withdrawn, not answered: nothing was invented on the user's behalf.
        Assert.Null(await fixture.Candidates.FindByPairAsync(left, right));
    }

    // ── Terminal statuses stay terminal ──────────────────────────────────────

    /// <summary>
    /// The one thing re-scoring must never touch. A rejected pair is an answer
    /// the user gave; new metadata is evidence about the games, not about the
    /// answer. It must not be re-scored, must not be withdrawn, and above all
    /// must never come back as pending.
    /// </summary>
    [Fact]
    public async Task A_rejected_pair_is_neither_rescored_nor_resurrected_by_new_metadata()
    {
        using var fixture = new MetadataFixture();
        var left = await fixture.AddAsync("Hollow Knight");
        var right = await fixture.AddAsync("Hollow Knight");

        await fixture.Sweep.SweepAsync();
        var queued = Assert.Single(await fixture.Candidates.GetPendingAsync());
        await fixture.Candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Rejected);

        await fixture.EnrichAsync(left, 2017, "Team Cherry");
        await fixture.EnrichAsync(right, 2017, "Team Cherry");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.Outcome.Queued);
        Assert.Equal(0, report.Outcome.Rescored);
        Assert.Equal(1, report.Outcome.PreviouslyRejected);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());

        var row = await fixture.Candidates.FindByPairAsync(left, right);
        Assert.NotNull(row);
        Assert.Equal(MergeCandidateStatuses.Rejected, row.Status);
        Assert.Equal(queued.Score, row.Score);
        Assert.Equal(queued.SignalsJson, row.SignalsJson);
    }

    /// <summary>
    /// Withdrawal applies to proposals, never to decisions. A pair the user
    /// answered is not deleted because new metadata drops it under the queue
    /// floor. 'rejected' is the only terminal status after migration 0019;
    /// the affirmative answer is a live identity link, which this table
    /// does not carry.
    /// </summary>
    [Fact]
    public async Task An_answered_pair_is_never_withdrawn_by_new_metadata()
    {
        using var fixture = new MetadataFixture();
        var left = await fixture.AddAsync("Prey");
        var right = await fixture.AddAsync("Prey");

        await fixture.Sweep.SweepAsync();
        var queued = Assert.Single(await fixture.Candidates.GetPendingAsync());
        await fixture.Candidates.SetStatusAsync(queued.Id, MergeCandidateStatuses.Rejected);

        await fixture.EnrichAsync(left, 2006, "2K Games");
        await fixture.EnrichAsync(right, 2017, "Bethesda Softworks");

        var report = await fixture.Sweep.SweepAsync();

        Assert.Equal(0, report.Outcome.Withdrawn);

        var row = await fixture.Candidates.FindByPairAsync(left, right);
        Assert.NotNull(row);
        Assert.Equal(MergeCandidateStatuses.Rejected, row.Status);
        Assert.Equal(queued.Score, row.Score);
    }

    /// <summary>
    /// The repository guard, tested directly rather than through the sweep: the
    /// <c>status = 'pending'</c> predicate lives in the SQL, so no ordering of
    /// caller code can rewrite or delete an answered row.
    /// </summary>
    [Fact]
    public async Task The_repository_refuses_to_rescore_or_withdraw_an_answered_row()
    {
        using var fixture = new MetadataFixture();
        var left = await fixture.AddAsync("Tunic");
        var right = await fixture.AddAsync("Tunic");

        var id = await fixture.Candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = Math.Min(left, right),
            RightReleaseId = Math.Max(left, right),
            Score = 0.65,
            SignalsJson = "{}",
            Status = MergeCandidateStatuses.Rejected,
        });

        Assert.False(await fixture.Candidates.UpdatePendingScoreAsync(id, 0.97, "{\"new\":true}"));
        Assert.False(await fixture.Candidates.WithdrawPendingAsync(id));

        var row = await fixture.Candidates.FindByPairAsync(left, right);
        Assert.NotNull(row);
        Assert.Equal(MergeCandidateStatuses.Rejected, row.Status);
        Assert.Equal(0.65, row.Score);
        Assert.Equal("{}", row.SignalsJson);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private sealed class MetadataFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 700000;

        public MetadataFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Candidates = new MergeCandidateRepository(_db.Factory);

            Sweep = new LibrarySoftMatchSweep(
                Releases,
                new SoftMatchResolver(new SoftMatcher(), Candidates, _db.Factory),
                new ResolveStateRepository(_db.Factory));
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public LibrarySoftMatchSweep Sweep { get; }

        /// <summary>One work, one release, one Steam id — the M1 shape, with no metadata.</summary>
        public async Task<long> AddAsync(string title)
        {
            var workId = await Works.InsertAsync(new Work { Name = title });
            var releaseId = await Releases.InsertAsync(new Release { WorkId = workId, Name = title });
            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
            });

            return releaseId;
        }

        /// <summary>
        /// What an enrichment run does to the work behind a release, through the
        /// same one-way repository path the real service uses.
        /// </summary>
        public async Task EnrichAsync(long releaseId, int year, string publisher)
        {
            var release = await Releases.GetAsync(releaseId);
            Assert.NotNull(release);

            await Works.ApplyEnrichmentAsync(new WorkEnrichment(
                release.WorkId, FirstReleaseYear: year, Publisher: publisher));
        }

        public void Dispose() => _db.Dispose();
    }
}
