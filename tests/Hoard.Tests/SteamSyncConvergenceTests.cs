using Dapper;
using Hoard.App.Services;
using Hoard.Core.Ingest;
using Hoard.Data.Repositories;
using Hoard.Enrich.SteamWeb;
using Hoard.Enrich.SteamWeb.Model;
using Hoard.Ingest.Steam;
using Hoard.Resolve;
using Hoard.Tests.SteamWeb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The whole sync path, end to end, against the property that was missing:
/// <b>two syncs over unchanged sources write zero play records on the
/// second.</b>
///
/// <para>Both readers are real — the §4.1 local scan over a synthetic Steam root
/// and the §4.2 <c>GetOwnedGames</c> parser over a canned response body — so the
/// two ways the sources used to disagree are both genuinely exercised rather
/// than stubbed out: the <c>86400</c> placeholder that one reader used to turn
/// into a literal 1970-01-02, and the minute of drift between Steam's own two
/// playtime figures. Only the HTTP transport is substituted.</para>
///
/// <para>On the author's live library those two disagreements had grown 1,073
/// play records for 946 ownerships — 45 of them dated 2 January 1970 — and a
/// sync that should have been a complete no-op still wrote 7 new rows, every 15
/// minutes, forever.</para>
/// </summary>
public sealed class SteamSyncConvergenceTests : IDisposable
{
    /// <summary>Installed, played, and reported a minute apart by the two sources.</summary>
    private const string DisagreeingAppId = "400";

    /// <summary>Uninstalled, played, and carrying the 86400 "date unknown" placeholder.</summary>
    private const string PlaceholderAppId = "60";

    /// <summary>Owned and never launched: invisible to localconfig.vdf entirely.</summary>
    private const string NeverLaunchedAppId = "1244090";

    private readonly TempDatabase _db = new();
    private readonly string _steamRoot;
    private readonly SteamSyncService _sync;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _playRecords;

    public SteamSyncConvergenceTests()
    {
        _steamRoot = Path.Combine(Path.GetTempPath(), $"hoard-converge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_steamRoot, "steamapps"));

        var escapedRoot = _steamRoot.Replace(@"\", @"\\");
        File.WriteAllText(
            Path.Combine(_steamRoot, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"{{escapedRoot}}"
                    "label"		""
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(_steamRoot, "steamapps", "appmanifest_400.acf"),
            """
            "AppState"
            {
                "appid"		"400"
                "name"		"Portal"
                "StateFlags"		"4"
                "installdir"		"Portal"
            }
            """);

        // localconfig: 400 with 280 minutes and a real date, 60 with 3 minutes
        // and the 86400 placeholder Steam writes for "played before we tracked
        // this".
        var config = Path.Combine(_steamRoot, "userdata", "12345678", "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "localconfig.vdf"),
            """
            "UserLocalConfigStore"
            {
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "400"
                                {
                                    "LastPlayed"		"1527216883"
                                    "Playtime"		"280"
                                }
                                "60"
                                {
                                    "LastPlayed"		"86400"
                                    "Playtime"		"3"
                                }
                            }
                        }
                    }
                }
            }
            """);

        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _playRecords = new PlayRecordRepository(_db.Factory);

        _sync = new SteamSyncService(
            new SteamLibrarySource(steamRoot: _steamRoot),
            SilentStores.Epic(),
            SilentStores.Gog(),
            new ExternalIdResolver(
                new WorkRepository(_db.Factory),
                _releases,
                _ownerships,
                _playRecords,
                new PlaytimeSnapshotRepository(_db.Factory),
                _db.Factory),
            NullLogger<SteamSyncService>.Instance,
            new OwnedLibraryStub());
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_steamRoot, recursive: true);
    }

    private long PlayRecordCount()
    {
        using var conn = _db.Factory.Open();
        return conn.ExecuteScalar<long>("SELECT COUNT(*) FROM play_records;");
    }

    [Fact]
    public async Task A_second_sync_over_unchanged_sources_writes_no_play_records()
    {
        var first = await _sync.SyncAsync();
        Assert.NotNull(first.Result);
        var afterFirst = PlayRecordCount();
        Assert.True(afterFirst > 0, "the first sync has to record something");

        var second = await _sync.SyncAsync();
        Assert.NotNull(second.Result);

        Assert.Equal(0, second.Result.PlayRecordsWritten);
        Assert.Equal(0, second.Result.SnapshotsWritten);
        Assert.Equal(afterFirst, PlayRecordCount());

        // And it stays settled — the scheduler runs this every 15 minutes.
        await _sync.SyncAsync();
        await _sync.SyncAsync();
        Assert.Equal(afterFirst, PlayRecordCount());
    }

    /// <summary>
    /// One ownership, one play record — not the alternating pair the two sources
    /// used to leave behind. The stored figure is the larger of the two, which
    /// here is the local one (§4.1's primary source), reached by <c>max</c>
    /// rather than by ranking the sources.
    /// </summary>
    [Fact]
    public async Task The_appid_both_sources_see_settles_on_a_single_record()
    {
        await _sync.SyncAsync();
        await _sync.SyncAsync();
        await _sync.SyncAsync();

        var record = Assert.Single(await RecordsForAsync(DisagreeingAppId));
        Assert.Equal(280, record.PlaytimeMinutes);
        Assert.Equal("steam_local", record.Source);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1527216883).UtcDateTime,
            record.LastPlayedAt);
    }

