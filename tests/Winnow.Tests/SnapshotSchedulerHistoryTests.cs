using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// What the scheduler is actually for (§1, §6 <c>playtime_snapshots</c>): the
/// longitudinal series has to grow while the app sits in the tray, not once per
/// launch. These tests run the real <see cref="ExternalIdResolver"/> against a
/// real migrated database on the scheduler's own ticks, substituting only the
/// filesystem scan — so they assert about rows, not about calls.
///
/// <para>The other half of the claim matters just as much: a tick over
/// unchanged files must write <b>nothing</b>. The resolver is idempotent by
/// change detection; ticking it 96 times a day is only safe if the scheduler
/// does not defeat that.</para>
/// </summary>
public sealed class SnapshotSchedulerHistoryTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureBound = TimeSpan.FromSeconds(20);
    private const string AppId = "2686630";

    private readonly TempDatabase _db = new();
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly PlayRecordRepository _playRecords;
    private readonly PlaytimeSnapshotRepository _snapshots;
    private readonly ExternalIdResolver _resolver;
    private readonly SchedulerClock _clock = new();

    /// <summary>
    /// What the next scan "finds on disk". Replaced wholesale rather than
    /// mutated so a tick reading it concurrently always sees a coherent list.
    /// </summary>
    private IReadOnlyList<CandidateOwnership> _onDisk = [];

    public SnapshotSchedulerHistoryTests()
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

    /// <summary>Stands in for the Steam scan: the same resolve, off a fake disk.</summary>
    private async Task<LibrarySyncReport> ScanAndResolveAsync(CancellationToken ct)
    {
        var candidates = Volatile.Read(ref _onDisk);
        var result = await _resolver.ResolveAsync(candidates, ct);
        return new LibrarySyncReport(candidates.Count, result, TimeSpan.Zero);
    }

    /// <summary>Steam's files now say the user has this many minutes on the app.</summary>
    private void SteamNowReports(long playtimeMinutes, DateTime lastPlayedAt)
        => Volatile.Write(ref _onDisk, new[]
        {
            new CandidateOwnership(
                Provider: ExternalIdProviders.Steam,
                ProviderId: AppId,
                Title: "Voyagers of Nera",
                AccountRef: "12345678",
                InstallPath: @"C:\Steam\steamapps\common\Voyagers of Nera",
                Installed: true,
                PlaytimeMinutes: playtimeMinutes,
                LastPlayedAt: lastPlayedAt,
                AcquiredAt: null,
                Source: "steam_local",
                ObservedAt: _clock.GetUtcNow().UtcDateTime),
        });

    private SnapshotSchedulerService Scheduler(FakeLocalLibrarySync sync)
        => new(
            sync,
            Options.Create(new SnapshotSchedulerOptions { Interval = Interval }),
            NullLogger<SnapshotSchedulerService>.Instance,
            _clock);

    private async Task<long> OwnershipIdAsync()
    {
        var release = await _releases.FindByExternalIdAsync(ExternalIdProviders.Steam, AppId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id)).Id;
    }

    /// <summary>
    /// The M2 headline: playtime that moved between two ticks becomes exactly
    /// one new snapshot and one new play record — the delta a storefront throws
    /// away, captured without the app being restarted.
    /// </summary>
    [Fact]
    public async Task A_tick_captures_a_playtime_delta_as_one_snapshot_and_one_play_record()
    {
        var firstSession = new DateTime(2026, 8, 23, 20, 0, 0, DateTimeKind.Utc);
        SteamNowReports(244, firstSession);

        var sync = new FakeLocalLibrarySync((_, ct) => ScanAndResolveAsync(ct));
        using var service = Scheduler(sync);
        await service.StartAsync(CancellationToken.None);
        await _clock.TimerCreated.WaitAsync(FailureBound);

        _clock.Advance(Interval);
        Assert.Equal(1, await sync.NextCompletionAsync());

        var ownershipId = await OwnershipIdAsync();
        Assert.Equal(244, Assert.Single(await _snapshots.GetByOwnershipAsync(ownershipId)).PlaytimeMinutes);
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownershipId));

        // The user keeps playing; Steam eventually flushes the new total.
        SteamNowReports(281, firstSession.AddMinutes(37));

        _clock.Advance(Interval);
        Assert.Equal(2, await sync.NextCompletionAsync());

        var snapshots = await _snapshots.GetByOwnershipAsync(ownershipId);
        Assert.Equal(new long[] { 244, 281 }, snapshots.Select(s => s.PlaytimeMinutes));

        var records = await _playRecords.GetByOwnershipAsync(ownershipId);
        Assert.Equal(2, records.Count);
        Assert.Equal(281, records[^1].PlaytimeMinutes);

        // Two points fifteen minutes apart, from one app launch — the thing the
        // startup-only sync could not produce.
        Assert.Equal(Interval, snapshots[^1].ObservedAt - snapshots[0].ObservedAt);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// Steam is an eventually-consistent writer (§4.1): most ticks re-read files
    /// that have not changed. Those must append nothing at all, or a week in the
    /// tray leaves ~670 identical rows per game behind.
    /// </summary>
    [Fact]
    public async Task Ticks_over_unchanged_files_write_nothing_at_all()
    {
        SteamNowReports(244, new DateTime(2026, 8, 23, 20, 0, 0, DateTimeKind.Utc));

        var sync = new FakeLocalLibrarySync((_, ct) => ScanAndResolveAsync(ct));
        using var service = Scheduler(sync);
        await service.StartAsync(CancellationToken.None);
        await _clock.TimerCreated.WaitAsync(FailureBound);

        _clock.Advance(Interval);
        Assert.Equal(1, await sync.NextCompletionAsync());

        var ownershipId = await OwnershipIdAsync();
        Assert.Single(await _snapshots.GetByOwnershipAsync(ownershipId));
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownershipId));

        // Five more ticks, nothing changed on disk. Note ObservedAt DOES move
        // with the clock on each pass — change detection is on playtime, not on
        // observation time, and the scheduler must not accidentally make the
        // clock the thing that varies.
        for (var tick = 2; tick <= 6; tick++)
        {
            _clock.Advance(Interval);
            Assert.Equal(tick, await sync.NextCompletionAsync());
        }

        Assert.Single(await _snapshots.GetByOwnershipAsync(ownershipId));
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownershipId));
        Assert.Equal(1, sync.MaxInFlight);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
    }

    /// <summary>
    /// M1's shutdown bug, guarded at the row level: stopping the host must not
    /// leave a pass writing through a factory that <c>Main</c>'s <c>finally</c>
    /// is about to dispose. The scan is cancelled mid-pass, so its transaction
    /// rolls back and the database is left exactly as the previous tick left it.
    /// </summary>
    [Fact]
    public async Task Stopping_mid_scan_rolls_the_pass_back_rather_than_writing_through_a_disposed_factory()
    {
        SteamNowReports(244, new DateTime(2026, 8, 23, 20, 0, 0, DateTimeKind.Utc));

        var secondScanReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sync = new FakeLocalLibrarySync(async (ordinal, ct) =>
        {
            if (ordinal == 1)
            {
                return await ScanAndResolveAsync(ct);
            }

            // Second pass: park inside the resolve, exactly where a real scan
            // would be when the user closes the window.
            secondScanReached.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return FakeLocalLibrarySync.NoChangeReport;
        });

        using var service = Scheduler(sync);
        await service.StartAsync(CancellationToken.None);
        await _clock.TimerCreated.WaitAsync(FailureBound);

        _clock.Advance(Interval);
        Assert.Equal(1, await sync.NextCompletionAsync());
        var ownershipId = await OwnershipIdAsync();

        SteamNowReports(999, new DateTime(2026, 8, 23, 21, 0, 0, DateTimeKind.Utc));
        _clock.Advance(Interval);
        await secondScanReached.Task.WaitAsync(FailureBound);

        await service.StopAsync(CancellationToken.None).WaitAsync(FailureBound);
        Assert.NotNull(service.ExecuteTask);
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);

        // The interrupted pass contributed nothing, and the factory is still
        // usable — these very reads go through it after the stop.
        Assert.Equal(244, Assert.Single(await _snapshots.GetByOwnershipAsync(ownershipId)).PlaytimeMinutes);
        Assert.Single(await _playRecords.GetByOwnershipAsync(ownershipId));

        _clock.Advance(5 * Interval);
        Assert.Equal(2, sync.Calls);
    }
}
