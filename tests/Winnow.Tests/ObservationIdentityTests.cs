using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// F19. The resolver decides whether to append by comparing against the NEWEST
/// row, which is only a meaningful question for an observation that IS the
/// newest. An older one — a delayed source, a replayed cache entry, or the M5
/// importer inserting historical points out of a GDPR export — never becomes the
/// newest, so it compared as "changed" forever and was appended on every pass.
///
/// <para>These run the real repositories against a real migrated database, so
/// what is asserted is the identity index from migration 0013 doing the work
/// rather than a comparison in C# that a future caller could route around.</para>
/// </summary>
public sealed class ObservationIdentityTests : IDisposable
{
    private const string AppId = "620";

    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LongAgo = new(2019, 3, 4, 21, 0, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _playRecords;
    private readonly PlaytimeSnapshotRepository _snapshots;
    private readonly ExternalIdResolver _resolver;

    public ObservationIdentityTests()
    {
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _playRecords = new PlayRecordRepository(_db.Factory);
        _snapshots = new PlaytimeSnapshotRepository(_db.Factory);
        _resolver = new ExternalIdResolver(
            new WorkRepository(_db.Factory),
            _releases,
            _ownerships,
            _playRecords,
            _snapshots,
            _db.Factory);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>An exact replay is one observation, however many times it arrives.</summary>
    [Fact]
    public async Task An_exact_replay_of_a_play_record_appends_nothing()
    {
        var ownershipId = await OwnershipAsync();
        var record = Record(ownershipId, 300, Now);

        Assert.NotNull(await _playRecords.TryAppendAsync(record));
        Assert.Null(await _playRecords.TryAppendAsync(record));
        Assert.Null(await _playRecords.TryAppendAsync(record));

        Assert.Single(await _playRecords.GetByOwnershipAsync(ownershipId));
    }

    /// <summary>
    /// The failure exactly: a historical point replayed AFTER a newer one is
    /// present. It can never be the latest row, so nothing that compares against
    /// the latest row can stop it.
    /// </summary>
    [Fact]
    public async Task An_older_observation_replayed_after_a_newer_one_appends_once_and_only_once()
    {
        var ownershipId = await OwnershipAsync();

        Assert.NotNull(await _playRecords.TryAppendAsync(Record(ownershipId, 300, Now)));

        var historical = Record(ownershipId, 40, LongAgo, source: "gdpr_export");
        Assert.NotNull(await _playRecords.TryAppendAsync(historical));
        Assert.Null(await _playRecords.TryAppendAsync(historical));
        Assert.Null(await _playRecords.TryAppendAsync(historical));

        var all = await _playRecords.GetByOwnershipAsync(ownershipId);
        Assert.Equal(2, all.Count);

        // Ordered oldest first, so the historical point took its place in the
        // series rather than being appended to the end of it.
        Assert.Equal([40, 300], all.Select(r => r.PlaytimeMinutes));

        // And it did not become "the latest" — the present is still the present.
        var latest = await _playRecords.GetLatestAsync(ownershipId);
        Assert.Equal(300, latest!.PlaytimeMinutes);
    }

    /// <summary>
    /// A null last-played is the commonest observation there is, and SQLite
    /// treats NULLs in a UNIQUE index as distinct from one another — so without
    /// the COALESCE in 0013 this is the case that would still replay unbounded.
    /// </summary>
    [Fact]
    public async Task A_replay_with_no_last_played_date_is_still_one_observation()
    {
        var ownershipId = await OwnershipAsync();
        var record = Record(ownershipId, 300, Now, lastPlayedAt: null);

        Assert.NotNull(await _playRecords.TryAppendAsync(record));
        Assert.Null(await _playRecords.TryAppendAsync(record));

        Assert.Single(await _playRecords.GetByOwnershipAsync(ownershipId));
    }

    /// <summary>
    /// Identity is the whole fact, not just its address. Two readers that
    /// genuinely disagree at the same instant are two observations and both are
    /// kept; only a repeat of the same reading is discarded.
    /// </summary>
    [Fact]
    public async Task Two_sources_disagreeing_at_the_same_instant_are_two_observations()
    {
        var ownershipId = await OwnershipAsync();

        Assert.NotNull(await _playRecords.TryAppendAsync(
            Record(ownershipId, 300, Now, source: "steam_local")));
        Assert.NotNull(await _playRecords.TryAppendAsync(
            Record(ownershipId, 900, Now, source: "steam_web_api")));

        Assert.Equal(2, (await _playRecords.GetByOwnershipAsync(ownershipId)).Count);
    }

    [Fact]
    public async Task An_exact_replay_of_a_snapshot_appends_nothing()
    {
        var ownershipId = await OwnershipAsync();
        var point = new PlaytimeSnapshot
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 300,
            ObservedAt = Now,
        };

        Assert.NotNull(await _snapshots.TryAppendAsync(point));
        Assert.Null(await _snapshots.TryAppendAsync(point));

        Assert.Single(await _snapshots.GetByOwnershipAsync(ownershipId));
    }

    /// <summary>The same, out of order, against a series that has already moved on.</summary>
    [Fact]
    public async Task An_older_snapshot_replayed_after_a_newer_one_appends_once_and_only_once()
    {
        var ownershipId = await OwnershipAsync();

        await _snapshots.TryAppendAsync(new PlaytimeSnapshot
        {
            OwnershipId = ownershipId, PlaytimeMinutes = 300, ObservedAt = Now,
        });

        var historical = new PlaytimeSnapshot
        {
            OwnershipId = ownershipId, PlaytimeMinutes = 40, ObservedAt = LongAgo,
        };

        Assert.NotNull(await _snapshots.TryAppendAsync(historical));
        Assert.Null(await _snapshots.TryAppendAsync(historical));

        var series = await _snapshots.GetByOwnershipAsync(ownershipId);
        Assert.Equal([40, 300], series.Select(s => s.PlaytimeMinutes));
    }

    /// <summary>
    /// The same guarantee through the whole resolver, which is where it has to
    /// hold: a delayed or cached source re-presenting a stale reading is an
    /// ordinary event, and the pass has to be able to run forever without the
    /// tables growing on it.
    /// </summary>
    [Fact]
    public async Task A_full_resolve_pass_replaying_a_stale_reading_writes_it_once()
    {
        // The present, established first.
        await _resolver.ResolveAsync([Candidate(300, Now, Now)]);

        // A source that answers with an older reading AND an older observation
        // time — a cache entry served after the fresh scan already landed.
        var stale = Candidate(40, LongAgo, LongAgo, source: "steam_web_api");

        var first = await _resolver.ResolveAsync([stale]);
        var second = await _resolver.ResolveAsync([stale]);
        var third = await _resolver.ResolveAsync([stale]);

        Assert.Equal(1, first.PlayRecordsWritten);
        Assert.Equal(1, first.SnapshotsWritten);

        // Every pass after the first learns nothing and writes nothing, and the
        // counts say so rather than quietly counting rejected inserts.
        Assert.Equal(0, second.PlayRecordsWritten);
        Assert.Equal(0, second.SnapshotsWritten);
        Assert.Equal(0, third.PlayRecordsWritten);
        Assert.Equal(0, third.SnapshotsWritten);

        var ownershipId = await OwnershipAsync(create: false);
        Assert.Equal(2, (await _playRecords.GetByOwnershipAsync(ownershipId)).Count);
        Assert.Equal(2, (await _snapshots.GetByOwnershipAsync(ownershipId)).Count);
    }

    /// <summary>
    /// The importer's requirement, stated as a test: a historical point goes in
    /// through the repository rather than the resolver, so it is judged on its
    /// own identity instead of being compared — or clamped — against today.
    /// </summary>
    [Fact]
    public async Task A_historical_backfill_is_not_read_as_a_change_to_the_present()
    {
        await _resolver.ResolveAsync([Candidate(900, Now, Now)], playtime: PlaytimeView.LowerBound);
        var ownershipId = await OwnershipAsync(create: false);

        // Three years of export, oldest last, to prove order does not matter.
        foreach (var (minutes, when) in new[]
                 {
                     (300L, LongAgo.AddYears(2)),
                     (120L, LongAgo.AddYears(1)),
                     (40L, LongAgo),
                 })
        {
            await _snapshots.TryAppendAsync(new PlaytimeSnapshot
            {
                OwnershipId = ownershipId, PlaytimeMinutes = minutes, ObservedAt = when,
            });
        }

        // The series reads as history, oldest first, with today still on the end
        // and no historical point rewritten up to it.
        Assert.Equal(
            [40, 120, 300, 900],
            (await _snapshots.GetByOwnershipAsync(ownershipId)).Select(s => s.PlaytimeMinutes));

        // Re-running the whole import changes nothing.
        foreach (var (minutes, when) in new[]
                 {
                     (40L, LongAgo),
                     (120L, LongAgo.AddYears(1)),
                     (300L, LongAgo.AddYears(2)),
                 })
        {
            Assert.Null(await _snapshots.TryAppendAsync(new PlaytimeSnapshot
            {
                OwnershipId = ownershipId, PlaytimeMinutes = minutes, ObservedAt = when,
            }));
        }

        Assert.Equal(4, (await _snapshots.GetByOwnershipAsync(ownershipId)).Count);
    }

    private static PlayRecord Record(
        long ownershipId,
        long minutes,
        DateTime observedAt,
        DateTime? lastPlayedAt = null,
        string source = "steam_local")
        => new()
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = minutes,
            LastPlayedAt = lastPlayedAt,
            Source = source,
            ObservedAt = observedAt,
        };

    private static CandidateOwnership Candidate(
        long minutes,
        DateTime lastPlayedAt,
        DateTime observedAt,
        string source = "steam_local")
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: AppId,
            Title: "Portal 2",
            AccountRef: "12345678",
            InstallPath: null,
            Installed: null,
            PlaytimeMinutes: minutes,
            LastPlayedAt: lastPlayedAt,
            AcquiredAt: null,
            Source: source,
            ObservedAt: observedAt);

    private async Task<long> OwnershipAsync(bool create = true)
    {
        if (create)
        {
            await _resolver.ResolveAsync([Candidate(0, Now, Now) with { PlaytimeMinutes = null, LastPlayedAt = null }]);
        }

        var release = await _releases.FindByExternalIdAsync(ExternalIdProviders.Steam, AppId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id)).Id;
    }
}
