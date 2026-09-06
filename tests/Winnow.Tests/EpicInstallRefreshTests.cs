using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Data.Repositories;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class EpicInstallRefreshTests
{
    private static EpicInstallRefreshService Service(Func<string?> read, Func<CancellationToken, Task> sync,
        Func<CancellationToken, Task>? refresh = null, TimeProvider? clock = null, bool enabled = true)
        => new(read, sync, refresh ?? (_ => Task.CompletedTask),
            NullLogger<EpicInstallRefreshService>.Instance, clock, enabled);

    [Fact]
    public async Task Completion_refreshes_real_ownership_only_after_stable_explicit_completion()
    {
        using var tree = EpicFixtureTree.Create([EpicFixtureTree.FezManifest], includeThirdParty: false);
        using var db = new TempDatabase();
        var path = Path.Combine(tree.DataRoot, "Manifests", EpicFixtureTree.FezManifest);
        var complete = File.ReadAllText(path);
        var queued = complete.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true");
        Assert.NotEqual(complete, queued);
        File.WriteAllText(path, queued);
        var resolver = new ExternalIdResolver(new WorkRepository(db.Factory), new ReleaseRepository(db.Factory),
            new OwnershipRepository(db.Factory), new PlayRecordRepository(db.Factory),
            new PlaytimeSnapshotRepository(db.Factory), db.Factory, new OwnershipAccountRepository(db.Factory));
        var sync = new LocalLibrarySyncService(new SteamLibrarySource(steamRoot: Path.Combine(tree.DataRoot, "NoSteam")), new EpicLibrarySource(dataRoot: tree.DataRoot),
            SilentStores.Gog(), resolver, new LibrarySyncGate(), NullLogger<LocalLibrarySyncService>.Instance);
        var published = new List<bool>();
        using var service = Service(new EpicManifestStateReader(tree.DataRoot).ReadFingerprint,
            ct => sync.SyncEpicAsync(ct), _ =>
            {
                using var connection = db.Factory.Open();
                published.Add(connection.QuerySingle<bool>("SELECT installed FROM ownerships o JOIN external_ids e ON e.release_id=o.release_id WHERE e.provider='epic' AND e.provider_id='7a70b499513441c792b541d53505e0b2'"));
                return Task.CompletedTask;
            });
        await service.PollAsync();
        await service.PollAsync();
        Assert.Equal([false], published);
        File.WriteAllText(path, "{");
        await service.PollAsync();
        File.WriteAllText(path, complete);
        await service.PollAsync();
        Assert.Equal([false], published);
        await service.PollAsync();
        Assert.Equal([false, true], published);
        await service.PollAsync();
        Assert.Equal(2, published.Count);
        File.Delete(path);
        await service.PollAsync();
        await service.PollAsync();
        Assert.Equal([false, true, false], published);
    }

    [Theory]
    [InlineData("{\"CatalogItemId\":\"test\"}")]
    [InlineData("{\"CatalogItemId\":\"test\",\"bIsIncompleteInstall\":null}")]
    [InlineData("{\"CatalogItemId\":\"test\",\"bIsIncompleteInstall\":\"false\"}")]
    public void Missing_or_invalid_completion_field_never_means_installed(string json)
    {
        using var tree = EpicFixtureTree.Create([]);
        var path = Path.Combine(tree.DataRoot, "Manifests", "test.item");
        File.WriteAllText(path, json);
        Assert.False(new EpicManifestReader().Read(path)!.IsFullyInstalled);
        Assert.Null(new EpicManifestStateReader(tree.DataRoot).ReadFingerprint());
    }

    [Fact]
    public void Pending_files_are_ignored_and_unreadable_directory_is_not_an_empty_snapshot()
    {
        using var tree = EpicFixtureTree.Create([]);
        var reader = new EpicManifestStateReader(tree.DataRoot);
        var empty = reader.ReadFingerprint();
        Assert.NotNull(empty);
        File.WriteAllText(Path.Combine(tree.DataRoot, "Manifests", "Pending", "partial.item"), "{");
        Assert.Equal(empty, reader.ReadFingerprint());
        Directory.Move(Path.Combine(tree.DataRoot, "Manifests"), Path.Combine(tree.DataRoot, "Unavailable"));
        Assert.Null(reader.ReadFingerprint());
    }

    [Fact]
    public async Task Read_failure_and_mutating_snapshot_restart_stability_window()
    {
        string? state = "a";
        var calls = 0;
        using var service = Service(() => state, _ => { calls++; return Task.CompletedTask; });
        await service.PollAsync();
        state = null;
        await service.PollAsync();
        state = "a";
        await service.PollAsync();
        Assert.Equal(0, calls);
        state = "b";
        await service.PollAsync();
        Assert.Equal(0, calls);
        await service.PollAsync();
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failed_sync_is_retried_and_change_during_sync_is_not_published()
    {
        var state = "a";
        var calls = 0;
        var refreshes = 0;
        using var service = Service(() => state, _ =>
        {
            calls++;
            if (calls == 1) throw new IOException("fixture failure");
            if (calls == 2) state = "b";
            return Task.CompletedTask;
        }, _ => { refreshes++; return Task.CompletedTask; });
        for (var i = 0; i < 4; i++) await service.PollAsync();
        Assert.Equal(2, calls);
        Assert.Equal(0, refreshes);
        await service.PollAsync();
        await service.PollAsync();
        Assert.Equal(3, calls);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task A_failed_publish_reconciles_even_when_files_return_to_the_last_applied_state()
    {
        var state = "a";
        var calls = 0;
        var published = 0;
        using var service = Service(() => state, _ =>
        {
            calls++;
            if (calls == 2) state = "a";
            return Task.CompletedTask;
        }, _ => { published++; return Task.CompletedTask; });
        await service.PollAsync();
        await service.PollAsync();
        state = "b";
        await service.PollAsync();
        await service.PollAsync();
        Assert.Equal(1, published);
        await service.PollAsync();
        await service.PollAsync();
        Assert.Equal(3, calls);
        Assert.Equal(2, published);
    }

    [Fact]
    public async Task Host_shutdown_cancels_pending_sync_and_does_not_publish()
    {
        var clock = new SchedulerClock();
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshes = 0;
        using var service = Service(() => { read.TrySetResult(); return "a"; }, async ct =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }, _ => { refreshes++; return Task.CompletedTask; }, clock);
        await service.StartAsync(CancellationToken.None);
        await clock.TimerCreated.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(EpicInstallRefreshService.Interval);
        await read.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(EpicInstallRefreshService.Interval);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task Disabled_service_never_reads_or_writes()
    {
        using var service = Service(() => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(), enabled: false);
        await service.StartAsync(CancellationToken.None);
        await service.PollAsync();
        await service.StopAsync(CancellationToken.None);
    }
}
