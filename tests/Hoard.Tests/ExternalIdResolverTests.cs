using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Hoard.Core.Repositories;
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
        _resolver = new ExternalIdResolver(
            _works, _releases, _ownerships, _playRecords, _snapshots, _db.Factory);
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

    /// <summary>
    /// A played-but-uninstalled Steam appid: localconfig knows the minutes,
    /// nothing on disk knows the title.
    /// </summary>
    private static CandidateOwnership TitlelessCandidate(
        string appId,
        long? playtimeMinutes,
        DateTime? lastPlayedAt,
        DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: null,
            AccountRef: "12345678",
            InstallPath: null,
            Installed: false,
            PlaytimeMinutes: playtimeMinutes,
            LastPlayedAt: lastPlayedAt,
            AcquiredAt: null,
            Source: "steam_local",
            ObservedAt: observedAt);

    private async Task<Work> WorkBehindAsync(string appId)
    {
        var release = await _releases.FindByExternalIdAsync("steam", appId);
        Assert.NotNull(release);
        var work = await _works.GetAsync(release.WorkId);
        Assert.NotNull(work);
        return work;
    }

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
    public async Task Titleless_candidate_creates_a_provisional_work_and_release()
    {
        var result = await _resolver.ResolveAsync(
            [TitlelessCandidate("1203620", 817, Utc(2026, 8, 15, 9, 3, 12), Utc(2026, 8, 23, 12, 0, 0))]);

        Assert.Equal(new ResolveResult(
            MatchedExisting: 0, CreatedReleases: 1, PlayRecordsWritten: 1, SnapshotsWritten: 1,
            NamesPromoted: 0), result);

        // works.name is NOT NULL, so a placeholder is minted — and flagged so
        // enrichment can tell it from a real title.
        var work = await WorkBehindAsync("1203620");
        Assert.Equal("App 1203620", work.Name);
        Assert.True(work.NameIsProvisional);

        var release = await _releases.FindByExternalIdAsync("steam", "1203620");
        Assert.NotNull(release);
        Assert.Equal("App 1203620", release.Name);

        // Playtime is still real data and is recorded normally.
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.False(ownership.Installed);
        Assert.Null(ownership.InstallPath);
        var playRecord = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(playRecord);
        Assert.Equal(817, playRecord.PlaytimeMinutes);
    }

    [Fact]
    public async Task Real_title_promotes_a_provisional_work_without_duplicating_it()
    {
        // Sync 1: played, uninstalled — nothing on disk names it.
        await _resolver.ResolveAsync(
            [TitlelessCandidate("1203620", 817, Utc(2026, 8, 15, 9, 3, 12), Utc(2026, 8, 23, 12, 0, 0))]);
        var provisional = await WorkBehindAsync("1203620");

        // Sync 2: the user reinstalled it, so the appmanifest now supplies a title.
        var result = await _resolver.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15, 9, 3, 12), Utc(2026, 8, 30, 12, 0, 0))]);

        Assert.Equal(new ResolveResult(
            MatchedExisting: 1, CreatedReleases: 0, PlayRecordsWritten: 0, SnapshotsWritten: 0,
            NamesPromoted: 1), result);

        // Promoted in place: same work row, real name, flag cleared.
        var promoted = await WorkBehindAsync("1203620");
        Assert.Equal(provisional.Id, promoted.Id);
        Assert.Equal("Elden Ring", promoted.Name);
        Assert.False(promoted.NameIsProvisional);

        // The 1:1 release name followed it, and no duplicate work was minted.
        var release = await _releases.FindByExternalIdAsync("steam", "1203620");
        Assert.NotNull(release);
        Assert.Equal("Elden Ring", release.Name);
        Assert.Single(await _works.GetAllAsync());
    }

    [Fact]
    public async Task A_real_title_is_never_demoted_to_a_provisional_one()
    {
        // Installed first, so the work holds a genuine title.
        await _resolver.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15, 9, 3, 12), Utc(2026, 8, 23, 12, 0, 0))]);

        // Uninstalled later: the appmanifest is gone, so the candidate arrives
        // title-less. The stored name must survive untouched.
        var result = await _resolver.ResolveAsync(
            [TitlelessCandidate("1203620", 817, Utc(2026, 8, 15, 9, 3, 12), Utc(2026, 8, 30, 12, 0, 0))]);

        Assert.Equal(0, result.NamesPromoted);

        var work = await WorkBehindAsync("1203620");
        Assert.Equal("Elden Ring", work.Name);
        Assert.False(work.NameIsProvisional);

        var release = await _releases.FindByExternalIdAsync("steam", "1203620");
        Assert.NotNull(release);
        Assert.Equal("Elden Ring", release.Name);

        // Install state still tracks reality, though.
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.False(ownership.Installed);
    }

    [Fact]
    public async Task Repeated_titleless_syncs_do_not_repromote_or_rename()
    {
        var observed = Utc(2026, 8, 23, 12, 0, 0);
        await _resolver.ResolveAsync([TitlelessCandidate("1203620", 817, null, observed)]);

        var result = await _resolver.ResolveAsync(
            [TitlelessCandidate("1203620", 817, null, Utc(2026, 8, 24, 12, 0, 0))]);

        Assert.Equal(new ResolveResult(
            MatchedExisting: 1, CreatedReleases: 0, PlayRecordsWritten: 0, SnapshotsWritten: 0,
            NamesPromoted: 0), result);

        var work = await WorkBehindAsync("1203620");
        Assert.Equal("App 1203620", work.Name);
        Assert.True(work.NameIsProvisional);
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

    /// <summary>
    /// §5.3: the work + release + external-id triple is atomic. A crash between
    /// the release insert and the external-id insert used to leave a work no
    /// later sync could find by external id — so the next sync minted a
    /// duplicate that GetAllAsync happily showed in the library, forever.
    /// </summary>
    [Fact]
    public async Task A_crash_mid_create_leaves_no_orphan_work_or_release()
    {
        var failing = new ThrowingReleaseRepository(_releases, failOnProviderId: "1244090");
        var resolver = new ExternalIdResolver(
            _works, failing, _ownerships, _playRecords, _snapshots, _db.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(
        [
            Candidate("2686630", "Voyagers of Nera", 244, Utc(2026, 8, 21), Utc(2026, 8, 23)),
            Candidate("1244090", "Sea of Stars: Sunset Edition", null, null, Utc(2026, 8, 23)),
        ]));

        // The half-built entity is gone — and so is the candidate that had
        // already succeeded: the whole pass is one transaction, not one per row.
        Assert.Empty(await _works.GetAllAsync());
        Assert.Empty(await _ownerships.GetAllAsync());
        Assert.Null(await _releases.FindByExternalIdAsync("steam", "1244090"));
        Assert.Null(await _releases.FindByExternalIdAsync("steam", "2686630"));
    }

    [Fact]
    public async Task The_sync_after_a_crash_creates_one_work_not_a_duplicate()
    {
        var failing = new ThrowingReleaseRepository(_releases, failOnProviderId: "1203620");
        var crashing = new ExternalIdResolver(
            _works, failing, _ownerships, _playRecords, _snapshots, _db.Factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => crashing.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15), Utc(2026, 8, 23))]));

        // Retry with a healthy resolver: exactly one work, findable by its
        // external id. Without the rollback there would be two.
        var result = await _resolver.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15), Utc(2026, 8, 24))]);

        Assert.Equal(1, result.CreatedReleases);
        Assert.Single(await _works.GetAllAsync());
        Assert.NotNull(await _releases.FindByExternalIdAsync("steam", "1203620"));
    }

    /// <summary>
    /// SteamLibrarySource guarantees minutes, last-played and account
    /// attribution move together and are never mixed. That guarantee survives
    /// only if the stored attribution is refreshed when the winner changes.
    /// </summary>
    [Fact]
    public async Task Attribution_moves_with_the_playtime_it_describes()
    {
        await _resolver.ResolveAsync(
            [Candidate("2686630", "Voyagers of Nera", 244, Utc(2026, 8, 1), Utc(2026, 8, 2))]);

        // A second account on the same machine now has more time on this game,
        // so it wins the whole record.
        var newWinner = Candidate("2686630", "Voyagers of Nera", 900, Utc(2026, 8, 20), Utc(2026, 8, 21))
            with
        { AccountRef = "87654321" };
        await _resolver.ResolveAsync([newWinner]);

        var release = await _releases.FindByExternalIdAsync("steam", "2686630");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        var latest = await _playRecords.GetLatestAsync(ownership.Id);

        Assert.NotNull(latest);
        Assert.Equal(900, latest.PlaytimeMinutes);
        Assert.Equal(Utc(2026, 8, 20), latest.LastPlayedAt);

        // The ownership must name the same account the record's minutes came
        // from — not the account that happened to be first.
        Assert.Equal("87654321", ownership.AccountRef);
    }

    [Fact]
    public async Task A_scan_that_names_no_account_keeps_the_last_known_attribution()
    {
        await _resolver.ResolveAsync(
            [Candidate("2686630", "Voyagers of Nera", 244, Utc(2026, 8, 1), Utc(2026, 8, 2))]);

        // A machine-level observation (unreadable userdata) knows nothing about
        // accounts: refresh means refresh, not erase.
        var unattributed = Candidate("2686630", "Voyagers of Nera", 244, Utc(2026, 8, 1), Utc(2026, 8, 9))
            with
        { AccountRef = null };
        await _resolver.ResolveAsync([unattributed]);

        var release = await _releases.FindByExternalIdAsync("steam", "2686630");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.Equal("12345678", ownership.AccountRef);
    }

    [Fact]
    public async Task Repeated_syncs_keep_exactly_one_ownership_per_release_and_store()
    {
        for (var day = 1; day <= 3; day++)
        {
            await _resolver.ResolveAsync(
                [Candidate("2686630", "Voyagers of Nera", 244 + day, Utc(2026, 8, day), Utc(2026, 9, day))]);
        }

        var release = await _releases.FindByExternalIdAsync("steam", "2686630");
        Assert.NotNull(release);
        Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.Single(await _ownerships.GetAllAsync());
    }

    /// <summary>
    /// A manifest can carry <c>"name" ""</c>. Blank is "unnamed", not a name:
    /// treating it as one created a work with a blank title and no provisional
    /// flag, which promotion could never repair.
    /// </summary>
    [Fact]
    public async Task Blank_title_creates_a_provisional_work_that_promotion_can_repair()
    {
        await _resolver.ResolveAsync(
            [Candidate("1203620", "   ", 817, Utc(2026, 8, 15), Utc(2026, 8, 23))]);

        var work = await WorkBehindAsync("1203620");
        Assert.Equal("App 1203620", work.Name);
        Assert.True(work.NameIsProvisional);

        var result = await _resolver.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15), Utc(2026, 8, 30))]);

        Assert.Equal(1, result.NamesPromoted);
        Assert.Equal("Elden Ring", (await WorkBehindAsync("1203620")).Name);
    }

    [Fact]
    public async Task A_blank_title_never_overwrites_a_real_one()
    {
        await _resolver.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15), Utc(2026, 8, 23))]);

        var result = await _resolver.ResolveAsync(
            [Candidate("1203620", " ", 817, Utc(2026, 8, 15), Utc(2026, 8, 30))]);

        Assert.Equal(0, result.NamesPromoted);
        var work = await WorkBehindAsync("1203620");
        Assert.Equal("Elden Ring", work.Name);
        Assert.False(work.NameIsProvisional);
    }

    /// <summary>
    /// A machine with appmanifests but no readable userdata knows a last-played
    /// date and no minutes. Gating the play-record write on minutes discarded
    /// those dates and read the whole library as never_played.
    /// </summary>
    [Fact]
    public async Task A_last_played_date_without_minutes_is_still_recorded()
    {
        var manifestOnly = Candidate("1203620", "Elden Ring", null, Utc(2026, 8, 15), Utc(2026, 8, 23))
            with
        { AccountRef = null };

        var result = await _resolver.ResolveAsync([manifestOnly]);

        // The date is an observation; the minutes are not, so no snapshot point.
        Assert.Equal(1, result.PlayRecordsWritten);
        Assert.Equal(0, result.SnapshotsWritten);

        var release = await _releases.FindByExternalIdAsync("steam", "1203620");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        var record = await _playRecords.GetLatestAsync(ownership.Id);

        Assert.NotNull(record);
        Assert.Equal(Utc(2026, 8, 15), record.LastPlayedAt);
        Assert.Equal(0, record.PlaytimeMinutes);
        Assert.Empty(await _snapshots.GetByOwnershipAsync(ownership.Id));
    }

    /// <summary>
    /// Fails the external-id write for one appid, simulating a process death
    /// after the work and release rows are already in.
    /// </summary>
    private sealed class ThrowingReleaseRepository : IReleaseRepository
    {
        private readonly IReleaseRepository _inner;
        private readonly string _failOnProviderId;

        internal ThrowingReleaseRepository(IReleaseRepository inner, string failOnProviderId)
        {
            _inner = inner;
            _failOnProviderId = failOnProviderId;
        }

        public Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default)
            => string.Equals(externalId.ProviderId, _failOnProviderId, StringComparison.Ordinal)
                ? throw new InvalidOperationException("simulated crash before the external id landed")
                : _inner.AddExternalIdAsync(externalId, ct);

        public Task<long> InsertAsync(Release release, CancellationToken ct = default)
            => _inner.InsertAsync(release, ct);

        public Task UpdateNameAsync(long id, string name, CancellationToken ct = default)
            => _inner.UpdateNameAsync(id, name, ct);

        public Task<Release?> GetAsync(long id, CancellationToken ct = default)
            => _inner.GetAsync(id, ct);

        public Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default)
            => _inner.GetByWorkAsync(workId, ct);

        public Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default)
            => _inner.GetExternalIdsAsync(releaseId, ct);

        public Task<Release?> FindByExternalIdAsync(string provider, string providerId, CancellationToken ct = default)
            => _inner.FindByExternalIdAsync(provider, providerId, ct);

        public Task<IReadOnlyList<Hoard.Core.Queries.ReleaseIdentity>> GetIdentitiesAsync(
            CancellationToken ct = default)
            => _inner.GetIdentitiesAsync(ct);
    }
}
