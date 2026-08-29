using Winnow.App.Services;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The hazard the F04 split introduced, at the level it would have shipped at.
/// The two jobs no longer run in one pass, so they no longer merge: the local
/// job sees only what the Steam client has synced to this machine, the remote
/// job sees the account-wide total, and on any library with a second PC or a
/// Steam Deck the first number sits permanently below the second (spike
/// <c>docs/spikes/steam-local-files.md</c> §3.7). Left alone that writes the
/// series down every fifteen minutes and back up every six hours — a sawtooth
/// in the exact table the scheduler exists to build, and a
/// <c>latest_play.last_played_at</c> that flips the ownership's bucket with it.
///
/// <para>Everything below the HTTP call is real: the real
/// <see cref="SteamLibrarySource"/> over a synthetic Steam root, the real
/// <see cref="ExternalIdResolver"/> over a real migrated database, and the real
/// bucket query. Only the owned-library response is substituted.</para>
/// </summary>
public sealed class CrossJobPlaytimeSeriesTests : IDisposable
{
    private const string AppId = "1203620";

    /// <summary>What localconfig.vdf on this machine has synced down.</summary>
    private const long LocalMinutes = 400;

    /// <summary>What the account has actually accrued, most of it on another PC.</summary>
    private const long AccountWideMinutes = 900;

