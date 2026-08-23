using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Hoard.Data.Repositories;
using Hoard.Resolve;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// M0 resolver tests on a real migrated temp database: hard-join by
/// external id only, first sync creates the full row chain, re-sync is
/// idempotent, and a playtime change appends exactly one snapshot and one
/// new play record.
/// </summary>
public sealed class ExternalIdResolverTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _playRecords;
    private readonly PlaytimeSnapshotRepository _snapshots;
    private readonly ExternalIdResolver _resolver;

    public ExternalIdResolverTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _playRecords = new PlayRecordRepository(_db.Factory);
        _snapshots = new PlaytimeSnapshotRepository(_db.Factory);
        _resolver = new ExternalIdResolver(_works, _releases, _ownerships, _playRecords, _snapshots);
    }

    public void Dispose() => _db.Dispose();

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    private static CandidateOwnership Candidate(
        string appId,
        string title,
        long? playtimeMinutes,
        DateTime? lastPlayedAt,
        DateTime observedAt,
        bool installed = true,
        string? installPath = null)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: installPath ?? $@"C:\Steam\steamapps\common\{title}",
            Installed: installed,
            PlaytimeMinutes: playtimeMinutes,
            LastPlayedAt: lastPlayedAt,
            AcquiredAt: null,
            Source: "steam_local",
            ObservedAt: observedAt);

    [Fact]
    public async Task First_sync_creates_work_release_external_id_ownership_and_play_rows()
    {
        var result = await _resolver.ResolveAsync(
        [
            Candidate("2686630", "Voyagers of Nera", 244, Utc(2026, 8, 21, 20, 15, 30), Utc(2026, 8, 23, 12, 0, 0)),
            Candidate("1244090", "Sea of Stars: Sunset Edition", null, null, Utc(2026, 8, 23, 12, 0, 0)),
        ]);

        Assert.Equal(new ResolveResult(
            MatchedExisting: 0, CreatedReleases: 2, PlayRecordsWritten: 1, SnapshotsWritten: 1), result);

        Assert.Equal(2, (await _works.GetAllAsync()).Count);

        // Hard join is queryable afterwards.
        var release = await _releases.FindByExternalIdAsync("steam", "2686630");
        Assert.NotNull(release);
        Assert.Equal("Voyagers of Nera", release.Name);

        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.Equal("steam", ownership.Store);
        Assert.Equal("12345678", ownership.AccountRef);
        Assert.True(ownership.Installed);

        var playRecord = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(playRecord);
        Assert.Equal(244, playRecord.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 8, 21, 20, 15, 30), playRecord.LastPlayedAt);
        Assert.Equal("steam_local", playRecord.Source);

        var snapshot = Assert.Single(await _snapshots.GetByOwnershipAsync(ownership.Id));
        Assert.Equal(244, snapshot.PlaytimeMinutes);

        // Never-played game: ownership exists, but no fabricated zero-minute observation.
        var neverPlayed = await _releases.FindByExternalIdAsync("steam", "1244090");
        Assert.NotNull(neverPlayed);
        var neverPlayedOwnership = Assert.Single(await _ownerships.GetByReleaseAsync(neverPlayed.Id));
        Assert.Null(await _playRecords.GetLatestAsync(neverPlayedOwnership.Id));
        Assert.Empty(await _snapshots.GetByOwnershipAsync(neverPlayedOwnership.Id));
    }

    [Fact]
    public async Task Second_sync_with_unchanged_data_is_idempotent()
    {
        var lastPlayed = Utc(2026, 8, 21, 20, 15, 30);
        await _resolver.ResolveAsync(
            [Candidate("2686630", "Voyagers of Nera", 244, lastPlayed, Utc(2026, 8, 23, 12, 0, 0))]);

        // Same facts observed later: ObservedAt differs, nothing else does.
        var result = await _resolver.ResolveAsync(
            [Candidate("2686630", "Voyagers of Nera", 244, lastPlayed, Utc(2026, 8, 24, 12, 0, 0))]);

        Assert.Equal(new ResolveResult(
            MatchedExisting: 1, CreatedReleases: 0, PlayRecordsWritten: 0, SnapshotsWritten: 0), result);

        Assert.Single(await _works.GetAllAsync());
        var release = await _releases.FindByExternalIdAsync("steam", "2686630");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownership.Id));
        Assert.Single(await _snapshots.GetByOwnershipAsync(ownership.Id));
    }

    [Fact]
    public async Task Playtime_change_appends_snapshot_and_new_play_record()
    {
        await _resolver.ResolveAsync(
            [Candidate("2686630", "Voyagers of Nera", 244, Utc(2026, 8, 21, 20, 15, 30), Utc(2026, 8, 23, 12, 0, 0))]);

        var result = await _resolver.ResolveAsync(
            [Candidate("2686630", "Voyagers of Nera", 300, Utc(2026, 8, 24, 22, 5, 0), Utc(2026, 8, 25, 12, 0, 0))]);

        Assert.Equal(new ResolveResult(
            MatchedExisting: 1, CreatedReleases: 0, PlayRecordsWritten: 1, SnapshotsWritten: 1), result);

        var release = await _releases.FindByExternalIdAsync("steam", "2686630");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));

        var latest = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(latest);
        Assert.Equal(300, latest.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 8, 24, 22, 5, 0), latest.LastPlayedAt);

        var history = await _snapshots.GetByOwnershipAsync(ownership.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(244, history[0].PlaytimeMinutes); // oldest first
        Assert.Equal(300, history[1].PlaytimeMinutes);
    }

    [Fact]
    public async Task Install_state_change_updates_ownership_in_place()
    {
        await _resolver.ResolveAsync(
            [Candidate("1244090", "Sea of Stars: Sunset Edition", null, null, Utc(2026, 8, 23, 12, 0, 0))]);

        // The game was uninstalled before the next sync.
        var uninstalled = Candidate(
            "1244090", "Sea of Stars: Sunset Edition", null, null,
            Utc(2026, 8, 30, 12, 0, 0), installed: false) with
        { InstallPath = null };
        await _resolver.ResolveAsync([uninstalled]);

        var release = await _releases.FindByExternalIdAsync("steam", "1244090");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.False(ownership.Installed);
        Assert.Null(ownership.InstallPath);
    }

    [Fact]
    public async Task Resolver_never_title_matches_only_external_ids()
    {
        // An existing release with the same title but a DIFFERENT appid must
        // not absorb the candidate (§5.3: no fuzzy matching, ever).
        var workId = await _works.InsertAsync(new Work { Name = "Prey" });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = "Prey" });
        await _releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = "3970", // Prey (2006)
        });

        var result = await _resolver.ResolveAsync(
            [Candidate("480490", "Prey", 10, Utc(2017, 5, 5), Utc(2026, 8, 23, 12, 0, 0))]); // Prey (2017)

        Assert.Equal(0, result.MatchedExisting);
        Assert.Equal(1, result.CreatedReleases);
        Assert.Equal(2, (await _works.GetAllAsync()).Count);

        var prey2017 = await _releases.FindByExternalIdAsync("steam", "480490");
        Assert.NotNull(prey2017);
        Assert.NotEqual(releaseId, prey2017.Id);
    }
}
