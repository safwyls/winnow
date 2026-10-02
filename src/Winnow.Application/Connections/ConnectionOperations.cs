using Microsoft.Extensions.Hosting;
using Winnow.Api.Contracts.Connections;
using Winnow.App.Services;

namespace Winnow.Application.Connections;

public sealed class ConnectionOperations(IManualMetadataSyncService metadata, IOfficialPluginInstaller plugins,
    IApplicationChangePublisher changes, IHostApplicationLifetime lifetime, TimeProvider clock) : IHostedService
{
    private sealed class Entry(BackendOperation snapshot, CancellationTokenSource cancellation, object request)
    {
        public BackendOperation Snapshot = snapshot;
        public readonly CancellationTokenSource Cancellation = cancellation;
        public readonly object Request = request;
        public Task Completion = Task.CompletedTask;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _operations = [];

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_gate)
        {
            foreach (var entry in _operations.Values.Where(e => e.Snapshot.State == "running")) entry.Cancellation.Cancel();
            pending = _operations.Values.Select(e => e.Completion).ToArray();
        }
        await Task.WhenAll(pending).WaitAsync(cancellationToken);
    }

    public IReadOnlyList<BackendOperation> GetAll()
    {
        lock (_gate) return _operations.Values.Select(e => e.Snapshot).OrderByDescending(o => o.UpdatedAt).ToArray();
    }
    public BackendOperation Get(string id)
    {
        lock (_gate) return _operations.TryGetValue(id, out var entry) ? entry.Snapshot : throw new ApplicationNotFoundException("Operation not found.");
    }
    public BackendOperation StartMetadata(StartMetadataSync request) => Start(request.OperationId, "metadata-sync", request,
        async (entry, ct) => await metadata.SyncAsync(new CallbackProgress<string>(message => Update(entry, message)), ct));
    public BackendOperation StartPlugin(StartPluginInstall request)
    {
        if (request.Request is null || !request.Request.IsValid) throw new ArgumentException("Invalid official plugin installation request.");
        return Start(request.OperationId, "plugin-install", request,
            async (entry, ct) => await plugins.InstallAsync(request.Request, new CallbackProgress<PluginInstallProgress>(progress => Update(entry, progress.Message)), ct));
    }
    public void Cancel(string id)
    {
        lock (_gate)
        {
            if (!_operations.TryGetValue(id, out var entry)) throw new ApplicationNotFoundException("Operation not found.");
            if (entry.Snapshot.State == "running") entry.Cancellation.Cancel();
        }
    }
    private BackendOperation Start(string id, string kind, object request, Func<Entry, CancellationToken, Task<object>> run)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("An operation ID in GUID N format is required.");
        lock (_gate)
        {
            if (_operations.TryGetValue(id, out var existing))
            {
                if (existing.Request != request && !existing.Request.Equals(request)) throw new ApplicationConflictException("Operation ID already belongs to another request.");
                return existing.Snapshot;
            }
            if (_operations.Count >= 256)
            {
                var oldest = _operations.Values.Where(e => e.Snapshot.State != "running").OrderBy(e => e.Snapshot.UpdatedAt).FirstOrDefault();
                if (oldest is null) throw new ApplicationConflictException("Too many operations are running.");
                _operations.Remove(oldest.Snapshot.Id);
            }
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            var entry = new Entry(new(id, kind, "running", "Starting…", clock.GetUtcNow()), cancellation, request);
            _operations.Add(id, entry);
            entry.Completion = Task.Run(async () =>
            {
                try
                {
                    var result = await run(entry, cancellation.Token);
                    Finish(entry, "completed", "Complete.", result);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Finish(entry, "cancelled", "Cancelled."); }
                catch (Exception) { Finish(entry, "failed", "The operation could not complete. Try again."); }
                finally { cancellation.Dispose(); }
            });
            changes.Publish("operations.changed", id);
            return entry.Snapshot;
        }
    }
    private void Update(Entry entry, string message)
    {
        lock (_gate)
        {
            if (entry.Snapshot.State != "running") return;
            entry.Snapshot = entry.Snapshot with { Message = message, UpdatedAt = clock.GetUtcNow() };
        }
        changes.Publish("operations.changed", entry.Snapshot.Id);
    }
    private void Finish(Entry entry, string state, string message, object? result = null)
    {
        lock (_gate) entry.Snapshot = entry.Snapshot with
        { State = state, Message = message, UpdatedAt = clock.GetUtcNow(), MetadataResult = result as MetadataSyncResult?, PluginResult = result as PluginInstallResult };
        changes.Publish("operations.changed", entry.Snapshot.Id);
        if (state == "completed") changes.Publish("library.changed");
    }
    private sealed class CallbackProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