    private static readonly DateTime LocallyLastPlayed = new(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AccountLastPlayed = new(2026, 8, 20, 21, 30, 0, DateTimeKind.Utc);

    private readonly TempDatabase _db = new();
    private readonly string _steamRoot;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _playRecords;
    private readonly PlaytimeSnapshotRepository _snapshots;
    private readonly LibraryQueryRepository _library;
    private readonly LocalLibrarySyncService _local;
    private readonly RemoteOwnershipSyncService _remote;

    public CrossJobPlaytimeSeriesTests()
    {
        _steamRoot = Path.Combine(Path.GetTempPath(), $"winnow-crossjob-{Guid.NewGuid():N}");
        WriteSteamRoot(_steamRoot, LocalMinutes, LocallyLastPlayed);

        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);
        _playRecords = new PlayRecordRepository(_db.Factory);
        _snapshots = new PlaytimeSnapshotRepository(_db.Factory);
        _library = new LibraryQueryRepository(_db.Factory);

        var resolver = new ExternalIdResolver(
            new WorkRepository(_db.Factory),
            _releases,
            _ownerships,
            _playRecords,
            _snapshots,
            _db.Factory);
        var gate = new LibrarySyncGate();

        _local = new LocalLibrarySyncService(
            new SteamLibrarySource(steamRoot: _steamRoot),
            SilentStores.Epic(),
            SilentStores.Gog(),
            resolver,
            gate,
            NullLogger<LocalLibrarySyncService>.Instance);

        _remote = new RemoteOwnershipSyncService(
            _local,
            resolver,
            gate,
            NullLogger<RemoteOwnershipSyncService>.Instance,
            new OwnedLibraryStub(AccountWideMinutes, AccountLastPlayed));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_steamRoot))
        {
            Directory.Delete(_steamRoot, recursive: true);
        }
    }

    /// <summary>
    /// Startup, then the six-hour backfill, then the fifteen-minute tick that
    /// used to undo it — twice, because the sawtooth only shows from the third
    /// pass on. The series must climb and stop, and the bucket must not move
    /// under a tick that learned nothing.
    /// </summary>
    [Fact]
    public async Task A_local_tick_after_a_remote_backfill_does_not_saw_the_series()
    {
        await _local.SyncAsync();
        var afterLocal = await BucketAsync();
        Assert.Equal(LocalMinutes, afterLocal.PlaytimeMinutes);

        await _remote.SyncAsync();
        var afterRemote = await BucketAsync();
        Assert.Equal(AccountWideMinutes, afterRemote.PlaytimeMinutes);
        Assert.Equal(AccountLastPlayed, afterRemote.LastPlayedAt);

        // The scheduler, twice.
        await _local.SyncAsync();
        await _local.SyncAsync();

        var series = (await _snapshots.GetByOwnershipAsync(await OwnershipIdAsync()))
            .Select(s => s.PlaytimeMinutes)
            .ToList();

        // Climbs once and stops. Not [400, 900, 400, 900, ...].
        Assert.Equal([LocalMinutes, AccountWideMinutes], series);
        Assert.Equal(series, series.Order());

        var records = await _playRecords.GetByOwnershipAsync(await OwnershipIdAsync());
        Assert.Equal([LocalMinutes, AccountWideMinutes], records.Select(r => r.PlaytimeMinutes));

        // The bucket is derived from latest_play, so a regressed record would
        // move the row between ticks. It has to be exactly where the backfill
        // left it.
        var afterTicks = await BucketAsync();
        Assert.Equal(afterRemote.Bucket, afterTicks.Bucket);
        Assert.Equal(afterRemote.LastPlayedAt, afterTicks.LastPlayedAt);
        Assert.Equal(afterRemote.PlaytimeMinutes, afterTicks.PlaytimeMinutes);

        // And a further backfill has nothing left to say either.
        var quiet = await _remote.SyncAsync();
        Assert.Equal(0, quiet.Result?.SnapshotsWritten);
        Assert.Equal(0, quiet.Result?.PlayRecordsWritten);
    }

    /// <summary>
    /// The other half of the guarantee: refusing to write DOWN must not become
    /// refusing to write. Real play on this machine still advances the series
    /// past what the backfill stored.
    /// </summary>
    [Fact]
    public async Task Genuine_local_progress_past_the_backfill_still_advances_the_series()
    {
        await _local.SyncAsync();
        await _remote.SyncAsync();

        // The user plays 130 minutes here; localconfig now leads the account
        // total the Web API last reported.
        var played = AccountLastPlayed.AddDays(2);
        WriteSteamRoot(_steamRoot, AccountWideMinutes + 130, played);

        await _local.SyncAsync();

        var bucket = await BucketAsync();
        Assert.Equal(AccountWideMinutes + 130, bucket.PlaytimeMinutes);
        Assert.Equal(played, bucket.LastPlayedAt);

        var series = (await _snapshots.GetByOwnershipAsync(await OwnershipIdAsync()))
            .Select(s => s.PlaytimeMinutes)
            .ToList();
        Assert.Equal([LocalMinutes, AccountWideMinutes, AccountWideMinutes + 130], series);
    }

    /// <summary>
    /// The asymmetric case the two fields make possible: a local tick that is
    /// NEWER on the date and LOWER on the minutes, which is what a machine sees
    /// after playing offline while its synced total still trails the account's.
    /// The two fields must not move together — recency has to advance, because
    /// the bucket and the dormancy signal are read off
    /// <c>latest_play.last_played_at</c> and the user did just play, while the
    /// series has to hold flat rather than record 450 minutes it knows to be a
    /// floor under the stored 900.
    /// </summary>
    [Fact]
    public async Task A_later_local_date_with_lower_minutes_advances_recency_without_moving_the_series()
    {
        await _local.SyncAsync();
        await _remote.SyncAsync();

        var beforeSeries = (await _snapshots.GetByOwnershipAsync(await OwnershipIdAsync()))
            .Select(s => s.PlaytimeMinutes)
            .ToList();

        // Played here today, but localconfig's synced total still trails the
        // account-wide figure the backfill stored.
        var playedToday = AccountLastPlayed.AddDays(9);
        WriteSteamRoot(_steamRoot, 450, playedToday);

        await _local.SyncAsync();

        // The play record advanced to the new date and carries the clamped
        // total, not the 450 the local files could see.
        var records = await _playRecords.GetByOwnershipAsync(await OwnershipIdAsync());
        var latest = records[^1];
        Assert.Equal(playedToday, latest.LastPlayedAt);
        Assert.Equal(AccountWideMinutes, latest.PlaytimeMinutes);
        Assert.Equal(
            [LocalMinutes, AccountWideMinutes, AccountWideMinutes],
            records.Select(r => r.PlaytimeMinutes));

        // The series did not move: 900 is still 900.
        var afterSeries = (await _snapshots.GetByOwnershipAsync(await OwnershipIdAsync()))
            .Select(s => s.PlaytimeMinutes)
            .ToList();
        Assert.Equal(beforeSeries, afterSeries);
        Assert.Equal([LocalMinutes, AccountWideMinutes], afterSeries);

        // And the surface the user reads agrees with both halves.
        var bucket = await BucketAsync();
        Assert.Equal(playedToday, bucket.LastPlayedAt);
        Assert.Equal(AccountWideMinutes, bucket.PlaytimeMinutes);
    }

    /// <summary>
    /// P3. "Registered and idle" is the common state, and the backfill must not
    /// spend a full scan-and-resolve there to produce the rows the local job
    /// just wrote. Proven by the candidate count: a scan of this root would
    /// find one.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_backfill_does_not_scan_or_resolve()
    {
        var resolver = new ExternalIdResolver(
            new WorkRepository(_db.Factory),
            _releases,
            _ownerships,
            _playRecords,
            _snapshots,
            _db.Factory);
        var gate = new LibrarySyncGate();

        var unconfigured = new RemoteOwnershipSyncService(
            new LocalLibrarySyncService(
                new SteamLibrarySource(steamRoot: _steamRoot),
                SilentStores.Epic(),
                SilentStores.Gog(),
                resolver,
                gate,
                NullLogger<LocalLibrarySyncService>.Instance),
            resolver,
            gate,
            NullLogger<RemoteOwnershipSyncService>.Instance,
            new OwnedLibraryStub(AccountWideMinutes, AccountLastPlayed, configured: false));

        var report = await unconfigured.SyncAsync();

        Assert.Equal(0, report.Candidates);
        Assert.Null(report.Result);
        Assert.Null(report.Scan);
        Assert.Empty(await _ownerships.GetAllAsync());
    }

    /// <summary>
    /// P3's other half: the startup pipeline hands the backfill the scan the
    /// local job just paid for. Proven by deleting the Steam root in between —
    /// a pass that re-read the disk would find nothing to attach the owned
    /// library to.
    /// </summary>
    [Fact]
    public async Task The_startup_backfill_reuses_the_local_passs_scan_instead_of_rescanning()
    {
        var local = await _local.SyncAsync();
        Assert.NotNull(local.Scan);

        Directory.Delete(_steamRoot, recursive: true);

        var report = await _remote.SyncAsync(local.Scan!.Value);

        Assert.Equal(2, report.Candidates);
        Assert.Equal(AccountWideMinutes, (await BucketAsync()).PlaytimeMinutes);
    }

    private async Task<long> OwnershipIdAsync()
    {
        var release = await _releases.FindByExternalIdAsync("steam", AppId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id)).Id;
    }

    private async Task<OwnershipBucket> BucketAsync()
    {
        var id = await OwnershipIdAsync();
        var buckets = await _library.GetOwnershipBucketsAsync(BucketThresholds.Default);
        return Assert.Single(buckets, b => b.OwnershipId == id);
    }

    /// <summary>
    /// One installed game with a localconfig playtime. Rewritten in place by the
    /// test that needs the local files to move.
    /// </summary>
    private static void WriteSteamRoot(string root, long minutes, DateTime lastPlayed)
    {
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));

        // VDF escapes backslashes, so the temp path goes in doubled.
        var escapedRoot = root.Replace(@"\", @"\\");
        File.WriteAllText(
            Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"{{escapedRoot}}"
                    "label"		""
                    "apps"
                    {
                        "{{AppId}}"		"44998366792"
                    }
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "steamapps", $"appmanifest_{AppId}.acf"),
            $$"""
            "AppState"
            {
                "appid"		"{{AppId}}"
                "name"		"Elden Ring"
                "StateFlags"		"4"
                "installdir"		"ELDEN RING"
            }
            """);

        // One account, so the local candidates carry an account_ref the backfill
        // can turn into a SteamID64.
        var config = Path.Combine(root, "userdata", "12345678", "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "localconfig.vdf"),
            $$"""
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
                                "{{AppId}}"
                                {
                                    "LastPlayed"		"{{new DateTimeOffset(lastPlayed).ToUnixTimeSeconds()}}"
                                    "Playtime"		"{{minutes}}"
                                }
                            }
                        }
                    }
                }
            }
            """);
    }

    /// <summary>
    /// Stands in for the HTTP call and nothing else: candidates come through the
    /// real <see cref="SteamOwnedGame.ToCandidate"/> projection.
    /// </summary>
    private sealed class OwnedLibraryStub(long minutes, DateTime lastPlayed, bool configured = true)
        : ISteamWebApiClient
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(configured);

        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(
            SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(new SteamOwnedLibrary(
                steamId,
                Succeeded: true,
                Games: [new SteamOwnedGame(AppId, "Elden Ring", minutes, 0, lastPlayed, null)],
                ObservedAt: new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc),
                FromCache: false));

        public async Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
            SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => (await GetOwnedGamesAsync(steamId, cacheTtl, ct))
                .ToCandidates(SteamWebApiClient.SourceName);
    }
}
