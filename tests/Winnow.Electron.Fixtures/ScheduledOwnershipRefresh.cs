using Dapper;
using Microsoft.Extensions.Options;
using Winnow.App.Services;
using Winnow.Data;

namespace Winnow.Electron.Fixtures;

/// <summary>Same gated remote and metadata work as the frozen UI composition test, behind real HTTP/SSE.</summary>
internal sealed class ScheduledOwnershipRefresh(ISqliteConnectionFactory database) : IAsyncDisposable
{
    private readonly TaskCompletionSource _releaseMetadata = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _publications = [];
    private RemoteOwnershipSchedulerService? _scheduler;
    private int _started;
    private int _acquisitions;
    private int _metadataEntered;
    private int _metadataCommitted;
    private int _optionalFailed;
    private int _finished;
    private int _coordinatorFailed;
    private bool _fail;

    public async Task StartAsync(IRemoteOwnershipSync coordinator, bool fail,
        ILogger<RemoteOwnershipSchedulerService> logger)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("This fixture already started its scheduled pass.");
        _fail = fail;
        _scheduler = new RemoteOwnershipSchedulerService(new RecordingRemote(coordinator, this),
            Options.Create(new RemoteOwnershipSchedulerOptions { RunOnStartup = true }), logger);
        await _scheduler.StartAsync(CancellationToken.None);
    }

    public async Task<LibrarySyncReport> AcquireAsync(CancellationToken ct)
    {
        using var connection = database.Open();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'New acquisition','New acquisition');
            INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'New acquisition','windows');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0);
            """, cancellationToken: ct));
        Interlocked.Increment(ref _acquisitions);
        if (_fail) throw new IOException("Synthetic later failure after commit");
        return new(1, null, TimeSpan.Zero);
    }

    public async Task EnrichAsync(CancellationToken ct)
    {
        Interlocked.Exchange(ref _metadataEntered, 1);
        await _releaseMetadata.Task.WaitAsync(ct);
        using var connection = database.Open();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE works SET name='Enriched acquisition',sort_name='Enriched acquisition' WHERE id=1",
            cancellationToken: ct));
        Interlocked.Exchange(ref _metadataCommitted, 1);
    }

    public Task OptionalAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!_fail) return Task.CompletedTask;
        Interlocked.Exchange(ref _optionalFailed, 1);
        return Task.FromException(new IOException("offline"));
    }

    public async Task PublishAsync(LibraryChangePublisher publisher, CancellationToken ct)
    {
        using var connection = database.Open();
        var title = await connection.QuerySingleAsync<string>(new CommandDefinition(
            "SELECT name FROM works WHERE id=1", cancellationToken: ct));
        await publisher.PublishAsync(ct);
        lock (_publications) _publications.Add(title);
    }

    public void ReleaseMetadata() => _releaseMetadata.TrySetResult();

    public object Snapshot()
    {
        string[] publications;
        lock (_publications) publications = [.. _publications];
        return new
        {
            Acquisitions = Volatile.Read(ref _acquisitions),
            MetadataEntered = Volatile.Read(ref _metadataEntered) == 1,
            MetadataCommitted = Volatile.Read(ref _metadataCommitted) == 1,
            OptionalFailed = Volatile.Read(ref _optionalFailed) == 1,
            Finished = Volatile.Read(ref _finished) == 1,
            CoordinatorFailed = Volatile.Read(ref _coordinatorFailed) == 1,
            SchedulerRunning = _scheduler?.ExecuteTask is { IsCompleted: false },
            Publications = publications,
        };
    }

    public async ValueTask DisposeAsync()
    {
        ReleaseMetadata();
        if (_scheduler is null) return;
        await _scheduler.StopAsync(CancellationToken.None);
        _scheduler.Dispose();
    }

    private sealed class RecordingRemote(IRemoteOwnershipSync inner, ScheduledOwnershipRefresh fixture) : IRemoteOwnershipSync
    {
        public async Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default)
        {
            try { return await inner.SyncAsync(ct); }
            catch { Interlocked.Exchange(ref fixture._coordinatorFailed, 1); throw; }
            finally { Interlocked.Exchange(ref fixture._finished, 1); }
        }
        public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => SyncAsync(ct);
    }
}

internal sealed class FixtureRemoteOwnership(ScheduledOwnershipRefresh fixture) : IRemoteOwnershipSync
{
    public Task<LibrarySyncReport> SyncAsync(CancellationToken ct = default) => fixture.AcquireAsync(ct);
    public Task<LibrarySyncReport> SyncAsync(LocalLibraryScan scan, CancellationToken ct = default) => SyncAsync(ct);
}