    /// <summary>
    /// The placeholder, end to end. Both readers now call 86400 unknown, so the
    /// pair agrees, one row is written, and no date in the database is older
    /// than Steam.
    /// </summary>
    [Fact]
    public async Task The_86400_placeholder_never_reaches_the_database_as_a_1970_date()
    {
        await _sync.SyncAsync();
        await _sync.SyncAsync();

        var record = Assert.Single(await RecordsForAsync(PlaceholderAppId));
        Assert.Null(record.LastPlayedAt);
        Assert.Equal(3, record.PlaytimeMinutes);

        using var conn = _db.Factory.Open();
        Assert.Equal(0, conn.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM play_records
            WHERE last_played_at IS NOT NULL
            AND   CAST(strftime('%s', last_played_at) AS INTEGER) < 315532800;
            """));
    }

    /// <summary>
    /// The union is still a union. Merging collapses the OVERLAP; it must not
    /// drop the games only one source can see — the never-launched title the
    /// local files cannot know about is the reason §4.2 is queried at all.
    /// </summary>
    [Fact]
    public async Task Merging_the_overlap_does_not_shrink_the_library()
    {
        await _sync.SyncAsync();
        await _sync.SyncAsync();

        var all = await _ownerships.GetAllAsync();
        Assert.Equal(3, all.Count);

        foreach (var appId in new[] { DisagreeingAppId, PlaceholderAppId, NeverLaunchedAppId })
        {
            Assert.NotNull(await _releases.FindByExternalIdAsync("steam", appId));
        }

        // The installed game is still installed: merging must not disturb the
        // three-valued install rule.
        var portal = await OwnershipForAsync(DisagreeingAppId);
        Assert.True(portal.Installed);
        Assert.Equal(Path.Combine(_steamRoot, "steamapps", "common", "Portal"), portal.InstallPath);

        // The never-launched one carries one settled record of zero minutes.
        // §4.2 zero IS an observation — it is how an owned, never-launched game
        // reports, and that whole population is invisible to localconfig.vdf —
        // so the row is real. What matters here is that there is exactly one of
        // it, and that it never grows a date.
        var unlaunched = Assert.Single(await RecordsForAsync(NeverLaunchedAppId));
        Assert.Equal(0, unlaunched.PlaytimeMinutes);
        Assert.Null(unlaunched.LastPlayedAt);
    }

    /// <summary>A real session between syncs is still a change and still appends.</summary>
    [Fact]
    public async Task A_session_between_syncs_still_appends_exactly_one_record()
    {
        await _sync.SyncAsync();
        await _sync.SyncAsync();
        var before = PlayRecordCount();

        // Steam writes the new total to localconfig.vdf.
        var configPath = Path.Combine(_steamRoot, "userdata", "12345678", "config", "localconfig.vdf");
        File.WriteAllText(
            configPath,
            File.ReadAllText(configPath).Replace("\"280\"", "\"331\"", StringComparison.Ordinal));

        var report = await _sync.SyncAsync();

        Assert.NotNull(report.Result);
        Assert.Equal(1, report.Result.PlayRecordsWritten);
        Assert.Equal(1, report.Result.SnapshotsWritten);
        Assert.Equal(before + 1, PlayRecordCount());

        var history = await RecordsForAsync(DisagreeingAppId);
        Assert.Equal(2, history.Count);
        Assert.Equal(280, history[0].PlaytimeMinutes);
        Assert.Equal(331, history[1].PlaytimeMinutes);

        // ...and then settles again.
        var after = await _sync.SyncAsync();
        Assert.NotNull(after.Result);
        Assert.Equal(0, after.Result.PlayRecordsWritten);
    }

    private async Task<Core.Domain.Ownership> OwnershipForAsync(string appId)
    {
        var release = await _releases.FindByExternalIdAsync("steam", appId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
    }

    private async Task<IReadOnlyList<Core.Domain.PlayRecord>> RecordsForAsync(string appId)
        => await _playRecords.GetByOwnershipAsync((await OwnershipForAsync(appId)).Id);

    /// <summary>
    /// Stands in for the HTTP transport and nothing else: the body below goes
    /// through the real <see cref="SteamWebJson"/> parser and the real
    /// <see cref="SteamOwnedGame.ToCandidate"/> projection, so the 86400
    /// placeholder is decoded by the code under test rather than by the test.
    ///
    /// <para>Portal reports 279 minutes here against localconfig's 280 — the
    /// exact drift observed live between Steam's own two figures (Arma 2: 2 vs
    /// 3, Operation Arrowhead: 153 vs 154, Portal: 279 vs 280).</para>
    /// </summary>
    private sealed class OwnedLibraryStub : ISteamWebApiClient
    {
        private static readonly string Body = SteamWebFixtures.OwnedGames(
            new SteamWebFixtures.OwnedGameFixture(
                400, "Portal", PlaytimeForever: 279, RtimeLastPlayed: 1527216883),
            new SteamWebFixtures.OwnedGameFixture(
                60, "Ricochet", PlaytimeForever: 3, RtimeLastPlayed: 86400),
            new SteamWebFixtures.OwnedGameFixture(
                1244090, "Sea of Stars", PlaytimeForever: 0, RtimeLastPlayed: 0));

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(
            SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(new SteamOwnedLibrary(
                steamId,
                Succeeded: true,
                Games: SteamWebJson.TryReadOwnedGames(Body)!,
                // Deliberately stale, the way a cached §4.2 response is: this is
                // what used to let the local answer win the "latest record"
                // query by accident, and hid the 1970 rows until the cache
                // happened to refresh inside a sync.
                ObservedAt: new DateTime(2026, 8, 25, 2, 25, 7, DateTimeKind.Utc),
                FromCache: true));

        public async Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
            SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => (await GetOwnedGamesAsync(steamId, cacheTtl, ct))
                .ToCandidates(SteamWebApiClient.SourceName);
    }
}
