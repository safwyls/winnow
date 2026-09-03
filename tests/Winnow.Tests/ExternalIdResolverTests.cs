using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

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
            _works, _releases, _ownerships, _playRecords, _snapshots, _db.Factory,
            new OwnershipAccountRepository(_db.Factory));
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
    /// A candidate from a source that cannot see the local disk — shaped like
    /// <c>SteamOwnedGame.ToCandidate</c>: it knows the licence, the title and
    /// the playtime, and has no opinion at all about install state.
    /// </summary>
    private static CandidateOwnership WebCandidate(
        string appId,
        string title,
        long playtimeMinutes,
        DateTime? lastPlayedAt,
        DateTime observedAt)
        => new(
            Provider: ExternalIdProviders.Steam,
            ProviderId: appId,
            Title: title,
            AccountRef: "12345678",
            InstallPath: null,
            Installed: null,
            PlaytimeMinutes: playtimeMinutes,
            LastPlayedAt: lastPlayedAt,
            AcquiredAt: null,
            Source: "steam_web_api",
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

    /// <summary>
    /// A source that cannot see the disk (Installed: null — §4.2's
    /// GetOwnedGames) must never clear what the local scan established, no
    /// matter which order the union puts them in. Resolving both orders is the
    /// point of the theory: the live bug was invisible precisely because the web
    /// candidates happened to be resolved second.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_opinionless_candidate_never_clears_install_state_in_either_union_order(bool webFirst)
    {
        var observed = Utc(2026, 8, 23, 12, 0, 0);
        var local = Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15, 9, 3, 12), observed);
        var web = WebCandidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15, 9, 3, 12), observed);

        await _resolver.ResolveAsync(webFirst ? [web, local] : [local, web]);

        var release = await _releases.FindByExternalIdAsync("steam", "1203620");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));

        Assert.True(ownership.Installed);
        Assert.Equal(@"C:\Steam\steamapps\common\Elden Ring", ownership.InstallPath);

        // Both orders reach the same row, not merely a true flag: one ownership,
        // the local account attribution, and the playtime both sources agree on.
        Assert.Equal("12345678", ownership.AccountRef);
        var playRecord = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(playRecord);
        Assert.Equal(817, playRecord.PlaytimeMinutes);
    }

    /// <summary>
    /// The converse, which COALESCE alone would have broken: an opinionless
    /// candidate in the same batch must not stop a genuine uninstall from
    /// showing. Either order, the game leaves the "Installed" filter.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_uninstall_still_shows_through_an_opinionless_candidate(bool webFirst)
    {
        await _resolver.ResolveAsync(
            [Candidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15, 9, 3, 12), Utc(2026, 8, 23, 12, 0, 0))]);

        var observed = Utc(2026, 8, 30, 12, 0, 0);
        var uninstalled = TitlelessCandidate("1203620", 817, Utc(2026, 8, 15, 9, 3, 12), observed);
        var web = WebCandidate("1203620", "Elden Ring", 817, Utc(2026, 8, 15, 9, 3, 12), observed);

        await _resolver.ResolveAsync(webFirst ? [web, uninstalled] : [uninstalled, web]);

        var release = await _releases.FindByExternalIdAsync("steam", "1203620");
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
            _works, failing, _ownerships, _playRecords, _snapshots, _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

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
            _works, failing, _ownerships, _playRecords, _snapshots, _db.Factory,
            new OwnershipAccountRepository(_db.Factory));

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

    // ── One pass, one observation per ownership ──────────────────────────────

    /// <summary>
    /// The alternating-row bug at resolver level. Steam's own two playtime
    /// figures for Portal differ by a minute (279 via GetOwnedGames, 280 via
    /// localconfig.vdf). Un-merged, each candidate "changed" relative to the
    /// other, so both appended a play record — and every later pass appended two
    /// more. One pass now sees one observation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_sources_a_minute_apart_write_one_play_record_not_a_pair(bool webFirst)
    {
        var played = Utc(2018, 5, 25, 3, 7, 27);
        var local = Candidate("400", "Portal", 280, played, Utc(2026, 8, 25, 16, 1, 0));
        var web = WebCandidate("400", "Portal", 279, played, Utc(2026, 8, 25, 2, 25, 0));

        var result = await _resolver.ResolveAsync(webFirst ? [web, local] : [local, web]);

        Assert.Equal(1, result.PlayRecordsWritten);
        Assert.Equal(1, result.SnapshotsWritten);

        var release = await _releases.FindByExternalIdAsync("steam", "400");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));

        var record = Assert.Single(await _playRecords.GetByOwnershipAsync(ownership.Id));
        Assert.Equal(280, record.PlaytimeMinutes);
        Assert.Equal(played, record.LastPlayedAt);
        Assert.Equal("steam_local", record.Source);
    }

    /// <summary>
    /// <b>The property that was missing.</b> Two passes over sources that have
    /// not changed must write nothing the second time — whatever the sources
    /// disagree about among themselves. Before the merge this wrote two rows per
    /// pass forever, at the snapshot scheduler's 15-minute cadence.
    /// </summary>
    [Fact]
    public async Task A_second_pass_over_unchanged_disagreeing_sources_writes_nothing()
    {
        var played = Utc(2018, 5, 25, 3, 7, 27);

        CandidateOwnership[] Pass(DateTime observedAt) =>
        [
            Candidate("400", "Portal", 280, played, observedAt),
            WebCandidate("400", "Portal", 279, played, observedAt.AddHours(-14)),
        ];

        await _resolver.ResolveAsync(Pass(Utc(2026, 8, 25, 16, 1, 0)));

        // A later wall clock, the same facts: idempotent by change detection,
        // not by observation time.
        var second = await _resolver.ResolveAsync(Pass(Utc(2026, 8, 25, 16, 16, 0)));
        var third = await _resolver.ResolveAsync(Pass(Utc(2026, 8, 25, 16, 31, 0)));

        Assert.Equal(0, second.PlayRecordsWritten);
        Assert.Equal(0, second.SnapshotsWritten);
        Assert.Equal(0, third.PlayRecordsWritten);
        Assert.Equal(0, third.SnapshotsWritten);

        var release = await _releases.FindByExternalIdAsync("steam", "400");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownership.Id));
        Assert.Single(await _snapshots.GetByOwnershipAsync(ownership.Id));
    }

    /// <summary>
    /// The other half of the live failure: the 86400 placeholder. Once both
    /// readers call it unknown, the pair agrees on null and settles — and the
    /// detail view can never show 2 January 1970 as a last-played date.
    /// </summary>
    [Fact]
    public async Task An_unknown_last_played_settles_on_one_row_with_no_date()
    {
        CandidateOwnership[] Pass(DateTime observedAt) =>
        [
            TitlelessCandidate("60", 3, null, observedAt),
            WebCandidate("60", "Ricochet", 3, null, observedAt),
        ];

        await _resolver.ResolveAsync(Pass(Utc(2026, 8, 25, 16, 1, 0)));
        var second = await _resolver.ResolveAsync(Pass(Utc(2026, 8, 25, 16, 16, 0)));

        Assert.Equal(0, second.PlayRecordsWritten);

        var release = await _releases.FindByExternalIdAsync("steam", "60");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));

        var record = Assert.Single(await _playRecords.GetByOwnershipAsync(ownership.Id));
        Assert.Null(record.LastPlayedAt);

        // The minutes are real and stay real — that is why the correcting
        // migration nulls the column instead of deleting the row.
        Assert.Equal(3, record.PlaytimeMinutes);
    }

    /// <summary>
    /// Merging simultaneous views must not flatten a genuine time series: a real
    /// session between two passes is still a change and still appends.
    /// </summary>
    [Fact]
    public async Task A_real_session_still_appends_after_the_sources_have_been_merged()
    {
        await _resolver.ResolveAsync(
        [
            Candidate("400", "Portal", 280, Utc(2018, 5, 25), Utc(2026, 8, 25, 16, 1, 0)),
            WebCandidate("400", "Portal", 279, Utc(2018, 5, 25), Utc(2026, 8, 25, 2, 25, 0)),
        ]);

        var result = await _resolver.ResolveAsync(
        [
            Candidate("400", "Portal", 331, Utc(2026, 8, 25, 15, 30, 0), Utc(2026, 8, 25, 16, 16, 0)),
            WebCandidate("400", "Portal", 279, Utc(2018, 5, 25), Utc(2026, 8, 25, 2, 25, 0)),
        ]);

        Assert.Equal(1, result.PlayRecordsWritten);
        Assert.Equal(1, result.SnapshotsWritten);

        var release = await _releases.FindByExternalIdAsync("steam", "400");
        Assert.NotNull(release);
        var ownership = Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));

        var history = await _playRecords.GetByOwnershipAsync(ownership.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(280, history[0].PlaytimeMinutes);
        Assert.Equal(331, history[1].PlaytimeMinutes);
    }

    // ── The cross-pass sawtooth: one minute of disagreement is not a change ──

    /// <summary>
    /// The live defect. <c>localconfig.vdf</c> says 280 minutes for Portal
    /// and <c>GetOwnedGames</c> says 279; the local job and the remote job
    /// run in separate passes, so each wrote its own figure and the pair
    /// fabricated a rise and a fall on every cycle. Ownerships 6, 46 and 47
    /// on the live database carried nine such phantom rises (verified
    /// 2026-08-29).
    /// </summary>
    [Fact]
    public async Task A_one_minute_disagreement_across_passes_is_absorbed()
    {
        var played = Utc(2018, 5, 25, 3, 7, 27);

        await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, played, Utc(2026, 8, 25, 16, 1, 0))],
            playtime: PlaytimeView.LowerBound);

        var second = await _resolver.ResolveAsync(
            [WebCandidate("400", "Portal", 279, played, Utc(2026, 8, 25, 16, 16, 0))],
            playtime: PlaytimeView.LowerBound);

        Assert.Equal(0, second.PlayRecordsWritten);
        Assert.Equal(0, second.SnapshotsWritten);

        var ownership = await OwnershipOfAsync("400");
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownership.Id));
        Assert.Single(await _snapshots.GetByOwnershipAsync(ownership.Id));
    }

    /// <summary>
    /// The sawtooth at full cadence: six passes alternating between the local
    /// and remote jobs the way the scheduler actually runs them. Every pass
    /// after the first must write nothing, because the underlying facts have
    /// not changed and the one-minute disagreement is noise.
    /// </summary>
    [Fact]
    public async Task Alternating_sources_write_nothing_after_the_first_pass()
    {
        var played = Utc(2018, 5, 25, 3, 7, 27);
        var observed = Utc(2026, 8, 25, 16, 1, 0);

        var first = await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, played, observed)],
            playtime: PlaytimeView.LowerBound);

        Assert.Equal(1, first.PlayRecordsWritten);
        Assert.Equal(1, first.SnapshotsWritten);

        for (var pass = 1; pass <= 6; pass++)
        {
            var at = observed.AddMinutes(15 * pass);
            var result = await _resolver.ResolveAsync(
                pass % 2 == 1
                    ? [WebCandidate("400", "Portal", 279, played, at)]
                    : [Candidate("400", "Portal", 280, played, at)],
                playtime: PlaytimeView.LowerBound);

            Assert.Equal(0, result.PlayRecordsWritten);
            Assert.Equal(0, result.SnapshotsWritten);
        }

        var ownership = await OwnershipOfAsync("400");
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownership.Id));
        Assert.Single(await _snapshots.GetByOwnershipAsync(ownership.Id));
    }

    /// <summary>
    /// The absorb-versus-record boundary, both directions. 281 is one minute
    /// above the stored 280 and is absorbed. 282 is two minutes of play and
    /// is recorded. 279 is one minute below and is absorbed by the band. 278
    /// is outside the band, so the clamp raises it back to 280 and the row
    /// is unchanged, writing nothing.
    /// </summary>
    [Theory]
    [InlineData(281, 0, 280)]  // a rise inside the band: absorbed, the stored figure stands
    [InlineData(282, 1, 282)]  // two minutes is play, and play is recorded
    [InlineData(279, 0, 280)]  // a fall inside the band: absorbed, nothing written
    [InlineData(278, 0, 280)]  // outside the band the clamp raises it back to the stored figure
    public async Task The_band_is_one_minute_wide_in_both_directions(
        long minutes, int expectedRows, long expectedLatest)
    {
        var played = Utc(2018, 5, 25, 3, 7, 27);

        await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, played, Utc(2026, 8, 25, 16, 1, 0))],
            playtime: PlaytimeView.LowerBound);

        var second = await _resolver.ResolveAsync(
            [WebCandidate("400", "Portal", minutes, played, Utc(2026, 8, 25, 16, 16, 0))],
            playtime: PlaytimeView.LowerBound);

        Assert.Equal(expectedRows, second.PlayRecordsWritten);
        Assert.Equal(expectedRows, second.SnapshotsWritten);

        var ownership = await OwnershipOfAsync("400");
        var latest = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(latest);
        Assert.Equal(expectedLatest, latest.PlaytimeMinutes);
    }

    /// <summary>
    /// A real session is still a real session. The recommender depends on
    /// episode signal from play records; absorbing a minute of cross-source
    /// noise must never absorb an evening of genuine play.
    /// </summary>
    [Fact]
    public async Task Genuine_progress_still_lands_under_the_tolerance()
    {
        await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, Utc(2018, 5, 25, 3, 7, 27), Utc(2026, 8, 25, 16, 1, 0))],
            playtime: PlaytimeView.LowerBound);

        var second = await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 331, Utc(2026, 8, 25, 15, 30, 0), Utc(2026, 8, 25, 16, 16, 0))],
            playtime: PlaytimeView.LowerBound);

        Assert.Equal(1, second.PlayRecordsWritten);
        Assert.Equal(1, second.SnapshotsWritten);

        var ownership = await OwnershipOfAsync("400");
        var history = await _playRecords.GetByOwnershipAsync(ownership.Id);
        Assert.Equal([280L, 331L], history.Select(r => r.PlaytimeMinutes));
    }

    /// <summary>
    /// The err-low proof. Inside the band the clamp does not raise, so a row
    /// written for another reason, here a last-played date that genuinely
    /// moved, carries the lower figure (279) under its own source
    /// (<c>steam_web_api</c>) rather than the higher one (280) under
    /// <c>+carried</c>. The snapshot series does not move at all, keeping
    /// the cumulative invariant intact.
    /// </summary>
    [Fact]
    public async Task Inside_the_band_the_lower_figure_is_kept_rather_than_carried_up()
    {
        await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, Utc(2018, 5, 25, 3, 7, 27), Utc(2026, 8, 25, 16, 1, 0))],
            playtime: PlaytimeView.LowerBound);

        var second = await _resolver.ResolveAsync(
            [WebCandidate("400", "Portal", 279, Utc(2026, 8, 25, 15, 30, 0), Utc(2026, 8, 25, 16, 16, 0))],
            playtime: PlaytimeView.LowerBound);

        Assert.Equal(1, second.PlayRecordsWritten);
        Assert.Equal(0, second.SnapshotsWritten);

        var ownership = await OwnershipOfAsync("400");
        var latest = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(latest);
        Assert.Equal(279, latest.PlaytimeMinutes);
        Assert.Equal("steam_web_api", latest.Source);
        Assert.Equal(Utc(2026, 8, 25, 15, 30, 0), latest.LastPlayedAt);

        Assert.Equal([280L], (await _snapshots.GetByOwnershipAsync(ownership.Id))
            .Select(s => s.PlaytimeMinutes));
    }

    /// <summary>
    /// Outside the band the clamp is unchanged. A figure two or more minutes
    /// below the stored one is a blind spot in this pass, not cross-source
    /// noise; it is raised to the floor and the source is marked
    /// <c>+carried</c> so downstream code knows the minutes are not the
    /// source's own report.
    /// </summary>
    [Fact]
    public async Task Outside_the_band_a_lower_figure_is_still_clamped_and_marked_carried()
    {
        await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, Utc(2018, 5, 25, 3, 7, 27), Utc(2026, 8, 25, 16, 1, 0))],
            playtime: PlaytimeView.LowerBound);

        await _resolver.ResolveAsync(
            [WebCandidate("400", "Portal", 200, Utc(2026, 8, 25, 15, 30, 0), Utc(2026, 8, 25, 16, 16, 0))],
            playtime: PlaytimeView.LowerBound);

        var ownership = await OwnershipOfAsync("400");
        var latest = await _playRecords.GetLatestAsync(ownership.Id);
        Assert.NotNull(latest);
        Assert.Equal(280, latest.PlaytimeMinutes);
        Assert.True(PlayRecordSources.IsCarried(latest.Source));
    }

    /// <summary>
    /// The tolerance is a <see cref="PlaytimeView.LowerBound"/> rule.
    /// <see cref="PlaytimeView.Complete"/> means the pass sees the whole
    /// truth, so a one-minute correction is a genuine correction and is
    /// recorded as a new observation, byte-for-byte unchanged from before.
    /// </summary>
    [Fact]
    public async Task Complete_records_a_one_minute_correction_as_before()
    {
        var played = Utc(2018, 5, 25, 3, 7, 27);

        await _resolver.ResolveAsync(
            [Candidate("400", "Portal", 280, played, Utc(2026, 8, 25, 16, 1, 0))]);

        var second = await _resolver.ResolveAsync(
            [WebCandidate("400", "Portal", 279, played, Utc(2026, 8, 25, 16, 16, 0))]);

        Assert.Equal(1, second.PlayRecordsWritten);
        Assert.Equal(1, second.SnapshotsWritten);
    }

    private async Task<Ownership> OwnershipOfAsync(string appId)
    {
        var release = await _releases.FindByExternalIdAsync("steam", appId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
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

        public Task<IReadOnlyList<Winnow.Core.Queries.ReleaseIdentity>> GetIdentitiesAsync(
            CancellationToken ct = default)
            => _inner.GetIdentitiesAsync(ct);
    }
}
